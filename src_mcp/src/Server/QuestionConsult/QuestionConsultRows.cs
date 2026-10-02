using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// A question record as its database rows: the question flattened and totalled over its rows, and one
/// entry per model row (schema step 17).
/// </summary>
/// <remarks>
/// Here rather than in <c>Store</c> because this is where the rows are understood — the same split
/// <see cref="ConsultationRows"/> makes for a consultation. Money is NULL when no row reported any,
/// never a zero that reads as "free"; a question where one row priced itself and another did not is the
/// sum of what is known.
/// </remarks>
public static class QuestionConsultRows
{
    public static QuestionConsultRow Head(QuestionConsultRecord record) => new(
        record.Id,
        record.Caller,
        record.CallerKind,
        record.SessionId,
        record.RepoPath,
        record.Branch,
        record.HeadSha,
        record.PlanKey,
        record.Question,
        record.Context,
        record.ProductionRisk,
        record.RiskReason,
        record.Status,
        record.Outcome,
        record.EscalationId,
        record.StartedUtc,
        record.EndedUtc,
        record.Rows.Count,
        record.Rows.Count(r => r.Answered),
        record.Rows.Sum(r => r.Seconds),
        record.Rows.Sum(r => r.TokensIn),
        record.Rows.Sum(r => r.TokensOut),
        record.Rows.Any(r => r.CostUsd is not null) ? record.Rows.Sum(r => r.CostUsd ?? 0) : null,
        record.Alert);

    public static IReadOnlyList<QuestionConsultRowEntry> Entries(QuestionConsultRecord record) =>
        [.. record.Rows.Select(r => new QuestionConsultRowEntry(
            record.Id, r.RowId, r.Vendor, r.Model, r.Runtime, r.PromptId, r.PromptTitle, r.Capability, r.Flag,
            r.Status, r.Reason, r.Seconds, r.TokensIn, r.TokensOut, r.CostUsd, r.Advice, r.Note))];
}
