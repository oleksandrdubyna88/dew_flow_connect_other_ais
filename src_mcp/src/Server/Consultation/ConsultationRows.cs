namespace CoaiMcp.Server;

/// <summary>
/// A consultation record as one database row: the turns totalled, the first problem and the last
/// advice kept.
/// </summary>
/// <remarks>
/// <para>Here rather than in <c>Store</c> because this is where the turns are understood. The row is
/// a flat answer to "what was asked, what did it cost, how did it end"; the record is the
/// conversation, and the panel reads that one directly while it is still running.</para>
/// <para><b>The FIRST problem and the LAST advice</b>, and they are different questions. The first
/// problem is what the consultation is ABOUT — a follow-up says "I ran your check and it printed 3",
/// which names nothing on its own. The last advice is the answer in force, because an earlier one was
/// superseded by whatever the caller reported back.</para>
/// </remarks>
public static class ConsultationRows
{
    public static Store.ConsultationRow From(ConsultationRecord record) => new(
        record.Id,
        record.Caller,
        record.CallerKind,
        record.RepoPath,
        record.Branch,
        record.HeadSha,
        record.Vendor,
        record.Model,
        record.Turns.Count,
        record.Status,
        record.Reason,
        record.Outcome,
        record.StartedUtc,
        record.EndedUtc,
        record.Turns.Sum(turn => turn.Seconds),
        record.Turns.Sum(turn => turn.TokensIn),
        record.Turns.Sum(turn => turn.TokensOut),
        // NULL when no turn reported money, rather than a zero that reads as "this was free" — the
        // rule a round's own cost already follows. A consultation where ONE vendor turn priced itself
        // and another did not is the sum of what is known, which is what the panel says with a tilde.
        record.Turns.Any(turn => turn.CostUsd is not null)
            ? record.Turns.Sum(turn => turn.CostUsd ?? 0)
            : null,
        record.Turns.Count > 0 ? record.Turns[0].Problem : string.Empty,
        record.Turns.Count > 0 ? record.Turns[^1].Advice : string.Empty,
        record.Alert);
}
