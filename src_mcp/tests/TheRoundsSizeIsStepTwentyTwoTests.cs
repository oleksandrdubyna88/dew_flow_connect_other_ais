using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 22: <c>round_changes</c>, the size states on <c>rounds</c> and <c>reviewers.effort</c> (E2.S3 of
/// <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>, contract C2) — appended after step 21, written
/// by <c>RecordRound</c> in its own transaction from the record's <c>Size</c> and <c>Gitlinks</c>, empty on every round
/// written before it, and reached by every database shape in the field — as step 21 was
/// (<see cref="TheRoundKeyIsStepTwentyOneTests"/>).
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory; the two preview-fork shapes are built as step 21's test builds them. A
/// read-only reader (<c>--round-counts</c>) on a step-21 file must still read, and leave the file at twenty-one. A binary
/// has no line count: NULL in the database, absent in JSON, never 0.</para>
/// </remarks>
public sealed class TheRoundsSizeIsStepTwentyTwoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-step22-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static int SizeStep => Array.FindIndex(Schema.Steps, step => step.Contains("CREATE TABLE IF NOT EXISTS round_changes", StringComparison.Ordinal));

    private static int QuestionsStep => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked);

    private void MakeAt(IEnumerable<string> steps, string alsoRun = "")
    {
        Directory.CreateDirectory(_dir);
        var list = steps.ToArray();
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", list) + $"; PRAGMA user_version={list.Length};" + alsoRun;
        make.ExecuteNonQuery();
    }

    private (long Version, bool Table, bool SizeState, bool Effort) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        return ((long)ask.ExecuteScalar()!,
            RoundsQuery.HasColumn(db, "round_changes", "round_id"),
            RoundsQuery.HasColumn(db, "rounds", "size_state"),
            RoundsQuery.HasColumn(db, "reviewers", "effort"));
    }

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true, true));
    }

    private const string OneOldRound =
        "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');"
        + "INSERT INTO reviewers (round_id, provider, role, status, model) VALUES (1, 'codex', 'Architecture', 'done', 'gpt-6-luna');";

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static RoundRecord Round(int number, RoundSize? size = null, int? gitlinks = null, params ReviewerState[] reviewers) =>
        new("CodeReview", number, "proceed", 0, "answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow,
            Size = size,
            Gitlinks = gitlinks,
            ReviewerStates = [.. reviewers],
        };

    private static RoundSize AFullSize() => new(
        new ScopeSize(ScopeState.Recorded, "origin/main",
        [
            new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 3, 40, 5),
            new SizeCell(ChangeCoverage.Excluded, ChangeKind.Config, 1, 100, 20),
            new SizeCell(ChangeCoverage.Binary, ChangeKind.Unknown, 2, null, null),
        ]),
        new ScopeSize(ScopeState.Recorded, "abc123", [new SizeCell(ChangeCoverage.Shown, ChangeKind.Test, 1, 7, 0)]));

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State, round, []);
        }
    }

    [Fact]
    public void TheRoundsSize_IsTheTwentySecondStep_AppendedNeverInserted()
    {
        SizeStep.Should().Be(21, "step 22 by position: step 21 is the round's key, and a step is never inserted ahead of one that shipped");
        Schema.Steps.Length.Should().BeGreaterThanOrEqualTo(22);
        Schema.Steps[21].Should().Be(Schema.TheRoundsSize);
    }

    [Fact]
    public void ADatabaseAtStepTwentyOne_GainsTheTableAndColumns_AndItsOlderRoundsReadNotRecorded()
    {
        MakeAt(Schema.Steps[..SizeStep], OneOldRound);
        Shape().Should().Be(((long)SizeStep, false, false, false), "the fixture is the previous release's database, not today's");

        OpensAndEndsWithEveryStep("the ordinary path: a database at step 21 migrates forward");

        Query("SELECT size_state || '|' || delta_state || '|' || delta_from || '|' || size_origin || '|' || size_gitlinks FROM rounds")
            .Should().Equal(["||||-1"], "a round written before the step has no size: empty states, no origin, gitlinks -1");
        Query("SELECT COUNT(*) FROM round_changes").Should().Equal(["0"], "no rows for a round that measured nothing — never a zero-line change");
        Query("SELECT effort FROM reviewers").Should().Equal(["not recorded"], "a reviewer row written before the step has no recorded effort (parent D11)");
    }

    [Fact]
    public void ARecordedRound_WritesOneRowPerCell_AndABinaryHasNoLineCount()
    {
        Record(Round(1, AFullSize()));

        Query("SELECT scope || '/' || coverage || '/' || kind || '/' || files || '/' || IFNULL(added, 'NULL') || '/' || IFNULL(removed, 'NULL') FROM round_changes ORDER BY rowid")
            .Should().Equal(
                ["whole/shown/code/3/40/5", "whole/excluded/config/1/100/20", "whole/binary/unknown/2/NULL/NULL", "delta/shown/test/1/7/0"],
                "one row per cell per scope, in the cells' own order; the binary's lines are NULL, and a measured zero stays 0");
        Query("SELECT COUNT(*) FROM round_changes WHERE added IS NULL OR removed IS NULL").Should().Equal(["1"], "only the binary has no line count");
        Query("SELECT size_state || '|' || delta_state || '|' || delta_from || '|' || size_origin FROM rounds")
            .Should().Equal(["recorded|recorded|abc123|live"]);
    }

    [Fact]
    public void TheGitlinkCount_IsWrittenWithTheSize_AndMinusOneWhenNotRecorded()
    {
        Record(Round(1, AFullSize(), gitlinks: 2), Round(2, AFullSize(), gitlinks: null), Round(3, size: null, gitlinks: 5));

        Query("SELECT number || '=' || size_gitlinks FROM rounds ORDER BY number")
            .Should().Equal(["1=2", "2=-1", "3=-1"],
                "the count rides with the size: 2 as measured; null with a size is -1; and a count without a size is never written");
    }

    [Fact]
    public void TheGitlinkCount_RoundTripsThroughTheSessionFile_AndOneWithoutReadsNull()
    {
        var with = new PersistedSession(State, [Round(1, AFullSize(), gitlinks: 2)]);
        var without = new PersistedSession(State, [Round(1)]);

        var jsonWith = JsonSerializer.Serialize(with, ServerJsonContext.Default.PersistedSession);
        var jsonWithout = JsonSerializer.Serialize(without, ServerJsonContext.Default.PersistedSession);

        JsonSerializer.Deserialize(jsonWith, ServerJsonContext.Default.PersistedSession)!.Rounds[0].Gitlinks.Should().Be(2);
        jsonWithout.Should().NotContain("gitlinks", "a null count is not written — absent says not recorded");
        JsonSerializer.Deserialize(jsonWithout, ServerJsonContext.Default.PersistedSession)!.Rounds[0].Gitlinks.Should().BeNull();
    }

    [Fact]
    public void AnUnavailableDelta_IsAStateNotAnEmptyResult()
    {
        var unavailable = new RoundSize(
            new ScopeSize(ScopeState.Recorded, "origin/main", [new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 1, 3, 1)]),
            ScopeSize.Unavailable(ScopeState.FirstRound));
        var zero = new RoundSize(
            new ScopeSize(ScopeState.Recorded, "origin/main", [new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 1, 3, 1)]),
            ScopeSize.ObservedZero("abc123"));

        Record(Round(1, unavailable), Round(2, zero));

        Query("SELECT number || '=' || delta_state || '|' || delta_from FROM rounds ORDER BY number")
            .Should().Equal(["1=unavailable: first round|", "2=recorded|abc123"],
                "no delta rows either way, and the state is the only thing that tells a refusal from an observed zero");
        Query("SELECT COUNT(*) FROM round_changes WHERE scope = 'delta'").Should().Equal(["0"]);
    }

    [Fact]
    public void TheSameRoundRecordedTwice_HasTheSameRows()
    {
        Record(Round(1, AFullSize(), gitlinks: 2));
        Record(Round(1, AFullSize(), gitlinks: 2));

        Query("SELECT COUNT(*) FROM round_changes").Should().Equal(["4"], "a re-record replaces the round's rows; it never doubles them");
        Query("SELECT COUNT(*) FROM rounds").Should().Equal(["1"]);
    }

    [Fact]
    public void AReRecordWithoutASize_LeavesTheEarlierRowsAndStatesUntouched()
    {
        Record(Round(1, AFullSize(), gitlinks: 2));
        Record(Round(1));

        Query("SELECT COUNT(*) FROM round_changes").Should().Equal(["4"], "a backfilled size survives a live re-record that measured nothing (parent D14)");
        Query("SELECT size_state || '|' || delta_state || '|' || delta_from || '|' || size_origin || '|' || size_gitlinks FROM rounds")
            .Should().Equal(["recorded|recorded|abc123|live|2"]);
    }

    [Fact]
    public void AReviewersEffort_IsPersisted_AndOneWithoutReadsEmpty()
    {
        Record(Round(1, null, null,
            new ReviewerState("codex", "Architecture", ReviewerState.Done, Model: "gpt-6-luna", Effort: "high"),
            new ReviewerState("claude", "Security", ReviewerState.Done, Model: "opus")));

        Query("SELECT provider || '=' || effort FROM reviewers ORDER BY provider")
            .Should().Equal(["claude=", "codex=high"],
                "the effort the launch applied; a reviewer that applied none is empty, never 'not recorded'");
    }

    [Fact]
    public void AReadOnlyReader_OnAStepTwentyOneFile_StillReads_AndLeavesItAtTwentyOne()
    {
        MakeAt(Schema.Steps[..SizeStep], OneOldRound);

        var counts = RoundCounts.Read(_dir, new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));

        counts.Windows.Should().NotBeEmpty("the counter reads a file without the table — it never asks for it");
        Shape().Should().Be(((long)SizeStep, false, false, false), "a reader opened read-only runs no step and leaves the file byte for byte");
    }

    [Fact]
    public void TheConsultantPreviewDatabase_StillReachesTheHead_SizeStepIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Version.Should().Be(17, "the consultant preview's own stamp");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote is repaired, then migrated to step 22");
    }

    [Fact]
    public void TheSecurityPreviewDatabase_StillReachesTheHead_SizeStepIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Version.Should().Be(17, "the security preview's own stamp");

        OpensAndEndsWithEveryStep("a database the security preview wrote is repaired, then migrated to step 22");
    }

    [Fact]
    public void AMigratedDatabase_OpensAgainUnchanged()
    {
        MakeAt(Schema.Steps[..SizeStep]);
        using (RoundsDb.Open(_dir, _log)) { }

        OpensAndEndsWithEveryStep("the ALTERs ran once; a second open is the ordinary path at the head and re-runs nothing");
    }

    [Fact]
    public void ASessionFileWithoutASize_RoundTripsWithoutOne()
    {
        var session = new PersistedSession(State, [Round(1)]);

        var json = JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession);
        var loaded = JsonSerializer.Deserialize(json, ServerJsonContext.Default.PersistedSession)!;
        var again = JsonSerializer.Serialize(loaded, ServerJsonContext.Default.PersistedSession);

        loaded.Rounds[0].Size.Should().BeNull("a round written before the field says nothing about a size");
        again.Should().NotContain("\"size\"", "the save must not materialise a zero-line size for a round that never measured one");
    }

    [Fact]
    public void ASessionFileWithABinaryCell_RoundTripsWithTheLineCountsAbsent()
    {
        var session = new PersistedSession(State, [Round(1, AFullSize())]);

        var json = JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession);
        var binary = JsonDocument.Parse(json).RootElement.GetProperty("rounds")[0].GetProperty("size")
            .GetProperty("whole").GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("coverage").GetString() == ChangeCoverage.Binary);
        var loaded = JsonSerializer.Deserialize(json, ServerJsonContext.Default.PersistedSession)!.Rounds[0].Size!;
        var loadedBinary = loaded.Whole.Cells.Single(cell => cell.Coverage == ChangeCoverage.Binary);

        binary.TryGetProperty("added", out _).Should().BeFalse("a binary has no line count: the member is absent, never 0");
        binary.TryGetProperty("removed", out _).Should().BeFalse();
        loadedBinary.Added.Should().BeNull();
        loadedBinary.Removed.Should().BeNull();
        loaded.Whole.Cells.Single(cell => cell.Coverage == ChangeCoverage.Shown).Added.Should().Be(40);
    }

    private IReadOnlyList<string> Query(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var values = new List<string>();
        while (rows.Read())
        {
            values.Add(rows.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }
}
