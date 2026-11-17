using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// The two places a completed round used to call a person, each now a fork between the person and policy auto — kept out of
/// <see cref="RoundMachine"/>, which is at its size cap, and so that <c>CompleteRound</c> and <c>CompleteFeatureRound</c> stay within cyclomatic
/// complexity 4 (<c>csharp/doctrine.md</c> § 6).
/// </summary>
/// <remarks>
/// A pure move: the person's branch is what those two methods held inline before policy auto, and the auto branch is
/// <see cref="AutoVerdicts"/>. The order of the fork is unchanged — auto is asked first, so a config under policy auto never reaches
/// <c>call_human</c> here.
/// </remarks>
internal static class RoundCompletion
{
    /// <summary>
    /// No reviewer answered a plan, code or document round: a person's call, or — under policy auto — the stage proceeds and says nothing was reviewed.
    /// </summary>
    internal static Transition NobodyAnswered(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun, int awaiting) =>
        s.Config.IsAutonomous
            ? AutoVerdicts.NobodyAnswered(s, gate, reviewers, roundsRun, awaiting)
            : new Transition.Ok(
                // AwaitingResolve stays TRUE even though there are no findings to decide on: the
                // round completed, and `resolve` is the only door a human's "proceed" can come
                // through. With it false the verdict was unresolvable — the gate answered
                // call_human and then refused the human, which is a dead end rather than a gate.
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, HumanGate = true },
                new RoundVerdict.CallHuman(
                    gate,
                    reviewers,
                    $"no reviewer answered — nothing was reviewed. {reviewers.Sentence}"));

    /// <summary>
    /// A feature review at its cap with a ground: a person under every policy but auto, which decides and says what it decided.
    /// </summary>
    /// <param name="s">The state before the round completed — the policy is read from its config.</param>
    /// <param name="next">The state the round completed into: its round counted, <c>AwaitingResolve</c> set.</param>
    internal static Transition FeatureAtItsCap(
        SessionState s, SessionState next, GateResult gate, ReviewerSummary reviewers, SecondRoundGround ground, int roundsRun, int awaiting) =>
        s.Config.IsAutonomous
            ? AutoVerdicts.FeatureAtItsCap(next, gate, reviewers, ground, roundsRun, awaiting)
            : new Transition.Ok(
                next with { HumanGate = true, SecondRound = SecondRoundGround.None },
                new RoundVerdict.CallHuman(gate, reviewers, RoundMachine.FeatureCallHumanReason(ground, gate, reviewers, roundsRun)));
}
