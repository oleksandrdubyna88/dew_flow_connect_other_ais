using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Kimi's failures in coai's words (S4.12 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the SHIM's three failure names and its one end word
/// become cures — never a phrase match on Kimi's own trace text — and the install cure per OS is Moonshot's own (D16, read 2026-10-14).
/// </summary>
public sealed class KimiIsDiagnosedInItsOwnWordsTests
{
    [Theory]
    [InlineData("not_signed_in: Authentication required — run `kimi login` in a terminal and try again", DiagnosisKind.Refused, "kimi login")]
    [InlineData("subscription_model: 401 Your current subscription does not have access to kimi-for-coding-highspeed — the Kimi Code plan does not include this model", DiagnosisKind.Refused, "plan")]
    [InlineData("balance_spent: provider.api_error: 429 insufficient balance — the Kimi allowance is spent (D14)", DiagnosisKind.Refused, "allowance is spent")]
    [InlineData("model_not_offered: the agent's session offers kimi-code/k3 for 'model'; 'x' is not one of them", DiagnosisKind.Refused, "does not offer this model")]
    public void TheShimsOwnWord_BecomesACure(string stderr, DiagnosisKind kind, string cureSays)
    {
        var diagnosis = VendorDiagnosis.Classify(stderr, wsl: false);

        diagnosis.Should().NotBeNull();
        diagnosis!.Kind.Should().Be(kind);
        diagnosis.Cure.Should().Contain(cureSays);
    }

    [Fact]
    public void KimisOwnTraceText_IsNeverMatched_OnlyTheShimsWords()
    {
        VendorDiagnosis.Classify("Authentication required\nsrc/quota.cs read\ncheck rate limit", wsl: false)
            .Should().BeNull("a phrase of Kimi's own is not a marker: the shim names the failure, and that name is the marker");
    }

    [Fact]
    public void TheInstallCure_IsMoonshotsOwn_PerOperatingSystem()
    {
        VendorDiagnosis.InstallCure("kimi", linux: true).Should().Be("install Kimi Code's own CLI: curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash");
        var windows = VendorDiagnosis.InstallCure("kimi", linux: false);
        windows.Should().Contain("irm https://code.kimi.com/kimi-code/install.ps1 | iex")
            .And.Contain("Git for Windows").And.Contain("winget install Git.Git");
    }
}
