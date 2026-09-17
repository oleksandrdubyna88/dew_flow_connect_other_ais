using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Revoking a key actually stops an ingest — before the limiter, and before any write.
/// </summary>
/// <remarks>
/// <para>"The operator's stop button does not stop anything" is the failure worth pinning, so the
/// stop button here is the real one: the <c>--revoke</c> one-shot, run in-process against the
/// database the server holds. A revoked key is one whose <c>revoked_utc</c> is non-empty, and
/// <see cref="Corpus.KeyFor"/> never answers one — deleting that filter is how the first test goes
/// red.</para>
/// <para><c>TheRouteTests.ARevokedKeyAndAnUnknownKeyAnswerTheSame</c> already proves the two are
/// indistinguishable; this proves the BEFORE and AFTER around the revocation, and the order the
/// refusal happens in.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheRevokedKeyTests
{
    private static readonly object OnePair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
            },
        },
    };

    private static Task<HttpResponseMessage> Ingest(HttpClient http) =>
        http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);

    [Fact]
    public async Task RevokingActuallyStopsAnIngest_BeforeAndAfter()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK, "before: the key works");

        Admin.Run(["--revoke", "--id", id], BugsServer.Secret, server.DataDir, server.Clock)
            .Should().Be(0, "the operator's stop button, while the server serves");

        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "after: it does not");

        using var corpus = server.Reading();
        corpus.WaitingCount().Should().Be(1, "the second request wrote nothing");
        corpus.UsageOf(id).Submissions.Should().Be(1, "and counted nothing");
    }

    /// <summary>The refusal happens BEFORE the limiter: a revoked key never gets a window.</summary>
    /// <remarks>
    /// With a limit of one, five requests on a revoked key are five 401s and never a 429 — and the
    /// limiter tracks nobody, which is the second half of "bounded by the keys that exist": a key
    /// that is no longer in force is not one of them.
    /// </remarks>
    [Fact]
    public async Task ARevokedKeyIsRefusedBeforeTheLimiterAndGetsNoWindow()
    {
        using var server = new BugsServer(ratePerMinute: 1);
        var (key, id) = server.IssueKey();
        using (var corpus = server.Reading())
        {
            corpus.Revoke(id, Audit.By(AdminIdentity.Cli, server.Clock)).Should().BeTrue();
        }

        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        for (var n = 0; n < 5; n++)
        {
            (await Ingest(http)).StatusCode.Should().Be(
                HttpStatusCode.Unauthorized, $"request {n + 1}: a revoked key is 401, never 429");
        }

        server.Services.GetRequiredService<RateLimiter>().Tracked.Should().Be(
            0, "the limiter never saw the key, so it holds no window for it");
    }
}
