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

        Admin.Run(["--revoke", "--id", id.Value], BugsServer.Secret, server.DataDir, server.Clock)
            .Should().Be(0, "the operator's stop button, while the server serves");

        (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "after: it does not");

        using var corpus = server.Reading();
        corpus.WaitingCount().Should().Be(1, "the second request wrote nothing");
        corpus.UsageOf(id).Count().Should().Be(1, "and counted nothing");
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
            corpus.Revoke(id, Audit.By(AdminId.Cli, server.Clock)).Should().BeTrue();
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

    /// <summary>
    /// A key revoked between the gate's check and the write stores nothing and counts nothing.
    /// </summary>
    /// <remarks>
    /// <para>The gate authenticates and the handler writes, in two steps, and a <c>--revoke</c> can
    /// commit between them: the key was in force when it was checked and is not when it writes. So
    /// the check is repeated INSIDE the write transaction, which is opened IMMEDIATE so no revoke can
    /// land between the re-check and the writes; a key found revoked there is a 401 with no row, no
    /// count and no month, and the limiter's stamp is given back.</para>
    /// <para>This drives the two steps exactly as <c>Program</c> does, with the revoke landing between
    /// them through a second connection — the operator's one-shot. An HTTP test cannot interleave a
    /// request deterministically, which is why the seam is exercised directly.</para>
    /// </remarks>
    [Fact]
    public void AKeyRevokedBetweenTheGateAndTheWriteStoresNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-interleave-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "coai-bugs.db");
        var clock = new FrozenClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        try
        {
            using var serving = Corpus.Open(db);
            const string Key = "a-key-about-to-be-revoked";
            var id = new KeyId(Guid.NewGuid().ToString("N")[..16]);
            serving.Issue(id, Corpus.HashOf(Key, "s"), string.Empty, Audit.By(AdminId.Cli, clock));

            var keyId = serving.KeyFor(Key, "s");
            keyId.Should().Be(id.Value, "the gate: in force");
            using (var operating = Corpus.Open(db))
            {
                operating.Revoke(id, Audit.By(AdminId.Cli, clock)).Should().BeTrue("the operator's revoke lands between");
            }

            serving.Accept(new KeyId(keyId), UtcMonth.Now(clock), scope => scope.Keep("CSharp", "a", "b"))
                .Should().BeOfType<Accepted<(Kept Kept, string EntryId)>.KeyNotInForce>(
                    "the key was revoked between the gate and the write, so the write must refuse itself");

            serving.WaitingCount().Should().Be(0, "a key revoked before the write must store nothing");
            serving.UsageOf(id).Count().Should().Be(0, "and count nothing");
        }
        finally
        {
            Scratch.Delete(dir);
        }
    }

    /// <summary>The scope handed to the callback is dead the moment the batch commits.</summary>
    /// <remarks>
    /// <para><b>The hole this closes.</b> <see cref="Corpus.Accept"/> hands the callback an
    /// <see cref="IngestScope"/> that holds the corpus, the key and the month. Nothing stopped a
    /// caller keeping that object, letting <c>Accept</c> return and commit, revoking the key, and
    /// then calling <c>Keep</c> on it: the write would land on the connection with no transaction
    /// around it, no re-check of the key, and no counter — quarantine rows for a revoked key that
    /// were never part of an accepted ingest.</para>
    /// <para>The scope is a LEASE now: <c>Accept</c> closes it on the way out, whichever way it
    /// leaves, and every operation on a closed scope refuses. Found by the code round, which was
    /// right that a captured reference is a capability nobody revoked. (codex.)</para>
    /// </remarks>
    [Fact]
    public void AScopeCannotOutliveTheBatchThatMadeIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-scope-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var clock = new FrozenClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        try
        {
            using var corpus = Corpus.Open(Path.Combine(dir, "coai-bugs.db"));
            var id = new KeyId(Guid.NewGuid().ToString("N")[..16]);
            corpus.Issue(id, Corpus.HashOf("a-key", "s"), string.Empty, Audit.By(AdminId.Cli, clock));

            IngestScope? escaped = null;
            corpus.Accept(id, UtcMonth.Now(clock), scope =>
            {
                escaped = scope;

                return scope.Keep("CSharp", "a", "b");
            }).Should().BeOfType<Accepted<(Kept Kept, string EntryId)>.Stored>();

            var after = () => escaped!.Keep("CSharp", "c", "d");

            after.Should().Throw<InvalidOperationException>(
                "the batch committed, so the scope is spent — a write through it would have no "
                + "transaction, no key check and no counter");
            corpus.WaitingCount().Should().Be(1, "and nothing was written by the attempt");
        }
        finally
        {
            Scratch.Delete(dir);
        }
    }
}
