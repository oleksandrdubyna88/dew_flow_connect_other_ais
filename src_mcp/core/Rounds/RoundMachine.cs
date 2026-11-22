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

    /// <summary>
    /// Rounds exhausted with findings still gating — take what is true and move on.
    /// </summary>
    /// <remarks>
    /// The findings travel with it because they are the WORK: the caller reads them, applies the
    /// ones that hold, records why it rejected the rest, and proceeds. Nobody is waited for: the stage advances on <c>resolve</c>.
    /// </remarks>
    public sealed record GoodEnough(GateResult Gate, ReviewerSummary Reviewers, string Reason = "") : RoundVerdict;

    /// <summary>No reviewer answered and the stage proceeds WITHOUT a review — never a pass, said out loud.</summary>
    public sealed record NotReviewed(GateResult Gate, ReviewerSummary Reviewers, string Reason) : RoundVerdict;

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
    public static Transition BeginPlanRound(SessionState s) => s switch
    {
        { AwaitingResolve: true } => new Transition.Refused(Unresolved),
        { IsDocumentSession: true } => new Transition.Refused(ThisIsADocumentSession),
        { IsFeatureSession: true } => new Transition.Refused(ThisIsAFeatureSession),
        { Stage: not Stage.PlanReview } => new Transition.Refused(
            $"the plan stage is over for this session (stage: {s.Stage}); open a new session for a new plan"),
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
    /// The same door as <see cref="CodeDone"/>, narrowed by D23: a second round runs only on a reviewer failure or a
    /// blocking finding (both keep a review OPEN, so it never reaches this sentence), and never a third; against a NEW
    /// base, <c>again</c> starts a fresh review. Whether the head has
    /// MOVED is the stage's check, not this transition's — it needs the commit, which a pure transition
    /// does not have.
    /// </remarks>
    internal const string FeatureDone =
        "this plan's feature review is complete. A second round runs only for a reviewer failure or a blocking " +
        "finding — never a third (D23). Against a NEW base, call review_feature with again: true, which starts " +
        "a fresh review. The finished rounds stay on the record";

    /// <summary>D23's cap: a feature review has two rounds at most, whatever the stage's budget says above it.</summary>
    public const int FeatureRoundCap = 2;

    /// <summary>
    /// Round 1 gave no ground for a second (D23) — and the one thing that can still give one is named.
    /// </summary>
    /// <remarks>
    /// The refusal a caller meets after a non-blocking round closed the review and it asked <c>again</c>
    /// over the same base. It names both grounds, because the caller is missing every one of them.
    /// </remarks>
    internal static string NoGroundForASecondRound(SessionState s) =>
        "this plan's feature review is one round unless a second is needed (D23), and round 1 gave no ground " +
        "for one: no reviewer failure and no blocking finding. A second round runs only on one of those two. " +
        "Against a NEW base, again: true starts a fresh review of \"" + s.Feature + "\"";

    /// <summary>Both rounds have run — at most two, ever (D23).</summary>
    internal static string TwoFeatureRoundsRun(SessionState s) =>
        "this plan's feature review has run its two rounds — at most two, ever (D23) — so no further round runs " +
        "against this base, whoever asks. A fresh review of \"" + s.Feature + "\" starts only against a NEW base: " +
        "call review_feature with again: true and the new baseRef; the finished rounds stay on the record";

    /// <summary>
    /// A document's review is finished — and the way to start another is NAMED.
    /// </summary>
    /// <remarks>
    /// A refusal with no door is a stall, which is the rule the finished-stage sentences are written to.
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
    /// <para>Everything else is exactly <see cref="BeginCodeRound"/>: an unresolved
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
        { Stage: Stage.Done, IsDocumentSession: false, AwaitingResolve: false, PlanProceeded: true } =>
            new Transition.Moved(s with
            {
                Stage = Stage.CodeReview,
                RoundsRunThisStage = 0,
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
    /// <para><b>The second round is admitted HERE and nowhere else</b> (S3.4): on the ground
    /// <see cref="CompleteRound"/> recorded from round 1 — a reviewer failure, a blocking finding.
    /// Nothing a caller passes reaches <see cref="SessionState.SecondRound"/>. A count of zero is round 1,
    /// which needs no ground: a new review, or a fresh review against another base.</para>
    /// </remarks>
    public static Transition BeginFeatureRound(SessionState s) => s switch
    {
        { IsFeatureSession: false } => new Transition.Refused(ThisIsNotAFeatureSession),
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
    /// So the reopened state meets <see cref="BeginFeatureRound"/>'s own admission: a recorded ground
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
        { Stage: Stage.Done, IsFeatureSession: true, AwaitingResolve: false } =>
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
        AdvanceOnResolve = false,
        Rejections = [],
        SecondRound = SecondRoundGround.None,
    };

    /// <param name="awaiting">How many findings the round leaves for <c>resolve</c> to decide — all of them, the lane's included, as the
    /// ordinary reviewers' <paramref name="reviewers"/> count does not say. Only the words of a round nobody ordinary answered read it.</param>
    public static Transition CompleteRound(SessionState s, GateResult gate, ReviewerSummary reviewers, int awaiting = 0)
    {
        // The feature stage has its own rule for what a round's outcome MEANS (D23): one round unless a
        // second is needed, two at most. Everything the gate computed is the same; what changes is which
        // verdict it becomes and what the state remembers about why.
        if (s.Stage == Stage.FeatureReview)
        {
            return CompleteFeatureRound(s, gate, reviewers, awaiting);
        }

        var roundsRun = s.RoundsRunThisStage + 1;

        // The gate must not fail open. Found by the first real run (2026-08-31): every reviewer
        // failed — one vendor out of quota, the other refusing an untrusted folder — so no
        // findings arrived and the round answered 'proceed'. A panel that did not review is not a
        // panel that approved; an empty result set is the ABSENCE of evidence, not evidence of
        // absence. One answer is enough to judge on; none and the stage
        // proceeds WITHOUT a review, saying so (AutoVerdicts).
        return reviewers.Answered == 0
            ? AutoVerdicts.NobodyAnswered(s, gate, reviewers, roundsRun, awaiting)
            : Judged(s, gate, reviewers, roundsRun);
    }

    /// <summary>A round some reviewer answered: passed, revised, or its rounds exhausted.</summary>
    private static Transition Judged(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun)
    {
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
    /// still standing, a failure again — → <c>good_enough</c> (<c>not_reviewed</c> when nobody answered): two rounds
    /// is the cap, and nobody is waited for.</para>
    /// <para><b>The gate must not fail open here either.</b> Nobody answering is a reviewer failure
    /// whatever the failure list says — a round of stood-down reviewers reviewed nothing — so it buys a
    /// retry in round 1 and <c>not_reviewed</c> in round 2, never a <c>proceed</c> over an empty result.</para>
    /// <para>A blocking finding outranks a failure as the ground: a fix wants every reviewer's eyes, while a
    /// retry asks only the ones that failed.</para>
    /// </remarks>
    private static Transition CompleteFeatureRound(SessionState s, GateResult gate, ReviewerSummary reviewers, int awaiting)
    {
        var roundsRun = s.RoundsRunThisStage + 1;
        var ground = GroundFor(gate, reviewers);
        var next = s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true };

        if (ground == SecondRoundGround.None)
        {
            return new Transition.Ok(
                next with { AdvanceOnResolve = true, SecondRound = SecondRoundGround.None },
                gate.Passed ? new RoundVerdict.Proceed(gate, reviewers) : new RoundVerdict.GoodEnough(gate, reviewers));
        }

        if (roundsRun < Math.Min(FeatureRoundCap, FeatureBudgetFor(s, gate, reviewers, ground)))
        {
            return new Transition.Ok(next with { SecondRound = ground }, new RoundVerdict.Revise(gate, reviewers, 1));
        }

        // At its cap with a ground: nobody is waited for — the round ends as not_reviewed or good_enough, saying why.
        return AutoVerdicts.FeatureAtItsCap(next, gate, reviewers, ground, roundsRun, awaiting);
    }

    /// <summary>
    /// The feature budget a second round is measured against: the ordinary roles' own, widened to the
    /// security lane's only when the lane ALONE needs the round.
    /// </summary>
    /// <remarks>
    /// The lane's budget is the lane's. A ground an ordinary reviewer gave — its failure, its blocking
    /// finding — is judged on the ordinary roles' budget alone, even beside a lane finding, because the
    /// round the lane's budget would buy runs without them: it could not retry the reviewer that failed
    /// or re-read the ordinary finding, and a lane answer in their place would be the lane standing in
    /// for an ordinary reviewer.
    /// </remarks>
    private static int FeatureBudgetFor(SessionState s, GateResult gate, ReviewerSummary reviewers, SecondRoundGround ground)
    {
        var ordinary = s.Config.For(s.Stage).MaxRounds;
        return ground == SecondRoundGround.BlockingFinding && OnlyTheLaneNeedsARound(s, gate, reviewers)
            ? Math.Max(ordinary, s.Config.SecurityLane.MaxRounds)
            : ordinary;
    }

    /// <summary>
    /// The round about to begin lies past every ordinary role's budget, so only the security lane's
    /// budget can have admitted it.
    /// </summary>
    /// <remarks>
    /// <para>The code stage revises for the roles over their threshold (<see cref="BudgetOfRolesWithWorkLeft"/>),
    /// and only the lane's budget reaches past the widest ordinary role's. The feature stage admits a
    /// round past the ordinary budget only on a blocking lane finding (<see cref="FeatureBudgetFor"/>);
    /// its other grounds stay the ordinary roles' own. Round one is never the lane's alone.</para>
    /// <para>Read from the state rather than from the lane's settings on purpose: a lane switched off
    /// after admitting the round has still admitted it — which is why its <c>Enabled</c> is not asked.
    /// Its BUDGET is: a round past the lane's own budget as well was admitted by nobody's, and keeps the
    /// ordinary "nobody to ask" answer rather than running empty in the lane's name. (coai code round 9.)</para>
    /// </remarks>
    public static bool AdmittedOnlyForTheLane(SessionState s) =>
        s.RoundsRunThisStage > 0
        && s.RoundsRunThisStage >= s.Config.For(s.Stage).MaxRounds
        && s.RoundsRunThisStage < s.Config.SecurityLane.MaxRounds
        && LaneCanAdmit(s);

    private static bool LaneCanAdmit(SessionState s) => s.Stage switch
    {
        Stage.CodeReview => true,
        Stage.FeatureReview => s.SecondRound == SecondRoundGround.BlockingFinding,
        _ => false,
    };

    /// <summary>Every blocking finding is the switched-on lane's own, and no ordinary reviewer failed.</summary>
    private static bool OnlyTheLaneNeedsARound(SessionState s, GateResult gate, ReviewerSummary reviewers) =>
        s.Config.SecurityLane.Enabled
        && reviewers.Failures.IsDefaultOrEmpty
        && BlockingFindingsAreTheLanes(gate);

    private static bool BlockingFindingsAreTheLanes(GateResult gate) =>
        gate.Gating.Where(f => f.Severity == Severity.Blocking).All(f => f.Role == Security.SecurityCatalog.Gate);

    /// <summary>Why a feature round needs another — or <see cref="SecondRoundGround.None"/>.</summary>
    private static SecondRoundGround GroundFor(GateResult gate, ReviewerSummary reviewers) =>
        gate.Gating.Any(f => f.Severity == Severity.Blocking) ? SecondRoundGround.BlockingFinding
        : reviewers.Answered == 0 || reviewers.Failures.Length > 0 ? SecondRoundGround.ReviewerFailure
        : SecondRoundGround.None;

    internal static string FeatureCallHumanReason(SecondRoundGround ground, GateResult gate, ReviewerSummary reviewers, int roundsRun)
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

    /// <summary>Rounds spent with findings still gating: good enough — nobody is waited for, and the stage advances on <c>resolve</c>.</summary>
    private static Transition Exhausted(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun) =>
        new Transition.Ok(
            s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
            new RoundVerdict.GoodEnough(gate, reviewers, AutoVerdicts.ExhaustedReason(gate, roundsRun)));

    /// <summary>
    /// The document clause exists because of the shape of the mistake, not the shape of the
    /// state: a caller that has just run a document round and resolves without naming the
    /// document lands on the BRANCH's session, which is idle and correct and has nothing to
    /// decide. Without the clause, the one sentence they are given sends them to run another
    /// review — the one thing that would make it worse.
    /// </summary>
    private const string NoRoundAwaitingDecisions =
        "there is no completed round awaiting decisions — run a review first. If you were "
      + "resolving a DOCUMENT round, pass its document: a document review is its own "
      + "session, keyed by the document rather than by the branch.";

    /// <summary>
    /// Records the decisions on the last round and, when the round's verdict ended the stage, advances it. A rejection without a
    /// reason is refused; a resolve with no round awaiting decisions is refused.
    /// </summary>
    public static Transition Resolve(SessionState s, IReadOnlyList<Decision> decisions)
    {
        if (!s.AwaitingResolve)
        {
            return new Transition.Refused(NoRoundAwaitingDecisions);
        }

        var unreasoned = decisions.OfType<Decision.Rejected>().Where(d => string.IsNullOrWhiteSpace(d.Reason)).ToList();
        if (unreasoned.Count > 0)
        {
            return new Transition.Refused(
                $"a rejection without a reason is not a decision — {unreasoned.Count} rejection(s) carry none");
        }

        // Only a REJECTION stands: it discounts the same finding in a later round.
        var rejections = s.Rejections.AddRange(
            decisions.OfType<Decision.Rejected>().Select(d => new PriorRejection(d.Finding, d.Reason)));

        var next = s with { AwaitingResolve = false, Rejections = rejections };
        if (s.AdvanceOnResolve)
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
