using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pairs: the artefact the whole plan exists to produce, and the decisions made about them.
/// </summary>
/// <remarks>
/// The collector computed both skeletons and threw them away until story 5 — it has to compute them,
/// because comparing them is how it decides the method changed at all. What shipped was a corpus of
/// pointers, and both the review page and the upload would have had to rebuild every pair from git.
/// </remarks>
public sealed class ThePairsThemselvesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-pairs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static readonly CollectedPair Pair =
        new("GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock { } }");

    private RoundsDb Db() => RoundsDb.Open(_dir, _log)!;

    /// <summary>Seeds one accepted, gating, runtime finding and hands back its row id.</summary>
    private long Seed()
    {
        using var db = Db();
        var found = new Finding(
            Severity.Major, Category.Reliability, "src/Totals.cs", 5, "a race", "it races",
            "hold the lock", ["codex"]);
        db.RecordRound(
            Session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 16)),
            [found],
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";

        return (long)one.ExecuteScalar()!;
    }

    [Fact]
    public void ACollectedCandidateKeepsItsPair()
    {
        var id = Seed();
        using var db = Db();

        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair).Should().BeTrue();

        var stored = db.Pairs(50).Should().ContainSingle().Subject;
        stored.FindingId.Should().Be(id);
        stored.SymbolName.Should().Be("GetOrAdd");
        stored.Language.Should().Be("CSharp");
        stored.SkeletonBefore.Should().Contain("method_1");
        stored.SkeletonAfter.Should().Contain("lock");
        stored.Keep.Should().Be(Keep.Undecided, "nobody has looked at it yet");
        stored.Title.Should().Be("a race", "the page shows what the reviewers said it WAS");
    }

    /// <summary>
    /// The page's row says where the pair WAS and what the reviewers said — from three tables, not one.
    /// </summary>
    /// <remarks>
    /// <para>Story 2.1 of the review-page plan: the page showed a method and two skeletons in code
    /// whose identifiers were already <c>var_1</c>, with no way to tell which file, which commit, or
    /// why the finding was raised. The three tables were always there; <c>Pairs()</c> read one.</para>
    /// <para><b>Two shas, named apart.</b> The BEFORE skeleton is the method at the commit the
    /// reviewers read; the AFTER skeleton is the method at the commit the collector found the fix in.
    /// The plan's brief labelled both "at head_sha", and the collector says otherwise
    /// (<c>Collector.LocateThenWalkAsync</c> normalises the after side from <c>touched.Sha</c>).</para>
    /// </remarks>
    [Fact]
    public void APairSaysWhereItWas_AndWhatTheReviewersSaid()
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);

        var shown = db.Pairs(50).Should().ContainSingle().Subject;

        shown.RepoPath.Should().Be("D:/repo");
        shown.HeadSha.Should().Be("aaaa111", "the before skeleton is the method at the commit the reviewers read");
        shown.FixSha.Should().Be("bbbb222", "the after skeleton is the method at the commit the fix was found in");
        shown.File.Should().Be("src/Totals.cs");
        shown.Line.Should().Be(5);
        shown.Why.Should().Be("it races");
        shown.Fix.Should().Be("hold the lock");
    }

    /// <summary>
    /// A pair whose session row is gone is still offered, and says it does not know where it was.
    /// </summary>
    /// <remarks>
    /// <para>An inner join would drop it in silence — a pair somebody has to decide about, missing
    /// from the page because a row two tables away went. Verified by mutation: with the session's
    /// <c>LEFT JOIN</c> made a plain <c>JOIN</c>, this goes red on <c>ContainSingle</c>.</para>
    /// <para><b>How a session row goes missing, since this code cannot do it.</b>
    /// Microsoft.Data.Sqlite turns foreign keys ON for every connection it opens, so the first
    /// version of this fixture died on <c>FOREIGN KEY constraint failed</c> — a real answer, and
    /// the reason the delete below runs with the pragma off: the <c>sqlite3</c> command-line shell
    /// leaves foreign keys OFF by default, and an operator pruning old sessions from it is exactly
    /// the hand that orphans a round. The page must not lose a pair to that.</para>
    /// </remarks>
    [Fact]
    public void APairWhoseSessionIsGone_IsStillOffered_AndSaysItsPlaceIsUnknown()
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
        Execute("PRAGMA foreign_keys = OFF; DELETE FROM sessions;");

        var shown = db.Pairs(50).Should()
            .ContainSingle("a pair is not less of a pair for losing its session row").Subject;

        shown.RepoPath.Should().BeEmpty("the database does not know, and must not invent");
        shown.HeadSha.Should().Be("aaaa111", "the round is still there");
        shown.Why.Should().Be("it races", "and so is the finding");
    }

    /// <summary>
    /// What the page gained, the send did not — pinned on the TYPES, so a later widening is a red build.
    /// </summary>
    /// <remarks>
    /// <para><c>OnlyFourFieldsLeaveTests</c> guards the wire and constructs <see cref="StoredPair"/>
    /// by name; this is the same promise seen from the page's side. The obvious maintenance move —
    /// one record for both readers — would put a repository path, a file and the reviewers' prose
    /// one edit away from <c>UploadRun.Wire</c>. Named rather than counted, for the reason that file
    /// gives: a count passes when somebody swaps one property for another.</para>
    /// </remarks>
    [Fact]
    public void TheWhereAndTheWhyAreOnThePagesRecord_AndNeverOnTheSends()
    {
        string[] local = ["RepoPath", "HeadSha", "FixSha", "File", "Line", "Why", "Fix"];
        var page = typeof(ReviewPair).GetProperties().Select(one => one.Name);
        var sent = typeof(StoredPair).GetProperties().Select(one => one.Name);

        page.Should().Contain(local);
        sent.Should().NotContain(local, "the send projects StoredPair, and none of this may become reachable from it");
        typeof(RoundsDb).GetMethod(nameof(RoundsDb.Sendable))!.ReturnType
            .Should().Be(typeof(IReadOnlyList<StoredPair>), "the send still reads the narrow record");
    }

    /// <summary>Statements against the file, on a connection of its own — the shell's, not the product's.</summary>
    private void Execute(string sql)
    {
        using var db = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var write = db.CreateCommand();
        write.CommandText = sql;
        write.ExecuteNonQuery();
    }

    /// <summary>A skip has no after, so it has no pair.</summary>
    [Fact]
    public void ASkippedCandidateStoresNothing()
    {
        var id = Seed();
        using var db = Db();

        db.RecordCollect(id, "", "skipped", "language_unsupported", "", "run-1");

        db.Pairs(50).Should().BeEmpty("a skip has no second half to keep");
    }

    /// <summary>
    /// `--all` rewrites the pair and does NOT touch a decision somebody already made.
    /// </summary>
    /// <remarks>
    /// The sharpest failure the plan round found, and two reviewers found it independently: a person
    /// reviews two hundred pairs, reruns `--all` to pick up a repaired walk, and an ordinary upsert
    /// takes every decision back to `-1` without a word. Asserted for a KEPT row and a DROPPED one,
    /// because both are decisions — a guard written `WHERE keep = -1` would preserve the first and
    /// quietly un-drop the second.
    /// </remarks>
    [Theory]
    [InlineData(Keep.Kept)]
    [InlineData(Keep.Dropped)]
    public void ARewrittenPairKeepsTheDecisionSomebodyMade(int decided)
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
        db.RecordKeep([new KeepDecision(id, decided)]).Should().Be(1);

        // A later run, with a repaired walk, producing a better skeleton for the same finding.
        var better = Pair with { SkeletonAfter = "method_1(var_1) { lock { var_2 = 0; } }" };
        db.RecordCollect(id, "collected", "collected", "", "cccc333", "run-2", better);

        var stored = db.Pairs(50).Should().ContainSingle().Subject;
        stored.SkeletonAfter.Should().Contain("var_2", "the pair itself is rewritten");
        stored.Keep.Should().Be(decided, "a person's decision is not the collector's to forget");
    }

    /// <summary>The outcome and the pair are one transaction, so they cannot disagree.</summary>
    /// <remarks>
    /// Proved from the outside: a write that is REFUSED — because another run already claimed the row —
    /// must leave no pair behind either. If the two writes were independent, the pair would land while
    /// the outcome did not, and the database would hold a pair for a finding this run never collected.
    /// </remarks>
    [Fact]
    public void AClaimThatLostLeavesNoPair()
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);

        // A second run that read the row while it was still pending, and only now gets to write.
        var lost = db.RecordCollect(id, "", "collected", "", "dddd444", "run-2", Pair with
        {
            SymbolName = "SomethingElse",
        });

        lost.Should().BeFalse("the row no longer says what that run read");
        db.Pairs(50).Should().ContainSingle().Which.SymbolName.Should()
            .Be("GetOrAdd", "the loser must not have written its pair either");
    }

    /// <summary>
    /// A pair whose finding stops being collected is REMOVED, not left behind.
    /// </summary>
    /// <remarks>
    /// `--all` revisits decided findings, and a repaired walk can conclude that what it called the
    /// fix last time was not one — the outcome goes from `collected` to `skipped`. The pair written
    /// then would otherwise survive with nothing pointing at it, and `Pairs()` would go on offering
    /// it for review. A pair belongs to a collected finding and to no other kind. (CodeRabbit.)
    /// </remarks>
    [Fact]
    public void APairGoesWhenItsFindingStopsBeingCollected()
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
        db.Pairs(50).Should().ContainSingle();

        // The later run decides the earlier fix was not one.
        db.RecordCollect(id, "collected", "skipped", "fix_commit_not_found", "", "run-2");

        db.Pairs(50).Should().BeEmpty(
            "a pair for a finding that is no longer collected is a pair of nothing");
    }

    /// <summary>
    /// The outcome and the pair really do roll back together.
    /// </summary>
    /// <remarks>
    /// <para><b>`AClaimThatLostLeavesNoPair` does not prove this and I thought it did.</b> Its swap
    /// updates zero rows, so `WritePair` is never reached at all — removing `BeginTransaction`
    /// entirely would leave that test green. A reviewer of the pull request said so, and it is the
    /// same shape as every structural assertion that survives its own break.</para>
    /// <para>So the pair write is made to FAIL, with a trigger that aborts it, after the finding
    /// update has already succeeded inside the transaction. The finding must come back unchanged.</para>
    /// </remarks>
    [Fact]
    public void AFailedPairWriteRollsTheOutcomeBackToo()
    {
        var id = Seed();
        using var db = Db();

        using (var trap = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False"))
        {
            trap.Open();
            using var make = trap.CreateCommand();
            make.CommandText = """
                CREATE TRIGGER no_pairs_today BEFORE INSERT ON collect_pairs
                BEGIN SELECT RAISE(ABORT, 'no'); END;
                """;
            make.ExecuteNonQuery();
        }

        var write = () => db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);

        write.Should().Throw<SqliteException>("the trigger refuses the pair");
        Collected(id).Should().BeEmpty(
            "the finding update was in the same transaction and must have gone back with it");
        db.Pairs(50).Should().BeEmpty();
    }

    /// <summary>The finding's own collect_state, straight out of SQLite.</summary>
    private string Collected(long id)
    {
        using var db = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT collect_state FROM findings WHERE id = $id";
        read.Parameters.AddWithValue("$id", id);

        return (string)read.ExecuteScalar()!;
    }

    [Fact]
    public void ADecisionCanBeChangedAndTakenBack()
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);

        db.RecordKeep([new KeepDecision(id, Keep.Kept)]);
        db.Pairs(50)[0].Keep.Should().Be(Keep.Kept);

        db.RecordKeep([new KeepDecision(id, Keep.Dropped)]);
        db.Pairs(50)[0].Keep.Should().Be(Keep.Dropped);

        db.RecordKeep([new KeepDecision(id, Keep.Undecided)]);
        db.Pairs(50)[0].Keep.Should().Be(Keep.Undecided, "a person may put one back to think about");
    }

    /// <summary>A decision about a pair nobody has is not a decision.</summary>
    [Fact]
    public void ADecisionForANonexistentPairChangesNothing()
    {
        Seed();
        using var db = Db();

        db.RecordKeep([new KeepDecision(999_999, Keep.Kept)]).Should().Be(0);
    }

    /// <summary>
    /// A database written before this table gains it, and keeps what it had.
    /// </summary>
    /// <remarks>
    /// Spelled by hand rather than taken from the current <c>Schema</c> constant: a migration test
    /// built from the code it tests follows that code forward and stops testing anything.
    /// </remarks>
    [Fact]
    public void ADatabaseFromBeforeThisTable_GainsIt()
    {
        Directory.CreateDirectory(_dir);
        using (var older = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False"))
        {
            older.Open();
            using var make = older.CreateCommand();
            make.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY, repo_path TEXT NOT NULL,
                    branch TEXT NOT NULL, opened_utc TEXT NOT NULL
                );
                INSERT INTO sessions (id, repo_path, branch, opened_utc)
                VALUES ('s0', 'D:/repo', 'feat/old', '2026-09-01T00:00:00Z');
                """;
            make.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var id = Seed();
        using var db = Db();

        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
        db.Pairs(50).Should().ContainSingle("the table must arrive as its own appended step");
    }

    // ------------------------------------------------------------------------------------------
    // What a person wrote about a pair (story 4.2 of PLAN_a_comment_crosses_the_machine_boundary.md).
    // ------------------------------------------------------------------------------------------

    /// <summary>A collected pair, kept, carrying the given words — the ordinary starting point.</summary>
    private long KeptWith(string comment)
    {
        var id = Seed();
        using var db = Db();
        db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
        db.RecordDecide([new CommentedDecision(id, Keep.Kept, comment)]).Refusal.Should().BeEmpty();

        return id;
    }

    /// <summary>One write carries the keep AND the words, and both read back — by name and by ordinal.</summary>
    /// <remarks>
    /// <c>Sendable</c> reads by ORDINAL, and its own docblock warns what that costs the day the SELECT
    /// is reordered: the wrong column, silently. So the comment is asserted there BY VALUE — an
    /// ordinal off by one answers the finding's title, which is text too and would pass a check that
    /// asked only whether something came back.
    /// </remarks>
    [Fact]
    public void ADecisionWritesTheKeepAndTheWordsTogether()
    {
        KeptWith("it races on the second call");
        using var db = Db();

        var page = db.Pairs(50).Should().ContainSingle().Subject;
        page.Keep.Should().Be(Keep.Kept);
        page.Comment.Should().Be("it races on the second call");
        db.Sendable(10).Should().ContainSingle().Which.Comment.Should().Be(
            "it races on the second call", "the send reads the words by ordinal, and must read THESE");
    }

    /// <summary>A sent pair's words cannot change here — but its keep still can.</summary>
    /// <remarks>
    /// The page makes the box read-only once the server has acknowledged the pair; the store must not
    /// be the way round it, or the local row diverges from what crossed and the page renders the
    /// divergence as sent. The refusal names the pair and never the text, and the WHOLE batch goes:
    /// the decision ahead of the refused one in the same batch is not written either.
    /// </remarks>
    [Fact]
    public void ASentPairsWordsCannotChange_ButItsKeepStillCan()
    {
        var id = KeptWith("the words that crossed");
        using var db = Db();
        db.RecordSendOutcome([new SendOutcome(id, string.Empty, false, "the words that crossed")]);

        var changed = db.RecordDecide([
            new CommentedDecision(id, Keep.Dropped, "the words that crossed"),
            new CommentedDecision(id, Keep.Kept, "new words"),
        ]);

        changed.Decided.Should().Be(0);
        changed.Refusal.Should().Contain($"pair {id}").And.NotContain("new words", "a refusal never quotes a comment");
        var after = db.Pairs(50)[0];
        after.Comment.Should().Be("the words that crossed", "nothing was written");
        after.Keep.Should().Be(Keep.Kept, "not even the decision ahead of the refused one");

        db.RecordDecide([new CommentedDecision(id, Keep.Dropped, "the words that crossed")])
            .Should().Be((1, string.Empty), "unchanged words let a sent pair's keep change");
        db.Pairs(50)[0].Keep.Should().Be(Keep.Dropped);
    }

    /// <summary>The server's sentence about words that did not land is kept with the pair.</summary>
    [Fact]
    public void TheServersSentenceAboutLostWordsIsKeptWithThePair()
    {
        var id = KeptWith("mine");
        using var db = Db();

        db.RecordSendOutcome([new SendOutcome(
            id, "this pair already carries a comment, and the first one stays, so yours was not stored",
            false, "mine")]);

        var pair = db.Pairs(50)[0];
        pair.SentUtc.Should().NotBeEmpty("the pair itself was taken");
        pair.CommentLost.Should().Contain("was not stored", "and the page must be able to say its words were not");
    }

    /// <summary>Words changed while their batch was in the air are said not to have gone.</summary>
    /// <remarks>
    /// A batch is read, the POST takes its time, and the box is still editable because the pair is
    /// not sent yet. The acknowledgement then arrives for the OLD words. Marking the pair sent with the
    /// new text on screen is the silent divergence the read-only box exists to prevent. (Plan round of
    /// 4.2, the local reviewer.)
    /// </remarks>
    [Fact]
    public void WordsChangedWhileTheirBatchWasInTheAirAreSaidNotToHaveGone()
    {
        var id = KeptWith("before the edit");
        using var db = Db();
        var inTheAir = db.Sendable(10)[0];

        db.RecordDecide([new CommentedDecision(id, Keep.Kept, "after the edit")]).Refusal.Should().BeEmpty(
            "the pair is not sent yet, so its words may still change");
        db.RecordSendOutcome([new SendOutcome(id, string.Empty, false, inTheAir.Comment)]);

        var pair = db.Pairs(50)[0];
        pair.SentUtc.Should().NotBeEmpty();
        pair.Comment.Should().Be("after the edit", "what the person wrote is kept, not overwritten");
        pair.CommentLost.Should().Be(RoundsDb.EditedWhileSending);
    }

    /// <summary>Words that were edited in the air AND lost on the server say both, not only the edit.</summary>
    /// <remarks>
    /// The server answered that another comment was there first, and the person changed the box while
    /// the batch was out. Recording only the edit tells them the server holds their OLD words — when it
    /// holds somebody else's. Both facts are true and both are kept. (Code round of 4.2, codex and gemini.)
    /// </remarks>
    [Fact]
    public void WordsEditedInTheAirAndLostOnTheServerSayBoth()
    {
        const string lost = "this pair already carries a comment, and the first one stays, so yours was not stored";
        var id = KeptWith("before the edit");
        using var db = Db();
        var inTheAir = db.Sendable(10)[0];

        db.RecordDecide([new CommentedDecision(id, Keep.Kept, "after the edit")]).Refusal.Should().BeEmpty();
        db.RecordSendOutcome([new SendOutcome(id, lost, false, inTheAir.Comment)]);

        db.Pairs(50)[0].CommentLost.Should().Be(
            $"{lost}; {RoundsDb.EditedAsWell}",
            "the server's reason is the first thing that happened to the words, and the edit the second");
    }

    /// <summary>
    /// A database with pairs, from before comments existed, gains both columns and loses nothing.
    /// </summary>
    /// <remarks>
    /// <para>The state is MADE rather than spelled: seed a pair on today's schema, then take the two
    /// columns away and stamp the file back to twelve steps — which is byte for byte what a database
    /// written by the previous release holds. The reopen must then run step 13 alone, and every pair
    /// must read back as having said nothing. (Plan round of 4.2, codex.)</para>
    /// <para>Opened twice, because the second open is the idempotence half: a file already at thirteen
    /// runs nothing, and a step that ran again would fail on a duplicate column.</para>
    /// </remarks>
    [Fact]
    public void ADatabaseFromBeforeComments_GainsThem_AndEveryPairSaysNothing()
    {
        var id = Seed();
        using (var db = Db())
        {
            db.RecordCollect(id, "", "collected", "", "bbbb222", "run-1", Pair);
            db.RecordKeep([new KeepDecision(id, Keep.Kept)]);
        }

        // Every column a step AFTER twelve added goes too, or the reopen re-runs a later step against a
        // column it already has: step 14 (issue #131) added two to `rounds`, step 15 (the consultation
        // cadence) three more and three to `consultations`, step 16 (the feature stage's skip reason)
        // one more to `rounds`.
        Execute("""
            ALTER TABLE collect_pairs DROP COLUMN comment;
            ALTER TABLE collect_pairs DROP COLUMN comment_lost;
            ALTER TABLE rounds DROP COLUMN commands;
            ALTER TABLE rounds DROP COLUMN plan_shape;
            ALTER TABLE rounds DROP COLUMN plan_key;
            ALTER TABLE rounds DROP COLUMN epic_number;
            ALTER TABLE rounds DROP COLUMN cadence_note;
            ALTER TABLE rounds DROP COLUMN note;
            ALTER TABLE consultations DROP COLUMN kind;
            ALTER TABLE consultations DROP COLUMN plan;
            ALTER TABLE consultations DROP COLUMN epics;
            PRAGMA user_version = 12;
            """);
        SqliteConnection.ClearAllPools();

        for (var open = 0; open < 2; open++)
        {
            using var db = Db();
            var pair = db.Pairs(50).Should().ContainSingle().Subject;
            pair.Keep.Should().Be(Keep.Kept, "the decision survives the step");
            pair.Comment.Should().BeEmpty();
            pair.CommentLost.Should().BeEmpty();
            db.Sendable(10).Should().ContainSingle().Which.Comment.Should().BeEmpty();
            SqliteConnection.ClearAllPools();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
