using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// <c>last_seen_month</c>: a UTC calendar month, moved only by an ACCEPTED ingest, in the same
/// commit as the pairs and the counter.
/// </summary>
/// <remarks>
/// <para>Through the real server on the frozen clock the harness injects, so a month boundary is
/// two assignments and the STORED value is what is asserted — against <see cref="UtcMonth.Shape"/>
/// and against the month the clock said. The month is a promise about what the server records, so
/// each way of NOT being an accepted ingest — a 401, a 429, a malformed body, an administrative
/// call — has its own test.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheLastSeenMonthTests
{
    private static readonly DateTimeOffset EndOfSeptember = new(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset StartOfOctober = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

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

    private static HttpClient Client(BugsServer server, string key)
    {
        var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return http;
    }

    private static Task<HttpResponseMessage> Ingest(HttpClient http, object? body = null) =>
        http.PostAsJsonAsync("/ingest", body ?? OnePair, TestContext.Current.CancellationToken);

    private static Usage UsageOf(BugsServer server, KeyId id)
    {
        using var corpus = server.Reading();

        return corpus.UsageOf(id);
    }

    /// <summary>What a key has done, in the shape <see cref="Corpus.UsageOf"/> answers.</summary>
    /// <remarks>
    /// An empty month is <see cref="LastSeen.Never"/> rather than a blank string — `UtcMonth.Read`
    /// makes that distinction, and writing it here once keeps every assertion below a single line.
    /// </remarks>
    private static Usage Used(int submissions, string month) =>
        new Usage.Known(new SubmissionCount(submissions), UtcMonth.Read(month));


    [Fact]
    public async Task AnAcceptedIngestStampsTheUtcMonthAndNothingFiner()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        using var http = Client(server, key);
        server.Clock.Set(EndOfSeptember);

        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);

        var september = UsageOf(server, id);
        september.Month().Should().MatchRegex(UtcMonth.Shape().ToString(), "a month, never a day or an hour");
        september.Should().Be(Used(1, "2026-09"), "one second before midnight on the 30th is still September");

        server.Clock.Set(StartOfOctober);
        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);

        UsageOf(server, id).Should().Be(Used(2, "2026-10"), "and midnight on the 1st is October");
    }

    /// <summary>Every accepted ingest counts, and the month is whatever the clock last said.</summary>
    /// <remarks>
    /// <para><b>This test used to assert that a same-month ingest did NOT rewrite the row</b>, through
    /// a `MonthAdvanced` flag on the answer. The code round refuted the saving that justified it: the
    /// counter update rewrites the same row on every single ingest, so a conditional month update
    /// avoided no page write at all — it only bought a second statement and a second B-tree lookup on
    /// the server's hottest path. The two are one `UPDATE` now, the flag has no subject, and asserting
    /// it would be asserting a claim that was never true.</para>
    /// <para>What IS true is kept and still asserted: three accepted ingests count three, and the
    /// month follows the clock across a boundary.</para>
    /// </remarks>
    [Fact]
    public void EveryAcceptedIngestCountsAndTheMonthFollowsTheClock()
    {
        using var server = new BugsServer();
        var (_, id) = server.IssueKey();
        using var corpus = server.Reading();
        var september = UtcMonth.Of(EndOfSeptember);

        corpus.Accept(id, september, _ => 0).Should().BeOfType<Accepted<int>.Stored>();
        corpus.Accept(id, september, _ => 0).Should().BeOfType<Accepted<int>.Stored>();
        corpus.Accept(id, UtcMonth.Of(StartOfOctober), _ => 0).Should().BeOfType<Accepted<int>.Stored>();

        corpus.UsageOf(id).Should().Be(Used(3, "2026-10"), "the counter moved every time; the month is the last one");
    }

    [Fact]
    public async Task A401DoesNotMoveTheMonth()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        server.Clock.Set(EndOfSeptember);
        using (var corpus = server.Reading())
        {
            corpus.Revoke(id, Audit.By(AdminId.Cli, server.Clock)).Should().BeOfType<Revoked.Now>();
        }

        using var revoked = Client(server, key);
        (await Ingest(revoked)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var none = server.CreateClient();
        (await Ingest(none)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        UsageOf(server, id).Should().Be(Used(0, string.Empty), "a refused request records nothing about the key");
    }

    [Fact]
    public async Task A429DoesNotMoveTheMonth()
    {
        using var server = new BugsServer(ratePerMinute: 1);
        var (key, id) = server.IssueKey();
        using var http = Client(server, key);
        server.Clock.Set(EndOfSeptember - TimeSpan.FromSeconds(29));
        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Thirty seconds on: a new month, but still inside the limiter's minute.
        server.Clock.Set(StartOfOctober);
        using var refused = await Ingest(http);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30), "the September stamp leaves the window in thirty seconds");
        UsageOf(server, id).Should().Be(Used(1, "2026-09"), "the refused October request moved neither the count nor the month");
    }

    [Fact]
    public async Task AMalformedBodyDoesNotMoveTheMonth()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        using var http = Client(server, key);
        server.Clock.Set(EndOfSeptember);

        (await Ingest(http, new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no items list");
        var tooMany = new
        {
            items = Enumerable.Range(0, CoaiBugs.Ingest.MostPerBatch + 1)
                .Select(n => new { language = "CSharp", skeletonBefore = $"method_{n}() {{ }}", skeletonAfter = $"method_{n}() {{ var_1 = 0; }}" })
                .ToArray(),
        };
        (await Ingest(http, tooMany)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "over the batch cap");

        UsageOf(server, id).Should().Be(Used(0, string.Empty));
    }

    /// <summary>An administrator's call is about the administrator, and moves nothing on the key's usage.</summary>
    [Fact]
    public void AnAdminCallDoesNotMoveTheMonth()
    {
        using var server = new BugsServer();
        var (_, id) = server.IssueKey();
        server.Clock.Set(EndOfSeptember);

        Admin.Run(["--waiting"], BugsServer.Secret, server.DataDir, server.Clock).Should().Be(0);
        Admin.Run(["--revoke", "--id", id.Value], BugsServer.Secret, server.DataDir, server.Clock).Should().Be(0);

        UsageOf(server, id).Should().Be(
            Used(0, string.Empty), "issuing, listing and revoking are audited about the administrator, not stamped on the key");
        using var corpus = server.Reading();
        corpus.AuditCount().Should().Be(2, "the issue and the revoke, with their exact times, on the administrator's side");
    }

    /// <summary>
    /// The counter is <c>submissions = submissions + 1</c> in SQL: sixteen ingests at once count sixteen.
    /// </summary>
    /// <remarks>
    /// Read-then-write loses an increment when two ingests race; the limiter is off so every one of
    /// them is accepted, and the whole batch — pairs, count, month — is one commit per request.
    /// </remarks>
    [Fact]
    public async Task TheCounterIsAtomicUnderConcurrentIngests()
    {
        using var server = new BugsServer(ratePerMinute: 0);
        var (key, id) = server.IssueKey();
        using var http = Client(server, key);
        const int AtOnce = 16;

        var replies = await Task.WhenAll(Enumerable.Range(0, AtOnce).Select(_ => Ingest(http)));

        replies.Select(reply => reply.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        UsageOf(server, id).Count().Should().Be(AtOnce, "every accepted ingest was counted, none lost to a race");
    }
}
