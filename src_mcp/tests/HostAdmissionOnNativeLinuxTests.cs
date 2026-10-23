using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1.S2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E1.md</c> (DN1, DN6 server half): the Linux arm of <see cref="HostAdmission"/> asks a PER-RUNTIME set of runtimes measured on a native Linux host — EMPTY in
/// E1, membership asked FIRST, so no answer of any host changes — and a runtime that IS in the set is still refused when the kernel banner could not be read (a WSL1 kernel whose banner is hidden must not start it).
/// The existing pins (<c>KimiLaunchChecksTests</c>, <c>VibeRuntimeTests</c>) are not edited: they are the proof that nothing moved.
/// </summary>
public sealed class HostAdmissionOnNativeLinuxTests
{
    private const string Wsl2OnlyVibe = "Vibe runs on WSL2 only for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of research/PLAN_kimi_and_vibe_on_linux.md)";

    private const string Wsl2OnlyKimi = "Kimi runs on WSL2 only for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of research/PLAN_kimi_and_vibe_on_linux.md)";

    private const string UnreadableVibe =
        "Vibe is refused on this Linux host — the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1 (DN6 of todo/PLAN_kimi_and_vibe_on_native_linux.md)";

    private static readonly IReadOnlySet<string> VibeMeasured = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vibe" };

    // ---------- S-N16 : the per-runtime set ----------

    /// <summary>S-N16: an injected set {Vibe} admits Vibe on a native Linux host with a read banner; Kimi keeps today's sentence.</summary>
    [Fact]
    public void AnInjectedSetWithVibe_AdmitsVibeOnAReadNativeLinuxHost_AndKimiKeepsTheWsl2OnlySentence()
    {
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", WslGeneration.None, VibeMeasured).Should().BeEmpty(
            "an injected measured set {Vibe} did not admit a native Linux host with a read banner — the set is not consulted");
        HostAdmission.Refusal("Kimi", HostKind.Linux, "kimi", WslGeneration.None, VibeMeasured).Should().Be(Wsl2OnlyKimi);
    }

    /// <summary>The names are compared case-insensitively — the way the callers spell them (<c>Vibe</c>) and the way a set may.</summary>
    [Theory]
    [InlineData("VIBE")]
    [InlineData("vibe")]
    public void TheSetIsComparedCaseInsensitively(string entry)
    {
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", WslGeneration.None, new HashSet<string> { entry }).Should().BeEmpty(
            $"an injected measured set {{{entry}}} did not admit Vibe on a read native Linux host — the names are compared case-sensitively");
    }

    /// <summary>S-N16 / S-N6 (E2.S2; the flipped E1 case): the PRODUCT's set holds Vibe (research/RESULTS_vibe_confinement.md §32) and not Kimi (E3's).</summary>
    [Fact]
    public void TheProductsSet_AdmitsVibe_AndNotKimi_OnAReadNativeLinuxHost()
    {
        HostAdmission.MeasuredOnNativeLinux.Select(name => name.ToLowerInvariant()).Should().Equal(["vibe"], "E2 adds Vibe and only Vibe: Kimi is E3's one entry (the names are the shared file's, compared case-insensitively)");
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", WslGeneration.None).Should().BeEmpty("a read native Linux banner should admit Vibe now that it is measured");
        HostAdmission.Refusal("Kimi", HostKind.Linux, "kimi", WslGeneration.None).Should().Be(Wsl2OnlyKimi);
    }

    /// <summary>
    /// S-N6: runtime x host, through the callers' own entry points. Only Windows, WSL2 and a native Linux host with a read banner start Vibe; Kimi starts on Windows and WSL2 only.
    /// </summary>
    [Theory]
    [InlineData("Vibe", HostKind.Windows, WslGeneration.None, true)]
    [InlineData("Vibe", HostKind.Wsl, WslGeneration.Wsl2, true)]
    [InlineData("Vibe", HostKind.Wsl, WslGeneration.Wsl1, false)]
    [InlineData("Vibe", HostKind.Linux, WslGeneration.None, true)]
    [InlineData("Vibe", HostKind.Linux, WslGeneration.Unreadable, false)]
    [InlineData("Vibe", HostKind.MacOs, WslGeneration.None, false)]
    [InlineData("Kimi", HostKind.Windows, WslGeneration.None, true)]
    [InlineData("Kimi", HostKind.Wsl, WslGeneration.Wsl2, true)]
    [InlineData("Kimi", HostKind.Wsl, WslGeneration.Wsl1, false)]
    [InlineData("Kimi", HostKind.Linux, WslGeneration.None, false)]
    [InlineData("Kimi", HostKind.Linux, WslGeneration.Unreadable, false)]
    [InlineData("Kimi", HostKind.MacOs, WslGeneration.None, false)]
    public void TheMatrix_StartsVibeOnWindowsWsl2AndAReadNativeLinux_AndKimiOnWindowsAndWsl2Only(string runtime, HostKind host, WslGeneration generation, bool starts)
    {
        var refusal = runtime == "Vibe"
            ? CoaiMcp.Runners.Vibe.VibeLaunch.HostRefusal(host, "vibe", generation)
            : CoaiMcp.Runners.Kimi.KimiLaunch.HostRefusal(host, "kimi", generation);

        (refusal.Length == 0).Should().Be(starts, $"{runtime} on {host}/{generation}: {refusal}");
    }

    /// <summary>S-N6: the sentences of the native Linux answers through the product's set — Vibe's unreadable-banner one, Kimi's WSL2-only one (whatever the banner says, membership is asked first).</summary>
    [Theory]
    [InlineData("Vibe", "vibe", WslGeneration.Unreadable, UnreadableVibe)]
    [InlineData("Kimi", "kimi", WslGeneration.None, Wsl2OnlyKimi)]
    [InlineData("Kimi", "kimi", WslGeneration.Unreadable, Wsl2OnlyKimi)]
    public void TheNativeLinuxSentences_AreTheBannerOneForVibe_AndTheWsl2OnlyOneForKimi(string runtime, string executable, WslGeneration generation, string sentence)
    {
        HostAdmission.Refusal(runtime, HostKind.Linux, executable, generation).Should().Be(sentence);
    }

    /// <summary>
    /// F4 of the E2 Opus judge round: the native Linux arm is an ALLOW-LIST — only a READ banner naming no WSL admits. HostKind.Linux with a WSL generation cannot happen in production (a WSL banner makes the host
    /// <see cref="HostKind.Wsl"/>), but "absence of a fact refuses" must hold for the impossible combination too: it is refused, with the sentence that says the two facts disagree (NOT the "could not be read" one — it was read).
    /// </summary>
    [Theory]
    [InlineData(WslGeneration.Wsl1)]
    [InlineData(WslGeneration.Wsl2)]
    public void ANativeLinuxHost_WhoseBannerNamesWsl_IsRefused_ByTheAllowList(WslGeneration generation)
    {
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", generation).Should().Contain("disagree").And.Contain("DN6").And.NotContain("could not be read",
            $"HostKind.Linux with {generation} was admitted — the native arm is a default-admit, not an allow-list");
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", generation, VibeMeasured).Should().Contain("disagree");
    }

    // ---------- S-N7 : an unreadable banner is never "native Linux" ----------

    /// <summary>S-N7 (teeth): a runtime in the set is still refused when the banner could not be read, with the DN6 sentence.</summary>
    [Fact]
    public void ARuntimeInTheSet_IsRefusedWhenTheBannerCouldNotBeRead_WithTheBannerSentence()
    {
        HostAdmission.Refusal("Vibe", HostKind.Linux, "vibe", WslGeneration.Unreadable, VibeMeasured).Should().Be(
            UnreadableVibe,
            "an unreadable /proc/version was admitted as native Linux — a WSL1 kernel would start Vibe");
    }

    /// <summary>S-N7: membership is asked FIRST — a runtime outside the set (here an empty one, E1's shape) gets today's WSL2-only sentence whatever the banner says, byte for byte.</summary>
    [Theory]
    [InlineData("Vibe", "vibe", Wsl2OnlyVibe)]
    [InlineData("Kimi", "kimi", Wsl2OnlyKimi)]
    public void ARuntimeOutsideTheSet_GetsTodaysWsl2OnlySentence_OnAnUnreadableBanner(string runtime, string executable, string sentence)
    {
        HostAdmission.Refusal(runtime, HostKind.Linux, executable, WslGeneration.Unreadable, new HashSet<string>()).Should().Be(
            sentence,
            "the banner was asked before the set — a runtime outside the set got the banner sentence");
    }

    /// <summary>The set is the Linux arm's alone: Windows and WSL answers do not read it.</summary>
    [Fact]
    public void TheSet_IsNotConsultedOutsideNativeLinux()
    {
        HostAdmission.Refusal("Vibe", HostKind.Windows, "vibe", WslGeneration.None, new HashSet<string>()).Should().BeEmpty();
        HostAdmission.Refusal("Vibe", HostKind.Wsl, "vibe", WslGeneration.Wsl2, new HashSet<string>()).Should().BeEmpty();
        HostAdmission.Refusal("Vibe", HostKind.Wsl, "vibe", WslGeneration.Wsl1, VibeMeasured).Should().Contain("this is WSL1", "a set entry admitted a WSL1 kernel");
    }

    // ---------- the generation: Unreadable is produced only for a Linux host that did not read its banner ----------

    /// <summary>DN6: an empty <c>/proc/version</c> is not a read banner.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   \n", false)]
    [InlineData("Linux version 6.8.0-45-generic (buildd@lcy02) #45-Ubuntu SMP", true)]
    public void AnEmptyBanner_IsNotRead(string text, bool read)
    {
        ProcVersion.ReadFrom(() => text).Read.Should().Be(read, $"banner '{text.Trim()}'");
    }

    /// <summary>DN6: a banner that cannot be opened (absent, or denied) is not a read banner.</summary>
    [Fact]
    public void ABannerThatCannotBeOpened_IsNotRead()
    {
        ProcVersion.ReadFrom(() => throw new FileNotFoundException("/proc/version")).Should().Be(BannerReading.Unread);
        ProcVersion.ReadFrom(() => throw new UnauthorizedAccessException("/proc/version")).Should().Be(BannerReading.Unread);
    }

    [Fact]
    public void OnLinux_AnUnreadBanner_IsUnreadable_NeverNone()
    {
        WslGenerations.Of(onLinux: true, BannerReading.Unread).Should().Be(
            WslGeneration.Unreadable, "an unreadable /proc/version read as None — a WSL1 kernel would pass for native Linux");
    }

    [Theory]
    [InlineData("Linux version 6.8.0-45-generic (buildd@lcy02) #45-Ubuntu SMP", WslGeneration.None)]
    [InlineData("Linux version 5.15.153.1-microsoft-standard-WSL2 (root@1c602f52c2e4) #1 SMP", WslGeneration.Wsl2)]
    [InlineData("Linux version 4.4.0-19041-Microsoft (Microsoft@Microsoft.com) #1", WslGeneration.Wsl1)]
    public void OnLinux_AReadBanner_IsJudgedByItsText(string banner, WslGeneration expected)
    {
        WslGenerations.Of(onLinux: true, new BannerReading(true, banner)).Should().Be(expected);
    }

    [Fact]
    public void OffLinux_ThereIsNoBannerToRead_SoTheGenerationIsNone()
    {
        WslGenerations.Of(onLinux: false, BannerReading.Unread).Should().Be(WslGeneration.None);
    }

    /// <summary>The text rule is untouched: <see cref="WslGenerations.Of(string)"/> never answers Unreadable, so the shared vectors do not move (B8).</summary>
    [Fact]
    public void TheTextRule_NeverAnswersUnreadable()
    {
        WslGenerations.Of(string.Empty).Should().Be(WslGeneration.None, "the shared banner vectors say an empty banner is None — only the reading says Unreadable");
    }

    /// <summary>Only WSL and native Linux are judged by the kernel — Windows, macOS and the rest are not.</summary>
    [Theory]
    [InlineData(HostKind.Windows)]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void ForHost_TakesNoKernelOffWslAndLinux(HostKind kind)
    {
        WslGenerations.ForHost(kind, WslGeneration.Unreadable).Should().Be(WslGeneration.None);
    }

    /// <summary>A native Linux host is judged by this machine's kernel, as WSL is — so the admission can ask whether the banner was read.</summary>
    [Theory]
    [InlineData(HostKind.Linux, WslGeneration.Unreadable)]
    [InlineData(HostKind.Linux, WslGeneration.None)]
    [InlineData(HostKind.Wsl, WslGeneration.Wsl2)]
    public void ForHost_TakesTheKernelOnWslAndNativeLinux(HostKind kind, WslGeneration kernel)
    {
        WslGenerations.ForHost(kind, kernel).Should().Be(kernel, $"a {kind} host's generation was not taken from the kernel — an unreadable banner could never be seen");
    }
}
