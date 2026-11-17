using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What the round machine decides under policy auto at the three sites that called a person: nobody answered, a feature review at its
/// cap, and the rounds running out. Each ends the round for good — <c>HumanGate</c> is never set, and the stage advances on <c>resolve</c>.
/// </summary>
/// <remarks>
/// <para>Kept out of <see cref="RoundMachine"/> on purpose: that file is at the size cap, and these three decisions are one idea — the
/// auto twin of every <c>call_human</c> — that reads better in one place than as three branches in three methods.</para>
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
    internal static Transition NobodyAnswered(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun) =>
        new Transition.Ok(
            s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
            new RoundVerdict.NotReviewed(
                gate,
                reviewers,
                $"policy auto: no reviewer answered — nothing was reviewed, and the stage proceeds without a review. {reviewers.Sentence}"));

    /// <summary>
    /// A feature review that reached its two-round cap with a ground: nobody answering is <c>not_reviewed</c>, a blocking finding or a
    /// failure is <c>good_enough</c> — read the findings, apply the true ones, reject the rest with reasons.
    /// </summary>
    /// <param name="next">The state the round completed into: its round counted, <c>AwaitingResolve</c> set.</param>
    internal static Transition FeatureAtItsCap(
        SessionState next, GateResult gate, ReviewerSummary reviewers, SecondRoundGround ground, int roundsRun)
    {
        var reason = "policy auto: " + RoundMachine.FeatureCallHumanReason(ground, gate, reviewers, roundsRun);
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
