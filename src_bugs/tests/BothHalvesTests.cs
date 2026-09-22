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

    /// <summary>A kept decision — spelled here because this class's own <c>Keep</c> is the seeding method.</summary>
    private const int Kept = CoaiMcp.Core.Collecting.Keep.Kept;

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

    // ------------------------------------------------------------------------------------------
    // A comment, end to end (story 4.2 of PLAN_a_comment_crosses_the_machine_boundary.md).
    // ------------------------------------------------------------------------------------------

    /// <summary>What a person wrote here is what the server holds, and the pair is marked sent.</summary>
    /// <remarks>
    /// The whole story in one assertion chain: the local column, the route an old server does not
    /// have, the server's own column, and the acknowledgement written back — with nothing said about
    /// a lost comment, because nothing was lost.
    /// </remarks>
    [Fact]
    public async Task ACommentWrittenHereIsHeldThereAndThePairIsMarkedSent()
    {
        var id = Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"))[0];

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using var db = Db();
        db.RecordDecide([new CommentedDecision(id, Kept,"this one bit us in production")]);

        (await Run(server, db, key)).Accepted.Should().Be(1);

        using var corpus = server.Reading();
        corpus.Waiting(10).Should().ContainSingle().Which.Comment.Should().Be(
            "this one bit us in production", "the words crossed with the pair, verbatim");
        var here = db.Pairs(10).Should().ContainSingle().Subject;
        here.SentUtc.Should().NotBeEmpty("the server acknowledged it");
        here.CommentLost.Should().BeEmpty("and it held the words, so nothing was lost");
    }

    /// <summary>
    /// A send that landed and was never acknowledged, retried, is not reported as a lost comment.
    /// </summary>
    /// <remarks>
    /// <para>The failure codex and gemini named in the plan round of 4.2, driven through both real
    /// halves. The server commits the pair and its words; the client never records the answer — the
    /// process killed, the connection dropped — so the pair is offered again with the SAME comment.
    /// Answered "this pair already carries a comment, so yours was not stored", the client would
    /// write <c>comment_lost</c>, and the page would tell a person their words are gone while they
    /// sit on the server.</para>
    /// <para>The lost acknowledgement is simulated the way <see cref="TheSecondSendIsADuplicateAndStillMarksThePair"/>
    /// simulates a resend: <c>sent_utc</c> cleared by hand, which is exactly the state a kill between
    /// the server's commit and the client's write leaves behind.</para>
    /// </remarks>
    [Fact]
    public async Task ARetriedSendWhoseAnswerWasLostDoesNotCallItsOwnCommentLost()
    {
        var id = Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"))[0];

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using (var first = Db())
        {
            first.RecordDecide([new CommentedDecision(id, Kept,"mine")]);
            (await Run(server, first, key)).Accepted.Should().Be(1);
        }

        ForgetTheAcknowledgement();

        using var db = Db();
        (await Run(server, db, key)).Duplicate.Should().Be(1, "the server already holds the pair");
        db.Pairs(10).Should().ContainSingle().Which.CommentLost.Should().BeEmpty(
            "the words the server holds ARE these words — the retry must not report them lost");
    }

    /// <summary>Somebody else's words were there first: the pair is sent, and the loss is SAID.</summary>
    /// <remarks>
    /// The other half of the same arbitration: different words on a pair the server already holds
    /// lose to the first speaker, and `duplicate` — a success — must not carry that loss in silence.
    /// </remarks>
    [Fact]
    public async Task WordsThatLostToAnEarlierCommentAreRecordedAsLost()
    {
        var id = Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"))[0];

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using (var first = Db())
        {
            first.RecordDecide([new CommentedDecision(id, Kept,"the first words")]);
            (await Run(server, first, key)).Accepted.Should().Be(1);
        }

        // Another contributor's position: the same pair, unsent here, carrying different words.
        ForgetTheAcknowledgement();
        using var db = Db();
        db.RecordDecide([new CommentedDecision(id, Kept,"different words")]);

        (await Run(server, db, key)).Duplicate.Should().Be(1);

        var here = db.Pairs(10).Should().ContainSingle().Subject;
        here.SentUtc.Should().NotBeEmpty("a duplicate is acknowledged, so the pair is not offered again");
        here.CommentLost.Should().Contain("was not stored", "and the person is told their words did not go");
        here.CommentLost.Should().NotContain("different words", "the sentence names no comment's text");
    }

    /// <summary>A comment-free batch still crosses as it always did, and its pair carries no words there.</summary>
    [Fact]
    public async Task ACommentFreePairStillCrossesWithoutWords()
    {
        Keep(new CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

        using var server = new BugsServer();
        var (key, _) = server.IssueKey();
        using var db = Db();

        (await Run(server, db, key)).Accepted.Should().Be(1);

        using var corpus = server.Reading();
        corpus.Waiting(10).Should().ContainSingle().Which.Comment.Should().BeEmpty();
    }

    /// <summary>Puts every pair back in the queue — the state a lost acknowledgement leaves.</summary>
    private void ForgetTheAcknowledgement()
    {
        using var write = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        write.Open();
        using var clear = write.CreateCommand();
        clear.CommandText = "UPDATE collect_pairs SET sent_utc = '', comment_lost = ''";
        clear.ExecuteNonQuery();
    }

    private static async Task<UploadSummary> Run(
        BugsServer server, RoundsDb db, string key, int limit = 10)
    {
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await new UploadRun(http).RunAsync(
            db, Anywhere, key, limit, TestContext.Current.CancellationToken);
    }

    private IReadOnlyList<long> Keep(params CollectedPair[] pairs) => Seed.KeptPairs(_dir, pairs);

    public void Dispose() => Scratch.Delete(_dir);
}
