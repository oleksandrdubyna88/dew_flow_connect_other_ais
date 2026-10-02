using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The lane's own round budget, against the ordinary roles' — through the pure round machine.</summary>
public sealed class SecurityLaneBudgetTests
{
    private static readonly PanelConfig OneOrdinaryRoundTwoLaneRounds =
        PanelConfig.Uniform(1, 5) with { SecurityLane = new RoleGate(2, 0, true) };

    private static SessionState FeatureRoundOne() =>
        new("feature", "repo", SessionKey.FeatureBranch, OneOrdinaryRoundTwoLaneRounds)
        { Stage = Stage.FeatureReview, Feature = "plan.md" };

    private static GateResult Blocking(string role) => GateRule.Evaluate(
        [new Finding(Severity.Blocking, Category.Security, "Orders.cs", 1, "Fixture", "Evidence", "Correction", ["codex"]) { Role = role }],
        [], r => r == SecurityCatalog.Gate ? 0 : 5);

    [Fact]
    public void The_stage_budget_is_the_ordinary_roles_alone()
    {
        OneOrdinaryRoundTwoLaneRounds.For(Stage.FeatureReview).MaxRounds.Should().Be(1);
        OneOrdinaryRoundTwoLaneRounds.For(Stage.CodeReview).MaxRounds.Should().Be(1);
        OneOrdinaryRoundTwoLaneRounds.For(SecurityCatalog.Gate).MaxRounds.Should().Be(2, "the lane keeps its own budget");
    }

    [Fact]
    public void A_blocking_lane_finding_still_earns_the_lane_its_second_feature_round()
    {
        var completed = (Transition.Ok)RoundMachine.CompleteRound(FeatureRoundOne(), Blocking(SecurityCatalog.Gate),
            ReviewerSummary.AllAnswered(2));

        completed.Verdict.Should().BeOfType<RoundVerdict.Revise>();
        completed.State.SecondRound.Should().Be(SecondRoundGround.BlockingFinding);
        RoundMachine.AdmittedOnlyForTheLane(completed.State with { AwaitingResolve = false }).Should().BeTrue();
    }

    [Fact]
    public void A_blocking_ordinary_finding_is_judged_on_the_ordinary_budget()
    {
        var completed = (Transition.Ok)RoundMachine.CompleteRound(FeatureRoundOne(), Blocking(RoleCatalog.FeatureRole),
            ReviewerSummary.AllAnswered(2));

        completed.Verdict.Should().BeOfType<RoundVerdict.CallHuman>()
            .Which.Reason.Should().Contain("the feature budget is one round");
    }

    [Fact]
    public void A_lane_finding_beside_an_ordinary_failure_does_not_buy_the_ordinary_side_a_lane_round()
    {
        var oneFailed = new ReviewerSummary(2, 1, ["codex/FeatureReview: exit 7"]);

        RoundMachine.CompleteRound(FeatureRoundOne(), Blocking(SecurityCatalog.Gate), oneFailed)
            .Should().BeOfType<Transition.Ok>().Which.Verdict.Should().BeOfType<RoundVerdict.CallHuman>(
                "round two would be the lane's alone and could never retry the ordinary reviewer that failed");
    }

    [Fact]
    public void A_lane_finding_beside_a_blocking_ordinary_finding_does_not_lift_the_ordinary_budget()
    {
        var both = GateRule.Evaluate(
            [.. Blocking(SecurityCatalog.Gate).Gating, .. Blocking(RoleCatalog.FeatureRole).Gating], [], _ => 0);

        RoundMachine.CompleteRound(FeatureRoundOne(), both, ReviewerSummary.AllAnswered(2))
            .Should().BeOfType<Transition.Ok>().Which.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();
    }

    [Fact]
    public void A_switched_off_lane_lends_no_round_even_to_its_own_finding()
    {
        var off = FeatureRoundOne() with { Config = OneOrdinaryRoundTwoLaneRounds with { SecurityLane = new RoleGate(2, 0, false) } };

        RoundMachine.CompleteRound(off, Blocking(SecurityCatalog.Gate), ReviewerSummary.AllAnswered(2))
            .Should().BeOfType<Transition.Ok>().Which.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Only_a_round_past_every_ordinary_budget_is_the_lanes_alone(int roundsRun, bool lanes)
    {
        var code = new SessionState("code", "repo", "main", OneOrdinaryRoundTwoLaneRounds)
        { Stage = Stage.CodeReview, PlanProceeded = true, RoundsRunThisStage = roundsRun };

        RoundMachine.AdmittedOnlyForTheLane(code).Should().Be(lanes);
    }

    [Fact]
    public void A_feature_round_two_on_a_failure_or_a_request_is_not_the_lanes()
    {
        var round2 = FeatureRoundOne() with { RoundsRunThisStage = 1 };

        RoundMachine.AdmittedOnlyForTheLane(round2 with { SecondRound = SecondRoundGround.ReviewerFailure }).Should().BeFalse();
        RoundMachine.AdmittedOnlyForTheLane(round2 with { SecondRound = SecondRoundGround.PersonAsked }).Should().BeFalse();
        RoundMachine.AdmittedOnlyForTheLane(round2 with { SecondRound = SecondRoundGround.BlockingFinding }).Should().BeTrue();
    }
}
