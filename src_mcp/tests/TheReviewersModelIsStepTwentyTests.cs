using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 20: <c>reviewers.model</c> and the <c>reviewers(round_id)</c> index (story 2.1 of
/// <c>todo/PLAN_every_round_is_counted.md</c>) — appended, filled on every recorded round, empty on
/// every reviewer row written before it, and reached by every database shape in the field.
/// </summary>
/// <remarks>
/// <para>The reviewers table never recorded WHICH model a launch ran (<c>Schema.Tables</c>), so the round
/// counter could not say that the Gemini row's step to <c>flash-medium</c> on 2026-10-02 is where the
/// weekly quota started running out. <c>ReviewerState.Model</c> has carried it in the session file since
/// the log named the model; this is the projection catching up.</para>
/// <para>Real SQLite over a temp directory. The two preview-fork shapes are built exactly as
/// <c>AConsultantPreviewDatabaseKeepsEveryStepTests</c> and <c>ASecurityPreviewDatabaseKeepsBothStepsTests</c>
/// build them, because a step appended after those repairs has to be reached THROUGH them.</para>
/// </remarks>
public sealed class TheReviewersModelIsStepTwentyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-step20-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static int ModelStep => Array.FindIndex(Schema.Steps, step => step.Contains("ALTER TABLE reviewers ADD COLUMN model", StringComparison.Ordinal));

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

    private (long Version, bool Model, bool Index, bool Questions) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        var version = (long)ask.ExecuteScalar()!;
        using var index = db.CreateCommand();
        index.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'reviewers_by_round' AND tbl_name = 'reviewers'";
        return (version,
            RoundsQuery.HasColumn(db, "reviewers", "model"),
            (long)index.ExecuteScalar()! == 1,
            RoundsQuery.HasColumn(db, "question_consults", "id"));
    }

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true, true));
    }

    [Fact]
    public void TheModelColumnAndTheIndex_AreTheTwentiethStep_AppendedNeverInserted()
    {
        ModelStep.Should().Be(19, "step 20 by position: step 19 is why a consultation failed, and a step is never inserted ahead of one that shipped");
        Schema.Steps[19].Should().Contain("CREATE INDEX IF NOT EXISTS reviewers_by_round ON reviewers (round_id)", "the counter joins rounds to reviewers by round, and Tables has no such index");
        Schema.Steps.Length.Should().Be(20);
    }

    [Fact]
    public void ADatabaseAtStepNineteen_GainsTheColumnAndTheIndex_AndItsOlderReviewersReadAnEmptyModel()
    {
        MakeAt(Schema.Steps[..ModelStep],
            "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
            + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');"
            + "INSERT INTO reviewers (round_id, provider, role, status) VALUES (1, 'codex', 'Architecture', 'done');");
        Shape().Should().Be(((long)ModelStep, false, false, true), "the fixture is the previous release's database, not today's");

        OpensAndEndsWithEveryStep("the ordinary path: a database at step 19 migrates forward");

        Query("SELECT model FROM reviewers").Should().Equal([""], "a reviewer recorded before the column exists recorded no model, and empty is what says so");
    }

    [Fact]
    public void ARecordedReviewer_CarriesItsModel()
    {
        var state = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(state, new RoundRecord("CodeReview", 1, "proceed", 0, "answered", DateTime.UtcNow)
            {
                StartedUtc = DateTime.UtcNow,
                ReviewerStates =
                [
                    new ReviewerState("codex", "Architecture", ReviewerState.Done, 0, "", 1.0, "gpt-6-luna"),
                    new ReviewerState("gemini", "Architecture", ReviewerState.Done, 0, "", 1.0, "gemini-3.8-flash-medium"),
                    new ReviewerState("local", "Architecture", ReviewerState.StoodDown),
                ],
            }, []);
        }

        Query("SELECT provider || '=' || model FROM reviewers ORDER BY provider")
            .Should().Equal(["codex=gpt-6-luna", "gemini=gemini-3.8-flash-medium", "local="], "the model as the launch ran it; a reviewer with none writes none");
    }

    [Fact]
    public void TheConsultantPreviewDatabase_StillReachesTheHead_ModelColumnIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Version.Should().Be(17, "the consultant preview's own stamp");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote is repaired, then migrated to step 20");
    }

    [Fact]
    public void TheSecurityPreviewDatabase_StillReachesTheHead_ModelColumnIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Version.Should().Be(17, "the security preview's own stamp");

        OpensAndEndsWithEveryStep("a database the security preview wrote is repaired, then migrated to step 20");
    }

    [Fact]
    public void AMigratedDatabase_OpensAgainUnchanged()
    {
        MakeAt(Schema.Steps[..ModelStep]);
        using (RoundsDb.Open(_dir, _log)) { }

        OpensAndEndsWithEveryStep("the ALTER ran once; a second open is the ordinary path at the head and re-runs nothing");
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
