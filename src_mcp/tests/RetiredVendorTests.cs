using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a fresh install — and an install made before the retirement — actually RUNS.
/// </summary>
/// <remarks>
/// <para>The Antigravity adapter shipped on 2026-08-31 and nothing started using it. Every default
/// in the product still named <c>gemini</c>, the panel offered no way to add Antigravity at all,
/// and the operator's saved reviewer list therefore still pointed at a CLI Google had closed. The
/// gate went on launching it for a day, on two machines, and the answer to "why is it still
/// calling gemini" was: because supporting a vendor and DEFAULTING to it are different changes,
/// and only the first one was made.</para>
/// <para>These tests hold the second one.</para>
/// </remarks>
public sealed class RetiredVendorTests
{
    // ---------- what a fresh install reviews with ----------

    [Fact]
    public void TheShippedReviewerList_NamesNoRetiredCli()
    {
        var providers = new PanelSettings().Providers;

        providers.Should().NotContain(p => p.Provider == "gemini",
            "a default is what an install runs before anybody configures anything");
        providers.Should().Contain(p => p.Provider == "antigravity" && p.Enabled);
    }

    [Fact]
    public void WithNothingConfigured_TheFanOutIsCodexAndAntigravity()
    {
        var settings = PanelSettings.FromEnvironment(_ => null);

        settings.Providers.Select(p => p.Provider).Should().BeEquivalentTo(["codex", "antigravity"]);
    }


    // ---------- an antigravity translator must launch antigravity ----------



    // ---------- providers must not call a closed door healthy ----------

    [Fact]
    public void AVersionFlagThatSucceeds_DoesNotMakeARetiredRuntimeHealthy()
    {
        // `gemini --version` exits 0: it prints a version without ever reaching Google. The
        // retirement only surfaces at sign-in, so a probe built on --version is structurally
        // incapable of seeing it — and reported "own auth, the CLI's own sign-in is used" for a
        // vendor that could not sign in at all. Green health on a dead vendor is worse than none.
        VendorDiagnosis.ForRuntime("gemini").Should().NotBeNull()
            .And.Subject.As<string>().Should().Contain("antigravity");
    }

    [Fact]
    public void ARuntimeThatStillWorks_IsNotMarkedRetired()
    {
        // The platform is stated rather than inherited. Antigravity's answer legitimately DEPENDS on
        // it — it works on Windows and has no CLI to install on Linux — so a test that let the host
        // decide passed on my machine and failed on CI, which is worse than a failing test.
        VendorDiagnosis.ForRuntime("antigravity", linux: false).Should().BeNull();
        VendorDiagnosis.ForRuntime("codex", linux: false).Should().BeNull();
        VendorDiagnosis.ForRuntime("codex", linux: true).Should().BeNull("codex installs from npm everywhere");
    }
}

/// <summary>Captures the request instead of launching it.</summary>
internal sealed class RecordingLauncher(string stdOut = "", int exitCode = 0) : IProcessLauncher
{
    public ProcessRequest? Last { get; private set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        Last = request;
        return Task.FromResult(new ProcessResult(exitCode, stdOut, string.Empty, TimedOut: false));
    }
}
