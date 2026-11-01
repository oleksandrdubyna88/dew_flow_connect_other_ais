using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Platform;

/// <summary>Which WSL a Linux kernel is: WSL2 (a real Linux kernel), WSL1 (a syscall translation layer), neither, or — on Linux only — not known because the banner could not be read.</summary>
public enum WslGeneration
{
    None = 0,
    Wsl1,
    Wsl2,

    /// <summary>
    /// A Linux host whose <c>/proc/version</c> is absent, throws or is empty (DN6 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>): coai cannot tell a native Linux host from WSL1. Produced ONLY by
    /// <see cref="WslGenerations.Current"/> on Linux — <see cref="WslGenerations.Of(string)"/> never answers it, so the shared banner vectors do not move.
    /// </summary>
    Unreadable,
}

/// <summary>
/// WSL2 is not WSL1 (DL1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, cadence consult a3933362): <see cref="VendorDiagnosis.NamesWsl"/>
/// answers yes for ANY banner holding <c>microsoft</c> or <c>WSL</c>, so a WSL1 kernel (<c>4.4.0-…-Microsoft</c>, no real <c>/proc</c>
/// process tree or <c>flock</c> semantics of its own) reads as <see cref="HostKind.Wsl"/>. "Measured on WSL2" must mean WSL2, so a
/// runtime's admission asks this second, narrower predicate. <see cref="VendorDiagnosis.NamesWsl"/> itself is NOT narrowed: its other
/// callers (the networking cure) are right for both generations.
/// </summary>
public static class WslGenerations
{
    /// <summary>Pure. The banner holds <c>WSL2</c> or <c>microsoft-standard</c> (case-insensitive) → WSL2; else any WSL banner → WSL1; else none.</summary>
    public static WslGeneration Of(string procVersion) =>
        NamesWsl2(procVersion) ? WslGeneration.Wsl2 : VendorDiagnosis.NamesWsl(procVersion) ? WslGeneration.Wsl1 : WslGeneration.None;

    internal static bool NamesWsl2(string procVersion) =>
        procVersion.Contains("WSL2", StringComparison.OrdinalIgnoreCase)
        || procVersion.Contains("microsoft-standard", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pure. On Linux a banner that was not read is <see cref="WslGeneration.Unreadable"/> (never <see cref="WslGeneration.None"/>: "no banner" is not "no WSL"); a read banner is judged by <see cref="Of(string)"/>; off
    /// Linux there is no <c>/proc/version</c> to read and the answer is <see cref="WslGeneration.None"/>.
    /// </summary>
    internal static WslGeneration Of(bool onLinux, BannerReading reading) => (onLinux, reading.Read) switch
    {
        (false, _) => WslGeneration.None,
        (true, false) => WslGeneration.Unreadable,
        _ => Of(reading.Text),
    };

    /// <summary>This process's kernel, asked once — through the SAME <c>/proc/version</c> read <see cref="VendorDiagnosis.OnWsl"/> makes.</summary>
    public static WslGeneration Current => ThisKernel.Value;

    /// <summary>
    /// The generation a host of <paramref name="kind"/> is judged by: this machine's kernel on WSL and on native Linux (the admission of a native Linux host asks whether the banner was read), none elsewhere. The ONE
    /// place the three host records (<c>VibeHost</c>, <c>KimiHost</c>, <c>VibeRuntime</c>'s test way in) decide whether to read the kernel.
    /// </summary>
    internal static WslGeneration ForHost(HostKind kind) => ForHost(kind, Current);

    /// <summary>Pure: <paramref name="thisKernel"/> for a WSL or native Linux host, none for every other.</summary>
    internal static WslGeneration ForHost(HostKind kind, WslGeneration thisKernel) => kind is HostKind.Wsl or HostKind.Linux ? thisKernel : WslGeneration.None;

    private static readonly Lazy<WslGeneration> ThisKernel = new(() => Of(OperatingSystem.IsLinux(), ProcVersion.Reading));
}

/// <summary>What reading the kernel banner came to: whether it was read (present, readable and not empty), and its text (empty when it was not).</summary>
internal sealed record BannerReading(bool Read, string Text)
{
    /// <summary>The banner could not be read.</summary>
    public static BannerReading Unread { get; } = new(false, string.Empty);
}

/// <summary>The kernel's <c>/proc/version</c> banner, read once — the one reader <see cref="VendorDiagnosis.OnWsl"/> and <see cref="WslGenerations.Current"/> share.</summary>
internal static class ProcVersion
{
    /// <summary>The banner, or empty off Linux or when it cannot be read (an unreadable banner names no WSL).</summary>
    public static string Text => Banner.Value.Text;

    /// <summary>The ONE read, with whether it read: <see cref="WslGenerations.Current"/> needs to tell "empty or absent" from a banner, <see cref="Text"/> does not.</summary>
    public static BannerReading Reading => Banner.Value;

    private static readonly Lazy<BannerReading> Banner = new(() => OperatingSystem.IsLinux() ? ReadFrom(() => File.ReadAllText("/proc/version")) : BannerReading.Unread);

    /// <summary>The banner <paramref name="read"/> gives; unread when it throws an I/O fault (a missing file is one). Pure over its seam.</summary>
    internal static BannerReading ReadFrom(Func<string> read)
    {
        try
        {
            var text = read();

            return string.IsNullOrWhiteSpace(text) ? BannerReading.Unread : new BannerReading(true, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BannerReading.Unread;
        }
    }
}
