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
/// <para>The four code reviewers do different jobs and deserve different budgets: architecture may
/// be worth two passes with different lenses, security three, performance one, conventions one. One
/// number for the whole stage forces the cheapest role to pay for the most expensive one.</para>
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

        // Conventions is not in the config above and takes part anyway, on its DEFAULT gate — an
        // unconfigured role is a role nobody changed, not a role nobody wants. This assertion used
        // to be a bare `HaveCount(3)` and went red the day Conventions became a role; naming the
        // roles is what makes the next addition read as a decision instead of an off-by-one.
        config.RolesForRound(Stage.CodeReview, round: 1).Should().BeEquivalentTo(
            [PromptCatalog.ConventionsRole, PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole]);
        config.RolesForRound(Stage.CodeReview, round: 2).Should().BeEquivalentTo(
            [PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole],
            "performance had one round, and conventions defaults to one");
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
    public void COAI_ENABLED_SwitchesOneCodeRoleOff_AndNothingElse()
    {
        var env = new Dictionary<string, string> { ["COAI_ENABLED_ARCHITECTURE"] = "false" };
        var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

        settings.Rounds.For(PromptCatalog.ArchitectureRole).Enabled.Should().BeFalse();
        settings.Rounds.For(PromptCatalog.SecurityRole).Enabled.Should().BeTrue();
        settings.Rounds.EnabledRolesOf(Stage.CodeReview).Should().BeEquivalentTo(
            [PromptCatalog.ConventionsRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole]);
    }

    [Fact]
    public void ARoleIsOnUnlessTheVariableSaysOffInSoManyWords()
    {
        // Not the inverse of the ordinary flag helper, and the asymmetry is the point: a role
        // wrongly ON costs one extra pass, a role wrongly OFF is a review nobody performed with
        // nothing on screen saying so. Absent, empty, "no", a typo and a shell-mangled value all
        // leave the reviewer working; only the four spellings of false stop it.
        foreach (var value in new[] { "", "no", "off", "0.0", "FaLsE", "true", " false " })
        {
            var env = new Dictionary<string, string> { ["COAI_ENABLED_UXDXPERFORMANCE"] = value };
            var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

            settings.Rounds.For(PromptCatalog.UxDxRole).Enabled.Should().BeTrue($"'{value}' does not say off");
        }

        foreach (var value in new[] { "0", "false", "FALSE", "False" })
        {
            var env = new Dictionary<string, string> { ["COAI_ENABLED_UXDXPERFORMANCE"] = value };
            var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

            settings.Rounds.For(PromptCatalog.UxDxRole).Enabled.Should().BeFalse($"'{value}' says off");
        }
    }

    [Fact]
    public void NoEnvironmentAtAll_LeavesEveryRoleOn()
    {
        var settings = PanelSettings.FromEnvironment(_ => null);

        settings.Rounds.EnabledRolesOf(Stage.CodeReview).Should().HaveCount(4);
        settings.Rounds.For(PromptCatalog.PlanRole).Enabled.Should().BeTrue();
    }

    [Fact]
    public void ThePlanRole_CannotBeSwitchedOffByAVariable()
    {
        // Code review only. The boundary refuses rather than trusting that nobody writes the key.
        var env = new Dictionary<string, string> { ["COAI_ENABLED_PLANCRITIQUE"] = "false" };
        var settings = PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

        settings.Rounds.For(PromptCatalog.PlanRole).Enabled.Should().BeTrue();
        settings.Rounds.RolesForRound(Stage.PlanReview, round: 1).Should().ContainSingle();
    }

    [Fact]
    public void TheShippedDefaults_AreOneRoundEverywhere_AndAreThePanelsNumbers()
    {
        // These four numbers are also the panel's DEFAULTS, and that is load-bearing rather than
        // tidy: the panel writes a COAI_ROUNDS_* key only where the value DIFFERS from its own
        // default, so a pristine install sends none and THIS is what runs. The two disagreed for a
        // day — panel showing one round, server running three. `panelServerDefaultsAgreement.test.ts`
        // reads these constants out of the C# and fails when they drift again.
        var config = new PanelConfig();

        config.For(PromptCatalog.PlanRole).Should().Be(new RoleGate(1, 6));
        config.For(PromptCatalog.ArchitectureRole).Should().Be(new RoleGate(1, 5));
        config.For(PromptCatalog.ConventionsRole).Should().Be(new RoleGate(1, 5),
            "a role nobody configured runs on the code default like every other code role");
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
    // ---------- a role the operator switched off does not take part ----------

    /// <remarks>
    /// Asked for on 2026-09-08: a checkbox on each of the four code-review boxes, and unticking one
    /// means "this role does not take part in the round at all" — not a reviewer that runs and is
    /// then ignored. Rounds could already keep a role out of a LATER round; nothing could keep it
    /// out of the first one except lying to a control that means something else.
    /// </remarks>
    [Fact]
    public void ARoleSwitchedOff_TakesPartInNoRoundAtAll()
    {
        var config = new PanelConfig(
            new Dictionary<string, RoleGate>
            {
                [PromptCatalog.ConventionsRole] = new(2, 3),
                [PromptCatalog.ArchitectureRole] = new(2, 3, Enabled: false),
                [PromptCatalog.SecurityRole] = new(2, 3),
                [PromptCatalog.UxDxRole] = new(2, 3),
            },
            StagePolicy.Human);

        config.RolesForRound(Stage.CodeReview, round: 1).Should().NotContain(PromptCatalog.ArchitectureRole);
        config.RolesForRound(Stage.CodeReview, round: 2).Should().NotContain(PromptCatalog.ArchitectureRole);
        config.RolesForRound(Stage.CodeReview, round: 1).Should().BeEquivalentTo(
            [PromptCatalog.ConventionsRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole]);
    }

    [Fact]
    public void AStagesBudget_IgnoresTheRolesThatAreSwitchedOff()
    {
        // The point of the requirement: a role that is off must not keep the stage running rounds
        // nobody reviews, and must not lend the stage its threshold either.
        var config = new PanelConfig(
            new Dictionary<string, RoleGate>
            {
                [PromptCatalog.ConventionsRole] = new(1, 2),
                [PromptCatalog.ArchitectureRole] = new(5, 9, Enabled: false),
                [PromptCatalog.SecurityRole] = new(2, 3),
                [PromptCatalog.UxDxRole] = new(1, 2),
            },
            StagePolicy.Human);

        config.For(Stage.CodeReview).MaxRounds.Should().Be(2, "architecture's five rounds are switched off");
        config.For(Stage.CodeReview).Threshold.Should().Be(3, "and so is its threshold of nine");
    }

    [Fact]
    public void EveryCodeRoleSwitchedOff_IsAnEmptyStageRatherThanAThrow()
    {
        // The all-off case is DECIDED here rather than crashing here: `Max` over an empty sequence
        // throws, and the round that must be refused is refused by the caller reading this.
        var config = new PanelConfig(
            PanelConfig.CodeRoleNames.ToDictionary(r => r, _ => new RoleGate(2, 3, Enabled: false)),
            StagePolicy.Human);

        config.EnabledRolesOf(Stage.CodeReview).Should().BeEmpty();
        config.For(Stage.CodeReview).Should().Be(new StageGate(0, 0));
        config.RolesForRound(Stage.CodeReview, round: 1).Should().BeEmpty();
    }

    [Fact]
    public void ThePlanRole_HasNoSwitchAndIsNeverSkipped()
    {
        // Code review only, by the operator's ruling. A plan stage with one role and a switch that
        // turns it off is a different feature nobody asked for.
        var config = new PanelConfig(
            new Dictionary<string, RoleGate> { [PromptCatalog.PlanRole] = new(1, 6) },
            StagePolicy.Human);

        config.For(PromptCatalog.PlanRole).Enabled.Should().BeTrue();
        config.RolesForRound(Stage.PlanReview, round: 1).Should().BeEquivalentTo([PromptCatalog.PlanRole]);
    }

    [Fact]
    public void ARoleNobodyConfigured_IsOn()
    {
        // Absent means ON, everywhere: an older stored record, a config written before the switch
        // existed, a role nobody has touched. The one thing that must never happen is a role
        // silently not reviewing because a key was missing.
        var config = Config((PromptCatalog.ArchitectureRole, 2, 3));

        config.For(PromptCatalog.SecurityRole).Enabled.Should().BeTrue();
        config.RolesForRound(Stage.CodeReview, round: 1).Should().Contain(PromptCatalog.SecurityRole);
    }

    /// <summary>
    /// Which roles a stage HAS comes from the catalog this config was built with.
    /// </summary>
    /// <remarks>
    /// It came from a hard-coded array until the catalog became data, which is why a person's own
    /// role could not take part in a round however it was configured. The gate itself is unchanged:
    /// a role with no gate of its own still falls back to its stage's shipped default, which is what
    /// makes a new role work with no settings at all.
    /// </remarks>
    [Fact]
    public void ACustomRoleInTheCatalog_TakesPartInItsStagesRound()
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry("Requirements", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general", "General", "Whether the requirement is met.")]),
        ]);
        var config = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog };

        config.RolesForRound(Stage.CodeReview, round: 1).Should().Contain("Requirements");
        config.For("Requirements").Should().Be(PanelConfig.CodeDefault,
            "a role nobody gave a budget gets its stage's shipped one");
    }

    [Fact]
    public void ACustomRoleSwitchedOffInTheCatalog_IsInNoRound()
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(PromptCatalog.ArchitectureRole, Active: false),
        ]);
        var config = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog };

        config.RolesForRound(Stage.CodeReview, round: 1).Should().NotContain(PromptCatalog.ArchitectureRole);
        config.EnabledRolesOf(Stage.CodeReview).Should().NotContain(PromptCatalog.ArchitectureRole);
    }
}
