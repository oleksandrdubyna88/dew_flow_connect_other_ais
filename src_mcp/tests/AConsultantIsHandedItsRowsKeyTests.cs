using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation launch carries the row's vault key exactly when the row authenticates with one — an api row, or a
/// codex row on somebody else's endpoint (research/PLAN_one_model_catalog.md, epic 2, story 3).
/// </summary>
/// <remarks>
/// A claude or plain codex consultant keeps its CLI's own sign-in, as it always has: handing it a key from the vault
/// would quietly move it from the person's subscription to per-token billing.
/// </remarks>
public sealed class AConsultantIsHandedItsRowsKeyTests
{
    private static readonly VaultKeys Vault = new(
        new Dictionary<string, string> { ["openrouter"] = "sk-or", ["qwen"] = "sk-qw", ["claude"] = "sk-ant", ["codex"] = "sk-oa" }, string.Empty);

    private static string KeyFor(ProviderSettings row) =>
        ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), "D:/data", Vault, Core.Api.ApiOverrides.None).ApiKey;

    [Fact]
    public void AnEndpointRow_IsHandedItsKey()
    {
        KeyFor(new ProviderSettings("openrouter") { Runtime = "codex", BaseUrl = "https://openrouter.example/api/v1" }).Should().Be("sk-or");
    }

    [Fact]
    public void AnApiRow_IsHandedItsKey()
    {
        KeyFor(new ProviderSettings("qwen") { Runtime = "api", BaseUrl = "https://q.example/v1" }).Should().Be("sk-qw");
    }

    [Theory]
    [InlineData("claude", "claude")]
    [InlineData("codex", "codex")]
    public void ACliOnItsOwnService_KeepsItsSignIn(string id, string runtime)
    {
        KeyFor(new ProviderSettings(id) { Runtime = runtime }).Should().BeEmpty("a vault key would move it onto per-token billing");
    }
}

/// <summary>An api consultant runs with its module's settings — the row's over the calibrated defaults — as an api reviewer and a question row do.</summary>
public sealed class AnApiConsultantRunsWithItsModulesSettingsTests
{
    [Fact]
    public void TheRowsEffortAndTheModulesCeiling_ReachTheLaunch()
    {
        var row = PanelSettings.ParseVendors(
            """[{"id":"glm","runtime":"api","model":"glm-5.3","baseUrl":"https://open.bigmodel.example/api/paas/v4","dialect":"glm","effort":"high"}]""")[0];

        var settings = ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), "D:/data", VaultKeys.None("t"), Core.Api.ApiOverrides.None);
        var expected = ApiRowView.Of(row, Core.Api.ApiOverrides.None).Effective;

        settings.ReasoningEffort.Should().Be("high").And.Be(expected.Effort);
        settings.MaxTokens.Should().Be(expected.MaxTokens);
        settings.ThinkingOn.Should().Be(expected.ThinkingOn);
    }
}
