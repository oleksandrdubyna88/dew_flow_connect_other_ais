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
        var s = Fresh(new PanelConfig(MaxRounds: 1, OnExhausted: policy));

        RoundMachine.CompleteRound(s, Failing(), AllSix).Should().BeOfType<Transition.Ok>()
            .Which.Verdict.Should().BeOfType(verdict);
    }

    [Fact]
    public void EscalationSteps_FireInLadderOrder_ThenAHuman()
    {
        var s = Fresh(new PanelConfig(MaxRounds: 1, OnExhausted: StagePolicy.Escalate));

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
