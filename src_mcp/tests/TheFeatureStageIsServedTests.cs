using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which vendor serves the feature stage, which keys its budget reads, and how many epics a plan
/// needs before the gate runs for it.
/// </summary>
/// <remarks>
/// <para>S2.1 of the feature-review plan (§4.2, §4.4, D17). Three settings cross the panel seam here
/// and every one of them is shaped by the lesson the vendor stage flags taught: ABSENT must mean
/// something on its own. A vendor's <c>feature</c> tick is absent on every settings file written
/// before this stage existed, and absent is FALSE — the opposite of the plan and code ticks, because
/// a vendor nobody ticked for a feature review must not silently be sent one, and a Team server
/// never is in this version (D10).</para>
/// <para>The budget keys are the stage's OWN — <c>COAI_MAX_ROUNDS_FEATURE</c>, <c>COAI_THRESHOLD_FEATURE</c>
/// — because without them the stage would read the <c>_CODE</c> keys in silence, and a person who
/// tightened their code gate would have tightened a gate they never meant to.</para>
/// </remarks>
public sealed class TheFeatureStageIsServedTests
{
    // ---------- who serves it ----------

    [Fact]
    public void AVendorTickedForFeatures_ServesTheFeatureStage() =>
        new ProviderSettings("grok") { Runtime = "api", Feature = true }.Serves(Stage.FeatureReview).Should().BeTrue();

    [Fact]
    public void AVendorNotTicked_DoesNot_AndAbsentIsNotTicked()
    {
        // Unlike Plan and Code, which default to TRUE so an older settings file keeps its gate.
        new ProviderSettings("codex").Serves(Stage.FeatureReview).Should().BeFalse("absent means no");
        new ProviderSettings("codex") { Feature = true }.Serves(Stage.CodeReview).Should().BeTrue("the other ticks are untouched");
    }

    [Fact]
    public void ATeamServerVendor_NeverServesTheFeatureStage_InThisVersion() =>
        new ProviderSettings("team-codex") { Runtime = "remote", BaseUrl = "https://coai.example.com", Feature = true }
            .Serves(Stage.FeatureReview).Should().BeFalse("D10: the Team server takes no part in v1, whatever the tick says");

    [Fact]
    public void ADisabledVendor_ServesNothing_HoweverItIsTicked() =>
        new ProviderSettings("grok") { Runtime = "api", Feature = true, Enabled = false }
            .Serves(Stage.FeatureReview).Should().BeFalse("the master switch outranks every stage tick");

    [Fact]
    public void TheFeatureTick_IsReadFromTheVendorList_AndAbsentIsFalse()
    {
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"grok","runtime":"api","feature":true},{"id":"codex","feature":false},{"id":"agy"}]""");

        vendors.Single(v => v.Provider == "grok").Feature.Should().BeTrue();
        vendors.Single(v => v.Provider == "codex").Feature.Should().BeFalse();
        vendors.Single(v => v.Provider == "agy").Feature.Should().BeFalse("a row written by an older extension carries no field");
    }

    // ---------- the stage's own budget keys ----------

    private static PanelSettings With(params (string Key, string Value)[] env) =>
        PanelSettings.FromEnvironment(name => env.FirstOrDefault(e => e.Key == name).Value);

    [Fact]
    public void TheFeatureRole_TakesTheFeatureDefault_WhenNothingIsSet()
    {
        var gate = With().Rounds.For(RoleCatalog.FeatureRole);

        gate.MaxRounds.Should().Be(PanelConfig.FeatureDefault.MaxRounds);
        gate.Threshold.Should().Be(PanelConfig.FeatureDefault.Threshold);
        gate.Enabled.Should().BeTrue();
    }

    [Fact]
    public void TheFeatureStage_ReadsItsOwnKeys()
    {
        var settings = With(("COAI_MAX_ROUNDS_FEATURE", "3"), ("COAI_THRESHOLD_FEATURE", "0"));

        settings.Rounds.For(RoleCatalog.FeatureRole).MaxRounds.Should().Be(3);
        settings.Rounds.For(RoleCatalog.FeatureRole).Threshold.Should().Be(0, "zero is a legitimate threshold");
        settings.Rounds.For(RoleCatalog.ArchitectureRole).MaxRounds.Should().Be(
            PanelConfig.CodeDefault.MaxRounds, "the feature keys reach no code role");
    }

    /// <summary>
    /// The trap the keys exist to close: without them the stage read <c>_CODE</c> in silence.
    /// </summary>
    [Fact]
    public void TheCodeStagesKeys_DoNotReachTheFeatureRole()
    {
        var settings = With(("COAI_MAX_ROUNDS_CODE", "4"), ("COAI_THRESHOLD_CODE", "1"));

        settings.Rounds.For(RoleCatalog.ArchitectureRole).MaxRounds.Should().Be(4, "the premise: the code key applies");
        settings.Rounds.For(RoleCatalog.FeatureRole).MaxRounds.Should().Be(
            PanelConfig.FeatureDefault.MaxRounds, "a person tightening the code gate did not touch the feature gate");
        settings.Rounds.For(RoleCatalog.FeatureRole).Threshold.Should().Be(PanelConfig.FeatureDefault.Threshold);
    }

    [Fact]
    public void TheLegacyPair_StillFillsInForTheFeatureRole() =>
        With(("COAI_MAX_ROUNDS", "2")).Rounds.For(RoleCatalog.FeatureRole).MaxRounds.Should().Be(2,
            "the originals fill in for everything, as they always have");

    /// <summary>"The feature gate is enabled" is the role's own switch — no second setting (§4.2).</summary>
    [Fact]
    public void TheFeatureRolesSwitch_IsTheGate()
    {
        var off = With(("COAI_ENABLED_FEATUREREVIEW", "false"));

        off.Rounds.For(RoleCatalog.FeatureRole).Enabled.Should().BeFalse();
        off.Rounds.EnabledRolesOf(Stage.FeatureReview).Should().BeEmpty("nobody is left to review a feature");
        With().Rounds.EnabledRolesOf(Stage.FeatureReview).Should().Equal(RoleCatalog.FeatureRole);
    }

    // ---------- D17: three or more epics ----------

    [Theory]
    [InlineData(null, 3)]
    [InlineData("5", 5)]
    [InlineData("1", 1)]
    [InlineData("0", 3)]
    [InlineData("many", 3)]
    public void TheMinimumEpics_IsThree_UnlessAPersonSetIt(string? value, int expected) =>
        PanelSettings.FromEnvironment(name => name == "COAI_FEATURE_MIN_EPICS" ? value : null)
            .FeatureMinEpics.Should().Be(expected);
}
