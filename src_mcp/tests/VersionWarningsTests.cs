using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// ONE builder for the "this version was not measured" sentence, both runtimes' (E4.2 decision 3 and brief sections 11–12,
/// S2.32–S2.33): Vibe's output is byte for byte what it was before the builder was shared, and Kimi's names the measured
/// version and what to re-run.
/// </summary>
public sealed class VersionWarningsTests
{
    /// <summary>
    /// The sentence <c>VibeLaunch</c> produces for 2.27.0 against a manifest that knows 2.26.1 — pinned as a string. Since the manifest is per
    /// platform (DL3 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>) it names each measured version with the platform it was measured on, so a
    /// version measured on another platform only says where.
    /// </summary>
    private const string VibeBefore =
        "Vibe 2.27.0 is not a version coai has measured (it measured 2.26.1 (win32)); it is run anyway, but its confinement "
        + "(research/RESULTS_vibe_confinement.md) and its home's plugin hashes belong to 2.26.1 (win32); this version's plugin files are "
        + "admitted by name only — re-run `node scripts/vibe-home-manifest.mjs --write` and `node scripts/probe-vibe-confinement.mjs` "
        + "with this Vibe to measure it";

    [Fact]
    public void VibesSentence_IsByteForByteWhatItWas_NowThroughTheSharedBuilder()
    {
        var manifest = new VibeHomeManifest("logs/session/plugins/", ["logs/**"], [new VibeHomeEntry("2.26.1", [])]);

        VibeLaunch.VersionWarning(manifest, "2.27.0", "win32", HostKind.Windows).Should().Be(VibeBefore);
        VersionWarnings.Unmeasured(
            "Vibe", "2.27.0", "2.26.1 (win32)",
            "its confinement (research/RESULTS_vibe_confinement.md) and its home's plugin hashes belong to 2.26.1 (win32); this version's plugin files are admitted by name only",
            "re-run `node scripts/vibe-home-manifest.mjs --write` and `node scripts/probe-vibe-confinement.mjs` with this Vibe to measure it")
            .Should().Be(VibeBefore, "the builder IS the sentence");
    }

    [Fact]
    public void BothRuntimes_BeginTheSameWay()
    {
        var vibe = VersionWarnings.Unmeasured("Vibe", "2.27.0", "2.26.1", "its numbers", "re-run it");
        var kimi = VersionWarnings.Unmeasured("Kimi", "2.2.0", "2.1.1", "its numbers", "re-run it");

        vibe.Should().StartWith("Vibe 2.27.0 is not a version coai has measured (it measured 2.26.1); it is run anyway, but ");
        kimi.Should().StartWith("Kimi 2.2.0 is not a version coai has measured (it measured 2.1.1); it is run anyway, but ");
        kimi.Should().EndWith("its numbers — re-run it");
    }

    [Theory]
    [InlineData("kimi 2.2.0\n", "2.2.0")]
    [InlineData("2.1.1", "2.1.1")]
    [InlineData("python.exe C:\\Users\\p\\.local\\bin\\vibe 2.26.1\r\n", "2.26.1")]
    [InlineData("no number here", "")]
    public void TheVersionIsTheFirstThreePartNumber(string printed, string expected)
    {
        VersionWarnings.VersionIn(printed).Should().Be(expected);
        VibeLaunch.VersionIn(printed).Should().Be(expected, "Vibe's reader delegates to the shared one");
    }

    [Fact]
    public void MeasuredVersions_IsTwoOneOne()
    {
        KimiAcpProfile.MeasuredVersions.Should().Equal("2.1.1");
    }

    [Fact]
    public void KimisWarning_IsEmptyForTheMeasuredVersion_AndTheSentenceForAnother()
    {
        KimiLaunch.VersionWarning("kimi 2.1.1").Should().BeEmpty();
        KimiLaunch.VersionWarning("not a version").Should().BeEmpty("no version printed, nothing to warn about — the launch's own refusal speaks for that");

        KimiLaunch.VersionWarning("kimi 2.2.0").Should()
            .StartWith("Kimi 2.2.0 is not a version coai has measured (it measured 2.1.1); it is run anyway, but ")
            .And.Contain("the write refusals, the owned home's inventory, the OS-home redirect (research/RESULTS_kimi_confinement.md)")
            .And.Contain("is 2.1.1's")
            .And.EndWith("re-run `node scripts/probe-kimi-confinement.mjs` (the confinement probe, a paid run) and `node scripts/probe-kimi-tree-hold.mjs` (the sign-in hold) on it");
    }
}
