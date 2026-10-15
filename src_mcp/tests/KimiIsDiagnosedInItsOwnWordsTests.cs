using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
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
        const string Script = "install Kimi Code's own CLI: curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash";
        foreach (var host in new[] { HostKind.Linux, HostKind.Wsl, HostKind.MacOs })
        {
            VendorDiagnosis.InstallCure("kimi", host).Should().Be(Script, $"{host} gets Moonshot's shell script (PR #823 gave macOS the vendor's script, not the PowerShell form)");
        }

        VendorDiagnosis.InstallCure("kimi", linux: true).Should().Be(Script);
        var windows = VendorDiagnosis.InstallCure("kimi", HostKind.Windows);
        windows.Should().Contain("irm https://code.kimi.com/kimi-code/install.ps1 | iex").And.NotContain("curl");
        VendorDiagnosis.InstallCure("kimi", linux: false).Should().Be(windows);
    }

    [Fact]
    public void TheWindowsCure_NamesTheSameThreeTools_AndIdsAsTheButtonInstalls()
    {
        // The panel's ⤓ runs winget for exactly these ids (kimiWindowsInstall.test.ts holds the line); the sentence a failed round
        // shows must not name fewer — a Kimi launch is refused without rg and fd (KimiTools), which Moonshot's script never installs.
        var windows = VendorDiagnosis.InstallCure("kimi", HostKind.Windows);

        windows.Should().Contain("winget install --id Git.Git -e")
            .And.Contain(KimiTools.RipgrepInstall).And.Contain(KimiTools.FdInstall)
            .And.Contain("Git for Windows");
    }
}
