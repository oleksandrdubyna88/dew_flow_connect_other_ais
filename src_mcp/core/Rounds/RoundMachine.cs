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

/// <summary>A decision, and the finding NUMBER the caller made it by.</summary>
/// <remarks>
/// <para>The number is the index <c>resolve</c> names in its entries — <c>{"finding": 2, …}</c> —
/// which is the same number the projection stored as that finding's <c>ordinal</c>, because
/// <c>Pending</c> and the list handed to <c>RecordRound</c> are one list in one order.</para>
/// <para>It travels WITH the decision because the projection used to re-derive it from the
/// decision's position in the list, which is the same number only for a caller that resolves top to
/// bottom. Nothing requires one to: a set that arrives out of order, or covers only some of a
/// round's findings, is accepted by <see cref="RoundMachine.Resolve"/> — it checks that rejections
/// carry reasons and counts nothing — and every mark then landed on the wrong finding. Nothing
/// visibly broke, because the session file is the state machine's own record and was never wrong;
/// only the database read by the log page and the export was.</para>
/// </remarks>
public sealed record DecisionAt(int Ordinal, Decision Decision);

/// <summary>What a completed round tells the main AI to do next.</summary>
public abstract record RoundVerdict
{
    /// <summary>The gate passed — resolve, then the next stage (or done).</summary>
    public sealed record Proceed(GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

    /// <summary>Findings gate — resolve, fix, review again.</summary>
    public sealed record Revise(GateResult Gate, ReviewerSummary Reviewers, int RoundsLeft) : RoundVerdict;

    /// <summary>Rounds exhausted, policy says proceed as-is — said out loud, never silently.</summary>
    public sealed record ContinueAnyway(GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

    /// <summary>
    /// Rounds exhausted, policy says: good enough — take what is true and move on.
    /// </summary>
    /// <remarks>
    /// The findings travel with it because they are the WORK: the caller reads them, applies the
    /// ones that hold, records why it rejected the rest, and proceeds. That last part is what keeps
    /// this different from <see cref="ContinueAnyway"/>, which proceeds and touches nothing.
    /// </remarks>
    public sealed record GoodEnough(GateResult Gate, ReviewerSummary Reviewers) : RoundVerdict;

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

    /// <summary>
    /// What a round is refused with once the rounds are spent and a person has not answered.
    /// </summary>
    /// <remarks>
    /// It names every way out, because a refusal with no door is a stall. The AI can fetch the
    /// person (<c>ask_human</c>), the person can end the stage (<c>resolve</c> with
    /// <c>humanDecision: proceed</c>), or they can grant more rounds from the panel.
    /// </remarks>
    internal const string GateHeld =
        "the rounds for this stage are spent and the verdict was call_human — a person has to decide " +
        "before another round runs. Ask them with ask_human: 'Keep going — more rounds' or 'Stop and " +
        "act on the findings' each grant a fresh set of rounds, and 'Stop and talk to me' advances " +
        "nothing. If they would rather ship with the findings open, they say so and you pass " +
        "humanDecision: \"proceed\" to resolve. Running the review again is not one of your options.";

    public static Transition BeginPlanRound(SessionState s) => s switch
    {
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { IsDocumentSession: true } => new Transition.Refused(ThisIsADocumentSession),
        { Stage: not Stage.PlanReview } => new Transition.Refused(
            $"the plan stage is over for this session (stage: {s.Stage}); open a new session for a new plan"),
        // The budget was never enforced HERE, and that was the defect: it was read only at
        // completion, to choose a verdict. So `call_human` was advice, and a stage on a three-round
        // budget reached round ten.
        { HumanGate: true } => new Transition.Refused(GateHeld),
        _ => new Transition.Moved(s),
    };

    /// <summary>The previous round is unresolved — the same sentence at every gate, on purpose.</summary>
    internal const string Unresolved =
        "the previous round's findings have not been resolved — record accept/reject decisions first (resolve)";

    /// <summary>
    /// A document session was asked for a code round, or the other way round.
    /// </summary>
    /// <remarks>
    /// Refused rather than quietly converted. The two kinds have separate stages and therefore
    /// separate budgets; a session that changed kind under the caller would count one budget against
    /// two different jobs, and the round that lost would look like a gate that simply stopped
    /// working.
    /// </remarks>
    internal const string ThisIsADocumentSession =
        "this session is reviewing a document, not a branch — call review_document for it, or open " +
        "a session on another branch for the code";

    internal const string ThisIsACodeSession =
        "this session is reviewing a branch, not a document — call review_plan and review_code for " +
        "it; review_document starts a session of its own, keyed by the document";

    /// <summary>
    /// A document's review is finished — and the way to start another is NAMED.
    /// </summary>
    /// <remarks>
    /// A refusal with no door is a stall, which is the rule <see cref="GateHeld"/> was written to.
    /// Without the door an unchanged policy would be permanently unreviewable: the same document has
    /// the same identity for ever, so it would meet a finished session every time and a person would
    /// have to EDIT their document to get it read again. (codex, the plan round.)
    /// </remarks>
    internal const string DocumentDone =
        "this document's review is complete. To review it again — unchanged, or for a different " +
        "purpose — call review_document with newReview: true, which starts a fresh review and leaves " +
        "the finished one on the record";

    public static Transition BeginCodeRound(SessionState s) => s switch
    {
        { IsDocumentSession: true } => new Transition.Refused(ThisIsADocumentSession),
        { HumanGate: true } => new Transition.Refused(GateHeld),
        { PlanProceeded: false } => new Transition.Refused(
            "no plan round has reached 'proceed' in this session — the plan gate comes first (review_plan)"),
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { Stage: Stage.Done } => new Transition.Refused("this session is complete; open a new one"),
        _ => new Transition.Moved(s),
    };

    /// <summary>
    /// The document gate. Every refusal the code gate has except the one that cannot apply.
    /// </summary>
    /// <remarks>
    /// <b>No <c>PlanProceeded</c> check</b>, and that is the one deliberate difference: there is no
    /// plan before a document, because the document IS the work. Requiring one would mean asking a
    /// person to write a plan about the thing they wanted reviewed.
    /// </remarks>
    public static Transition BeginDocumentRound(SessionState s) => s switch
    {
        { IsDocumentSession: false } => new Transition.Refused(ThisIsACodeSession),
        { HumanGate: true } => new Transition.Refused(GateHeld),
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { Stage: Stage.Done } => new Transition.Refused(DocumentDone),
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

        // The budget of the roles that are actually OVER their threshold — not the stage's widest.
        // A role with one round that is still over cannot run again, so revising for its sake would
        // loop until the stage's widest role ran out, asking nothing new of anybody.
        var budget = BudgetOfRolesWithWorkLeft(s, gate);
        if (roundsRun < budget)
        {
            return new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true },
                new RoundVerdict.Revise(gate, reviewers, budget - roundsRun));
        }

        return Exhausted(s, gate, reviewers, roundsRun);
    }

    /// <summary>
    /// How many rounds the over-threshold roles have between them, at most.
    /// </summary>
    /// <remarks>
    /// Falls back to the stage's own budget when nothing is attributed — a plan round, or findings
    /// from a session file written before roles were recorded. Without that fallback an
    /// unattributed finding could never be revised for.
    /// </remarks>
    private static int BudgetOfRolesWithWorkLeft(SessionState s, GateResult gate)
    {
        var named = gate.OverThreshold.Where(r => r.Length > 0).ToList();
        return named.Count == 0
            ? s.Config.For(s.Stage).MaxRounds
            : named.Max(r => s.Config.For(r).MaxRounds);
    }

    /// <summary>
    /// The person's answer, applied to the state — the only thing that reopens a held gate.
    /// </summary>
    /// <remarks>
    /// <para><c>Continue</c> and <c>Fix</c> both grant a FRESH set of rounds, because that is what
    /// the panel already tells the person they mean: "the stage gets a fresh set of rounds and the
    /// review runs again". One more round would be a different promise.</para>
    /// <para><c>Discuss</c> deliberately changes nothing. It says "stop and talk to me", and a
    /// state that advanced would be the opposite of stopping.</para>
    /// <para><c>None</c> is prose with no button pressed. It reaches the AI as their words, and it
    /// is not a decision: the gate holds.</para>
    /// </remarks>
    public static SessionState ApplyHumanDecision(SessionState s, HumanDecision decision) => decision switch
    {
        HumanDecision.Continue or HumanDecision.Fix =>
            s with { HumanGate = false, RoundsRunThisStage = 0 },
        _ => s,
    };

    private static Transition Exhausted(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun) =>
        s.Config.OnExhausted switch
        {
            StagePolicy.Continue => new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
                new RoundVerdict.ContinueAnyway(gate, reviewers)),

            // Advances like Continue and differs entirely in the INSTRUCTION: this one tells the
            // caller to read the findings and apply the ones that hold before moving on.
            StagePolicy.GoodEnough => new Transition.Ok(
                s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
                new RoundVerdict.GoodEnough(gate, reviewers)),

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
            // The document clause exists because of the shape of the mistake, not the shape of the
            // state: a caller that has just run a document round and resolves without naming the
            // document lands on the BRANCH's session, which is idle and correct and has nothing to
            // decide. Without the clause, the one sentence they are given sends them to run another
            // review — the one thing that would make it worse.
            return new Transition.Refused(
                "there is no completed round awaiting decisions — run a review first. If you were "
              + "resolving a DOCUMENT round, pass its document: a document review is its own "
              + "session, keyed by the document rather than by the branch.");
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

        // The gate is NOT cleared by recording decisions. It used to be, unconditionally, which
        // meant the AI reopened the gate it had just been stopped by simply by doing the next
        // thing the protocol asks of it. Only a person clears it: `humanSaysProceed` here, or a
        // decision through ApplyHumanDecision.
        var next = s with
        {
            AwaitingResolve = false,
            Rejections = rejections,
            HumanGate = s.HumanGate && !humanSaysProceed,
        };
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
