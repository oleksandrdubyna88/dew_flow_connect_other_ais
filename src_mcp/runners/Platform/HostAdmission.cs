namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The ONE host admission for a runtime that is measured on Windows and on WSL2 (DL1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): Vibe's since E2, Kimi's since E6. The sentences are written
/// once and take the runtime's name, so the two runtimes cannot word the same refusal two ways.
/// </summary>
/// <remarks>
/// Admitted: a Windows host starting a Windows CLI, and a WSL2 host. Refused, each with its own sentence: a WSL1 kernel (which reads as <see cref="HostKind.Wsl"/>, and is a syscall translation layer),
/// a native Linux host (nothing is measured there yet), a Windows host that would start the Linux side by <c>wsl.exe</c> or a <c>\\wsl$</c> path (D18: a Windows process cannot take a Linux lock or end a
/// Linux process tree), and every other host.
/// </remarks>
public static class HostAdmission
{
    private const string Plan = "DL1 of research/PLAN_kimi_and_vibe_on_linux.md";

    private const string Parent = "D18 of todo/PLAN_vibe_and_kimi_runtimes.md";

    /// <summary>
    /// Empty when <paramref name="runtime"/> (<c>Vibe</c>, <c>Kimi</c>) may start on this host, else the sentence. <paramref name="executable"/> is the CLI the launch would start; <paramref name="wsl"/> is
    /// which WSL the kernel is when <paramref name="host"/> is <see cref="HostKind.Wsl"/>.
    /// </summary>
    public static string Refusal(string runtime, HostKind host, string executable, WslGeneration wsl) => host switch
    {
        HostKind.Windows => HostKinds.RunsInWsl(executable) ? WslBridge(runtime, executable) : string.Empty,
        HostKind.Wsl => wsl == WslGeneration.Wsl2 ? string.Empty : Wsl2Only(runtime, "it is measured on WSL2, and this is WSL1", "; Run this from Windows (not inside WSL): `wsl --set-version <distro> 2`"),
        HostKind.Linux => Wsl2Only(runtime, "it is measured on WSL2 and not yet on a native Linux host", string.Empty),
        _ => NothingMeasured(runtime, host),
    };

    private static string NothingMeasured(string runtime, HostKind host) =>
        $"{runtime} is refused on a {HostKinds.Word(host)} host until it is measured there ({Parent}) — {runtime} is measured on Windows and WSL2 only";

    private static string Wsl2Only(string runtime, string why, string cure) =>
        $"{runtime} runs on WSL2 only for now — {why} ({Plan}){cure}";

    private static string WslBridge(string runtime, string executable) =>
        $"'{executable}' would start {runtime} on the Linux side, which is refused until it is measured there ({Parent}) — point this row at a Windows {runtime.ToLowerInvariant()}";
}
