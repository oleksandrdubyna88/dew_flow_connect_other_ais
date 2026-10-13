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

    [Fact]
    public void TheInstallCure_IsMistralsOwn_PerOperatingSystem()
    {
        VendorDiagnosis.InstallCure("vibe", linux: false).Should().Contain("uv tool install mistral-vibe");
        VendorDiagnosis.InstallCure("vibe", linux: true).Should().Be("install Mistral's own CLI: curl -LsSf https://mistral.ai/vibe/install.sh | bash");
    }
}
