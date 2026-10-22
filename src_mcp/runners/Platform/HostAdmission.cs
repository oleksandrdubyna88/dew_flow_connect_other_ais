namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The ONE host admission for a runtime that is measured on Windows and on WSL2 (DL1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): Vibe's since E2, Kimi's since E6. The sentences are written
/// once and take the runtime's name, so the two runtimes cannot word the same refusal two ways.
/// </summary>
/// <remarks>
/// Admitted: a Windows host starting a Windows CLI, and a WSL2 host. Refused, each with its own sentence: a WSL1 kernel (which reads as <see cref="HostKind.Wsl"/>, and is a syscall translation layer),
/// a native Linux host (unless the runtime is in <see cref="MeasuredOnNativeLinux"/>, DN1 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>: <b>Vibe is</b> — measured on a native Linux host, research/RESULTS_vibe_confinement.md §32
/// (Vibe 2.26.1, ubuntu-claude, 2026-10-22), and its launch there closes the keyring road, <c>PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring</c> (DN10, <c>VibeLaunch.Built</c>) — the keyring road was measured closed on Vibe 2.26.1
/// (keyring 25.7.0); <b>Kimi is not</b> (E3's), so its native Linux answer is still the WSL2-only sentence; a runtime in the set is still refused when the kernel banner could not be read, DN6), a Windows host that would start the Linux side by <c>wsl.exe</c> or a <c>\\wsl$</c> path (D18: a Windows process cannot take a Linux lock or end a
/// Linux process tree), and every other host.
/// </remarks>
public static class HostAdmission
{
    private const string Plan = "DL1 of research/PLAN_kimi_and_vibe_on_linux.md";

    private const string Parent = "D18 of todo/PLAN_vibe_and_kimi_runtimes.md";

    private const string NativePlan = "DN6 of todo/PLAN_kimi_and_vibe_on_native_linux.md";

    /// <summary>
    /// The runtimes (<c>Vibe</c>, <c>Kimi</c>) measured on a native Linux host. <c>Vibe</c> is in it, on the evidence of research/RESULTS_vibe_confinement.md §32 (Vibe 2.26.1, ubuntu-claude, 2026-10-22: N-V1–N-V6 all passed with the
    /// keyring road closed in every launch); <c>Kimi</c> is not (E3 adds it: one entry plus its texts, never a second admission). Membership is asked FIRST, so a runtime outside the set gets the WSL2-only sentence whatever the banner says.
    /// The ABSENCE of a fact is a refusal: no entry, no admission; no read banner, no admission (DN6). There is no default member, no settings switch and no environment switch — a runtime enters by an edit to THIS line that names the
    /// RESULTS section its cells are in; and a keyring-session fact (<c>KeyringSessionFacts</c>) is recorded by <c>--host-facts</c> and decides nothing here (DN10): a fact added to this arm is a REFUSAL condition on a member, never a road into the set.
    /// Compared case-insensitively, as <see cref="Refusal(string, HostKind, string, WslGeneration)"/> receives the names.
    /// </summary>
    public static IReadOnlySet<string> MeasuredOnNativeLinux { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vibe" };

    /// <summary>
    /// Empty when <paramref name="runtime"/> (<c>Vibe</c>, <c>Kimi</c>) may start on this host, else the sentence. <paramref name="executable"/> is the CLI the launch would start; <paramref name="wsl"/> is
    /// which WSL the kernel is when <paramref name="host"/> is <see cref="HostKind.Wsl"/>.
    /// </summary>
    public static string Refusal(string runtime, HostKind host, string executable, WslGeneration wsl) => Refusal(runtime, host, executable, wsl, MeasuredOnNativeLinux);

    /// <summary><see cref="Refusal(string, HostKind, string, WslGeneration)"/> against <paramref name="measuredOnNativeLinux"/> — a test's way to give a runtime a native Linux admission the product has not yet earned.</summary>
    internal static string Refusal(string runtime, HostKind host, string executable, WslGeneration wsl, IReadOnlySet<string> measuredOnNativeLinux) => host switch
    {
        HostKind.Windows => HostKinds.RunsInWsl(executable) ? WslBridge(runtime, executable) : string.Empty,
        HostKind.Wsl => wsl == WslGeneration.Wsl2 ? string.Empty : Wsl2Only(runtime, "it is measured on WSL2, and this is WSL1", "; Run this from Windows (not inside WSL): `wsl --set-version <distro> 2`"),
        HostKind.Linux => NativeLinux(runtime, wsl, measuredOnNativeLinux),
        _ => NothingMeasured(runtime, host),
    };

    /// <summary>
    /// Membership is asked FIRST: a runtime not in the set gets today's sentence whatever the banner says, so an empty set changes no answer. A runtime in the set is admitted only on a banner that was READ and names no WSL — an
    /// ALLOW-LIST (<c>(true, None)</c> is the one admitting arm; every other generation is refused, so a generation added later is refused until someone admits it). An unreadable banner could be a WSL1 kernel (DN6); a
    /// WSL banner on a host that read as native Linux cannot happen in production, and the two facts disagreeing is a refusal too.
    /// </summary>
    private static string NativeLinux(string runtime, WslGeneration wsl, IReadOnlySet<string> measured) => (InSet(runtime, measured), wsl) switch
    {
        (false, _) => Wsl2Only(runtime, "it is measured on WSL2 and not yet on a native Linux host", string.Empty),
        (true, WslGeneration.None) => string.Empty,
        (true, WslGeneration.Unreadable) => BannerUnreadable(runtime),
        (true, _) => BannerDisagrees(runtime, wsl),
    };

    private static bool InSet(string runtime, IReadOnlySet<string> measured) => measured.Any(name => name.Equals(runtime, StringComparison.OrdinalIgnoreCase));

    private static string BannerUnreadable(string runtime) =>
        $"{runtime} is refused on this Linux host — the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1 ({NativePlan})";

    private static string BannerDisagrees(string runtime, WslGeneration wsl) =>
        $"{runtime} is refused on this Linux host — the host reads as native Linux but its kernel banner says {wsl}; the two facts disagree, so coai cannot tell what it is running on ({NativePlan})";

    private static string NothingMeasured(string runtime, HostKind host) =>
        $"{runtime} is refused on a {HostKinds.Word(host)} host until it is measured there ({Parent}) — {runtime} is measured on Windows and WSL2 only";

    private static string Wsl2Only(string runtime, string why, string cure) =>
        $"{runtime} runs on WSL2 only for now — {why} ({Plan}){cure}";

    private static string WslBridge(string runtime, string executable) =>
        $"'{executable}' would start {runtime} on the Linux side, which is refused until it is measured there ({Parent}) — point this row at a Windows {runtime.ToLowerInvariant()}";
}
