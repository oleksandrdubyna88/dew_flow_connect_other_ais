using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The writer of schema step 17: one question upserted as it advances, its rows upserted beside it.
/// </summary>
/// <remarks>
/// Its own file rather than more lines in <see cref="RoundsDb"/>, which is past the family's ceiling
/// already; it reaches the connection through <see cref="RoundsDb.Command"/>, the one seam that class
/// opens for a table owned elsewhere. Every call is best-effort by the caller's <see cref="Projection"/>
/// — the record file is the truth and this is a view of it.
/// </remarks>
public static class QuestionConsultTable
{
    public static void Record(RoundsDb db, QuestionConsultRow question, IReadOnlyList<QuestionConsultRowEntry> rows)
    {
        Upsert(db, question);
        foreach (var row in rows)
        {
            Upsert(db, row);
        }
    }

    private static void Upsert(RoundsDb db, QuestionConsultRow q)
    {
        using var write = db.Command();
        write.CommandText = """
            INSERT INTO question_consults (
                id, caller, caller_kind, session_id, repo_path, branch, head_sha, plan_key, question, context,
                production_risk, risk_reason, status, outcome, escalation_id, started_utc, ended_utc,
                rows, answered, seconds, tokens_in, tokens_out, cost_usd, alert)
            VALUES (
                $id, $caller, $kind, $session, $repo, $branch, $sha, $plan, $question, $context,
                $risk, $riskReason, $status, $outcome, $escalation, $started, $ended,
                $rows, $answered, $seconds, $in, $out, $cost, $alert)
            ON CONFLICT(id) DO UPDATE SET
                status = excluded.status, outcome = excluded.outcome, escalation_id = excluded.escalation_id,
                ended_utc = excluded.ended_utc, rows = excluded.rows, answered = excluded.answered,
                seconds = excluded.seconds, tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out,
                cost_usd = excluded.cost_usd, alert = excluded.alert
            """;
        Bind(write, "$id", q.Id);
        Bind(write, "$caller", q.Caller);
        Bind(write, "$kind", q.CallerKind);
        Bind(write, "$session", q.SessionId);
        Bind(write, "$repo", q.RepoPath);
        Bind(write, "$branch", q.Branch);
        Bind(write, "$sha", q.HeadSha);
        Bind(write, "$plan", q.PlanKey);
        Bind(write, "$question", q.Question);
        Bind(write, "$context", q.Context);
        Bind(write, "$risk", q.ProductionRisk ? 1 : 0);
        Bind(write, "$riskReason", q.RiskReason);
        Bind(write, "$status", q.Status);
        Bind(write, "$outcome", q.Outcome);
        Bind(write, "$escalation", q.EscalationId);
        Bind(write, "$started", q.StartedUtc);
        Bind(write, "$ended", q.EndedUtc);
        Bind(write, "$rows", q.Rows);
        Bind(write, "$answered", q.Answered);
        Bind(write, "$seconds", q.Seconds);
        Bind(write, "$in", q.TokensIn);
        Bind(write, "$out", q.TokensOut);
        Bind(write, "$cost", q.CostUsd is { } usd ? usd : DBNull.Value);
        Bind(write, "$alert", q.Alert);
        write.ExecuteNonQuery();
    }

    private static void Upsert(RoundsDb db, QuestionConsultRowEntry r)
    {
        using var write = db.Command();
        write.CommandText = """
            INSERT INTO question_consult_rows (
                consult_id, row_id, vendor, model, runtime, prompt_id, prompt_title, capability, flag,
                status, reason, seconds, tokens_in, tokens_out, cost_usd, advice, note)
            VALUES (
                $consult, $row, $vendor, $model, $runtime, $prompt, $title, $capability, $flag,
                $status, $reason, $seconds, $in, $out, $cost, $advice, $note)
            ON CONFLICT(consult_id, row_id) DO UPDATE SET
                status = excluded.status, reason = excluded.reason, seconds = excluded.seconds,
                tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out, cost_usd = excluded.cost_usd,
                advice = excluded.advice, note = excluded.note
            """;
        Bind(write, "$consult", r.ConsultId);
        Bind(write, "$row", r.RowId);
        Bind(write, "$vendor", r.Vendor);
        Bind(write, "$model", r.Model);
        Bind(write, "$runtime", r.Runtime);
        Bind(write, "$prompt", r.PromptId);
        Bind(write, "$title", r.PromptTitle);
        Bind(write, "$capability", r.Capability);
        Bind(write, "$flag", r.Flag);
        Bind(write, "$status", r.Status);
        Bind(write, "$reason", r.Reason);
        Bind(write, "$seconds", r.Seconds);
        Bind(write, "$in", r.TokensIn);
        Bind(write, "$out", r.TokensOut);
        Bind(write, "$cost", r.CostUsd is { } usd ? usd : DBNull.Value);
        Bind(write, "$advice", QuestionConsultRowEntry.Bounded(r.Advice));
        Bind(write, "$note", r.Note);
        write.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
