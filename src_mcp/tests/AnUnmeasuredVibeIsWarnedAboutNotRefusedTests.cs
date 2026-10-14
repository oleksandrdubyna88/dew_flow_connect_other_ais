using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The owner's decision of 2026-10-13: "Vibe must WARN, not refuse — set it up once and it must work if it can." A Vibe
/// version with no entry in <c>shared/vibe-home-manifest.json</c> is still run, and the person is told — on the row's card
/// before a round (<c>providers</c>: the health's <c>Warning</c>, the wire's <c>warning</c>) as well as on the reviewer
/// the round ran (<c>VibeLaunchControlTests</c>, <c>AVibeReviewerRunsThroughAFakeVibeTests</c>).
/// </summary>
public sealed class AnUnmeasuredVibeIsWarnedAboutNotRefusedTests
{
    private static Task<VendorHealth> ProbeAsync(string printed) =>
        VendorProbe.RunAsync(new RecordingLauncher(stdOut: printed), new VendorIdentity("vibe", "vibe", string.Empty), enabled: true,
            executablePath: "vibe", model: "glm-5-3", hasVaultKey: false, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnUnmeasuredVersion_IsHealthyAndWarnedAbout_NamingBothVersionsAndWhatToRerun()
    {
        var health = await ProbeAsync("python.exe C:\\Users\\p\\.local\\bin\\vibe 2.27.0\r\n");

        health.Auth.Should().NotBe("unavailable", "an unmeasured version is still run");
        health.Warning.Should().Contain("2.27.0").And.Contain("2.26.1").And.Contain("vibe-home-manifest.mjs").And.Contain("probe-vibe-confinement.mjs");
    }

    [Fact]
    public async Task TheMeasuredVersion_CarriesNoWarning()
    {
        (await ProbeAsync("python.exe C:\\Users\\p\\.local\\bin\\vibe 2.26.1\r\n")).Warning.Should().BeEmpty();
    }

    [Fact]
    public async Task Providers_CarriesTheWarningOnTheWire_ForTheVibeRowAlone()
    {
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-vibewarn-{Guid.NewGuid():N}"),
                Providers = PanelSettings.ParseVendors("""[{"id":"vibe","runtime":"vibe","model":"glm-5-3"},{"id":"codex","runtime":"codex"}]"""),
            },
            VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: "vibe 2.27.0\n"), Serilog.Core.Logger.None, Noticing.None);

        var answer = System.Text.Json.JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;
        var rows = answer.GetProperty("providers").EnumerateArray().ToDictionary(row => row.GetProperty("provider").GetString()!);

        rows["vibe"].TryGetProperty("warning", out var warning).Should().BeTrue("the card reads the warning from providers");
        warning.GetString().Should().Contain("2.27.0");
        rows["codex"].TryGetProperty("warning", out _).Should().BeFalse("every other row's JSON reads as it did");
    }
}
