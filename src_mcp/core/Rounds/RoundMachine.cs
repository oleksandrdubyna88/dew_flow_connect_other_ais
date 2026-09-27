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
        { IsFeatureSession: true } => new Transition.Refused(ThisIsAFeatureSession),
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

    /// <summary>A feature session was asked for a plan, code or document round, or the other way round.</summary>
    /// <remarks>Refused by name, for the reason <see cref="ThisIsADocumentSession"/> gives: one budget, one job.</remarks>
    internal const string ThisIsAFeatureSession =
        "this session is reviewing a whole feature, not a branch or a document — call review_feature for " +
        "it; the branch's own gates are review_plan and review_code, and a document has review_document";

    internal const string ThisIsNotAFeatureSession =
        "this session is reviewing a branch or a document, not a feature — review_feature starts a session " +
        "of its own, keyed by the plan's path, and needs no open";

    /// <summary>
    /// A plan's feature review is finished — and the ways to run another are NAMED (D14, D23).
    /// </summary>
    /// <remarks>
    /// The same door as <see cref="CodeDone"/>, narrowed by D23: <c>again</c> reopens the review for its
    /// second round only on the person's request (the other two grounds keep a review OPEN, so it never
    /// reaches this sentence), and never for a third; a NEW base is a fresh review. Whether the head has
    /// MOVED is the stage's check, not this transition's — it needs the commit, which a pure transition
    /// does not have.
    /// </remarks>
    internal const string FeatureDone =
        "this plan's feature review is complete. A second round runs only for a reviewer failure, a blocking " +
        "finding, or when the person asks for one — never a third (D23). With the person's request, call " +
        "review_feature with again: true, which reopens the feature stage for its second round; against a NEW " +
        "base, again: true starts a fresh review. The finished rounds stay on the record";

    /// <summary>D23's cap: a feature review has two rounds at most, whatever the stage's budget says above it.</summary>
    public const int FeatureRoundCap = 2;

    /// <summary>
    /// Round 1 gave no ground for a second (D23) — and the one thing that can still give one is named.
    /// </summary>
    /// <remarks>
    /// The refusal a caller meets after a non-blocking round closed the review and it asked <c>again</c>
    /// over the same base. It names all three grounds, because the caller is missing every one of them,
    /// and says how the third arrives: through the person's own surfaces, never through an argument.
    /// </remarks>
    internal static string NoGroundForASecondRound(SessionState s) =>
        "this plan's feature review is one round unless a second is needed (D23), and round 1 gave no ground " +
        "for one: no reviewer failure, no blocking finding, and the person has not asked. A second round runs " +
        "only on one of those three. To ask the person, call ask_human with feature: \"" + s.Feature + "\" — their " +
        "'Keep going — more rounds' (or 'Stop and act on the findings') is the request, and review_feature with " +
        "again: true then runs round 2; no argument of yours can grant it. Against a NEW base, again: true starts " +
        "a fresh review";

    /// <summary>Both rounds have run — at most two, ever (D23).</summary>
    internal static string TwoFeatureRoundsRun(SessionState s) =>
        "this plan's feature review has run its two rounds — at most two, ever (D23) — so no further round runs " +
        "against this base, whoever asks. A fresh review of \"" + s.Feature + "\" starts only against a NEW base: " +
        "call review_feature with again: true and the new baseRef; the finished rounds stay on the record";

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
        { IsFeatureSession: true } => new Transition.Refused(ThisIsAFeatureSession),
        { HumanGate: true } => new Transition.Refused(GateHeld),
        { PlanProceeded: false } => new Transition.Refused(
            "no plan round has reached 'proceed' in this session — the plan gate comes first (review_plan)"),
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { Stage: Stage.Done } => new Transition.Refused(CodeDone),
        _ => new Transition.Moved(s),
    };

    /// <summary>
    /// A branch's code review is finished — and the way to run another is NAMED.
    /// </summary>
    /// <remarks>
    /// It said "open a new one", and <c>open</c> is idempotent per repo+branch: that door never
    /// existed. A checkpoint round therefore forbade the final one, and agents cut ten review/*
    /// branches for one feature purely to get fresh sessions (issue #490). The door is
    /// <see cref="BeginCodeRoundAgain"/>, the same shape as <see cref="DocumentDone"/>'s.
    /// </remarks>
    internal const string CodeDone =
        "this branch's code review is complete. To review it again — new commits after a checkpoint, " +
        "the final round of an epic, or after a crash — commit them and call review_code with again: true, " +
        "which reopens the code stage; the finished rounds stay on the record";

    /// <summary>
    /// The code gate, asked AGAIN: a finished code session is reopened for a fresh code round.
    /// </summary>
    /// <remarks>
    /// <para>Everything else is exactly <see cref="BeginCodeRound"/>: a held human gate, an unresolved
    /// round and a missing plan all still refuse first, and a session that is not finished is simply
    /// begun — asking again of a session that never closed changes nothing.</para>
    /// <para>What is kept is what the stage was AGREED on — <c>PlanProceeded</c>, the plan text, the
    /// standing rejections. What starts over is the stage's own count, so the round is round one of a
    /// fresh budget rather than a round past an exhausted one. That the branch has MOVED since the
    /// last code round is the caller's check (<c>PanelService.ReviewCodeAsync</c>): it needs the
    /// commit, which a pure transition does not have.</para>
    /// </remarks>
    public static Transition BeginCodeRoundAgain(SessionState s) => s switch
    {
        { Stage: Stage.Done, IsDocumentSession: false, HumanGate: false, AwaitingResolve: false, PlanProceeded: true } =>
            new Transition.Moved(s with
            {
                Stage = Stage.CodeReview,
                RoundsRunThisStage = 0,
                EscalationsUsed = 0,
                AdvanceOnResolve = false,
            }),
        _ => BeginCodeRound(s),
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
        { IsFeatureSession: true } => new Transition.Refused(ThisIsAFeatureSession),
        { IsDocumentSession: false } => new Transition.Refused(ThisIsACodeSession),
        { HumanGate: true } => new Transition.Refused(GateHeld),
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { Stage: Stage.Done } => new Transition.Refused(DocumentDone),
        _ => new Transition.Moved(s),
    };

    /// <summary>
    /// The feature gate (S2.1 of the feature-review plan). Every refusal the document gate has, in the
    /// document gate's order — and, like it, no <c>PlanProceeded</c> check: the plan IS the input. Then
    /// D23's two: a review that has run both its rounds, and a second round no ground admits.
    /// </summary>
    /// <remarks>
    /// <para>The held gate is asked BEFORE the finished stage and before the roster is ever looked at, and
    /// that order is kept on purpose by the engine too: a standing <c>call_human</c> from a round whose
    /// reviewers all failed is a person's decision, and un-ticking every vendor must not dissolve it
    /// into a <c>skipped</c> round (§4.4).</para>
    /// <para><b>The second round is admitted HERE and nowhere else</b> (S3.4): on the ground
    /// <see cref="CompleteRound"/> recorded from round 1 — a reviewer failure, a blocking finding — or
    /// the one <see cref="ApplyPersonsRequest"/> recorded from the person. Nothing a caller passes reaches
    /// <see cref="SessionState.SecondRound"/>. A count of zero is round 1, which needs no ground: a new
    /// review, a fresh review against another base, or the fresh set a person granted on a held gate.</para>
    /// </remarks>
    public static Transition BeginFeatureRound(SessionState s) => s switch
    {
        { IsFeatureSession: false } => new Transition.Refused(ThisIsNotAFeatureSession),
        { HumanGate: true } => new Transition.Refused(GateHeld),
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { Stage: Stage.Done } => new Transition.Refused(FeatureDone),
        { RoundsRunThisStage: >= FeatureRoundCap } => new Transition.Refused(TwoFeatureRoundsRun(s)),
        { RoundsRunThisStage: >= 1, SecondRound: SecondRoundGround.None } => new Transition.Refused(NoGroundForASecondRound(s)),
        _ => new Transition.Moved(s),
    };

    /// <summary>
    /// The feature gate, asked AGAIN: a finished feature review is reopened for its SECOND round — the
    /// shape of <see cref="BeginCodeRoundAgain"/>, with the same refusals still first, and D23's after.
    /// </summary>
    /// <remarks>
    /// <para>What is kept is what the review is OF and what it agreed on — the plan, the standing
    /// rejections — AND its count: a finished review reopened over the same base is on its second round,
    /// not on round one of a budget it never had (the earlier shape reset the count, and with it D23's cap).
    /// So the reopened state meets <see cref="BeginFeatureRound"/>'s own admission: the person's ground
    /// lets round 2 run, two rounds run refuse a third, and a review closed with no ground is refused
    /// naming what would give one.</para>
    /// <para>That the head has moved, and that the base is the one recorded, are the stage's checks
    /// (<see cref="FeatureBases"/>), because they need commits a pure transition does not have — and a
    /// review asked again against ANOTHER base keeps nothing it agreed on: <see cref="FreshFeatureReview"/>,
    /// which the engine applies BEFORE this begin once that finding is made, so a fresh review arrives
    /// here at round one.</para>
    /// </remarks>
    public static Transition BeginFeatureRoundAgain(SessionState s) => s switch
    {
        { Stage: Stage.Done, IsFeatureSession: true, HumanGate: false, AwaitingResolve: false } =>
            BeginFeatureRound(s with { Stage = Stage.FeatureReview, AdvanceOnResolve = false }),
        _ => BeginFeatureRound(s),
    };

    /// <summary>
    /// A feature review started over against ANOTHER base (§4.3): the stage's own count, its escalations,
    /// its standing rejections and its second-round ground all start again, whatever the stage was — a
    /// rejection made in one review must not discount a finding in a different one, and a round of the
    /// old budget is not a round of the new.
    /// </summary>
    /// <remarks>
    /// Not a transition of its own, on purpose: that the base IS another one is the stage's finding
    /// (<see cref="FeatureBases.IsAnother"/>), so the engine applies this to the session it read, before
    /// the begin — which then sees round one of a fresh review — and saves nothing unless the round runs.
    /// A held gate and an unresolved round are still refused by the begin, fresh or not.
    /// </remarks>
    public static SessionState FreshFeatureReview(SessionState s) => s with
    {
        Stage = Stage.FeatureReview,
        RoundsRunThisStage = 0,
        EscalationsUsed = 0,
        AdvanceOnResolve = false,
        Rejections = [],
        SecondRound = SecondRoundGround.None,
        RequestQuestions = [],
    };

    /// <summary>
    /// An <c>ask_human</c> question, recorded where its answer may later count: on the current hold
    /// (<see cref="SessionState.HoldQuestions"/>), or — on a feature session with a round run and no hold —
    /// as one whose answer may be the person's request for round 2 (<see cref="SessionState.RequestQuestions"/>).
    /// Any other question is asked and not recorded: its answer decides nothing here.
    /// </summary>
    /// <remarks>
    /// A question asked BEFORE the first round is not a request question: an answer to it given after
    /// round 1 is about whatever it asked, and binding the request to it was the clock rule in a new coat.
    /// </remarks>
    public static SessionState RecordQuestion(SessionState s, string id) => s switch
    {
        { HumanGate: true } => s with { HoldQuestions = [.. s.HoldQuestions, id] },
        { IsFeatureSession: true, RoundsRunThisStage: >= 1 } => s with { RequestQuestions = [.. s.RequestQuestions, id] },
        _ => s,
    };

    /// <summary>
    /// The person's request for a feature review's second round — D23's third ground, applied to the
    /// state from a decision only a person's surface produces.
    /// </summary>
    /// <remarks>
    /// <para><c>Continue</c> and <c>Fix</c> both say "run the review again", which for a feature review
    /// that has run one round means its second and last. Recorded only where it can mean that: a feature
    /// session, one round run, no ground yet, and no held gate — a held gate is answered by
    /// <see cref="ApplyHumanDecision"/>, which grants a fresh SET, a different promise. Two rounds run is
    /// refused by the begin whoever asks, so nothing is recorded for it.</para>
    /// <para>WHICH answer is the request is the engine's check — it needs the answer files — and the rule is
    /// identity: an answer to one of <see cref="SessionState.RequestQuestions"/>, the questions asked on this
    /// session after round 1 (<see cref="RecordQuestion"/>). The questions are spent with the request: the
    /// round they admitted runs, and nothing asked before it can admit another.</para>
    /// </remarks>
    public static SessionState ApplyPersonsRequest(SessionState s, HumanDecision decision) =>
        decision is HumanDecision.Continue or HumanDecision.Fix
        && s is { IsFeatureSession: true, HumanGate: false, RoundsRunThisStage: 1, SecondRound: SecondRoundGround.None }
            ? s with { SecondRound = SecondRoundGround.PersonAsked, RequestQuestions = [] }
            : s;

    public static Transition CompleteRound(SessionState s, GateResult gate, ReviewerSummary reviewers)
    {
        // The feature stage has its own rule for what a round's outcome MEANS (D23): one round unless a
        // second is needed, two at most. Everything the gate computed is the same; what changes is which
        // verdict it becomes and what the state remembers about why.
        if (s.Stage == Stage.FeatureReview)
        {
            return CompleteFeatureRound(s, gate, reviewers);
        }

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
    /// The feature stage's round, completed under D23: one round unless a second is needed, two at most.
    /// </summary>
    /// <remarks>
    /// <para><b>Round 1.</b> No ground → the review closes on <c>resolve</c>: <c>proceed</c> at or under
    /// the threshold, <c>good_enough</c> over it — the findings are decided, the accepted fixes land as
    /// pull requests, and no second review reads them. A ground → <c>revise</c>, with the ground written
    /// on the state for the begin to read; the operator's own budget still caps this (a budget of one runs
    /// no second round, and D23 caps a bigger one at two).</para>
    /// <para><b>Round 2.</b> No ground → closes on resolve as round 1 would. A ground — a blocking finding
    /// still standing, a failure again — → <c>call_human</c>, the gate held: two rounds is the cap, and a
    /// review that needs a third is a person's call.</para>
    /// <para><b>The gate must not fail open here either.</b> Nobody answering is a reviewer failure
    /// whatever the failure list says — a round of stood-down reviewers reviewed nothing — so it buys a
    /// retry in round 1 and a person in round 2, never a <c>proceed</c> over an empty result.</para>
    /// <para>A blocking finding outranks a failure as the ground: a fix wants every reviewer's eyes, while a
    /// retry asks only the ones that failed.</para>
    /// </remarks>
    private static Transition CompleteFeatureRound(SessionState s, GateResult gate, ReviewerSummary reviewers)
    {
        var roundsRun = s.RoundsRunThisStage + 1;
        var ground = GroundFor(gate, reviewers);
        // The questions that may request a second round are the ones asked AFTER this one completed.
        var next = s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, RequestQuestions = [] };

        if (ground == SecondRoundGround.None)
        {
            return new Transition.Ok(
                next with { AdvanceOnResolve = true, SecondRound = SecondRoundGround.None },
                gate.Passed ? new RoundVerdict.Proceed(gate, reviewers) : new RoundVerdict.GoodEnough(gate, reviewers));
        }

        if (roundsRun < Math.Min(FeatureRoundCap, s.Config.For(s.Stage).MaxRounds))
        {
            return new Transition.Ok(next with { SecondRound = ground }, new RoundVerdict.Revise(gate, reviewers, 1));
        }

        return new Transition.Ok(
            next with { HumanGate = true, SecondRound = SecondRoundGround.None },
            new RoundVerdict.CallHuman(gate, reviewers, FeatureCallHumanReason(ground, gate, reviewers, roundsRun)));
    }

    /// <summary>Why a feature round needs another — or <see cref="SecondRoundGround.None"/>.</summary>
    private static SecondRoundGround GroundFor(GateResult gate, ReviewerSummary reviewers) =>
        gate.Gating.Any(f => f.Severity == Severity.Blocking) ? SecondRoundGround.BlockingFinding
        : reviewers.Answered == 0 || reviewers.Failures.Length > 0 ? SecondRoundGround.ReviewerFailure
        : SecondRoundGround.None;

    private static string FeatureCallHumanReason(SecondRoundGround ground, GateResult gate, ReviewerSummary reviewers, int roundsRun)
    {
        // The second round is the ordinary case; the first is an operator whose feature budget is one.
        var which = roundsRun == 1 ? "round 1, and the feature budget is one round" : "the second round";

        return ground switch
        {
            SecondRoundGround.BlockingFinding =>
                $"a blocking finding still stands after {which} — {gate.GatingCount} finding(s) gate, and a feature review has two rounds at most (D23)",
            _ when reviewers.Answered == 0 =>
                $"no reviewer answered in {which} — nothing was reviewed, and a feature review has two rounds at most (D23). {reviewers.Sentence}",
            _ => $"{which} had a reviewer failure, and a feature review has two rounds at most (D23): {reviewers.Sentence}",
        };
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
        // The feature stage's ground starts over with the set: what admitted the round that called the
        // person has been spent, and the fresh set's own first round decides whether it needs a second.
        // The hold's questions go with the hold: an answer to one of them, found later, must not release
        // a hold raised after this one (the epic 3 risk consultation, 2026-09-26).
        HumanDecision.Continue or HumanDecision.Fix =>
            s with { HumanGate = false, RoundsRunThisStage = 0, SecondRound = SecondRoundGround.None, HoldQuestions = [], RequestQuestions = [] },
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
            // The person's override releases the hold the way their decision does: its questions go with it.
            HoldQuestions = humanSaysProceed ? [] : s.HoldQuestions,
        };
        if (s.AdvanceOnResolve || humanSaysProceed)
        {
            next = next with
            {
                AdvanceOnResolve = false,
                // The feature stage KEEPS its count into Done: `again` reopens it for its second round or
                // is refused a third (D23), and the machine can only tell which from the count. Every
                // other stage starts the next one at zero, as it always did.
                RoundsRunThisStage = s.Stage == Stage.FeatureReview ? s.RoundsRunThisStage : 0,
                // Where this stage goes is the stage's own row: it was `PlanReview ? CodeReview :
                // Done`, which is right for three stages and a silent answer for a fourth.
                Stage = Stages.Of(s.Stage).AdvancesTo,
                PlanProceeded = s.PlanProceeded || s.Stage == Stage.PlanReview,
            };
        }

        return new Transition.Moved(next);
    }
}
