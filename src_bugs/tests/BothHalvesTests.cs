using System.Net.Http.Headers;
using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The real client against the real server: the one check that is not two suites agreeing with
/// themselves.
/// </summary>
/// <remarks>
/// <para><b>This file is the answer to the loudest finding of the code round.</b> The wire contract
/// had two implementations and no live check between them — the convention is explicit that two
/// suites agreeing with the same file is not that check — and the catalogue said so in as many
/// words. The types are shared now, which removes the drift this was most likely to catch; what it
/// still catches is everything a shared record cannot promise: that `UploadRun` derives the id the
/// server derives, that it reads the words the server writes, that the AOT serializers on both sides
/// bind each other's documents, and that a refusal arrives as a refusal rather than as an unknown
/// word.</para>
/// <para>`UploadRun.RunAsync` had no test at all before this — only its private mapping did.</para>
/// </remarks>
[Collection("the-server")]
public sealed class BothHalvesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-both-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    /// <summary>Where the client sends. The factory answers it in-process whatever the host says.</summary>
    private static readonly Uri Anywhere = new("http://localhost");

    private RoundsDb Db() => RoundsDb.Open(_dir, _log)!;

    [Fact]
    public async Task APairKeptHereArrivesThereAndIsMarkedSent()
    {
        Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using var db = Db();

        var summary = await Run(server, db, key);

        summary.Should().BeEquivalentTo(
            new UploadSummary(Offered: 1, Accepted: 1), "the pair crossed and was taken");

        using var corpus = server.Reading();
        var there = corpus.Waiting(10).Should().ContainSingle().Subject;
        there.Language.Should().Be("CSharp");
        there.Before.Should().Contain("method_1");
        there.EntryId.Should().Be(
            PairId.Of(new UploadedPair("CSharp", there.Before, there.After)),
            "the client matches acknowledgements against this id, so it must be derivable");

        db.Sendable(10).Should().BeEmpty("a pair the server acknowledged is not offered again");
    }

    /// <summary>Sending the same pair twice is a success, and the client stops offering it.</summary>
    /// <remarks>
    /// `duplicate` is the one outcome that is easy to get backwards: reported as a failure it makes a
    /// client retry for ever, and the local row would never be marked.
    /// </remarks>
    [Fact]
    public async Task TheSecondSendIsADuplicateAndStillMarksThePair()
    {
        Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();

        using (var first = Db())
        {
            (await Run(server, first, key)).Accepted.Should().Be(1);
            first.RequeueRefused();

            // Put it back in the queue by hand, which is the only way to send the same pair twice.
            using var write = new SqliteConnection(
                $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
            write.Open();
            using var clear = write.CreateCommand();
            clear.CommandText = "UPDATE collect_pairs SET sent_utc = ''";
            clear.ExecuteNonQuery();
        }

        using var db = Db();
        var again = await Run(server, db, key);

        again.Duplicate.Should().Be(1, "the corpus holds it; that is a success");
        again.Refused.Should().Be(0);
        db.Sendable(10).Should().BeEmpty("a duplicate is acknowledged, so it is marked sent");
    }

    /// <summary>
    /// A refusal crosses as a refusal, is written down here, and can be undone.
    /// </summary>
    /// <remarks>
    /// The word matters twice: the client recognises it (an unknown word now stops the batch rather
    /// than being read as a refusal), and `--requeue-refused` is what clears the column after the
    /// normaliser that caused it is repaired. Without that command a refusal is permanent.
    /// </remarks>
    [Fact]
    public async Task ARefusalIsRecordedWithItsWordAndCanBeRequeued()
    {
        Keep(new CollectedPair(
            "ChargeAcmeCustomer",
            "CSharp",
            "method_1() { ChargeAcmeCustomer(); }",
            "method_1() { lock (var_1) { } }"));

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using var db = Db();

        var summary = await Run(server, db, key);

        summary.Refused.Should().Be(1);
        db.Sendable(10).Should().BeEmpty("a refused pair is not offered again");
        using (var corpus = server.Reading())
        {
            corpus.Waiting(10).Should().BeEmpty("a refusal lands in no table");
        }

        db.RequeueRefused().Should().Be(1);
        db.Sendable(10).Should().ContainSingle("a repaired normaliser must be able to re-offer it");
    }

    /// <summary>A key the server does not know marks nothing at all.</summary>
    [Fact]
    public async Task AnUnknownKeyLeavesEveryPairSendable()
    {
        Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

        using var server = new BugsServer();
        using var db = Db();

        var summary = await Run(server, db, "never-issued");

        summary.Trouble.Should().Contain("401");
        db.Sendable(10).Should().ContainSingle("nothing is marked when the whole request was refused");
    }

    /// <summary>More pairs than one batch holds, and every one of them arrives.</summary>
    /// <remarks>
    /// It sent ONE batch and answered success, so a queue past the cap reported a number that was
    /// true about the batch and false about the queue. The cap here is the real one, so this test
    /// sends a real 200-pair request and then another. (Code round, codex.)
    /// </remarks>
    [Fact]
    public async Task AQueuePastOneBatchIsSentInFull()
    {
        Keep([.. Enumerable.Range(0, UploadRun.PerBatch + 3).Select(n => new CollectedPair(
            $"Method{n}", "CSharp", $"method_1(var_{n}) {{ }}", $"method_1(var_{n}) {{ var_2 = 0; }}"))]);

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using var db = Db();

        var summary = await Run(server, db, key, limit: 1000);

        summary.Accepted.Should().Be(UploadRun.PerBatch + 3, "one batch is not the whole queue");
        db.Sendable(1000).Should().BeEmpty();
        using var corpus = server.Reading();
        corpus.WaitingCount().Should().Be(UploadRun.PerBatch + 3);
    }

    private static async Task<UploadSummary> Run(
        BugsServer server, RoundsDb db, string key, int limit = 10)
    {
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await new UploadRun(http).RunAsync(
            db, Anywhere, key, limit, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds one accepted finding per pair, collects each, and keeps all of them.
    /// </summary>
    /// <remarks>
    /// One round for the whole set, because that is what a round is: a review answers about every
    /// finding it was given, and seeding them one round each would be a database no run produces.
    /// </remarks>
    private void Keep(params CollectedPair[] pairs)
    {
        using var db = Db();
        var found = pairs
            .Select(pair => new Finding(
                Severity.Major, Category.Reliability, $"src/{pair.SymbolName}.cs", 5, "a race",
                "it races", "hold the lock", ["codex"]))
            .ToList();
        var session = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig())
        {
            Stage = Stage.CodeReview,
        };

        db.RecordRound(
            session,
            new RoundRecord("CodeReview", 1, "revise", found.Count, "all answered", new DateTime(2026, 9, 16)),
            found,
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions(
            "s1",
            "CodeReview",
            1,
            [.. Enumerable.Range(0, found.Count).Select(at => DecisionAt.Accept(found, at)!)]);

        var ids = Seeded();
        ids.Should().HaveCount(pairs.Length, "every finding must have landed");
        for (var at = 0; at < pairs.Length; at++)
        {
            db.RecordCollect(ids[at], "", "collected", "", "bbbb222", "run-1", pairs[at])
                .Should().BeTrue();
        }

        db.RecordKeep([.. ids.Select(id => new KeepDecision(id, CoaiMcp.Core.Collecting.Keep.Kept))])
            .Should().Be(pairs.Length);
    }

    /// <summary>The finding row ids, in the order they were written.</summary>
    private IReadOnlyList<long> Seeded()
    {
        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var all = read.CreateCommand();
        all.CommandText = "SELECT id FROM findings ORDER BY id";
        using var rows = all.ExecuteReader();
        var ids = new List<long>();
        while (rows.Read())
        {
            ids.Add(rows.GetInt64(0));
        }

        return ids;
    }

    public void Dispose() => Scratch.Delete(_dir);
}
