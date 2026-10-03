namespace CoaiMcp.Server;

/// <summary>The record an ANSWERED turn leaves behind — the success half of what <see cref="ConsultationFailing"/> builds for a failure.</summary>
/// <remarks>
/// Moved out of <c>ConsultationService</c> with the failure builders (PLAN_the_consultant_works_on_every_vendor.md,
/// E2.2): a pure record transition has no business in a file past the 800-line ceiling, and the two halves of
/// "what a turn writes" now sit side by side, where the rule that an answer clears the failure it followed
/// is visible next to the rule that writes one.
/// </remarks>
internal static class ConsultationAnswering
{
    /// <summary>The record this turn leaves behind — closed when the budget is spent, open otherwise.</summary>
    /// <remarks>
    /// Pure, and its own method because the three <c>IsLast</c> decisions are ONE decision wearing
    /// three hats: whether this was the last turn. Reading them apart, inside an object initialiser
    /// inside a method that also launches and logs, was the complexity the gate objected to.
    /// (CodeRabbit, on the pull request.)
    /// </remarks>
    public static ConsultationRecord Answered(ConsultationRecord record, ConsultationTurn turn, string handle)
    {
        var last = record.Budget.IsLast;

        // The LAST turn ends the consultation with nobody having said whether it worked, which is
        // what `lapsed` records; an earlier turn leaves the outcome untouched. (issue #309.)
        return (last ? ConsultationClosing.Lapse(record) : record) with
        {
            Turns = [.. record.Turns, turn],
            Handle = handle,
            Status = last ? ConsultationStatuses.Closed : ConsultationStatuses.Open,
            // The sentence a LATER call is refused with is this one, so it carries the cure — the
            // setting's name — rather than leaving the caller to find it.
            Reason = last ? $"all {record.MaxTurns} of its turns are used (COAI_CONSULT_TURNS sets the cap)" : string.Empty,
            EndedUtc = last ? turn.Utc : string.Empty,
            UpdatedUtc = turn.Utc,
            // An answer is the state now: the failure it followed is history, kept in the ledger and the
            // health file, not on the record a reader takes as current.
            FailureKind = string.Empty,
            FailureCure = string.Empty,
            Evidence = string.Empty,
        };
    }
}
