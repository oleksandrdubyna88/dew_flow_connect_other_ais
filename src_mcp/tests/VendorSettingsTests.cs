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
            ReviewRole.Architecture, "review", "D:/wt", "D:/schema.json", "D:/out",
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
}
