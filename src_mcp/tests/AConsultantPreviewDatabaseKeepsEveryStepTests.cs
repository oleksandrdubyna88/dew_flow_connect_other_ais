using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 17 was numbered on a third branch too. The consultant-works-everywhere preview builds (2026-10-02/03,
/// run against REAL data directories) stamped <c>user_version = 17</c> for <c>consultations.failure_kind</c>,
/// <c>failure_cure</c> and <c>evidence</c>, while <c>main</c> gave 17 to the question consultant's tables and 18 to
/// the security evidence. Merged, those columns are step 19 — so a preview database would skip the question tables
/// for ever and fail step 19 with <c>duplicate column name</c>, losing the rounds database entirely.
/// </summary>
/// <remarks>
/// Two field shapes, both reachable: the preview's own (17), and the preview's file after a released <c>main</c>
/// build opened it — that build reads 17 as "the questions ran", applies its step 18 (the evidence column) and stamps
/// 18, still without the question tables and still with the failure columns.
/// </remarks>
public sealed class AConsultantPreviewDatabaseKeepsEveryStepTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-consultant-fork-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private void MakeAt(IEnumerable<string> steps)
    {
        Directory.CreateDirectory(_dir);
        var list = steps.ToArray();
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", list) + $"; PRAGMA user_version={list.Length}";
        make.ExecuteNonQuery();
    }

    private (long Version, bool Questions, bool Evidence, bool Failure) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        return ((long)ask.ExecuteScalar()!,
            RoundsQuery.HasColumn(db, "question_consults", "id"),
            RoundsQuery.HasColumn(db, "findings", "security_evidence"),
            RoundsQuery.HasColumn(db, "consultations", "failure_kind"));
    }

    private static int QuestionsStep => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked);

    private static int FailureStep => Array.IndexOf(Schema.Steps, Schema.WhyAConsultationFailed);

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, Serilog.Core.Logger.None))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true, true));
    }

    [Fact]
    public void TheFailureColumnsAreStep19_AfterMainsQuestionsAndEvidence()
    {
        QuestionsStep.Should().Be(16, "main's question consultant keeps step 17");
        Array.IndexOf(Schema.Steps, Schema.SecurityEvidence).Should().Be(17, "main's security evidence keeps step 18");
        FailureStep.Should().Be(18, "why a consultation failed is step 19 — it was 17 only on its own branch");
    }

    [Fact]
    public void A_database_the_consultant_preview_stamped_17_gains_the_question_tables_and_the_evidence_and_keeps_opening()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Should().Be((17L, false, false, true), "the fixture is the preview's database, not today's");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote must still open, not fail step 19 on a duplicate column");
    }

    [Fact]
    public void A_preview_database_a_released_main_build_already_moved_to_18_still_gains_the_question_tables()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed, Schema.SecurityEvidence]);
        Shape().Should().Be((18L, false, true, true), "main read 17 as its questions, added its evidence and stamped 18");

        OpensAndEndsWithEveryStep("that file must open too, and gain the question tables main skipped");
    }

    [Fact]
    public void A_database_main_stamped_18_gains_the_failure_columns_as_step_19()
    {
        MakeAt(Schema.Steps[..FailureStep]);
        Shape().Should().Be((18L, true, true, false), "the positive control: the ordinary path, main's database");

        OpensAndEndsWithEveryStep("main's database migrates forward as it always has");
    }

    [Fact]
    public void A_repaired_preview_database_opens_again_unchanged()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        using (RoundsDb.Open(_dir, Serilog.Core.Logger.None)) { }

        OpensAndEndsWithEveryStep("the repair is applied once; a second open is the ordinary path at the head");
    }
}
