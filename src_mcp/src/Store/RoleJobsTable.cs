using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>One row of <c>role_jobs</c> (schema step 26): a job as its <c>job.json</c> says it is. A time that is empty means "not yet".</summary>
internal sealed record RoleJobRow(
    string JobId, string Role, string Repo, string WorkItem, string TaskFile, string State, int Generation, string Launcher,
    string CreatedUtc, string StartedUtc, string EndedUtc, string UpdatedUtc, string FinalModel, int Attempts, string Stop, string Reason,
    int RepairTurns, int Complexity, bool Security);

/// <summary>One row of <c>role_attempts</c> (schema steps 26 and 27): one try of one model, <see cref="Ordinal"/> from 1; <see cref="KeyId"/> is the claude key it ran on (empty when it ran on none).</summary>
internal sealed record RoleAttemptRow(
    string JobId, int Ordinal, string Model, string Vendor, string StartedUtc, string EndedUtc, string Stop, string Reason, long TokensIn, long TokensOut, string KeyId = "");

/// <summary>
/// The writer of schema step 26: a role job and its attempts, rewritten whole from the job file on every change — the file is the authority, this is a view of it, and every call is best-effort
/// by the caller's <see cref="Projection"/>. Its own file for the reason <see cref="QuestionConsultTable"/> is: it reaches the connection through <see cref="RoundsDb.Command"/>.
/// </summary>
internal static class RoleJobsTable
{
    public static void Record(RoundsDb db, RoleJobRow job, IReadOnlyList<RoleAttemptRow> attempts)
    {
        Upsert(db, job);
        foreach (var attempt in attempts)
        {
            Upsert(db, attempt);
        }
    }

    private static void Upsert(RoundsDb db, RoleJobRow j)
    {
        using var write = db.Command();
        write.CommandText = """
            INSERT INTO role_jobs (
                job_id, role, repo, work_item, task_file, state, generation, launcher, created_utc, started_utc, ended_utc, updated_utc,
                final_model, attempts, stop, reason, repair_turns, complexity, security)
            VALUES (
                $id, $role, $repo, $item, $file, $state, $generation, $launcher, $created, $started, $ended, $updated,
                $model, $attempts, $stop, $reason, $repair, $complexity, $security)
            ON CONFLICT(job_id) DO UPDATE SET
                state = excluded.state, generation = excluded.generation, launcher = excluded.launcher, started_utc = excluded.started_utc,
                ended_utc = excluded.ended_utc, updated_utc = excluded.updated_utc, final_model = excluded.final_model,
                attempts = excluded.attempts, stop = excluded.stop, reason = excluded.reason, repair_turns = excluded.repair_turns,
                complexity = excluded.complexity, security = excluded.security
            """;
        write.Parameters.AddWithValue("$id", j.JobId);
        write.Parameters.AddWithValue("$role", j.Role);
        write.Parameters.AddWithValue("$repo", j.Repo);
        write.Parameters.AddWithValue("$item", j.WorkItem);
        write.Parameters.AddWithValue("$file", j.TaskFile);
        write.Parameters.AddWithValue("$state", j.State);
        write.Parameters.AddWithValue("$generation", j.Generation);
        write.Parameters.AddWithValue("$launcher", j.Launcher);
        write.Parameters.AddWithValue("$created", j.CreatedUtc);
        write.Parameters.AddWithValue("$started", j.StartedUtc);
        write.Parameters.AddWithValue("$ended", j.EndedUtc);
        write.Parameters.AddWithValue("$updated", j.UpdatedUtc);
        write.Parameters.AddWithValue("$model", j.FinalModel);
        write.Parameters.AddWithValue("$attempts", j.Attempts);
        write.Parameters.AddWithValue("$stop", j.Stop);
        write.Parameters.AddWithValue("$reason", j.Reason);
        write.Parameters.AddWithValue("$repair", j.RepairTurns);
        write.Parameters.AddWithValue("$complexity", j.Complexity);
        write.Parameters.AddWithValue("$security", j.Security ? 1 : 0);
        write.ExecuteNonQuery();
    }

    private static void Upsert(RoundsDb db, RoleAttemptRow a)
    {
        using var write = db.Command();
        write.CommandText = """
            INSERT INTO role_attempts (job_id, ordinal, model, vendor, started_utc, ended_utc, stop, reason, tokens_in, tokens_out, key_id)
            VALUES ($id, $ordinal, $model, $vendor, $started, $ended, $stop, $reason, $in, $out, $key)
            ON CONFLICT(job_id, ordinal) DO UPDATE SET
                model = excluded.model, vendor = excluded.vendor, started_utc = excluded.started_utc, ended_utc = excluded.ended_utc,
                stop = excluded.stop, reason = excluded.reason, tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out, key_id = excluded.key_id
            """;
        write.Parameters.AddWithValue("$id", a.JobId);
        write.Parameters.AddWithValue("$ordinal", a.Ordinal);
        write.Parameters.AddWithValue("$model", a.Model);
        write.Parameters.AddWithValue("$vendor", a.Vendor);
        write.Parameters.AddWithValue("$started", a.StartedUtc);
        write.Parameters.AddWithValue("$ended", a.EndedUtc);
        write.Parameters.AddWithValue("$stop", a.Stop);
        write.Parameters.AddWithValue("$reason", a.Reason);
        write.Parameters.AddWithValue("$in", a.TokensIn);
        write.Parameters.AddWithValue("$out", a.TokensOut);
        write.Parameters.AddWithValue("$key", a.KeyId);
        write.ExecuteNonQuery();
    }
}
