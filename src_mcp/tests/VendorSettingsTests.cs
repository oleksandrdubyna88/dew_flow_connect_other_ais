using CoaiMcp.Core.Rounds;
using Xunit;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer the operator added in the panel is DATA — a name, a runtime, a base URL — and never
/// a release. These hold that promise from the environment through to the argv.
/// </summary>
public sealed class VendorSettingsTests
{
    private static PanelSettings From(Dictionary<string, string> env) =>
        PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name));

    [Fact]
    public void NoVendorsConfigured_IsTheShippedPair()
    {
        var settings = From([]);

        settings.Providers.Select(p => p.Provider).Should().Equal("codex", "antigravity");
    }

    [Fact]
    public void TheJsonList_CarriesWhatACommaListCouldNot()
    {
        var settings = From(new()
        {
            ["COAI_VENDORS"] =
                """
                [{"id":"gemini","runtime":"gemini","model":"gemini-pro-latest","baseUrl":""},
                 {"id":"mistral","runtime":"codex","model":"mistral-large","baseUrl":"https://api.mistral.ai/v1"}]
                """,
        });

        settings.Providers.Should().HaveCount(2);
        settings.Providers[0].Runtime.Should().Be("gemini");
        settings.Providers[1].Should().Match<ProviderSettings>(p =>
            p.Provider == "mistral" && p.Model == "mistral-large" && p.BaseUrl == "https://api.mistral.ai/v1");
    }

    [Fact]
    public void AVendorWithoutAnId_IsNotAVendor() =>
        PanelSettings.ParseVendors("""[{"runtime":"codex"},{"id":"  "},{"id":"ok"}]""")
            .Select(p => p.Provider).Should().Equal("ok");

    [Fact]
    public void MalformedJson_FallsBackRatherThanRunningAHalfWrittenList()
    {
        var settings = From(new() { ["COAI_VENDORS"] = "[{oops" });

        // A review run against a vendor list somebody half-wrote is worse than the default one.
        settings.Providers.Select(p => p.Provider).Should().Equal(["codex", "antigravity"]);
    }

    [Fact]
    public void AnUnknownRuntime_BecomesCodex_TheOneThatTakesABaseUrl() =>
        PanelSettings.ParseVendors("""[{"id":"x","runtime":"llama.cpp"}]""")[0].Runtime.Should().Be("codex");

    [Fact]
    public void ACustomVendor_GetsItsOwnProviderOverrides_AndItsOwnKeyVariable()
    {
        var invocation = new CustomCodexRuntime("mistral", "https://api.mistral.ai/v1").Build(
            RoleCatalog.ArchitectureRole, "review", "D:/wt", "D:/schema.json", "D:/out",
            new ReviewerSettings("mistral") { ApiKey = "sk-mistral" });

        invocation.Request.Arguments.Should().Contain("model_provider=mistral");
        invocation.Request.Arguments.Should().Contain("model_providers.mistral.base_url=https://api.mistral.ai/v1");
        invocation.Request.Arguments.Should().Contain("model_providers.mistral.env_key=MISTRAL_API_KEY");
        invocation.Request.Arguments.Should().Contain("--ephemeral", "it inherits every codex safety flag");
        invocation.Request.Environment.Should().ContainKey("MISTRAL_API_KEY");
        string.Join(' ', invocation.Request.Arguments).Should().NotContain("sk-mistral");
    }

    [Theory]
    [InlineData("mistral", "MISTRAL_API_KEY")]
    [InlineData("open-router", "OPEN_ROUTER_API_KEY")]
    [InlineData("z.ai", "Z_AI_API_KEY")]
    public void TheKeyVariable_IsDerivedFromTheId_SoNothingHasToBeKeptInStep(string id, string variable) =>
        CustomCodexRuntime.KeyVariableFor(id).Should().Be(variable);

    // ---------- a vendor per STAGE ----------

    [Fact]
    public void AVendorListWithoutTheFlags_ReviewsBothStages()
    {
        // The update path. A list written by an older extension says nothing about stages, and a
        // vendor that silently stopped reviewing either would be a gate that quietly got weaker.
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"codex","runtime":"codex","model":"gpt-5.6"}]""");

        vendors.Should().ContainSingle();
        vendors[0].Serves(isPlan: true).Should().BeTrue();
        vendors[0].Serves(isPlan: false).Should().BeTrue();
    }

    [Fact]
    public void AVendorNarrowedToPlans_DoesNotServeTheCodeStage()
    {
        // The setting the measurement asked for: local was 19 % useful on a plan and 3 % on code.
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"local","runtime":"local","model":"qwen","plan":true,"code":false}]""");

        vendors[0].Serves(isPlan: true).Should().BeTrue();
        vendors[0].Serves(isPlan: false).Should().BeFalse();
    }

    [Fact]
    public void TheMasterSwitchBeatsBothStageFlags()
    {
        // Otherwise "off" and "on for plans" would contradict each other and a reader could not tell
        // which the gate obeyed.
        var off = new ProviderSettings("codex") { Enabled = false, Plan = true, Code = true };

        off.Serves(isPlan: true).Should().BeFalse();
        off.Serves(isPlan: false).Should().BeFalse();
    }

    [Fact]
    public void AVendorServingNeitherStage_ServesNothing()
    {
        var neither = new ProviderSettings("codex") { Plan = false, Code = false };

        neither.Serves(isPlan: true).Should().BeFalse();
        neither.Serves(isPlan: false).Should().BeFalse();
    }
}
