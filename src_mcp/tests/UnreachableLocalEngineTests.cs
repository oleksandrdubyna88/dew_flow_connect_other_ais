using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a round says when the local engine is not on this side of WSL.
/// </summary>
/// <remarks>
/// <para><b>The observation these tests are written from.</b> One machine, coai 12.1 on both sides,
/// settings byte-identical. From Windows the local reviewer answers; from a VS Code attached to WSL
/// the ledger records this, ten times, each round lasting zero seconds:</para>
/// <code>
/// "provider":"local","seconds":0,"outcome":"exit 69: [coai-mcp] the local engine at
///  http://127.0.0.1:11434/v1 could not be reached: Connection refused (127.0.0.1:11434)"
/// </code>
/// <para>Every word of that is true and none of it says what to do. The panel had the cure and the
/// ROUND did not, because <c>VendorDiagnosis.Known</c> only knew vendor-CLI failures — so
/// the one surface a person actually watches during a review stayed silent ten times running.</para>
/// <para><b>Why the WSL test is not a Linux test.</b> The first draft of this said "on Linux".
/// A native Linux box has no <c>%USERPROFILE%\.wslconfig</c> and no <c>wsl --shutdown</c>, and
/// advice for a machine you are not on is worse than none. Raised by Gemini 3.7 Flash on the plan,
/// before any of this was written.</para>
/// </remarks>
public sealed class UnreachableLocalEngineTests
{
    private const string TheLedgerSentence =
        "[coai-mcp] the local engine at http://127.0.0.1:11434/v1 could not be reached: "
        + "Connection refused (127.0.0.1:11434)";

    [Fact]
    public void TheRefusedLocalEngineUnderWsl_IsToldTheMirroredCure()
    {
        var cure = VendorDiagnosis.For(TheLedgerSentence, wsl: true);

        cure.Should().NotBeNull();
        cure.Should().Contain("http://127.0.0.1:11434/v1",
            "the address is the half of the message that was already useful — a cure must not cost it");
        cure.Should().Contain("mirrored");
        cure.Should().Contain(".wslconfig");
    }

    [Fact]
    public void TheSameRefusalOnNativeLinux_IsNotToldToEditAWindowsFile()
    {
        var cure = VendorDiagnosis.For(TheLedgerSentence, wsl: false);

        cure.Should().NotBeNull();
        cure.Should().Contain("http://127.0.0.1:11434/v1");
        cure.Should().NotContain(".wslconfig");
        cure.Should().NotContain("wsl --shutdown");
    }

    [Fact]
    public void ALocalEngineThatAnsweredTooSlowly_IsNotCalledUnreachable()
    {
        // The shim distinguishes these two on purpose: one is a machine with nothing listening, the
        // other is a model that is thinking. One cure for both would send somebody to reconfigure a
        // network because their model is slow.
        var timedOut = "[coai-mcp] the local engine at http://127.0.0.1:11434/v1 did not answer "
                       + "within the round's deadline: The operation was canceled.";

        VendorDiagnosis.For(timedOut, wsl: true).Should().BeNull();
    }

    [Fact]
    public void AVendorCliRefusingSomethingElse_IsNotSweptIntoTheLocalCure()
    {
        // The marker is this product's OWN sentence, not the word "refused", which appears in every
        // vendor's stack traces. A cure that fires on anything is a cure nobody can trust.
        VendorDiagnosis.For("Error: connect ECONNREFUSED 127.0.0.1:443", wsl: true).Should().BeNull();
        VendorDiagnosis.For("request failed: connection refused", wsl: true).Should().BeNull();
    }

    [Fact]
    public void AKernelThatSaysMicrosoft_IsWsl_AndAPlainOneIsNot()
    {
        var wsl = "Linux version 6.18.33.2-2 (root@build) #1 SMP PREEMPT_DYNAMIC "
                  + "Fri Aug 15 00:00:00 UTC 2026 Microsoft WSL2";
        var native = "Linux version 6.11.0-19-generic (buildd@lcy02) #19-Ubuntu SMP "
                     + "Mon Feb 17 11:51:52 UTC 2026";

        VendorDiagnosis.NamesWsl(wsl).Should().BeTrue();
        VendorDiagnosis.NamesWsl(native).Should().BeFalse();
        VendorDiagnosis.NamesWsl("").Should().BeFalse();
    }

    [Fact]
    public void TheKnownVendorCures_StillAnswerForTheirOwnFailures()
    {
        // A new branch in front of the table is a new way for the table to stop being reached.
        VendorDiagnosis.For("Error: Missing bearer or basic authentication in header", wsl: true)
            .Should().Contain("not signed in");
        VendorDiagnosis.For("throwIneligibleOrProjectIdError at foo.js:1", wsl: false)
            .Should().Contain("antigravity");
    }
}
