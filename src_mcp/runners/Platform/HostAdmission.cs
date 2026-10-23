namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The ONE host admission for a runtime that is measured on Windows and on WSL2 (DL1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): Vibe's since E2, Kimi's since E6 — and admitted on a native Linux host, Vibe on its
/// measurement and Kimi on the owner's decision of 2026-10-23 (DN1 of E2: "unmeasured" is a NOTE, never a refusal). The sentences are written once and take the runtime's name, so the two runtimes cannot word the same refusal two ways.
/// </summary>
/// <remarks>
/// Admitted: a Windows host starting a Windows CLI, and a WSL2 host. Refused, each with its own sentence: a WSL1 kernel (which reads as <see cref="HostKind.Wsl"/>, and is a syscall translation layer),
/// a native Linux host (unless the runtime is in <see cref="AdmittedOnNativeLinux"/>, DN1 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>: <b>Vibe is</b>, on its measurement — measured on a native Linux host, research/RESULTS_vibe_confinement.md §32
/// (Vibe 2.26.1, ubuntu-claude, 2026-10-22), and its launch there closes the keyring road, <c>PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring</c> (DN10, <c>VibeLaunch.Built</c>) — the keyring road was measured closed on Vibe 2.26.1
/// (keyring 25.7.0); <b>Kimi is</b>, on the owner's decision of 2026-10-23 and NOT on a measurement — its launch there carries <see cref="Note(string, HostKind, WslGeneration)"/>; a runtime in the set is still refused when the kernel banner could not be read, DN6 — an AMBIGUITY refusal, coai cannot tell native Linux from WSL1, so it stays), a Windows host that would start the Linux side by <c>wsl.exe</c> or a <c>\\wsl$</c> path (D18: a Windows process cannot take a Linux lock or end a
/// Linux process tree), and every other host.
/// </remarks>
public static class HostAdmission
{
    private const string Plan = "DL1 of research/PLAN_kimi_and_vibe_on_linux.md";

    private const string Parent = "D18 of todo/PLAN_vibe_and_kimi_runtimes.md";

    private const string NativePlan = "DN6 of todo/PLAN_kimi_and_vibe_on_native_linux.md";

    /// <summary>
    /// The runtimes (<c>Vibe</c>, <c>Kimi</c>) admitted on a native Linux host. <c>Vibe</c> is in it on a MEASUREMENT, research/RESULTS_vibe_confinement.md §32 (Vibe 2.26.1, ubuntu-claude, 2026-10-22: N-V1–N-V6 all passed with the
    /// keyring road closed in every launch); <c>Kimi</c> is in it on the OWNER'S DECISION of 2026-10-23 (basis <c>owner-decision</c>: admitted unmeasured, with a note). Membership is asked FIRST, so a runtime outside the set gets the WSL2-only sentence whatever the banner says.
    /// The ABSENCE of a fact is a refusal: no entry, no admission; no read banner, no admission (DN6). There is no default member, no settings switch and no environment switch — a runtime enters by an edit to <c>shared/native-linux-admission.json</c> (the names of <see cref="NativeLinuxAdmission.Since"/>) that names the
    /// RESULTS section its cells are in (or the decision it was admitted by); and a keyring-session fact (<c>KeyringSessionFacts</c>) is recorded by <c>--host-facts</c> and decides nothing here (DN10): a fact added to this arm is a REFUSAL condition on a member, never a road into the set.
    /// Compared case-insensitively, as <see cref="Refusal(string, HostKind, string, WslGeneration)"/> receives the names.
    /// </summary>
    public static IReadOnlySet<string> AdmittedOnNativeLinux { get; } = new HashSet<string>(NativeLinuxAdmission.Since.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one-sentence NOTE a launch on this host carries (the launch's warning, the card's warning — the channel an unmeasured CLI version already uses), or empty. A runtime admitted on a native Linux host by the OWNER'S DECISION
    /// (<see cref="AdmissionBasis.OwnerDecision"/>, 2026-10-23: "unmeasured" is a note, never a refusal) says so there, and only there: a measured runtime, every other host, and a banner that was not read say nothing — a launch refused for
    /// the banner says the refusal. The sentence is the shared file's own <c>note</c>.
    /// </summary>
    public static string Note(string runtime, HostKind host, WslGeneration wsl) => Note(runtime, host, wsl, NativeLinuxAdmission.Entries);

    /// <summary><see cref="Note(string, HostKind, WslGeneration)"/> against <paramref name="admissions"/> — a test's way to give a runtime an owner-decision row the product has not made.</summary>
    internal static string Note(string runtime, HostKind host, WslGeneration wsl, IReadOnlyDictionary<string, NativeAdmission> admissions) =>
        host == HostKind.Linux && wsl == WslGeneration.None && admissions.TryGetValue(runtime, out var admission) && admission.Basis == AdmissionBasis.OwnerDecision
            ? admission.Note
            : string.Empty;

    /// <summary>
    /// Empty when <paramref name="runtime"/> (<c>Vibe</c>, <c>Kimi</c>) may start on this host, else the sentence. <paramref name="executable"/> is the CLI the launch would start; <paramref name="wsl"/> is
    /// which WSL the kernel is when <paramref name="host"/> is <see cref="HostKind.Wsl"/>.
    /// </summary>
    public static string Refusal(string runtime, HostKind host, string executable, WslGeneration wsl) => Refusal(runtime, host, executable, wsl, AdmittedOnNativeLinux);

    /// <summary><see cref="Refusal(string, HostKind, string, WslGeneration)"/> against <paramref name="admittedOnNativeLinux"/> — a test's way to give a runtime a native Linux admission the product has not yet earned.</summary>
    internal static string Refusal(string runtime, HostKind host, string executable, WslGeneration wsl, IReadOnlySet<string> admittedOnNativeLinux) => host switch
    {
        HostKind.Windows => HostKinds.RunsInWsl(executable) ? WslBridge(runtime, executable) : string.Empty,
        HostKind.Wsl => wsl == WslGeneration.Wsl2 ? string.Empty : Wsl1(runtime),
        HostKind.Linux => NativeLinux(runtime, wsl, admittedOnNativeLinux),
        _ => NoLaunchPath(runtime, host),
    };

    /// <summary>
    /// Membership is asked FIRST: a runtime not in the set gets today's sentence whatever the banner says, so an empty set changes no answer. A runtime in the set is admitted only on a banner that was READ and names no WSL — an
    /// ALLOW-LIST (<c>(true, None)</c> is the one admitting arm; every other generation is refused, so a generation added later is refused until someone admits it). An unreadable banner could be a WSL1 kernel (DN6); a
    /// WSL banner on a host that read as native Linux cannot happen in production, and the two facts disagreeing is a refusal too.
    /// </summary>
    private static string NativeLinux(string runtime, WslGeneration wsl, IReadOnlySet<string> admitted) => (InSet(runtime, admitted), wsl) switch
    {
        (false, _) => NoRow(runtime),
        (true, WslGeneration.None) => string.Empty,
        (true, WslGeneration.Unreadable) => BannerUnreadable(runtime),
        (true, _) => BannerDisagrees(runtime, wsl),
    };

    private static bool InSet(string runtime, IReadOnlySet<string> admitted) => admitted.Any(name => name.Equals(runtime, StringComparison.OrdinalIgnoreCase));

    private static string BannerUnreadable(string runtime) =>
        $"{runtime} is refused on this Linux host — the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1 ({NativePlan})";

    private static string BannerDisagrees(string runtime, WslGeneration wsl) =>
        $"{runtime} is refused on this Linux host — the host reads as native Linux but its kernel banner says {wsl}; the two facts disagree, so coai cannot tell what it is running on ({NativePlan})";

    /// <summary>
    /// A host with NO launch path (macOS, any other): the sentence names what is missing there, not that nobody measured it (the owner's decision of 2026-10-23: a refusal whose only reason is "not measured" is a note). Kimi: its sign-in hold
    /// needs an exclusive lock that its processes inherit (<see cref="HostKinds.HasKimiLockFamily"/>). Vibe: coai links its sign-in only after reading the file through a descriptor, which <c>RegularFiles</c> can do on Linux only
    /// (<c>RegularFiles.LinuxOnly</c>) — on such a host every launch would be refused at that step, or run signed out.
    /// </summary>
    private static string NoLaunchPath(string runtime, HostKind host) =>
        $"{runtime} has no launch path on a {HostKinds.Word(host)} host — {NoPathReason(runtime)} ({Parent})";

    private static string NoPathReason(string runtime) => runtime.Equals("Kimi", StringComparison.OrdinalIgnoreCase)
        ? "its sign-in hold needs an exclusive lock that its processes inherit, and only Windows, Linux and WSL give one"
        : "coai links its sign-in only after reading it through a file descriptor, which it can do on Linux only";

    /// <summary>
    /// WSL1 is refused for what it IS, not for being unmeasured: it translates Linux system calls for Windows and has no pidfd, so coai cannot end the CLI's process tree by identity (the tree supervisor and the verified teardown need one).
    /// The cure is the distro's: WSL2.
    /// </summary>
    private static string Wsl1(string runtime) =>
        $"{runtime} is refused on this WSL1 host — WSL1 translates Linux system calls for Windows and has no pidfd, so coai cannot end {runtime}'s process tree by identity ({Plan}); Run this from Windows (not inside WSL): `wsl --set-version <distro> 2`";

    /// <summary>A runtime with NO row in <c>shared/native-linux-admission.json</c>: an absent fact refuses (no runtime is in that state today).</summary>
    private static string NoRow(string runtime) =>
        $"{runtime} is not admitted on a native Linux host — shared/native-linux-admission.json has no row for it, and an absent fact refuses ({NativePlan})";

    private static string WslBridge(string runtime, string executable) =>
        $"'{executable}' would start {runtime} on the Linux side, which a Windows process cannot supervise — it cannot take a Linux lock or end a Linux process tree ({Parent}) — point this row at a Windows {runtime.ToLowerInvariant()}";
}
