using CoaiMcp.Storage;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reconciles the databases whose step 17 was the consultant-works-everywhere preview's: <c>consultations</c>'
/// failure columns where <c>main</c> has the question consultant's tables.
/// </summary>
/// <remarks>
/// <para>The sibling of <see cref="SecurityPreviewFork"/>, for the same reason. The preview builds of
/// PLAN_the_consultant_works_on_every_vendor.md (2026-10-02/03) were run against real data directories and
/// stamped <c>user_version = 17</c> for <see cref="Schema.WhyAConsultationFailed"/>, while <c>main</c> gave 17
/// to <see cref="Schema.TheQuestionsAsked"/> and 18 to <see cref="Schema.SecurityEvidence"/>. Merged, the failure
/// columns are step 19 — so such a file would skip the question tables for ever and then fail step 19 with
/// <c>duplicate column name</c>, and the rounds database would stop opening.</para>
/// <para><b>Two shapes, recognised by what the file contains, not by a version alone.</b> At 17 with
/// <c>failure_kind</c>: the preview's own file. At 18 with <c>failure_kind</c>: the same file after a released
/// <c>main</c> build opened it — that build read 17 as "the questions ran", added its evidence column and
/// stamped 18. Both lack <c>question_consults</c>. No other number is reachable: the branch only ever had this
/// step at 17 (its history of <c>Schema.cs</c>), no build of <c>main</c> before this one knows a step past 18,
/// and a <see cref="SecurityPreviewFork"/> file (17, evidence, no failure columns) is a different shape that
/// fork owns.</para>
/// <para>The repair is the steps the file skipped — the question tables (<c>IF NOT EXISTS</c> throughout) and the
/// evidence column only when it is absent — and the version of a file that now has all three, in one IMMEDIATE
/// transaction, the write lock the migrator takes, so two openers cannot both apply it.</para>
/// </remarks>
internal static class ConsultantPreviewFork
{
    private static int QuestionsVersion => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked) + 1;
    private static int EvidenceVersion => Array.IndexOf(Schema.Steps, Schema.SecurityEvidence) + 1;
    private static int FailureVersion => Array.IndexOf(Schema.Steps, Schema.WhyAConsultationFailed) + 1;

    internal static void Repair(SqliteConnection db)
    {
        using var repairing = db.BeginTransaction(deferred: false);
        if (IsTheFork(db))
        {
            Run(db, Schema.TheQuestionsAsked);
            AddTheEvidenceIfAbsent(db);
            Run(db, $"PRAGMA user_version={FailureVersion}");
        }

        repairing.Commit();
    }

    private static bool IsTheFork(SqliteConnection db) =>
        SqliteMigrator.Version(db) is var version
        && (version == QuestionsVersion || version == EvidenceVersion)
        && RoundsQuery.HasColumn(db, "consultations", "failure_kind")
        && !RoundsQuery.HasColumn(db, "question_consults", "id");

    private static void AddTheEvidenceIfAbsent(SqliteConnection db)
    {
        if (!RoundsQuery.HasColumn(db, "findings", "security_evidence"))
        {
            Run(db, Schema.SecurityEvidence);
        }
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
