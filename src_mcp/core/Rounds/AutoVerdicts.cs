using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What the round machine decides at the three sites that used to call a person: nobody answered, a feature review at its
/// cap, and the rounds running out. Each ends the round for good — nobody is waited for, and the stage advances on <c>resolve</c>.
/// </summary>
/// <remarks>
/// <para>Kept out of <see cref="RoundMachine"/> on purpose: that file is at the size cap, and these three decisions are one idea
/// that reads better in one place than as three branches in three methods.</para>
/// <para>Every reason begins <c>policy auto:</c>, so the note on the round and the log line say who decided.</para>
/// </remarks>
internal static class AutoVerdicts
{
    /// <summary>
    /// No reviewer answered a plan, code or document round: nothing was reviewed, and the stage proceeds without a review.
    /// </summary>
    /// <remarks>
    /// <c>AwaitingResolve</c> stays true like every round that ran: <c>resolve</c> is the only door the stage moves through, and with
    /// no findings to decide it is accepted with none.
    /// </remarks>
    /// <param name="awaiting">The findings <c>resolve</c> will ask a decision for: only the security lane's can exist when no ordinary
    /// reviewer answered, and then "nothing was reviewed" is false and "nothing to decide" is wrong.</param>
    internal static Transition NobodyAnswered(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun, int awaiting = 0) =>
        new Transition.Ok(
            s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
            new RoundVerdict.NotReviewed(
                gate,
                reviewers,
                awaiting > 0
                    ? $"policy auto: no reviewer answered among the ordinary roles — the security lane's {awaiting} finding(s) stand and await a decision, and the stage proceeds without an ordinary review. {reviewers.Sentence}"
                    : $"policy auto: no reviewer answered — nothing was reviewed, and the stage proceeds without a review. {reviewers.Sentence}"));

    /// <summary>
    /// A feature review that reached its two-round cap with a ground: nobody answering is <c>not_reviewed</c>, a blocking finding or a
    /// failure is <c>good_enough</c> — read the findings, apply the true ones, reject the rest with reasons.
    /// </summary>
    /// <param name="next">The state the round completed into: its round counted, <c>AwaitingResolve</c> set.</param>
    internal static Transition FeatureAtItsCap(
        SessionState next, GateResult gate, ReviewerSummary reviewers, SecondRoundGround ground, int roundsRun, int awaiting = 0)
    {
        var reason = reviewers.Answered == 0 && awaiting > 0
            ? $"policy auto: no reviewer answered among the ordinary roles after {roundsRun} round(s) — the security lane's {awaiting} finding(s) stand and await a decision, and a feature review has two rounds at most (D23). {reviewers.Sentence}"
            : "policy auto: " + RoundMachine.FeatureCallHumanReason(ground, gate, reviewers, roundsRun);
        return new Transition.Ok(
            next with { AdvanceOnResolve = true, SecondRound = SecondRoundGround.None },
            reviewers.Answered == 0
                ? new RoundVerdict.NotReviewed(gate, reviewers, reason)
                : new RoundVerdict.GoodEnough(gate, reviewers, reason));
    }

    /// <summary>Why the rounds running out ended as <c>good_enough</c> under policy auto.</summary>
    internal static string ExhaustedReason(GateResult gate, int roundsRun) =>
        $"policy auto: {gate.GatingCount} finding(s) still gate after {roundsRun} round(s) — good enough";
}
