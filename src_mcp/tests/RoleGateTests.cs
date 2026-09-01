using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Rounds and threshold belong to a ROLE, not to a stage.
/// </summary>
/// <remarks>
/// <para>The three code reviewers do different jobs and deserve different budgets: architecture may
/// be worth two passes with different lenses, security three, performance one. One number for the
/// whole stage forces the cheapest role to pay for the most expensive one.</para>
/// <para>The consequence is that a finding must be counted against the threshold of the role that
/// RAISED it, which is why <see cref="Finding.Role"/> exists.</para>
/// </remarks>
public sealed class RoleGateTests
{
    private static PanelConfig Config(params (string Role, int Rounds, int Threshold)[] roles) =>
        new(roles.ToDictionary(r => r.Role, r => new RoleGate(r.Rounds, r.Threshold)), StagePolicy.Human);

    private static Finding From(string role, string title) =>
        new(Severity.Major, Category.Reliability, "a.cs", 1, title, "why", "fix", []) { Role = role };

    [Fact]
    public void EachRole_ReadsItsOwnBudget()
    {
        var config = Config(
            (PromptCatalog.ArchitectureRole, 2, 3),
            (PromptCatalog.SecurityRole, 3, 1),
            (PromptCatalog.UxDxRole, 1, 5));

        config.For(PromptCatalog.ArchitectureRole).Should().Be(new RoleGate(2, 3));
        config.For(PromptCatalog.SecurityRole).MaxRounds.Should().Be(3);
        config.For(PromptCatalog.UxDxRole).MaxRounds.Should().Be(1);
    }

    [Fact]
    public void AStagesRoundBudget_IsTheWidestOfItsRoles()
    {
        // The stage still counts rounds once; a role simply stops taking part when its own budget is
        // spent. So the stage runs as long as its most patient role.
        var config = Config(
            (PromptCatalog.ArchitectureRole, 2, 3),
            (PromptCatalog.SecurityRole, 3, 3),
            (PromptCatalog.UxDxRole, 1, 3));

        config.For(Stage.CodeReview).MaxRounds.Should().Be(3);
    }

    [Fact]
    public void ARoleWithNoBudgetLeft_DoesNotTakePartInTheRound()
    {
        var config = Config(
            (PromptCatalog.ArchitectureRole, 2, 3),
            (PromptCatalog.SecurityRole, 3, 3),
            (PromptCatalog.UxDxRole, 1, 3));

        config.RolesForRound(Stage.CodeReview, round: 1).Should().HaveCount(3);
        config.RolesForRound(Stage.CodeReview, round: 2).Should().BeEquivalentTo(
            [PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole], "performance had one round");
        config.RolesForRound(Stage.CodeReview, round: 3).Should().BeEquivalentTo([PromptCatalog.SecurityRole]);
    }

    // ---------- a finding is counted against ITS role's threshold ----------

    [Fact]
    public void EachRolesFindings_AreCountedAgainstThatRolesThreshold()
    {
        var config = Config(
            (PromptCatalog.ArchitectureRole, 2, 2),
            (PromptCatalog.SecurityRole, 2, 0),
            (PromptCatalog.UxDxRole, 2, 2));

        var gate = GateRule.Evaluate(
            [
                From(PromptCatalog.ArchitectureRole, "a layer reaches around another"),
                From(PromptCatalog.ArchitectureRole, "two implementations of one thing"),
                From(PromptCatalog.SecurityRole, "token compared with =="),
            ],
            [],
            role => config.For(role).Threshold);

        gate.Passed.Should().BeFalse("security allows none and has one");
        gate.OverThreshold.Should().BeEquivalentTo([PromptCatalog.SecurityRole],
            "architecture is at its threshold of two, which passes");
    }

    [Fact]
    public void EveryRoleUnderItsOwnThreshold_Passes()
    {
        var config = Config((PromptCatalog.ArchitectureRole, 2, 2), (PromptCatalog.SecurityRole, 2, 1));

        var gate = GateRule.Evaluate(
            [
                From(PromptCatalog.ArchitectureRole, "one"),
                From(PromptCatalog.ArchitectureRole, "two"),
                From(PromptCatalog.SecurityRole, "one"),
            ],
            [],
            role => config.For(role).Threshold);

        gate.Passed.Should().BeTrue();
        gate.OverThreshold.Should().BeEmpty();
    }

    [Fact]
    public void AFindingWithNoRole_IsCountedSomewhere_NeverDropped()
    {
        // A plan-stage finding, or one from an older session file. Silently not counting it would
        // make a round pass on findings nobody looked at.
        var gate = GateRule.Evaluate(
            [new Finding(Severity.Blocking, Category.Security, "", 0, "no rollback", "why", "fix", [])],
            [],
            _ => 0);

        gate.Passed.Should().BeFalse();
        gate.GatingCount.Should().Be(1);
    }

    // ---------- the settings ----------

    [Fact]
    public void ARolesOwnKeys_OutrankTheStageKeys()
    {
        var env = new Dictionary<string, string>
        {
            ["COAI_MAX_ROUNDS_CODE"] = "2",
            ["COAI_ROUNDS_SECURITYRELIABILITY"] = "3",
            ["COAI_THRESHOLD_UXDXPERFORMANCE"] = "0",
        };
        var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

        settings.Rounds.For(PromptCatalog.SecurityRole).MaxRounds.Should().Be(3);
        settings.Rounds.For(PromptCatalog.ArchitectureRole).MaxRounds.Should().Be(2, "the stage key fills in");
        settings.Rounds.For(PromptCatalog.UxDxRole).Threshold.Should().Be(0);
    }

    [Fact]
    public void TheShippedDefaults_AreStillStricterOnThePlanThanOnTheDiff()
    {
        var config = new PanelConfig();

        config.For(PromptCatalog.PlanRole).Should().Be(new RoleGate(3, 2));
        config.For(PromptCatalog.ArchitectureRole).Threshold.Should().Be(3);
    }

    [Fact]
    public void AThresholdOfZero_SurvivesTheServer_BecauseThePanelAcceptsIt()
    {
        // A pre-existing disagreement between the halves, found by the per-role test above: the
        // reader required a POSITIVE number, so a person who set "any finding blocks" got the
        // shipped default of three instead, silently. The panel has always accepted zero and has a
        // test saying so.
        var settings = PanelSettings.FromEnvironment(
            name => name == "COAI_GATE_THRESHOLD" ? "0" : null);

        settings.Rounds.For(PromptCatalog.ArchitectureRole).Threshold.Should().Be(0);
        settings.Rounds.For(PromptCatalog.PlanRole).Threshold.Should().Be(0);
    }

    [Fact]
    public void ARoundBudgetOfZero_IsStillRefused_BecauseItWouldGateNothing()
    {
        PanelSettings.FromEnvironment(name => name == "COAI_MAX_ROUNDS" ? "0" : null)
            .Rounds.For(PromptCatalog.ArchitectureRole).MaxRounds.Should().Be(
                PanelConfig.CodeDefault.MaxRounds, "zero rounds would run no review at all");
    }

    // ---------- the machine revises for roles that can still act ----------

    [Fact]
    public void ARoleOutOfRoundsButStillOver_DoesNotKeepTheStageRevising()
    {
        // Performance has one round and is over its threshold. Revising for its sake would loop
        // until the WIDEST role ran out, asking nothing new of anybody — which is what a
        // stage-shaped budget did.
        var config = new PanelConfig(new Dictionary<string, RoleGate>
        {
            ["PlanCritique"] = PanelConfig.PlanDefault,
            ["Architecture"] = new(3, 9),
            ["SecurityReliability"] = new(3, 9),
            ["UxDxPerformance"] = new(1, 0),
        });
        var state = new SessionState("s", "D:/r", "main", config) with
        {
            Stage = Stage.CodeReview,
            RoundsRunThisStage = 0,
        };

        var gate = GateRule.Evaluate(
            [From(PromptCatalog.UxDxRole, "a list grows without bound")],
            [],
            role => config.For(role).Threshold);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(state, gate, ReviewerSummary.AllAnswered(3));
        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>(
            "the only role with work left cannot run again, so the rounds are spent");
    }

    [Fact]
    public void ARoleThatStillHasRounds_KeepsTheStageRevising()
    {
        var config = new PanelConfig(new Dictionary<string, RoleGate>
        {
            ["PlanCritique"] = PanelConfig.PlanDefault,
            ["Architecture"] = new(1, 9),
            ["SecurityReliability"] = new(3, 0),
            ["UxDxPerformance"] = new(1, 9),
        });
        var state = new SessionState("s", "D:/r", "main", config) with { Stage = Stage.CodeReview };

        var gate = GateRule.Evaluate(
            [From(PromptCatalog.SecurityRole, "token compared with ==")],
            [],
            role => config.For(role).Threshold);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(state, gate, ReviewerSummary.AllAnswered(3));
        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>("security has three rounds and has used one");
    }
}
