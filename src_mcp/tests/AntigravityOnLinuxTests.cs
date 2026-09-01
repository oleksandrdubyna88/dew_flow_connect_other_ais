using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// A vendor whose CLI is PRESENT is never reported as having none.
/// </summary>
/// <remarks>
/// <para>Observed against the published 0.9.0 binary in WSL: <c>~/.local/bin/agy</c> existed, had
/// answered nine review rounds in the pre-delivery campaign an hour earlier, and <c>providers</c>
/// said <c>cliFound: false</c>, <c>auth: unavailable</c>, "Antigravity has no Linux CLI that Google
/// publishes".</para>
/// <para>The cause was a blanket Linux door in <see cref="VendorDiagnosis.ForRuntime"/>. A door
/// there fires BEFORE the probe — which is right for Gemini, a runtime that is closed whatever its
/// binary says, and wrong for a runtime whose CLI is merely sometimes absent. The two cases had been
/// collapsed into one.</para>
/// <para>Its text was also false: Google publishes <c>antigravity.google/cli/install.sh</c>, which
/// handles Linux and macOS. The product spent a day telling people otherwise.</para>
/// </remarks>
public class AntigravityOnLinuxTests
{
    [Fact]
    public void OnLinux_AntigravityIsNotAClosedRuntime()
    {
        // The platform is stated, never inherited: this assertion's whole point is what happens on
        // Linux, and a test that let the host decide would pass on Windows and mean nothing.
        VendorDiagnosis.ForRuntime("antigravity", linux: true).Should().BeNull(
            "Google publishes an install script for Linux, so the runtime is not closed — a missing "
            + "binary is the probe's business, not a door in front of it");
    }

    [Fact]
    public async Task OnLinux_APresentAntigravityCli_IsReportedFound()
    {
        var data = Directory.CreateTempSubdirectory("coai-agy-").FullName;
        // The launcher stands in for a CLI that answers --version with 0, which is what a present
        // `agy` does. Nothing here needs a real binary; what is under test is whether it is ASKED.
        //
        // Honest about its own reach: `ProvidersAsync` asks the platform itself, so on a Windows
        // host this passed BEFORE the fix too. The assertion with teeth is the one above, which
        // states `linux: true` and went red on the real sentence. This one is the end-to-end
        // sanity check that a present CLI is probed at all.
        var service = new PanelService(
            new PanelSettings
            {
                Providers = [new("antigravity") { ExecutablePath = "/home/somebody/.local/bin/agy" }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            new RecordingLauncher(stdOut: "agy 0.4.2"),
            Logger.None);

        var answer = await service.ProvidersAsync();

        answer.Should().Contain("\"cliFound\": true")
            .And.NotContain("no Linux CLI",
                "the sentence that reported a working CLI as absent");
    }

    [Fact]
    public void AMissingCli_IsAnsweredWithTheVendorsOwnInstallCommand()
    {
        // "'agy' was not found on this machine" is true and leaves somebody searching a docs page,
        // which is the reason a reviewer never gets added. Only official sources: Google's script,
        // never the third-party snap.
        VendorDiagnosis.InstallCure("antigravity", linux: true)
            .Should().Contain("antigravity.google/cli/install.sh");
        VendorDiagnosis.InstallCure("antigravity", linux: false)
            .Should().Contain("install.ps1");
        VendorDiagnosis.InstallCure("codex", linux: true)
            .Should().Contain("npm install -g @openai/codex");
    }

    [Fact]
    public void TheRefutedWslInteropAdvice_IsOfferedNowhere()
    {
        // Measured: a Windows agy.exe launched from a Linux server exits 1 after 60 seconds with
        // "authentication timed out". This product recommended it for a day. Advice that has been
        // refuted is worse than none, so it is pinned out of every string the vendor tables hold.
        string?[] everything =
        [
            VendorDiagnosis.ForRuntime("antigravity", linux: true),
            VendorDiagnosis.ForRuntime("antigravity", linux: false),
            VendorDiagnosis.InstallCure("antigravity", linux: true),
            VendorDiagnosis.InstallCure("antigravity", linux: false),
        ];

        everything.Should().OnlyContain(s => s == null || !s.Contains("agy.exe"));
    }
}
