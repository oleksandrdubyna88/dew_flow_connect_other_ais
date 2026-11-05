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

    /// <summary>S-N16: the PRODUCT's set admits neither runtime on a native Linux host.</summary>
    [Theory]
    [InlineData("Vibe", "vibe", Wsl2OnlyVibe)]
    [InlineData("Kimi", "kimi", Wsl2OnlyKimi)]
    public void TheProductsSet_AdmitsNeitherRuntime_OnAReadNativeLinuxHost(string runtime, string executable, string sentence)
    {
        HostAdmission.MeasuredOnNativeLinux.Should().BeEmpty("E1 admits nothing: E2 adds Vibe and E3 Kimi, one entry each");
        HostAdmission.Refusal(runtime, HostKind.Linux, executable, WslGeneration.None).Should().Be(sentence);
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

    /// <summary>S-N7: membership is asked FIRST — with the product's (empty) set an unreadable banner gets today's WSL2-only sentence, byte for byte, so no answer changed in E1.</summary>
    [Theory]
    [InlineData("Vibe", "vibe", Wsl2OnlyVibe)]
    [InlineData("Kimi", "kimi", Wsl2OnlyKimi)]
    public void WithTheProductsSet_AnUnreadableBanner_GetsTodaysWsl2OnlySentence(string runtime, string executable, string sentence)
    {
        HostAdmission.Refusal(runtime, HostKind.Linux, executable, WslGeneration.Unreadable).Should().Be(
            sentence,
            "the banner was asked before the set — an answer changed while the set is empty");
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
