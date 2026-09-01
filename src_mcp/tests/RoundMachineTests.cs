using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class RoundMachineTests
{
    private static SessionState Fresh(PanelConfig? config = null) =>
        new("s-1", "D:/repo", "feature/x", config ?? new PanelConfig());

    private static Finding F(string title = "finding") =>
        new(Severity.Major, Category.Security, "src/A.cs", 1, title, "why it breaks", "fix", ["codex"]);

    private static GateResult Failing(int count = 3) =>
        new(count, false, [.. Enumerable.Range(0, count).Select(i => F($"finding {i}"))], []);

    private static GateResult Passing() => GateResult.Empty;

    private static readonly ReviewerSummary AllSix = ReviewerSummary.AllAnswered(6);

    /// <summary>Run one full round: begin → complete → resolve(accept all).</summary>
    private static SessionState RoundTrip(SessionState s, GateResult gate)
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(s, gate, AllSix);
        return ((Transition.Moved)RoundMachine.Resolve(
            ok.State,
            [.. gate.Gating.Select(f => (Decision)new Decision.Accepted(f))])).State;
    }

    [Fact]
    public void ReviewCode_BeforePlanProceed_Refuses() =>
        RoundMachine.BeginCodeRound(Fresh()).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("plan gate comes first");

    [Fact]
    public void ReviewCode_AfterThePlanProceeds_IsAllowed()
    {
        var afterPlan = RoundTrip(Fresh(), Passing());

        afterPlan.Stage.Should().Be(Stage.CodeReview);
        afterPlan.PlanProceeded.Should().BeTrue();
        RoundMachine.BeginCodeRound(afterPlan).Should().BeOfType<Transition.Moved>();
    }

    [Fact]
    public void Resolve_WithoutAReviewRound_Refuses() =>
        RoundMachine.Resolve(Fresh(), []).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("no completed round");

    [Fact]
    public void SecondRound_BeforeResolve_Refuses()
    {
        var awaiting = ((Transition.Ok)RoundMachine.CompleteRound(Fresh(), Failing(), AllSix)).State;

        RoundMachine.BeginPlanRound(awaiting).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("resolve");
    }

    [Fact]
    public void RejectionWithoutAReason_RefusesTheWholeResolve()
    {
        var awaiting = ((Transition.Ok)RoundMachine.CompleteRound(Fresh(), Failing(1), AllSix)).State;

        RoundMachine.Resolve(awaiting, [new Decision.Rejected(F(), "   ")])
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("without a reason");
    }

    [Theory]
    [InlineData(StagePolicy.Continue, typeof(RoundVerdict.ContinueAnyway))]
    [InlineData(StagePolicy.Human, typeof(RoundVerdict.CallHuman))]
    [InlineData(StagePolicy.Escalate, typeof(RoundVerdict.Escalated))]
    public void MaxRounds_ReachedWithFindings_YieldsTheConfiguredOutcome(StagePolicy policy, Type verdict)
    {
        var s = Fresh(PanelConfig.Uniform(1, 2, policy));

        RoundMachine.CompleteRound(s, Failing(), AllSix).Should().BeOfType<Transition.Ok>()
            .Which.Verdict.Should().BeOfType(verdict);
    }

    [Fact]
    public void EscalationSteps_FireInLadderOrder_ThenAHuman()
    {
        var s = Fresh(PanelConfig.Uniform(1, 2, StagePolicy.Escalate));

        var steps = new List<EscalationStep>();
        for (var i = 0; i < 3; i++)
        {
            var ok = (Transition.Ok)RoundMachine.CompleteRound(s, Failing(), AllSix);
            steps.Add(((RoundVerdict.Escalated)ok.Verdict).Step);
            s = ((Transition.Moved)RoundMachine.Resolve(ok.State, [])).State;
        }

        steps.Should().Equal(
            EscalationStep.ReviewerEffortUp,
            EscalationStep.ReviewerModelUp,
            EscalationStep.ArbiterModelUp);

        // The ladder is exhausted: there is nothing left to raise, so a person decides.
        RoundMachine.CompleteRound(s, Failing(), AllSix).Should().BeOfType<Transition.Ok>()
            .Which.Verdict.Should().BeOfType<RoundVerdict.CallHuman>()
            .Which.Reason.Should().Contain("ladder is exhausted");
    }

    [Fact]
    public void NobodyAnswered_NeverPasses_TheGateMustNotFailOpen()
    {
        // The real run's most serious finding (2026-08-31): every reviewer failed, no findings
        // arrived, and the round answered 'proceed'. A panel that did not review is not a panel
        // that approved — an empty result set is the ABSENCE of evidence, not evidence of absence.
        var nobody = new ReviewerSummary(6, 0, ["codex: quota", "gemini: untrusted folder"]);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), Passing(), nobody);

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>()
            .Which.Reason.Should().Contain("no reviewer");
    }

    [Fact]
    public void OneAnswerIsEnoughToJudgeOn_ThoughTheSummaryNamesWhoDidNot()
    {
        var one = new ReviewerSummary(6, 1, ["five failures"]);

        RoundMachine.CompleteRound(Fresh(), Passing(), one).Should().BeOfType<Transition.Ok>()
            .Which.Verdict.Should().BeOfType<RoundVerdict.Proceed>();
    }

    [Fact]
    public void AfterCallHuman_TheHumansProceed_AdvancesTheStage()
    {
        // The gap the first live run exposed: rounds exhausted, verdict call_human, the person
        // says "go" — and the machine had no way to hear it.
        var s = Fresh(PanelConfig.Uniform(1, 2, StagePolicy.Human));
        var ok = (Transition.Ok)RoundMachine.CompleteRound(s, Failing(), AllSix);
        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();

        var moved = RoundMachine.Resolve(ok.State, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Moved>().Subject;

        moved.State.Stage.Should().Be(Stage.CodeReview);
        moved.State.PlanProceeded.Should().BeTrue();
    }

    [Fact]
    public void TheHumanOverride_IsRefused_WhileRoundsRemain()
    {
        // Before exhaustion the gate decides, not the flag: a model must not skip the loop by
        // claiming permission it was never given.
        var awaiting = ((Transition.Ok)RoundMachine.CompleteRound(Fresh(), Failing(), AllSix)).State;

        RoundMachine.Resolve(awaiting, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("call_human");
    }

    [Fact]
    public void TheHumanOverride_IsRefused_WhenTheExhaustedStageEscalatesInstead()
    {
        // The code gate's own review of this file found it, and it is the sharper half of the
        // override rule: "no rounds left" is NOT "a human was asked". An escalated stage whose
        // rounds are also spent would have passed the old round-count check, and honouring the
        // flag there skips the ladder the operator configured.
        var s = Fresh(PanelConfig.Uniform(1, 2, StagePolicy.Escalate));
        var ok = (Transition.Ok)RoundMachine.CompleteRound(s, Failing(), AllSix);
        ok.Verdict.Should().BeOfType<RoundVerdict.Escalated>();
        var spent = ok.State with { RoundsRunThisStage = ok.State.Config.For(Stage.PlanReview).MaxRounds };

        RoundMachine.Resolve(spent, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("call_human");
    }

    [Fact]
    public void ARedundantOverride_IsIgnored_NotRefused()
    {
        // The gate already said proceed. The flag adds nothing — and refusing the resolve over a
        // redundant argument would discard a whole round's recorded decisions.
        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), Passing(), AllSix);

        RoundMachine.Resolve(ok.State, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Moved>()
            .Which.State.Stage.Should().Be(Stage.CodeReview);
    }

    [Fact]
    public void TheOverrideDoorCloses_BehindTheHumanWhoWalkedThrough()
    {
        // One call_human verdict authorises ONE override. Left open, a later resolve in a fresh
        // stage would still be carrying permission granted for a decision already made.
        var s = Fresh(PanelConfig.Uniform(1, 2, StagePolicy.Human));
        var ok = (Transition.Ok)RoundMachine.CompleteRound(s, Failing(), AllSix);
        var moved = (Transition.Moved)RoundMachine.Resolve(ok.State, [], humanSaysProceed: true);

        moved.State.HumanGate.Should().BeFalse();
    }

    [Fact]
    public void PartialReviewerFailures_StillAdvance_AndSaySo()
    {
        var partial = new ReviewerSummary(6, 4, ["gemini/perf: timeout", "codex/security: rate limited"]);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), Failing(), partial);

        var revise = ok.Verdict.Should().BeOfType<RoundVerdict.Revise>().Subject;
        revise.Reviewers.Sentence.Should().Contain("4 of 6").And.Contain("timeout");
    }

    [Fact]
    public void ResolveRejections_FeedTheNextRoundsGate()
    {
        var finding = F("disputed");
        var awaiting = ((Transition.Ok)RoundMachine.CompleteRound(Fresh(), Failing(1), AllSix)).State;

        var resolved = ((Transition.Moved)RoundMachine.Resolve(
            awaiting, [new Decision.Rejected(finding, "not applicable to this endpoint")])).State;

        resolved.Rejections.Should().ContainSingle().Which.Reason.Should().Contain("not applicable");
    }

    [Fact]
    public void CodeStagePassing_EndsTheSession()
    {
        var s = RoundTrip(RoundTrip(Fresh(), Passing()), Passing());

        s.Stage.Should().Be(Stage.Done);
        RoundMachine.BeginCodeRound(s).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("complete");
    }

    [Fact]
    public void SameRepoAndBranch_IsTheSameSessionKey_WhateverTheSpelling()
    {
        SessionKey.For(@"D:\repo\", "main").Should().Be(SessionKey.For("d:/repo", "main"));
        SessionKey.For("D:/repo", "main").Should().NotBe(SessionKey.For("D:/repo", "feature/x"));
    }
}
