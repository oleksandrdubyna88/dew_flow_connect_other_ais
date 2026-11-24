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

    // The four outcomes a round can end in — the whole verdict set of v2. Each test below fails if its branch is deleted
    // (checked by breaking the branch in RoundMachine / AutoVerdicts, then restoring it).

    private static PanelConfig Patient(int rounds = 3) =>
        new(new Dictionary<string, RoleGate> { ["PlanCritique"] = new(rounds, 2) });

    [Fact]
    public void UnderTheThreshold_IsProceed_AndTheStageAdvancesOnResolve()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), Passing(), AllSix);

        ok.Verdict.Should().BeOfType<RoundVerdict.Proceed>();
        ok.State.AdvanceOnResolve.Should().BeTrue();
        ok.State.AwaitingResolve.Should().BeTrue();
    }

    [Fact]
    public void OverTheThreshold_WithRoundsLeft_IsRevise_AndTheStageStays()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(Patient()), Failing(), AllSix);

        var revise = ok.Verdict.Should().BeOfType<RoundVerdict.Revise>().Subject;
        revise.RoundsLeft.Should().Be(2);
        ok.State.AdvanceOnResolve.Should().BeFalse("a revise asks for another round, it does not end the stage");
        ok.State.RoundsRunThisStage.Should().Be(1);
    }

    [Fact]
    public void RoundsSpent_WithFindingsStillGating_IsGoodEnough_AndNobodyIsWaitedFor()
    {
        var s = Fresh(PanelConfig.Uniform(1, 2));

        var ok = RoundMachine.CompleteRound(s, Failing(), AllSix).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>().Which.Reason.Should().Contain("3 finding(s) still gate after 1 round(s)");
        ok.State.AdvanceOnResolve.Should().BeTrue("the stage moves on resolve; there is no hold");
    }

    [Fact]
    public void ARoundOverTheThresholdOnTheLastRound_IsGoodEnough_NotARevise()
    {
        var s = Fresh(Patient(2));
        var first = (Transition.Ok)RoundMachine.CompleteRound(s, Failing(), AllSix);
        first.Verdict.Should().BeOfType<RoundVerdict.Revise>();
        var again = ((Transition.Moved)RoundMachine.Resolve(first.State, [])).State;

        RoundMachine.CompleteRound(again, Failing(), AllSix).Should().BeOfType<Transition.Ok>()
            .Which.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>();
    }

    [Fact]
    public void NobodyAnswered_NeverPasses_TheGateMustNotFailOpen()
    {
        // The real run's most serious finding (2026-08-31): every reviewer failed, no findings
        // arrived, and the round answered 'proceed'. A panel that did not review is not a panel
        // that approved — an empty result set is the ABSENCE of evidence, not evidence of absence.
        var nobody = new ReviewerSummary(6, 0, ["codex: quota", "gemini: untrusted folder"]);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), Passing(), nobody);

        var notReviewed = ok.Verdict.Should().BeOfType<RoundVerdict.NotReviewed>().Subject;
        notReviewed.Reason.Should().Contain("no reviewer answered");
        ok.State.AdvanceOnResolve.Should().BeTrue("nobody is waited for: the stage proceeds on resolve, said out loud");
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

        // A round must remain for the verdict to be Revise at all — with the shipped one-round
        // budget the stage is spent and ends good enough, which says nothing about partial failures.
        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(Patient()), Failing(), partial);

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
    public void AFinishedCodeStage_IsReopenedByAgain_KeepingWhatWasAgreed()
    {
        // Issue #490: the refusal named a door ("open a new one") that `open` does not have.
        var done = RoundTrip(RoundTrip(Fresh(), Passing()), Passing());

        var reopened = RoundMachine.BeginCodeRoundAgain(done).Should().BeOfType<Transition.Moved>().Which.State;

        reopened.Stage.Should().Be(Stage.CodeReview);
        reopened.RoundsRunThisStage.Should().Be(0, "a fresh budget, not a round past an exhausted one");
        reopened.PlanProceeded.Should().BeTrue("the plan the stage agreed on still stands");
        reopened.SessionId.Should().Be(done.SessionId);
    }

    [Fact]
    public void AskingAgain_StillRefusesAnUnresolvedRound()
    {
        var done = RoundTrip(RoundTrip(Fresh(), Passing()), Passing());

        RoundMachine.BeginCodeRoundAgain(done with { AwaitingResolve = true }).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Be(RoundMachine.Unresolved);
    }

    [Fact]
    public void AskingAgain_OfAStageStillOpen_ChangesNothing()
    {
        var planPassed = RoundTrip(Fresh(), Passing());

        RoundMachine.BeginCodeRoundAgain(planPassed).Should().BeOfType<Transition.Moved>()
            .Which.State.Should().Be(planPassed);
    }

    [Fact]
    public void SameRepoAndBranch_IsTheSameSessionKey_WhateverTheSpelling()
    {
        SessionKey.For(@"D:\repo\", "main").Should().Be(SessionKey.For("d:/repo", "main"));
        SessionKey.For("D:/repo", "main").Should().NotBe(SessionKey.For("D:/repo", "feature/x"));
    }
}
