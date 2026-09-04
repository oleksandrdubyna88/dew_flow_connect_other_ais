using Xunit;
using FluentAssertions;
using CoaiMcp.Server;
using CoaiMcp.Runners.Processes;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// A local vendor with no model is not a healthy vendor.
/// </summary>
/// <remarks>
/// <para>Found by this repository's bench on its first real campaign. `providers` reported the local
/// reviewer as <c>own auth — a local engine at http://127.0.0.1:11434/v1</c>, which is true and
/// useless: every reviewer it ran answered</para>
/// <code>
/// exit 70: the local engine answered 400: {"error":{"message":"model is required"}}
/// </code>
/// <para>Three of nine reviewers in a code round, gone, and the health probe had said the vendor was
/// fine. The probe tests the ENDPOINT, and an endpoint cannot know which model nobody named — so it
/// has to look at the configuration too. This is the same blind spot the retired-Gemini diagnosis
/// exists for: a vendor that answers a probe and cannot answer a round.</para>
/// </remarks>
public sealed class LocalVendorNeedsAModelTests
{
    private static PanelService ServiceWith(ProviderSettings vendor) =>
        new(
            new PanelSettings { Providers = [vendor], DataDir = Directory.CreateTempSubdirectory("coai-local-").FullName },
            VaultKeys.None("no vault in tests"),
            default,
            new ProcessLauncher(),
            Logger.None);

    [Fact]
    public async Task ALocalVendorWithNoModel_IsReportedUnavailable()
    {
        var answer = await ServiceWith(new ProviderSettings("local") { Runtime = "local" }).ProvidersAsync();

        answer.Should().Contain("unavailable");
        answer.Should().Contain("no model", "the sentence has to say what to do about it");
    }

    [Fact]
    public async Task ALocalVendorWithAModel_IsStillReportedHealthy()
    {
        var answer = await ServiceWith(
            new ProviderSettings("local") { Runtime = "local", Model = "qwen3.5" }).ProvidersAsync();

        answer.Should().Contain("own auth");
        answer.Should().NotContain("no model");
    }
}
