using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Vibe's own failure sentences become cures (S2 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): the not-signed-in wording
/// M-V7 captured (research/RESULTS_vibe_confinement.md §11, arm a2), the Rust CLI's refusal of the shipped flags (§3), and
/// the install cure per OS from Mistral's own instructions (D16).
/// </summary>
public sealed class VibeIsDiagnosedInItsOwnWordsTests
{
    [Fact]
    public void NotSignedIn_IsARefusal_CuredByVibesOwnSetup()
    {
        // The exact stderr of §11 arm a2, 3 of 3.
        const string said = "Error: Missing MISTRAL_API_KEY environment variable for mistral provider. Set the environment variable "
            + "(e.g. in ~/.vibe/.env or your shell), or run `vibe --setup` once interactively.";

        var diagnosis = VendorDiagnosis.Classify(said, wsl: false);

        diagnosis.Should().NotBeNull();
        diagnosis!.Kind.Should().Be(DiagnosisKind.Refused);
        diagnosis.Cure.Should().Contain("vibe --setup").And.Contain("keyring");
    }

    [Fact]
    public void TheRustCli_RefusingTheShippedFlags_IsNamedAsSuch()
    {
        // The clap usage error of §3: exit 2 in under two seconds when the launcher picked the Rust CLI.
        const string said = "error: unexpected argument '--time-limit' found\n\nUsage: vibe --prompt [<PROMPT>] --output <OUTPUT>";

        var diagnosis = VendorDiagnosis.Classify(said, wsl: false);

        diagnosis.Should().NotBeNull();
        diagnosis!.Kind.Should().Be(DiagnosisKind.UnknownOption);
        diagnosis.Cure.Should().Contain("Rust CLI").And.Contain("VIBE_CLI=python");
    }

    /// <summary>
    /// The encoding crash of research/RESULTS_vibe_confinement.md §26, named rather than left as a bare exit 2: Vibe's Python
    /// wrote a pipe in the console's code page and died on a character outside it. coai launches Vibe with UTF-8 mode on, so
    /// meeting this means the launch did not — the sentence says what fixes it. Never read as a review (see VibeRuntime).
    /// </summary>
    [Fact]
    public void ThePythonEncodingCrash_IsNamedAsSuch()
    {
        const string said = "Error: 'charmap' codec can't encode character '\u2192' in position 710: character maps to <undefined>";

        var diagnosis = VendorDiagnosis.Classify(said, wsl: false);

        diagnosis.Should().NotBeNull("§26: a bare exit 2 tells nobody what to do");
        diagnosis!.Kind.Should().Be(DiagnosisKind.BrokenInstall);
        diagnosis.Cure.Should().Contain("PYTHONUTF8=1");
    }

    [Fact]
    public void TheInstallCure_IsMistralsOwn_PerOperatingSystem()
    {
        VendorDiagnosis.InstallCure("vibe", linux: false).Should().Contain("uv tool install mistral-vibe");
        VendorDiagnosis.InstallCure("vibe", linux: true).Should().Be("install Mistral's own CLI: curl -LsSf https://mistral.ai/vibe/install.sh | bash");
    }

    /// <summary>
    /// macOS gets the vendor's shell script, never the Windows commands: the default used to be `OperatingSystem.IsLinux()`, which gave a
    /// Mac `uv tool install` (and Google's PowerShell line) although the comment beside it, and the vendors' docs, say the script serves
    /// Linux and macOS. The host is passed outright, so this runs on any machine.
    /// </summary>
    [Theory]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    public void EveryHostButWindows_GetsTheVendorsShellScript(HostKind host)
    {
        VendorDiagnosis.InstallCure("vibe", host).Should().Be("install Mistral's own CLI: curl -LsSf https://mistral.ai/vibe/install.sh | bash");
        VendorDiagnosis.InstallCure("antigravity", host).Should().Contain("antigravity.google/cli/install.sh").And.NotContain("install.ps1");
    }

    [Fact]
    public void Windows_GetsUvAndPowerShell_AndTheBoolSpellingStillMeansWhatItDid()
    {
        VendorDiagnosis.InstallCure("vibe", HostKind.Windows).Should().Contain("uv tool install mistral-vibe");
        VendorDiagnosis.InstallCure("antigravity", HostKind.Windows).Should().Contain("install.ps1");
        VendorDiagnosis.InstallCure("vibe", linux: true).Should().Be(VendorDiagnosis.InstallCure("vibe", HostKind.Linux));
        VendorDiagnosis.InstallCure("vibe", linux: false).Should().Be(VendorDiagnosis.InstallCure("vibe", HostKind.Windows));
        VendorDiagnosis.InstallCure("codex", HostKind.MacOs).Should().Contain("npm install -g @openai/codex", "a cure with no OS in it is the same everywhere");
    }
}
