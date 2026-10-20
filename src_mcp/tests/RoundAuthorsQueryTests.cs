using System.Security.Cryptography;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// E6.S1: one read-only open of <c>coai.db</c> returns, per round of the touched sessions, everything the report needs and no text column — and
/// a database at step 21, 22 or 23 is READ, not migrated, with its absences as <i>not recorded</i>. Every store here is seeded through the real
/// writers; the older files are the one place hand SQL is allowed (<see cref="RoundAuthorsSeed.OlderFile"/>).
/// </summary>
public sealed class RoundAuthorsQueryTests : IDisposable
{
    private const string Sentinel = "SENTINEL-7f3a";

    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-authors-query-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private IReadOnlyList<RoundFacts> Read(string stage = "CodeReview", DateTime? since = null, DateTime? until = null) =>
        RoundAuthorsQuery.Read(_dir, stage, Stamp(since ?? T0.AddDays(-100)), Stamp(until ?? T0.AddDays(100)));

    private void Seed(Action<RoundsDb> write)
    {
        using var db = Open(_dir);
        write(db);
    }

    [Fact]
    public void ASeededRound_ReadsBackAsItsFacts()
    {
        Seed(db => Round(
            db, "s1", "D:/repo", "feat/x", "CodeReview", 1, T0,
            reviewers: [Done("codex", "gpt-6-luna", "high"), Done("codex", "gpt-6-luna", "high", "Security"), new ReviewerState("gemini", "Ux", ReviewerState.Failed, 0, "", 1, "gemini-3", "")],
            findings: [Found("a"), Found("b", Severity.Blocking), Found("c", Severity.Minor), Found("d", Severity.Minor), Found("e", Severity.Nit)],
            size: Whole(Cell("shown", "code", 30, 10), Cell("shown", "test", 5, 1), Cell("shown", "docs", 7), Binary(2)),
            authors: Snapshot("CodeReview", 1, rows: [Author(AgentKey.MainId, "claude-opus-5-5", Association.SameSession), Author("a1", "claude-sonnet-5-5"), Author("a2", "claude-sonnet-5-5", Association.ChildOf, WindowKind.BeforeOpen)]),
            gating: 1, accept: [0, 2], reject: [3], reRaised: [3]));

        var round = Read().Should().ContainSingle().Subject;

        round.Should().BeEquivalentTo(
            new RoundFacts(
                round.RoundId, "s1", "D:/repo", "feat/x", "CodeReview", 1, "done", "proceed", Stamp(T0), true,
                new AuthorsFacts(
                    AuthorsStateKind.Observed, [], AuthorsSnapshotFixtures.Caller,
                    [
                        new StoredAuthorRow(0, "main", "", "opus", "claude-opus-5-5", "high", "", Association.SameSession, "round", 1, 2, 1),
                        new StoredAuthorRow(1, "subagent", "a1", "opus", "claude-sonnet-5-5", "high", "general-purpose", Association.WroteHere, "round", 1, 2, 1),
                        new StoredAuthorRow(2, "subagent", "a2", "opus", "claude-sonnet-5-5", "high", "general-purpose", Association.ChildOf, "before-open", 1, 2, 1),
                    ],
                    new AmbiguousFacts(true, 0, 0, 0, 0, 0, 0, 0), 0),
                round.Size,
                [
                    new ReviewerFact("codex", "gpt-6-luna", "high", true, "done"),
                    new ReviewerFact("codex", "gpt-6-luna", "high", true, "done"),
                    new ReviewerFact("gemini", "gemini-3", "", true, "failed"),
                ],
                new FindingFacts(All: 5, Severe: 2, GateCount: 1, ReRaised: 1, Accepted: 2, Unresolved: 2, Resolved: true)),
            options => options.WithStrictOrdering());
        round.Size.Recorded.Should().BeTrue();
        round.Size.Size.Whole.Cells.Should().HaveCount(4);
        round.Size.Size.Whole.Cells.Single(cell => cell.Coverage == "binary").Added.Should().BeNull("a binary has no line count, and null is not 0");
    }

    [Fact]
    public void ANeverResolvedRound_IsNotResolved_AndARoundWithNoFinding_ReadsZeros()
    {
        Seed(db =>
        {
            Round(db, "s1", "D:/repo", "b", "CodeReview", 1, T0, findings: [Found("a"), Found("b")]);
            Round(db, "s1", "D:/repo", "b", "CodeReview", 2, T0.AddHours(1));
        });

        var rounds = Read();

        rounds[0].Findings.Should().Be(new FindingFacts(2, 2, 0, 0, 0, Unresolved: 2, Resolved: false), "unresolved is every finding when resolve never ran");
        rounds[1].Findings.Should().Be(new FindingFacts(0, 0, 0, 0, 0, 0, false), "no finding has no grouped row: all zeros in memory, never a missing key");
    }

    [Fact]
    public void OutOfWindowRoundsOfATouchedSession_AreRead_AndUntouchedSessionsAreNot()
    {
        Seed(db =>
        {
            Round(db, "A", "D:/a", "b", "CodeReview", 1, T0.AddDays(-30));
            Round(db, "A", "D:/a", "b", "CodeReview", 2, T0.AddDays(-1));
            Round(db, "B", "D:/b", "b", "CodeReview", 1, T0.AddDays(-30));
        });

        var rounds = Read(since: T0.AddDays(-7), until: T0);

        rounds.Select(round => (round.SessionId, round.Number, round.InWindow)).Should().Equal([("A", 1, false), ("A", 2, true)]);
    }

    [Fact]
    public void AWindowIsHalfOpen_ARoundAtSinceIsIn_AtUntilIsOut()
    {
        Seed(db =>
        {
            Round(db, "s", "D:/a", "b", "CodeReview", 1, T0);
            Round(db, "s", "D:/a", "b", "CodeReview", 2, T0.AddHours(1));
        });

        var rounds = Read(since: T0, until: T0.AddHours(1));

        rounds.Select(round => (round.Number, round.InWindow)).Should().Equal([(1, true), (2, false)]);
    }

    [Fact]
    public void ThePlanRoundsOfASession_AreContextWhenTheCodeStageIsAsked()
    {
        Seed(db =>
        {
            Round(db, "s", "D:/a", "b", "PlanReview", 1, T0);
            Round(db, "s", "D:/a", "b", "CodeReview", 1, T0.AddHours(1));
        });

        Read("CodeReview").Select(round => (round.Stage, round.InWindow)).Should().BeEquivalentTo([("PlanReview", false), ("CodeReview", true)]);
        Read("PlanReview").Select(round => (round.Stage, round.InWindow)).Should().BeEquivalentTo([("PlanReview", true), ("CodeReview", false)]);
    }

    [Fact]
    public void AStepTwentyOneFile_IsReadNotMigrated_WithItsAbsencesAsNotRecorded()
    {
        var stopBefore = StepIndex(Schema.TheRoundsSize);
        OlderFile(_dir, stopBefore, OlderRound("s", "D:/r", "b", T0));
        var (bytes, version) = Fingerprint();

        var round = Read().Should().ContainSingle().Subject;

        round.Authors.State.Should().Be(AuthorsStateKind.NotRecorded);
        round.Size.Recorded.Should().BeFalse();
        round.Reviewers.Should().ContainSingle().Which.EffortRecorded.Should().BeFalse("effort arrives with step 22");
        Fingerprint().Should().Be((bytes, version), "a reader that migrates is a writer");
        version.Should().Be(stopBefore);
    }

    [Fact]
    public void AStepTwentyTwoFileAndAStepTwentyThreeFile_AreReadWithTheirSizes_AndNoAuthors()
    {
        var sizeSql = "UPDATE rounds SET size_state = 'recorded', delta_state = 'unavailable: first round' WHERE id = 1;"
            + "INSERT INTO round_changes (round_id, scope, coverage, kind, files, added, removed) VALUES (1, 'whole', 'shown', 'code', 2, 12, 3);";

        foreach (var step in new[] { Schema.ThePlanSource, Schema.TheRoundsAuthors })
        {
            using var dir = TempDir.For("testsToDelete-coai-authors-query-old-");
            var stopBefore = StepIndex(step);
            OlderFile(dir, stopBefore, OlderRound("s", "D:/r", "b", T0, ", effort", ", ''") + sizeSql);
            var before = Fingerprint(dir);

            var round = RoundAuthorsQuery.Read(dir, "CodeReview", Stamp(T0.AddDays(-1)), Stamp(T0.AddDays(1))).Should().ContainSingle().Subject;

            round.Authors.State.Should().Be(AuthorsStateKind.NotRecorded, $"a file before step 24 ({stopBefore}) has no authors");
            round.Size.Recorded.Should().BeTrue();
            round.Size.Size.Whole.Cells.Should().ContainSingle().Which.Added.Should().Be(12);
            round.Reviewers.Single().EffortRecorded.Should().BeTrue("step 22 added the column");
            Fingerprint(dir).Should().Be(before, "the step-23 file is today's released store, and it is read, not migrated");
        }
    }

    [Fact]
    public void AMalformedStoredState_ReadsNotRecorded_ForThatRoundOnly()
    {
        Seed(db =>
        {
            Round(db, "s", "D:/a", "b", "CodeReview", 1, T0, authors: Snapshot("CodeReview", 1, rows: [Author("a1", "claude-sonnet-5-5")]));
            Round(db, "s", "D:/a", "b", "CodeReview", 2, T0.AddHours(1), authors: Snapshot("CodeReview", 2, rows: [Author("a1", "claude-sonnet-5-5", number: 2)]));
            Round(db, "s", "D:/a", "b", "CodeReview", 3, T0.AddHours(2), authors: Snapshot("CodeReview", 3, rows: [Author("a1", "claude-sonnet-5-5", number: 3)]));
        });
        Raw(_dir, "UPDATE rounds SET authors_state = 'done' WHERE number = 1; UPDATE rounds SET authors_state = 'partial' WHERE number = 2;");

        var rounds = Read();

        rounds.Select(round => round.Authors.State).Should().Equal([AuthorsStateKind.NotRecorded, AuthorsStateKind.NotRecorded, AuthorsStateKind.Observed]);
        rounds[0].Authors.Rows.Should().BeEmpty("a state the shared rule refuses is a state somebody else wrote: no row of it is trusted");
        rounds[2].Authors.Rows.Should().ContainSingle();
    }

    [Fact]
    public void AnUnknownAmbiguousTotal_IsNotAZero_AndAnUnknownWriteCountIsKeptAsStored()
    {
        Seed(db =>
        {
            Round(db, "s", "D:/a", "b", "CodeReview", 1, T0, authors: AuthorsSnapshotFixtures.Unobserved("CodeReview", 1));
            Round(db, "s", "D:/a", "b", "CodeReview", 2, T0.AddHours(1), authors: AuthorsSnapshot.Unknown("CodeReview", 2, AuthorsSnapshotFixtures.Caller, ObservationKinds.Partial, ["read budget"], CallerSessionId.Registry, 1, 0));
        });

        var rounds = Read();

        rounds[0].Authors.State.Should().Be(AuthorsStateKind.Unobserved);
        rounds[0].Authors.Ambiguous.Known.Should().BeFalse("E5 wrote -1 in every ambiguous column");
        rounds[0].Authors.Ambiguous.Should().Be(AmbiguousFacts.Unknown, "no -1 survives into a field a sum could take");
        rounds[1].Authors.State.Should().Be(AuthorsStateKind.Partial);
        rounds[1].Authors.Reasons.Should().Equal(["read budget"]);
        rounds[1].Authors.UncoveredWrites.Should().Be(-1, "unknown coverage is read as stored, never clamped to a covered 0 (D12)");
        rounds[1].Authors.CallerSessionId.Should().Be(AuthorsSnapshotFixtures.Caller);
    }

    [Fact]
    public void MainAndASubagent_MapThroughTheOneInverse_AndSkippedOrInterruptedRoundsHaveNoState()
    {
        Seed(db =>
        {
            Round(db, "s", "D:/a", "b", "CodeReview", 1, T0, authors: Snapshot("CodeReview", 1, rows: [Author(AgentKey.MainId, "claude-opus-5-5"), Author("a1", "claude-sonnet-5-5")]));
            Round(db, "s", "D:/a", "b", "CodeReview", 2, T0.AddHours(1), verdict: RoundRecord.Skipped);
            Round(db, "s", "D:/a", "b", "CodeReview", 3, T0.AddHours(2), status: RoundRecord.Interrupted);
        });

        var rounds = Read();
        var mapped = StoredAuthors.Of(rounds[0]);

        mapped.Rows.Select(row => row.Agent).Should().Equal([RoundAuthorsTable.AgentKeyOf(string.Empty), RoundAuthorsTable.AgentKeyOf("a1")]);
        mapped.Rows[0].Agent.Should().Be(AgentKey.Main);
        mapped.CallerSessionId.Should().Be(AuthorsSnapshotFixtures.Caller);
        mapped.Elsewhere.Should().Be(ActivityTotals.Unknown, "a member step 24 does not project takes AuthorsSnapshot.Unknown's value, as the heal does");
        mapped.UnobservedLaunches.Should().Be(-1);
        mapped.Windows.Should().BeEmpty();
        rounds[1].Authors.Should().Be(AuthorsFacts.NotRecorded);
        rounds[2].Authors.Should().Be(AuthorsFacts.NotRecorded);
    }

    [Fact]
    public void AMissingDatabase_IsAnEmptyAnswer_AndNoFileIsCreated()
    {
        Read().Should().BeEmpty();

        File.Exists(_dir.At(RoundsDb.FileName)).Should().BeFalse();
    }

    [Fact]
    public void NoTextColumnCrosses_AndTheProbeSeesStrings()
    {
        Seed(db => Round(
            db, "s", "D:/a", "branch-" + Sentinel.ToLowerInvariant(), "CodeReview", 1, T0,
            reviewers: [Done("codex", "gpt-6-luna")],
            findings: [new Finding(Severity.Major, Category.Security, Sentinel + ".cs", 1, Sentinel + " title", Sentinel + " why", Sentinel + " fix", ["codex"])],
            reject: [0]));
        Raw(_dir, $"UPDATE rounds SET plan_text = '{Sentinel}', agent_log = '{Sentinel}', subject = '{Sentinel}', note = '{Sentinel}', commands = '{Sentinel}';");

        var round = Read().Should().ContainSingle().Subject;

        round.ToString().Should().NotContain(Sentinel, "the facts carry ids, counts and closed words only");
        round.Branch.Should().Contain(Sentinel.ToLowerInvariant(), "the companion: the probe DOES see a string the facts keep, so its silence above means something");
    }

    [Fact]
    public void TheWholeReadIsOneSnapshot_ARoundRewrittenBetweenTwoQueriesIsSeenWholeOrNotAtAll()
    {
        Seed(db => Round(db, "s", "D:/a", "b", "CodeReview", 1, T0, findings: [Found("a")], gating: 0));
        Thread? writing = null;

        var rounds = RoundAuthorsQuery.Read(_dir, "CodeReview", Stamp(T0.AddDays(-1)), Stamp(T0.AddDays(1)), between: () =>
        {
            // The round is re-recorded with three findings and a gate count of two — a RecordRound writes the round row and its findings in one transaction.
            writing = new Thread(() => Seed(db => Round(db, "s", "D:/a", "b", "CodeReview", 1, T0, findings: [Found("x"), Found("y"), Found("z")], gating: 2)));
            writing.Start();
            writing.Join(TimeSpan.FromMilliseconds(750));
        });
        writing!.Join(TimeSpan.FromSeconds(20)).Should().BeTrue("the writer lands once the reader has ended its transaction");

        rounds.Should().ContainSingle().Which.Findings.Should().Be(
            new FindingFacts(All: 1, Severe: 1, GateCount: 0, ReRaised: 0, Accepted: 0, Unresolved: 1, Resolved: false),
            "the round row and its findings are the SAME moment: the old gate count with the old findings, never the old row with the new findings");
        Read().Single().Findings.All.Should().Be(3, "a later read sees the rewrite whole");
    }

    private (string Hash, long Version) Fingerprint() => Fingerprint(_dir);

    private static (string Hash, long Version) Fingerprint(string dir)
    {
        SqliteConnection.ClearAllPools();
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, RoundsDb.FileName))));
        using var check = new SqliteConnection($"Data Source={Path.Combine(dir, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        check.Open();
        using var ask = check.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return (hash, (long)ask.ExecuteScalar()!);
    }
}
