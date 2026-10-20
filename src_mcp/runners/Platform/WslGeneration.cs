using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Platform;

/// <summary>Which WSL a Linux kernel is: WSL2 (a real Linux kernel), WSL1 (a syscall translation layer), or neither.</summary>
public enum WslGeneration
{
    None = 0,
    Wsl1,
    Wsl2,
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

    /// <summary>This process's kernel, asked once — through the SAME <c>/proc/version</c> read <see cref="VendorDiagnosis.OnWsl"/> makes.</summary>
    public static WslGeneration Current => ThisKernel.Value;

    private static readonly Lazy<WslGeneration> ThisKernel = new(() => Of(ProcVersion.Text));
}

/// <summary>The kernel's <c>/proc/version</c> banner, read once — the one reader <see cref="VendorDiagnosis.OnWsl"/> and <see cref="WslGenerations.Current"/> share.</summary>
internal static class ProcVersion
{
    /// <summary>The banner, or empty off Linux or when it cannot be read (an unreadable banner names no WSL).</summary>
    public static string Text => Banner.Value;

    private static readonly Lazy<string> Banner = new(Read);

    private static string Read()
    {
        try
        {
            return OperatingSystem.IsLinux() && File.Exists("/proc/version") ? File.ReadAllText("/proc/version") : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
