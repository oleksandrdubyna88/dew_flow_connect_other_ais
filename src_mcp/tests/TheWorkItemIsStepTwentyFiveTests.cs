using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 25: <c>work_item</c> on <c>rounds</c>, <c>consultations</c> and <c>question_consults</c> (coai v2 E3.S8, task t1) — appended after step 24, empty on every
/// row written before it, written by the three projections, and reached by every database shape in the field, as step 23 was
/// (<see cref="ThePlanSourceIsStepTwentyThreeTests"/>).
/// </summary>
public sealed class TheWorkItemIsStepTwentyFiveTests : IDisposable
{
    private const string Item = "v2/epoch-1/e3-x/stories/s8-y/tasks/t1-z";

    private readonly string _dir = Path.Combine(TempDir.Root, "coai-step25-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static int WorkItemStep => Array.IndexOf(Schema.Steps, Schema.TheWorkItem);

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

    private (long Version, bool Rounds, bool Consultations, bool Questions) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return ((long)ask.ExecuteScalar()!, RoundsQuery.HasColumn(db, "rounds", "work_item"),
            RoundsQuery.HasColumn(db, "consultations", "work_item"), RoundsQuery.HasColumn(db, "question_consults", "work_item"));
    }

    private const string OldRows =
        "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'PlanReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');"
        + "INSERT INTO consultations (id, caller, caller_kind, repo_path, branch, head_sha, vendor, model, turns, status, started_utc) VALUES ('c1', 'k', 'claude', 'D:/repo', 'main', 'abc', 'codex', 'm', 1, 'open', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO question_consults (id, caller, started_utc) VALUES ('q1', 'k', '2026-10-01T00:00:00.0000000Z');";

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.PlanReview };

    private IReadOnlyList<string> Query(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var found = new List<string>();
        while (rows.Read())
        {
            found.Add(Convert.ToString(rows.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return found;
    }

    [Fact]
    public void TheWorkItem_IsTheTwentyFifthStep_AppendedNeverInserted()
    {
        WorkItemStep.Should().Be(24, "step 25 by position: step 24 is the round's authors, and a step is never inserted ahead of one that shipped");
        Schema.Steps[24].Should().Be(Schema.TheWorkItem);
    }

    [Fact]
    public void ADatabaseAtStepTwentyFour_OpensAtTheHead_WithEveryOldRowNotAttributed()
    {
        MakeAt(Schema.Steps[..WorkItemStep], OldRows);
        Shape().Should().Be(((long)WorkItemStep, false, false, false), "the fixture is the previous release's database, not today's");

        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull("a database at step 24 migrates forward");
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true, true));
        Query("SELECT work_item FROM rounds").Should().Equal([""], "a round written before the step is attributed to nothing, never to a guess");
        Query("SELECT work_item FROM consultations").Should().Equal([""]);
        Query("SELECT work_item FROM question_consults").Should().Equal([""]);
    }

    [Fact]
    public void ARoundWithAWorkItem_IsWritten_AndTheSameRoundRecordedAgain_KeepsOneRowAndTheNewValue()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(State, new RoundRecord("PlanReview", 1, "proceed", 0, "answered", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow, WorkItem = Item }, []);
            db.RecordRound(State, new RoundRecord("CodeReview", 1, "proceed", 0, "answered", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow }, []);
        }

        Query("SELECT stage || '=' || work_item FROM rounds ORDER BY stage").Should().Equal([$"CodeReview=", $"PlanReview={Item}"], "a round that named no item keeps the empty string");

        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(State, new RoundRecord("PlanReview", 1, "proceed", 0, "answered", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow, WorkItem = Item + "2" }, []);
        }

        Query("SELECT COUNT(*) FROM rounds").Should().Equal(["2"]);
        Query("SELECT work_item FROM rounds WHERE stage = 'PlanReview'").Should().Equal([Item + "2"], "the upsert rewrites the column from the record, as round_key is");
    }

    [Fact]
    public void AConsultation_AndAQuestion_AreWrittenWithTheirItem_AndAFollowUpRowDoesNotMoveThem()
    {
        var consultation = new ConsultationRow("c1", "k", "claude", "D:/repo", "main", "abc", "codex", "m", 1, "open", "", "", "", "2026-10-01T00:00:00Z", "", 1, 1, 1, null, "p", "a", "")
        {
            WorkItem = Item,
        };
        var question = new QuestionConsultRow("q1", "k", "claude", "s", "D:/repo", "main", "abc", "", "q", "c", false, "", "complete", "", "", "2026-10-01T00:00:00Z", "", 1, 1, 1, 1, 1, null, "")
        {
            WorkItem = Item,
        };
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordConsultation(consultation);
            QuestionConsultTable.Record(db, question, []);
            db.RecordConsultation(consultation with { Turns = 2, WorkItem = "another/item" });
            QuestionConsultTable.Record(db, question with { Rows = 2, WorkItem = "another/item" }, []);
        }

        Query("SELECT turns || '=' || work_item FROM consultations").Should().Equal([$"2={Item}"], "the turns advance, the item stays where the first turn put it");
        Query("SELECT rows || '=' || work_item FROM question_consults").Should().Equal([$"2={Item}"]);
    }

    [Fact]
    public void AReadOnlyReader_OnAStepTwentyFourFile_AnswersZeros_AndLeavesItAtTwentyFour()
    {
        MakeAt(Schema.Steps[..WorkItemStep], OldRows);
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(Path.Combine(repo, "v2", "epoch-1"));

        var answer = PlanReportMode.Answer(_dir, repo, Path.Combine(repo, "v2"));

        using var json = System.Text.Json.JsonDocument.Parse(answer);
        json.RootElement.GetProperty("items").EnumerateArray().Should().AllSatisfy(item =>
        {
            item.GetProperty("rounds").GetInt32().Should().Be(0);
            item.GetProperty("cons").GetInt32().Should().Be(0);
        });
        Shape().Should().Be(((long)WorkItemStep, false, false, false), "a reader opened read-only runs no step and leaves the file as it was");
    }

    [Fact]
    public void TheConsultantAndSecurityPreviewDatabases_StillReachTheHead_TheWorkItemStepIncluded()
    {
        MakeAt([.. Schema.Steps[..Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked)], Schema.WhyAConsultationFailed]);

        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull("a database the consultant preview wrote is repaired, then migrated to the head");
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true, true));
    }
}
