using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 23: <c>rounds.plan_source</c> (E2.S1 of <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E2.md</c>,
/// contract C1) — appended after step 22, written by <c>RecordRound</c> from the record's <c>PlanSource</c>, empty on every
/// round written before it and on every round that is not a plan round, and reached by every database shape in the field —
/// as step 22 was (<see cref="TheRoundsSizeIsStepTwentyTwoTests"/>).
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory; the two preview-fork shapes are built as step 22's test builds them. A read-only
/// reader on a step-22 file must still read, and leave the file at twenty-two. The value is rewritten from the record on
/// every re-record, as <c>round_key</c> is — it has no separate measurement to protect.</para>
/// </remarks>
public sealed class ThePlanSourceIsStepTwentyThreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-step23-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static int PlanSourceStep => Array.IndexOf(Schema.Steps, Schema.ThePlanSource);

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

    private (long Version, bool Column) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return ((long)ask.ExecuteScalar()!, RoundsQuery.HasColumn(db, "rounds", "plan_source"));
    }

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true));
    }

    private const string OneOldRound =
        "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'PlanReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');";

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.PlanReview };

    private static readonly PlanSource Committed = new(
        PlanOrigin.Commit, new string('a', 40), "todo/PLAN_x.md", new string('b', 40), 98304, "0123456789abcdef");

    private static readonly PlanSource Pasted = new(PlanOrigin.Text, new string('c', 40), "", "", 512, "fedcba9876543210");

    private static RoundRecord Round(string stage, int number, PlanSource? source = null) =>
        new(stage, number, "proceed", 0, "answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow,
            PlanSource = source,
        };

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State, round, []);
        }
    }

    [Fact]
    public void ThePlanSource_IsTheTwentyThirdStep_AppendedNeverInserted()
    {
        PlanSourceStep.Should().Be(22, "step 23 by position: step 22 is the round's size, and a step is never inserted ahead of one that shipped");
        Schema.Steps.Length.Should().BeGreaterThanOrEqualTo(23);
        Schema.Steps[22].Should().Be(Schema.ThePlanSource);
    }

    [Fact]
    public void ADatabaseAtStepTwentyTwo_OpensAtTheHead_WithEveryRowNotRecorded_ThenRecordsAPlanRound()
    {
        MakeAt(Schema.Steps[..PlanSourceStep], OneOldRound);
        Shape().Should().Be(((long)PlanSourceStep, false), "the fixture is the previous release's database, not today's");

        OpensAndEndsWithEveryStep("the ordinary path: a database at step 22 migrates forward");
        Query("SELECT plan_source FROM rounds").Should().Equal([""], "a round written before the step has no source: empty, never a made-up one");

        Record(Round("PlanReview", 2, Committed));

        Query("SELECT number || '=' || (plan_source <> '') FROM rounds ORDER BY number")
            .Should().Equal(["1=0", "2=1"], "the migrated file takes the new round's source and leaves the old round as it was");
    }

    [Fact]
    public void ACommittedPlanRound_WritesItsSourceAsJson()
    {
        Record(Round("PlanReview", 1, Committed));

        var stored = Query("SELECT plan_source FROM rounds").Should().ContainSingle().Subject;
        JsonSerializer.Deserialize(stored, ServerJsonContext.Default.PlanSource).Should().Be(Committed, "the column is the record's own JSON");
        using var doc = JsonDocument.Parse(stored);
        doc.RootElement.GetProperty("origin").GetString().Should().Be("commit", "camelCase, as the session file writes it");
        doc.RootElement.GetProperty("textId").GetString().Should().Be("0123456789abcdef");
    }

    [Fact]
    public void APastedPlanRound_WritesOriginText()
    {
        Record(Round("PlanReview", 1, Pasted));

        var stored = Query("SELECT plan_source FROM rounds").Should().ContainSingle().Subject;
        var source = JsonSerializer.Deserialize(stored, ServerJsonContext.Default.PlanSource)!;
        source.Origin.Should().Be(PlanOrigin.Text);
        source.Should().Be(Pasted, "a pasted plan keeps its text id, its byte count and the engine's own commit");
    }

    [Fact]
    public void ACodeRound_WritesNothing()
    {
        Record(Round("CodeReview", 1));

        Query("SELECT plan_source FROM rounds").Should().Equal([""], "only a plan round has a source; empty says not recorded");
    }

    [Fact]
    public void TheSameRoundRecordedTwice_KeepsOneRowAndOneValue()
    {
        var second = Committed with { Bytes = 7, Path = "todo/PLAN_y.md" };
        Record(Round("PlanReview", 1, Committed));
        Record(Round("PlanReview", 1, second));

        Query("SELECT COUNT(*) FROM rounds").Should().Equal(["1"]);
        var stored = Query("SELECT plan_source FROM rounds").Should().ContainSingle().Subject;
        JsonSerializer.Deserialize(stored, ServerJsonContext.Default.PlanSource).Should().Be(second,
            "the upsert rewrites the column from the record, as round_key is rewritten — it is the ON CONFLICT entry that is pinned here");
    }

    [Fact]
    public void AReadOnlyReader_OnAStepTwentyTwoFile_LeavesItAtTwentyTwo()
    {
        MakeAt(Schema.Steps[..PlanSourceStep], OneOldRound);

        var counts = RoundCounts.Read(_dir, new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));

        counts.Windows.Should().NotBeEmpty("the counter reads a file without the column — it never asks for it");
        Shape().Should().Be(((long)PlanSourceStep, false), "a reader opened read-only runs no step and leaves the file byte for byte");
    }

    [Fact]
    public void TheConsultantPreviewDatabase_StillReachesTheHead_PlanSourceStepIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Version.Should().Be(17, "the consultant preview's own stamp");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote is repaired, then migrated to step 23");
    }

    [Fact]
    public void TheSecurityPreviewDatabase_StillReachesTheHead_PlanSourceStepIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Version.Should().Be(17, "the security preview's own stamp");

        OpensAndEndsWithEveryStep("a database the security preview wrote is repaired, then migrated to step 23");
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
