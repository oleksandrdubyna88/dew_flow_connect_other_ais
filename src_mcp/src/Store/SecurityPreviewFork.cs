using CoaiMcp.Storage;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reconciles the one database whose step 17 is not <c>main</c>'s: the security lane's preview build.
/// </summary>
/// <remarks>
/// <para>Steps 17 and 18 were numbered on two branches at once. The security lane's Native-AOT preview
/// (2026-10-02) was run against a real data directory and stamped <c>user_version = 17</c> for ITS
/// evidence column, while <c>main</c> gave 17 to the question consultant's tables. Merged, the questions
/// are 17 and the evidence 18 — so such a file would skip the question tables for ever and then fail
/// step 18 with <c>duplicate column name</c>, and the rounds database would stop opening.</para>
/// <para>The fork is recognised by its shape, not by a version alone: at 17, WITH the evidence column and
/// WITHOUT the question tables. Its repair is the step it skipped — additive and idempotent
/// (<c>IF NOT EXISTS</c> throughout) — and the version of a file that now has both, in one transaction
/// with the same write lock the migrator takes, so two openers cannot both apply it.</para>
/// </remarks>
internal static class SecurityPreviewFork
{
    private static int QuestionsVersion => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked) + 1;
    private static int EvidenceVersion => Array.IndexOf(Schema.Steps, Schema.SecurityEvidence) + 1;

    internal static void Repair(SqliteConnection db)
    {
        using var repairing = db.BeginTransaction(deferred: false);
        if (IsTheFork(db))
        {
            Run(db, Schema.TheQuestionsAsked);
            Run(db, $"PRAGMA user_version={EvidenceVersion}");
        }

        repairing.Commit();
    }

    private static bool IsTheFork(SqliteConnection db) =>
        SqliteMigrator.Version(db) == QuestionsVersion
        && RoundsQuery.HasColumn(db, "findings", "security_evidence")
        && !RoundsQuery.HasColumn(db, "question_consults", "id");

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
