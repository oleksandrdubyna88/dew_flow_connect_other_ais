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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
