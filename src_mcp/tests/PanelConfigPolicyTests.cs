using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a <see cref="PanelConfig"/> says about policy auto, and who may write it
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.2).
/// </summary>
/// <remarks>
/// A config made in code always says <c>true</c> or <c>false</c>; only the constructor a session FILE is read through can
/// leave the marker absent, because absent is what a file from before policy auto holds. Pure: no store, no service.
/// </remarks>
public sealed class PanelConfigPolicyTests
{
    public static TheoryData<StagePolicy> EveryPolicy() => [.. Enum.GetValues<StagePolicy>()];

    [Theory]
    [MemberData(nameof(EveryPolicy))]
    public void APolicyNamed_IsThatPolicy_AndNeverAuto(StagePolicy policy)
    {
        var made = new Dictionary<string, PanelConfig>
        {
            ["positional"] = new PanelConfig(null, policy),
            ["named"] = new PanelConfig(OnExhausted: policy),
            ["uniform"] = PanelConfig.Uniform(3, 2, policy),
        };

        foreach (var (how, config) in made)
        {
            config.OnExhausted.Should().Be(policy, how);
            config.Autonomous.Should().BeFalse($"{how}: a named policy is never auto, and never absent");
            config.IsAutonomous.Should().BeFalse(how);
            config.PredatesAuto.Should().BeFalse(how);
        }
    }

    public static TheoryData<string> DefaultsBuiltInCode() =>
        ["new()", "new(roles)", "new { Catalog }", "Uniform", "PanelSettings().Rounds", "FromEnvironment(absent).Rounds"];

    private static PanelConfig BuiltByDefault(string label) => label switch
    {
        "new()" => new PanelConfig(),
        "new(roles)" => new PanelConfig(PanelConfig.AllRoles.ToDictionary(r => r, _ => new RoleGate(1, 5))),
        "new { Catalog }" => new PanelConfig { Catalog = RoleCatalog.Builtin },
        "Uniform" => PanelConfig.Uniform(1, 6),
        "PanelSettings().Rounds" => new PanelSettings().Rounds,
        "FromEnvironment(absent).Rounds" => PanelSettings.FromEnvironment(_ => null).Rounds,
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    [Theory]
    [MemberData(nameof(DefaultsBuiltInCode))]
    public void NoPolicyNamed_IsPolicyAuto_WhereverADefaultIsBuilt(string label)
    {
        var config = BuiltByDefault(label);

        config.OnExhausted.Should().Be(StagePolicy.GoodEnough, label);
        config.Autonomous.Should().BeTrue(label);
        config.IsAutonomous.Should().BeTrue(label);
    }

    [Fact]
    public void OnlyTheFilesConstructor_LeavesTheMarkerAbsent()
    {
        var absent = new PanelConfig(roles: null, autonomous: null);
        absent.Autonomous.Should().BeNull();
        absent.PredatesAuto.Should().BeTrue();
        absent.IsAutonomous.Should().BeFalse();
        absent.OnExhausted.Should().Be(StagePolicy.Human, "a file with no onExhausted always read as Human");
        absent.Roles.Keys.Should().BeEquivalentTo(PanelConfig.AllRoles, "no roles in the file means every shipped role");

        new PanelConfig(roles: null, autonomous: false).Autonomous.Should().BeFalse();

        var auto = new PanelConfig(roles: null, autonomous: true, StagePolicy.GoodEnough);
        auto.Autonomous.Should().BeTrue();
        auto.IsAutonomous.Should().BeTrue();
        auto.OnExhausted.Should().Be(StagePolicy.GoodEnough);

        foreach (var name in new[] { nameof(PanelConfig.Autonomous), nameof(PanelConfig.OnExhausted) })
        {
            typeof(PanelConfig).GetProperty(name)!.SetMethod!.IsPrivate.Should().BeTrue(
                $"{name} is written by the two constructors and UnderPolicyOf, and nothing outside the type");
        }
    }

    public static TheoryData<string> Currents() => ["auto", "continue", "human", "escalate", "good_enough"];

    private static PanelConfig Current(string label) => label switch
    {
        // Built through the file constructor so that this test holds before and after the code constructor's default flips.
        "auto" => new PanelConfig(roles: null, autonomous: true, StagePolicy.GoodEnough),
        "continue" => new PanelConfig(OnExhausted: StagePolicy.Continue),
        "human" => new PanelConfig(OnExhausted: StagePolicy.Human),
        "escalate" => new PanelConfig(OnExhausted: StagePolicy.Escalate),
        "good_enough" => new PanelConfig(OnExhausted: StagePolicy.GoodEnough),
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    [Theory]
    [MemberData(nameof(Currents))]
    public void UnderPolicyOf_TakesBothHalves_AndKeepsTheRest(string label)
    {
        var current = Current(label);
        var bag = new Dictionary<string, System.Text.Json.JsonElement> { ["future"] = System.Text.Json.JsonDocument.Parse("1").RootElement.Clone() };
        var targets = new[]
        {
            new PanelConfig(roles: null, autonomous: null) with { Unknown = bag, SecurityLane = new RoleGate(5, 1, true) },
            new PanelConfig(OnExhausted: StagePolicy.Human) with { Unknown = bag, SecurityLane = new RoleGate(5, 1, true) },
        };

        foreach (var target in targets)
        {
            var result = target.UnderPolicyOf(current);

            result.OnExhausted.Should().Be(current.OnExhausted, label);
            result.Autonomous.Should().NotBeNull().And.Be(current.IsAutonomous, label);
            result.Roles.Should().BeSameAs(target.Roles);
            result.SecurityLane.Should().BeSameAs(target.SecurityLane);
            result.Unknown.Should().BeSameAs(target.Unknown);
            result.Catalog.Should().BeSameAs(target.Catalog);
            (!result.IsAutonomous || result.OnExhausted == StagePolicy.GoodEnough).Should().BeTrue("policy auto is always good_enough");
        }
    }
}
