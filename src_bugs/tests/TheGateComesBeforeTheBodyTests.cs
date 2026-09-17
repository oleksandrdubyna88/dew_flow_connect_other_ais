using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The key and the limit are judged BEFORE the body is read: a body nobody may send is never parsed.
/// </summary>
/// <remarks>
/// <para>The code round's arithmetic: <c>UploadRequest?</c> is bound from the body, so the framework
/// deserialised every request before the handler ran — a hundred one-megabyte bodies at a limit of
/// ten were all parsed before ninety of them were told to wait, and a malformed body from a stranger
/// got the framework's 400 before the 401 that was owed. The bearer check and the rate admission live
/// in middleware ahead of routing now, and the body is read only for a request that has passed
/// both.</para>
/// <para>The observable is the status: a body that cannot parse answers 400 only when it was READ,
/// so a 401 or a 429 for the same body proves it was not.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheGateComesBeforeTheBodyTests
{
    private static StringContent NotJson() => new("this is not json", Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> Post(HttpClient http) =>
        http.PostAsync("/ingest", NotJson(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task WithoutAKeyAMalformedBodyIsA401_NotAFramework400()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();

        (await Post(http)).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "the key is judged before the body is read, so a stranger's body is never parsed");
    }

    [Fact]
    public async Task ARevokedKeyIsA401BeforeItsBodyIsRead()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        using (var corpus = server.Reading())
        {
            corpus.Revoke(id, Audit.By(AdminId.Cli, server.Clock)).Should().BeOfType<Revoked.Now>();
        }

        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        (await Post(http)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The first malformed body is parsed and refused; the second is refused before it is parsed.</summary>
    [Fact]
    public async Task AKeyOverItsLimitIsA429BeforeItsBodyIsRead()
    {
        using var server = new BugsServer(ratePerMinute: 1);
        var (key, _) = server.IssueKey();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        (await Post(http)).StatusCode.Should().Be(
            HttpStatusCode.BadRequest, "authenticated and admitted, so the body is read — and it does not parse");
        (await Post(http)).StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests, "over the limit, so the body is never read: a flood costs nothing to parse");
    }
}
