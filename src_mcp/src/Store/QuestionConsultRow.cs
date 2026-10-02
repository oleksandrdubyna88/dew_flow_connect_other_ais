namespace CoaiMcp.Store;

/// <summary>
/// One question as the database holds it — the record file's head, flattened and totalled over its
/// rows (<c>todo/PLAN_question_consultant.md</c>, S2; schema step 17).
/// </summary>
/// <remarks>
/// A type of its own rather than the server's <c>QuestionConsultRecord</c>, so this project keeps
/// owning its own tables: the record is a question with a list of rows in it, and these two are what
/// the log page asks a question of. Nothing is nullable but the money, for the reason a round's is:
/// a vendor that does not report what it charged leaves a BLANK, and a zero would read as "free".
/// </remarks>
public sealed record QuestionConsultRow(
    string Id,
    string Caller,
    string CallerKind,
    string SessionId,
    string RepoPath,
    string Branch,
    string HeadSha,
    string PlanKey,
    string Question,
    string Context,
    bool ProductionRisk,
    string RiskReason,
    string Status,
    string Outcome,
    string EscalationId,
    string StartedUtc,
    string EndedUtc,
    int Rows,
    int Answered,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Alert);

/// <summary>One model row of a question, as the database holds it. The advice is cut at <see cref="AdviceLimit"/>.</summary>
public sealed record QuestionConsultRowEntry(
    string ConsultId,
    string RowId,
    string Vendor,
    string Model,
    string Runtime,
    string PromptId,
    string PromptTitle,
    string Capability,
    string Flag,
    string Status,
    string Reason,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Advice,
    string Note)
{
    /// <summary>The most advice one row keeps in the table (§5 of the plan): the record file keeps the whole text.</summary>
    public const int AdviceLimit = 16 * 1024;

    /// <summary>The advice as the table stores it — whole when it fits, else cut with a marker that says so.</summary>
    public static string Bounded(string advice) =>
        advice.Length <= AdviceLimit ? advice : advice[..AdviceLimit] + "\n[cut at 16 KB — the record file holds the rest]";
}
