using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// One question as <c>--log</c> lists it: the projected head, and every model row under it
/// (<c>todo/PLAN_question_consultant.md</c>, S4 — the Logs page's Questions tab).
/// </summary>
/// <remarks>
/// The projection's own two row types, not a third shape: the table IS what the page asks a question of,
/// and a log type that restated it would be one more list of columns to keep level.
/// </remarks>
public sealed record LoggedQuestion(QuestionConsultRow Asked, IReadOnlyList<QuestionConsultRowEntry> Answers);

/// <summary>Reading the question tables for <c>--log</c> — newest first, bounded, and empty on a database that has none.</summary>
/// <remarks>
/// <para><b>Bounded twice, and the numbers are stated.</b> At most <see cref="Shown"/> questions, and each row's
/// advice cut to <see cref="AdviceShown"/> characters: the table keeps up to 16 KB a row (§5 of the plan), and six
/// rows of that under two hundred questions would be a 19 MB answer the panel parses every five seconds while
/// the page is open. A question is rarer than a round (ten a session at most), so a hundred is weeks of them;
/// the record file under <c>question-consults/</c> keeps the whole advice for the seven days it is kept.</para>
/// <para>A database last written by a binary older than schema step 17 has no tables — it answers an EMPTY
/// list, the promise <c>LoggedLog.Consultations</c> already makes, so a newer extension against an older
/// database draws an empty tab rather than no page.</para>
/// </remarks>
public static class QuestionConsultLog
{
    /// <summary>The most questions one <c>--log</c> answer carries.</summary>
    public const int Shown = 100;

    /// <summary>The most advice one row carries in that answer, in characters.</summary>
    public const int AdviceShown = 4000;

    public static IReadOnlyList<LoggedQuestion> Read(SqliteConnection db)
    {
        try
        {
            var heads = Heads(db);
            var answers = Answers(db, heads);

            return [.. heads.Select(head => new LoggedQuestion(head, answers.TryGetValue(head.Id, out var rows) ? rows : []))];
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteGenericError && e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
    }

    /// <summary>SQLite's generic error code — a missing table arrives as it, so the message decides.</summary>
    private const int SqliteGenericError = 1;

    private const string SqlHeads = """
        SELECT id, caller, caller_kind, session_id, repo_path, branch, head_sha, plan_key, question, context,
               production_risk, risk_reason, status, outcome, escalation_id, started_utc, ended_utc,
               rows, answered, seconds, tokens_in, tokens_out, cost_usd, alert
        FROM question_consults ORDER BY started_utc DESC, id DESC LIMIT $limit
        """;

    private const string SqlAnswers = """
        SELECT r.consult_id, r.row_id, r.vendor, r.model, r.runtime, r.prompt_id, r.prompt_title, r.capability,
               r.flag, r.status, r.reason, r.seconds, r.tokens_in, r.tokens_out, r.cost_usd,
               substr(r.advice, 1, $advice) AS advice, r.note
        FROM question_consult_rows r
        JOIN (SELECT id FROM question_consults ORDER BY started_utc DESC, id DESC LIMIT $limit) q ON q.id = r.consult_id
        ORDER BY r.consult_id, r.rowid
        """;

    private static List<QuestionConsultRow> Heads(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = SqlHeads;
        read.Parameters.AddWithValue("$limit", Shown);
        using var rows = read.ExecuteReader();
        var heads = new List<QuestionConsultRow>();
        while (rows.Read())
        {
            heads.Add(Head(rows));
        }

        return heads;
    }

    private static QuestionConsultRow Head(SqliteDataReader r) => new(
        Text(r, "id"), Text(r, "caller"), Text(r, "caller_kind"), Text(r, "session_id"), Text(r, "repo_path"), Text(r, "branch"),
        Text(r, "head_sha"), Text(r, "plan_key"), Text(r, "question"), Text(r, "context"), r.GetInt64(r.GetOrdinal("production_risk")) == 1,
        Text(r, "risk_reason"), Text(r, "status"), Text(r, "outcome"), Text(r, "escalation_id"), Text(r, "started_utc"), Text(r, "ended_utc"),
        r.GetInt32(r.GetOrdinal("rows")), r.GetInt32(r.GetOrdinal("answered")), r.GetDouble(r.GetOrdinal("seconds")),
        r.GetInt64(r.GetOrdinal("tokens_in")), r.GetInt64(r.GetOrdinal("tokens_out")), Money(r), Text(r, "alert"));

    private static Dictionary<string, List<QuestionConsultRowEntry>> Answers(SqliteConnection db, List<QuestionConsultRow> heads)
    {
        var byQuestion = new Dictionary<string, List<QuestionConsultRowEntry>>(StringComparer.Ordinal);
        if (heads.Count == 0)
        {
            return byQuestion;
        }

        using var read = db.CreateCommand();
        read.CommandText = SqlAnswers;
        read.Parameters.AddWithValue("$limit", Shown);
        read.Parameters.AddWithValue("$advice", AdviceShown);
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            var entry = Entry(rows);
            if (!byQuestion.TryGetValue(entry.ConsultId, out var list))
            {
                byQuestion[entry.ConsultId] = list = [];
            }

            list.Add(entry);
        }

        return byQuestion;
    }

    private static QuestionConsultRowEntry Entry(SqliteDataReader r) => new(
        Text(r, "consult_id"), Text(r, "row_id"), Text(r, "vendor"), Text(r, "model"), Text(r, "runtime"), Text(r, "prompt_id"),
        Text(r, "prompt_title"), Text(r, "capability"), Text(r, "flag"), Text(r, "status"), Text(r, "reason"),
        r.GetDouble(r.GetOrdinal("seconds")), r.GetInt64(r.GetOrdinal("tokens_in")), r.GetInt64(r.GetOrdinal("tokens_out")),
        Money(r), Text(r, "advice"), Text(r, "note"));

    private static string Text(SqliteDataReader r, string column) => r.GetString(r.GetOrdinal(column));

    /// <summary>The price, or nothing when no vendor reported one — a blank, never a zero that reads as free.</summary>
    private static double? Money(SqliteDataReader r)
    {
        var at = r.GetOrdinal("cost_usd");

        return r.IsDBNull(at) ? null : r.GetDouble(at);
    }
}
