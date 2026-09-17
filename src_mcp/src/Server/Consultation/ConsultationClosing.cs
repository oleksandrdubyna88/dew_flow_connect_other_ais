namespace CoaiMcp.Server;

/// <summary>The words a consultation may end with. A closed set, validated before anything is written.</summary>
/// <remarks>
/// <para>Three of them are somebody's verdict — a caller that verified the advice, or a person
/// closing one by hand. <see cref="Lapsed"/> is the server's own and is deliberately NOT a verdict:
/// it says the budget ran out or the thing sat idle, which is a fact about the clock rather than
/// about whether the advice was any good. Flattening it into <see cref="Abandoned"/> would put words
/// in somebody's mouth. (issue #309, the plan round.)</para>
/// </remarks>
public static class ConsultationOutcomes
{
    /// <summary>The advice was verified and it worked.</summary>
    public const string Solved = "solved";

    /// <summary>It was verified and it did not.</summary>
    public const string NotSolved = "not_solved";

    /// <summary>Nobody is going to act on it.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>The budget ran out or it sat idle. Written by the SERVER and by nobody else.</summary>
    public const string Lapsed = "lapsed";

    /// <summary>What a caller or a person may write. <see cref="Lapsed"/> is not among them.</summary>
    public static readonly string[] Verdicts = [Solved, NotSolved, Abandoned];

    /// <summary>Is this one of ours at all?</summary>
    public static bool Known(string outcome) => Verdicts.Contains(outcome) || outcome == Lapsed;

    /// <summary>
    /// Is this outcome somebody's VERDICT — as against the absence of one?
    /// </summary>
    /// <remarks>
    /// Empty is the absence of a verdict, and so is <see cref="Lapsed"/>. Both may therefore be
    /// replaced by one; a verdict may not. Every record written before this field existed carries
    /// empty, which is why that half matters as much as the other.
    /// </remarks>
    public static bool IsVerdict(string outcome) => Verdicts.Contains(outcome);
}

/// <summary>
/// Whether a consultation may be closed with a given word, and the sentence when it may not.
/// </summary>
/// <remarks>
/// <para>Pure and beside <see cref="ConsultationRules"/>, for the reason that file gives: every
/// refusal is a table test and every one names its cure. A rule with an owner check in it that lived
/// inside an async service would be a rule no test reaches.</para>
/// </remarks>
public static class ConsultationClosing
{
    /// <summary>Why this close may not happen, or <c>null</c> when it may.</summary>
    /// <param name="record">The consultation as the server last left it.</param>
    /// <param name="caller">Who is asking, as <c>CallerOf(repoPath)</c> resolves it. Ignored for a person.</param>
    /// <param name="outcome">The word being recorded.</param>
    /// <param name="byPerson">
    /// True for the panel's own door. It is not subject to the caller check, and that is deliberate:
    /// the person drives this through a one-shot CLI mode on their own machine against their own data
    /// directory, which is a stronger trust position than another AI's rather than a weaker one.
    /// </param>
    public static string? Refusal(ConsultationRecord record, string caller, string outcome, bool byPerson) =>
        !ConsultationOutcomes.Known(outcome)
            ? $"'{outcome}' is not an outcome — one of {string.Join(", ", ConsultationOutcomes.Verdicts)}"
        : outcome == ConsultationOutcomes.Lapsed
            ? "'lapsed' is the server's own word for a budget that ran out — record what you found instead"
        : !byPerson && record.Caller != caller
            ? $"consultation {record.Id} belongs to another caller session — you may only close your own"
        : record.Status == ConsultationStatuses.Failed
            ? $"consultation {record.Id} failed ({record.Reason}) and produced no advice — there is no outcome to record"
        // A VERDICT is immutable, and a repeat of the same one is not a change. `lapsed` and empty
        // are both the absence of a verdict, so either may still be answered — which is the whole
        // point of the human close: the consultation in the issue's own screenshot is in exactly
        // that state, and a server that refused it would leave the existing history unclosable.
        : ConsultationOutcomes.IsVerdict(record.Outcome) && record.Outcome != outcome
            ? $"consultation {record.Id} is already recorded as '{record.Outcome}' — an outcome is not rewritten"
        : null;

    /// <summary>
    /// The record as it ends when nobody said anything — the budget spent, or the clock run out.
    /// </summary>
    /// <remarks>
    /// <para>Its own function so both automatic paths reach the same decision: the sweep in
    /// <c>ConsultationStore</c> and the last turn in <c>ConsultationService</c> are written in two
    /// files and would otherwise be two chances to leave the field empty — which is indistinguishable
    /// from a record written before it existed.</para>
    /// <para><b>It never overwrites a verdict.</b> A caller that closed its consultation and then let
    /// the clock run out keeps what it said; the sweep runs afterwards and would otherwise erase it.</para>
    /// </remarks>
    public static ConsultationRecord Lapse(ConsultationRecord record) =>
        ConsultationOutcomes.IsVerdict(record.Outcome)
            ? record
            : record with { Outcome = ConsultationOutcomes.Lapsed };

    /// <summary>
    /// Would this close CHANGE the record? A repeat of the same outcome would not.
    /// </summary>
    /// <remarks>
    /// Asked separately from <see cref="Refusal"/> because a retry whose answer was lost on the way
    /// back must succeed without rewriting anything — a second <c>EndedUtc</c> would move the moment
    /// the consultation ended to whenever the network failed.
    /// </remarks>
    public static bool Writes(ConsultationRecord record, string outcome) => record.Outcome != outcome;
}
