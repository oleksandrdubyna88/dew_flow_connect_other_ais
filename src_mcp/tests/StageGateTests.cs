using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Each stage gets its own rounds and its own threshold.
/// </summary>
/// <remarks>
/// <para>One threshold for both was wrong in a way that only showed up in use. A plan is a
/// document: two findings still open is a lot of doubt about a page of text. A diff is hundreds of
/// lines across a dozen files, and three open findings there is an ordinary Tuesday — so the same
/// number that makes the plan gate strict makes the code gate a permanent <c>call_human</c>.</para>
/// <para>Measured on this product's own rounds: the plan stage passed at two and the code stage
/// never passed at all.</para>
/// </remarks>
public sealed class StageGateTests
{
    [Fact]
    public void EachStage_ReadsItsOwnNumbers()
    {
        var config = new PanelConfig(new Dictionary<string, RoleGate> { ["PlanCritique"] = new(3, 2), ["Architecture"] = new(3, 3), ["SecurityReliability"] = new(3, 3), ["UxDxPerformance"] = new(3, 3) });

        config.For(Stage.PlanReview).Threshold.Should().Be(2);
        config.For(Stage.CodeReview).Threshold.Should().Be(3, "a diff is not a document");
    }

    [Fact]
    public void TheShippedDefaults_AreStricterOnThePlanThanOnTheDiff()
    {
        var config = new PanelConfig();

        config.For(Stage.PlanReview).Should().Be(new StageGate(3, 2));
        config.For(Stage.CodeReview).Should().Be(new StageGate(2, 3));
    }

    [Fact]
    public void RaisingTheCodeThreshold_LeavesThePlanGateWhereItWas()
    {
        var config = new PanelConfig() with { Roles = new Dictionary<string, RoleGate> { ["PlanCritique"] = PanelConfig.PlanDefault, ["Architecture"] = new(3, 6), ["SecurityReliability"] = new(3, 6), ["UxDxPerformance"] = new(3, 6) } };

        config.For(Stage.PlanReview).Threshold.Should().Be(2, "the stages are independent or they are not split");
    }

    // ---------- the machine picks the gate; no call site chooses by hand ----------

    [Fact]
    public void ACodeRound_WithThreeFindings_PassesUnderTheCodeThreshold()
    {
        var state = new SessionState("s", "D:/r", "main", new PanelConfig()) with { Stage = Stage.CodeReview };
        var three = GateRule.Evaluate(
            [Gating("a"), Gating("b"), Gating("c")], [], state.Config.For(state.Stage).Threshold);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(state, three, ReviewerSummary.AllAnswered(6));
        ok.Verdict.Should().BeOfType<RoundVerdict.Proceed>("three is the code threshold, and at-or-under passes");
    }

    [Fact]
    public void ThePlanStage_WithThreeFindings_StillRevises()
    {
        var state = new SessionState("s", "D:/r", "main", new PanelConfig());
        var three = GateRule.Evaluate(
            [Gating("a"), Gating("b"), Gating("c")], [], state.Config.For(state.Stage).Threshold);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(state, three, ReviewerSummary.AllAnswered(2));
        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>();
    }

    // ---------- a person who set the old keys does not get a changed gate ----------

    [Fact]
    public void TheLegacySingleValue_AppliesToBothStages()
    {
        var env = new Dictionary<string, string> { ["COAI_MAX_ROUNDS"] = "5", ["COAI_GATE_THRESHOLD"] = "1" };
        var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

        settings.Rounds.For(Stage.PlanReview).Should().Be(new StageGate(5, 1));
        settings.Rounds.For(Stage.CodeReview).Should().Be(new StageGate(5, 1),
            "somebody who set this once must not have their gate silently change under them");
    }

    [Fact]
    public void ThePerStageKeys_OutrankTheLegacyOne()
    {
        var env = new Dictionary<string, string>
        {
            ["COAI_GATE_THRESHOLD"] = "1",
            ["COAI_THRESHOLD_CODE"] = "4",
            ["COAI_MAX_ROUNDS_PLAN"] = "2",
        };
        var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

        settings.Rounds.For(Stage.CodeReview).Threshold.Should().Be(4);
        settings.Rounds.For(Stage.PlanReview).Threshold.Should().Be(1, "the legacy value still fills what is unset");
        settings.Rounds.For(Stage.PlanReview).MaxRounds.Should().Be(2);
    }

    private static Finding Gating(string title) =>
        new(Severity.Major, Category.Reliability, "a.cs", 1, title, "why", "fix", []);
}
