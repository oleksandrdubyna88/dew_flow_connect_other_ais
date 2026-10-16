using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The kind of machine this server runs on, as far as what a vendor CLI can do differs between them.
/// </summary>
/// <remarks>
/// <para><b>WSL is its own kind, not Linux.</b> A Claude Code or Codex session inside WSL spawns a Linux
/// <c>coai-mcp</c> with its own data directory and its own CLIs, and what those CLIs can reach differs from a native
/// Linux box in the way that matters here: the Windows drives are mounted under <c>/mnt/c</c>, so a CLI that reads
/// anywhere on a WSL side reads the Windows user's files too.</para>
/// <para><b><see cref="Other"/> is a real answer</b>, not a fall-through to Linux. A limitation row is a claim about
/// a platform somebody measured or read about; on a platform nobody has, the honest row is "not measured" — never
/// the nearest platform's row (shared/consultant-limitations.json, <c>ConsultantLimitations</c>).</para>
/// </remarks>
public enum HostKind
{
    Other = 0,
    Windows,
    Linux,
    Wsl,
    MacOs,
}

/// <summary>Which <see cref="HostKind"/> this is, and the word each one is written as.</summary>
public static class HostKinds
{
    /// <summary>This process's host — the operating system, and the WSL detection <see cref="VendorDiagnosis"/> already has.</summary>
    public static HostKind Current =>
        Of(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), VendorDiagnosis.OnWsl);

    /// <summary>
    /// Whether a configured executable would start the Linux side: <c>wsl</c> / <c>wsl.exe</c> by file name, or anything under
    /// <c>\\wsl…</c> — D18's "a WSL bridge named as the executable is the Linux side by another door". Shared by the Vibe and Kimi
    /// launch controls (E4.2 decision 7), moved here from <c>VibeLaunch</c> rather than copied.
    /// </summary>
    /// <remarks>The file name is cut at EITHER separator, not by <c>Path.GetFileName</c>: a Windows path is judged the same on a Linux test host.</remarks>
    public static bool RunsInWsl(string executable)
    {
        var trimmed = executable.Trim();
        var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];

        return name.Equals("wsl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wsl.exe", StringComparison.OrdinalIgnoreCase)
            || executable.TrimStart().StartsWith(@"\\wsl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The host the four facts describe — pure, so every platform is a unit test on any one of them.</summary>
    public static HostKind Of(bool windows, bool linux, bool macOs, bool wsl) => (windows, linux, macOs) switch
    {
        (true, _, _) => HostKind.Windows,
        (_, true, _) => wsl ? HostKind.Wsl : HostKind.Linux,
        (_, _, true) => HostKind.MacOs,
        _ => HostKind.Other,
    };

    /// <summary>
    /// The word a health file, a limitation row and the panel name the host by: <c>windows</c>, <c>linux</c>,
    /// <c>wsl</c>, <c>macos</c> — or <c>other</c>.
    /// </summary>
    public static string Word(HostKind host) => host switch
    {
        HostKind.Windows => "windows",
        HostKind.Linux => "linux",
        HostKind.Wsl => "wsl",
        HostKind.MacOs => "macos",
        _ => "other",
    };
}
