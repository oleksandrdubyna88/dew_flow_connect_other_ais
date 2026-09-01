using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Rounds;

/// <summary>The main AI's decision on one finding. A rejection without a reason is not a decision.</summary>
public abstract record Decision
{
    public sealed record Accepted(Finding Finding) : Decision;

    public sealed record Rejected(Finding Finding, string Reason) : Decision;

    private Decision() { }
}

/// <summary>What a completed round tells the main AI to do next.</summary>
public abstract record RoundVerdict
{
    /// <summary>The gate passed — resolve, then the next stage (or done).</summary>
    public sealed record Proceed(GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

    /// <summary>Findings gate — resolve, fix, review again.</summary>
    public sealed record Revise(GateResult Gate, ReviewerSummary Reviewers, int RoundsLeft) : RoundVerdict;

    /// <summary>Rounds exhausted, policy says proceed as-is — said out loud, never silently.</summary>
    public sealed record ContinueAnyway(GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

    /// <summary>Rounds exhausted, policy says a person decides.</summary>
    public sealed record CallHuman(GateResult Gate, ReviewerSummary Reviewers, string Reason) : RoundVerdict;

    /// <summary>Rounds exhausted, policy says climb: this step, then a fresh set of rounds.</summary>
    public sealed record Escalated(EscalationStep Step, GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

    private RoundVerdict() { }
}

/// <summary>A transition either happened or was refused with the sentence the main AI will read.</summary>
public abstract record Transition
{
    public sealed record Ok(SessionState State, RoundVerdict Verdict) : Transition;

    public sealed record Moved(SessionState State) : Transition;

    public sealed record Refused(string Sentence) : Transition;

    private Transition() { }
}

/// <summary>
/// The round protocol as pure transitions. Ordering is enforced by refusal:
/// a code round without a proceeded plan stage is impossible, not discouraged.
/// </summary>
public static class RoundMachine
{
    private static readonly ImmutableArray<EscalationStep> Ladder =
        [EscalationStep.ReviewerEffortUp, EscalationStep.ReviewerModelUp, EscalationStep.ArbiterModelUp];

    public static Transition BeginPlanRound(SessionState s) => s switch
    {
        { AwaitingResolve: true } => new Transition.Refused(
            "the previous round's findings have not been resolved — record accept/reject decisions first (resolve)"),
        { Stage: not Stage.PlanReview } => new Transition.Refused(
            $"the plan stage is over for this session (stage: {s.Stage}); open a new session for a new plan"),
        _ => new Transition.Moved(s),
    };

    public static Transition BeginCodeRound(SessionState s) => s switch
    {
        { PlanProceeded: false } => new Transition.Refused(
            "no plan round has reached 'proceed' in this session — the plan gate comes first (review_plan)"),
        { AwaitingResolve: true } => new Transition.Refused(
            "the previous round's findings have not been resolved — record accept/reject decisions first (resolve)"),
        { Stage: Stage.Done } => new Transition.Refused("this session is complete; open a new one"),
        _ => new Transition.Moved(s),
    };

    public static Transition CompleteRound(SessionState s, GateResult gate, ReviewerSummary reviewers)
    {
        var roundsRun = s.RoundsRunThisStage + 1;

        // The gate must not fail open. Found by the first real run (2026-08-31): every reviewer
        // failed — one vendor out of quota, the other refusing an untrusted folder — so no
        // findings arrived and the round answered 'proceed'. A panel that did not review is not a
        // panel that approved; an empty result set is the ABSENCE of evidence, not evidence of
        // absence. One answer is enough to judge on; none is a person's call.
        if (reviewers.Answered == 0)
        {
            return new Transition.Ok(
                // AwaitingResolve stays TRUE even though there are no findings to decide on: the
                // round completed, and `resolve` is the only door a human's "proceed" can come
                // through. With it false the verdict was unresolvable — the gate answered
                // call_human and then refused the human, which is a dead end rather than a gate.
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, HumanGate = true },
                new RoundVerdict.CallHuman(
                    gate,
                    reviewers,
                    $"no reviewer answered — nothing was reviewed. {reviewers.Sentence}"));
        }

        if (gate.Passed)
        {
            return new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
                new RoundVerdict.Proceed(gate, reviewers));
        }

        var budget = s.Config.For(s.Stage).MaxRounds;
        if (roundsRun < budget)
        {
            return new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true },
                new RoundVerdict.Revise(gate, reviewers, budget - roundsRun));
        }

        return Exhausted(s, gate, reviewers, roundsRun);
    }

    private static Transition Exhausted(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun) =>
        s.Config.OnExhausted switch
        {
            StagePolicy.Continue => new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
                new RoundVerdict.ContinueAnyway(gate, reviewers)),

            StagePolicy.Escalate when s.EscalationsUsed < Ladder.Length => new Transition.Ok(
                s with { RoundsRunThisStage = 0, AwaitingResolve = true, EscalationsUsed = s.EscalationsUsed + 1 },
                new RoundVerdict.Escalated(Ladder[s.EscalationsUsed], gate, reviewers)),

            // Escalate with the ladder exhausted falls through to a human — there is nothing left to raise.
            _ => new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, HumanGate = true },
                new RoundVerdict.CallHuman(
                    gate,
                    reviewers,
                    $"{gate.GatingCount} finding(s) still gate after {roundsRun} round(s)" +
                    (s.Config.OnExhausted == StagePolicy.Escalate ? " and the escalation ladder is exhausted" : string.Empty))),
        };

    /// <param name="humanSaysProceed">
    /// The human's override after a <see cref="RoundVerdict.CallHuman"/>: the rounds are spent,
    /// findings still gate, and a PERSON decided to go anyway. Honoured only in exactly that
    /// state — the first live run exposed the gap where the human said "proceed" and the machine
    /// had no way to hear it, leaving the code gate unreachable forever.
    /// </param>
    public static Transition Resolve(SessionState s, IReadOnlyList<Decision> decisions, bool humanSaysProceed = false)
    {
        if (!s.AwaitingResolve)
        {
            return new Transition.Refused("there is no completed round awaiting decisions — run a review first");
        }

        // The override is judged by what it would CHANGE, not by how many rounds are left.
        // Two corrections, both from the code gate's own review of this file: the old check asked
        // whether rounds remained, and an exhausted Escalate stage has none either — so the flag
        // could skip a configured ladder (the reachable bypass). And when the gate has already
        // decided to advance, the flag adds nothing, so refusing the whole resolve over a
        // redundant argument would throw away a legitimate round's decisions.
        if (humanSaysProceed && !s.HumanGate && !s.AdvanceOnResolve)
        {
            return new Transition.Refused(
                "a human override applies only after the verdict was call_human — until then the gate decides, " +
                "so revise and review again");
        }

        var unreasoned = decisions.OfType<Decision.Rejected>().Where(d => string.IsNullOrWhiteSpace(d.Reason)).ToList();
        if (unreasoned.Count > 0)
        {
            return new Transition.Refused(
                $"a rejection without a reason is not a decision — {unreasoned.Count} rejection(s) carry none");
        }

        var rejections = s.Rejections.AddRange(
            decisions.OfType<Decision.Rejected>().Select(d => new PriorRejection(d.Finding, d.Reason)));

        var next = s with { AwaitingResolve = false, Rejections = rejections, HumanGate = false };
        if (s.AdvanceOnResolve || humanSaysProceed)
        {
            next = next with
            {
                AdvanceOnResolve = false,
                RoundsRunThisStage = 0,
                Stage = s.Stage == Stage.PlanReview ? Stage.CodeReview : Stage.Done,
                PlanProceeded = s.PlanProceeded || s.Stage == Stage.PlanReview,
            };
        }

        return new Transition.Moved(next);
    }
}
