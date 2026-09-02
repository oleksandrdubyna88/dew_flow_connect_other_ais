using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// After <c>call_human</c>, no further round runs until a PERSON has decided.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> A round budget was never a budget. <c>BeginPlanRound</c> and
/// <c>BeginCodeRound</c> refused only two things — an unresolved previous round and the wrong
/// stage — and asked nothing about how many rounds had been spent. The budget was consulted only
/// at COMPLETION, to choose between <c>revise</c> and <c>call_human</c>. And <c>Resolve</c>
/// cleared <c>HumanGate</c> unconditionally. So the loop after exhaustion was: run a full round,
/// get <c>call_human</c>, resolve, run a full round, get <c>call_human</c>, forever — every one a
/// complete panel of reviewers, and every one asking for a person nobody was fetching.</para>
///
/// <para>Observed on a colleague's machine: a stage reached round TEN on a three-round budget. Its
/// own summary of what those rounds bought is the argument for this change — rounds 1–3 found real
/// defects, 4–9 chased "progressively narrower crash windows", and round 10 INTRODUCED a bug. A
/// gate that says "ask a person" and then lets the AI carry on is not a gate, and the cost of
/// pretending otherwise is not only money.</para>
///
/// <para><b>What a person decides</b> already existed and is unchanged: <c>continue</c> and
/// <c>fix</c> grant a fresh set of rounds, <c>discuss</c> advances nothing, and a
/// <c>resolve</c> carrying <c>humanDecision: proceed</c> moves the stage on with the findings open.
/// The only thing added is that none of it can be skipped.</para>
/// </remarks>
public class HumanGateHoldsTests
{
    private static SessionState Exhausted(Stage stage = Stage.PlanReview) => new SessionState(
        "s1", "/repo", "main", PanelConfig.Uniform(maxRounds: 2, threshold: 2))
    {
        Stage = stage,
        PlanProceeded = stage == Stage.CodeReview,
        RoundsRunThisStage = 2,
        HumanGate = true,
    };

    [Fact]
    public void APlanRoundIsRefusedWhileAPersonHasNotDecided()
    {
        var transition = RoundMachine.BeginPlanRound(Exhausted() with { AwaitingResolve = false });

        transition.Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("call_human").And.Contain("decide");
    }

    [Fact]
    public void ACodeRoundIsRefusedTheSameWay()
    {
        var transition = RoundMachine.BeginCodeRound(
            Exhausted(Stage.CodeReview) with { AwaitingResolve = false });

        transition.Should().BeOfType<Transition.Refused>();
    }

    [Fact]
    public void TheRefusalNamesEveryWayOut_BecauseARefusalWithNoDoorIsAStall()
    {
        var reason = RoundMachine.BeginPlanRound(Exhausted() with { AwaitingResolve = false })
            .Should().BeOfType<Transition.Refused>().Subject.Sentence;

        reason.Should().Contain("ask_human", "the AI has to know how to fetch the person");
        reason.Should().Contain("proceed", "and that a person may simply say go on");
    }

    [Fact]
    public void ResolveNoLongerClearsTheGateOnItsOwn()
    {
        // This one line was the whole loop: `resolve` cleared HumanGate for anybody who called it,
        // so the AI recording its own decisions re-opened the gate it had just been stopped by.
        var next = RoundMachine.Resolve(Exhausted() with { AwaitingResolve = true }, [])
            .Should().BeOfType<Transition.Moved>().Subject.State;

        next.AwaitingResolve.Should().BeFalse("the decisions WERE recorded");
        next.HumanGate.Should().BeTrue("but recording them is not a person deciding");
    }

    [Fact]
    public void AHumanProceedStillClearsIt_AndAdvancesTheStage()
    {
        var next = RoundMachine.Resolve(Exhausted() with { AwaitingResolve = true }, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Moved>().Subject.State;

        next.HumanGate.Should().BeFalse();
        next.Stage.Should().Be(Stage.CodeReview);
        next.PlanProceeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(HumanDecision.Continue)]
    [InlineData(HumanDecision.Fix)]
    public void ContinueAndFixGrantAFreshSetOfRounds(HumanDecision choice)
    {
        // Both already mean this in the words a person is shown: "the stage gets a fresh set of
        // rounds and the review runs again". Until now nothing carried that into the state.
        var next = RoundMachine.ApplyHumanDecision(Exhausted(), choice);

        next.HumanGate.Should().BeFalse();
        next.RoundsRunThisStage.Should().Be(0, "a fresh set, not one more round");
        RoundMachine.BeginPlanRound(next).Should().BeOfType<Transition.Moved>();
    }

    [Fact]
    public void DiscussAdvancesNothing_WhichIsWhatItSays()
    {
        var next = RoundMachine.ApplyHumanDecision(Exhausted(), HumanDecision.Discuss);

        next.HumanGate.Should().BeTrue();
        RoundMachine.BeginPlanRound(next).Should().BeOfType<Transition.Refused>();
    }

    [Fact]
    public void NoDecisionChangesNothing()
    {
        var state = Exhausted();

        RoundMachine.ApplyHumanDecision(state, HumanDecision.None).Should().Be(state);
    }

    [Fact]
    public void AStageThatNeverReachedTheGateIsUnaffected()
    {
        // The ordinary path must not acquire a new way to fail: rounds 1 and 2 of a two-round
        // budget open exactly as before.
        var fresh = new SessionState("s2", "/repo", "main", PanelConfig.Uniform(2, 2));

        RoundMachine.BeginPlanRound(fresh).Should().BeOfType<Transition.Moved>();
        RoundMachine.BeginPlanRound(fresh with { RoundsRunThisStage = 1 })
            .Should().BeOfType<Transition.Moved>();
    }

    [Fact]
    public void GoodEnoughAndContinueAnywayNeverRaiseTheGate()
    {
        // Only the `human` policy stops. The other exhausted-policies advance on resolve, and a
        // gate raised over them would break a configuration whose whole point is not to stop.
        foreach (var policy in (StagePolicy[])[StagePolicy.Continue, StagePolicy.GoodEnough])
        {
            var config = PanelConfig.Uniform(maxRounds: 1, threshold: 0, onExhausted: policy);
            var state = new SessionState("s3", "/repo", "main", config);
            var gate = GateRule.Evaluate(
                [new Finding(Severity.Blocking, Category.Reliability, "a.cs", 1, "t", "why", "fix", ["codex"])],
                [],
                _ => 0);

            var completed = RoundMachine.CompleteRound(state, gate, new ReviewerSummary(1, 1, []))
                .Should().BeOfType<Transition.Ok>().Subject;

            completed.State.HumanGate.Should().BeFalse($"{policy} does not ask a person");
        }
    }
}
