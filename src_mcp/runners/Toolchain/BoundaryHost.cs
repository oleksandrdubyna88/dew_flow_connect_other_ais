namespace CoaiMcp.Runners.Toolchain;

/// <summary>Which operating system the checks of a coder job would run on. Only <see cref="Linux"/> can have the boundary.</summary>
public enum BoundaryPlatform
{
    Linux,
    Windows,
    MacOs,
    Other,
}

/// <summary>
/// The facts about THIS machine the check boundary is built from: the person's real home (hidden from the unit, and the one source of the two read-only caches), the PATH coai was started with, the program that starts a unit, and a Windows
/// program the preflight tries to run. All of it is read once, here, so a test can hand in a temporary home and never touch the real one.
/// </summary>
/// <param name="Platform">The operating system. Anything but Linux is refused: there is no OS boundary and no write probe on Windows or macOS in this epic (decision 8, <c>RESULTS_coder_write_boundary_judgement.md</c> § 3).</param>
/// <param name="RealHome">The person's home folder. The unit never sees it: <c>ProtectHome=tmpfs</c> covers <c>/home</c>, <c>/root</c> and <c>/run/user</c>.</param>
/// <param name="NuGetPackages">The shared NuGet package folder (<c>~/.nuget/packages</c>) the unit gets back read-only as a fallback; empty or missing: none.</param>
/// <param name="Path">The PATH of coai's own process; only the entries under <c>~/.nvm</c> are taken from it (see <see cref="UnitPath"/>).</param>
/// <param name="SystemdRun">The program that starts a unit — <c>systemd-run</c>. A name that is not there, or a program that exits non-zero, is how a test makes a machine with no user manager.</param>
/// <param name="Systemctl">The program that stops a unit — <c>systemctl</c>.</param>
/// <param name="WindowsProgram">A Windows <c>.exe</c> the preflight tries to run from inside the unit; empty when this host has no WSL interop, and then there is nothing to try.</param>
public sealed record BoundaryHost(
    BoundaryPlatform Platform,
    string RealHome,
    string NuGetPackages,
    string Path,
    string SystemdRun,
    string Systemctl,
    string WindowsProgram)
{
    /// <summary>The system folders a check finds its tools in. Never a folder of the person's home and never a Windows folder under <c>/mnt</c>.</summary>
    public const string SystemPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    private const string InteropMarker = "/proc/sys/fs/binfmt_misc/WSLInterop";

    private const string WindowsWhoami = "/mnt/c/Windows/System32/whoami.exe";

    /// <summary>This machine, read now.</summary>
    public static BoundaryHost Current() => new(
        CurrentPlatform(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
        Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
        "systemd-run",
        "systemctl",
        System.IO.File.Exists(InteropMarker) && System.IO.File.Exists(WindowsWhoami) ? WindowsWhoami : string.Empty);

    private static BoundaryPlatform CurrentPlatform() =>
        OperatingSystem.IsLinux() ? BoundaryPlatform.Linux
        : OperatingSystem.IsWindows() ? BoundaryPlatform.Windows
        : OperatingSystem.IsMacOS() ? BoundaryPlatform.MacOs
        : BoundaryPlatform.Other;

    /// <summary>Where node is installed under the real home (<c>~/.nvm</c>).</summary>
    private string NvmRoot => System.IO.Path.Combine(RealHome, ".nvm");

    /// <summary>The PATH entries of coai's own PATH that lie under <c>~/.nvm</c> and exist — the one place under the home a check may find a tool.</summary>
    public IReadOnlyList<string> NvmEntries =>
        RealHome.Length == 0
            ? []
            : [.. Path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(entry => System.IO.Path.IsPathFullyQualified(entry) && entry.StartsWith(NvmRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) && Directory.Exists(entry))
                .Distinct(StringComparer.Ordinal)];

    /// <summary>The PATH inside the unit: the system folders, then the <c>~/.nvm</c> folders that are in coai's PATH.</summary>
    public string UnitPath => string.Join(':', [SystemPath, .. NvmEntries]);

    /// <summary>
    /// The folders of the real home the unit gets back, READ-ONLY, at the same path: the shared NuGet package folder (a fallback; the job's own <c>NUGET_PACKAGES</c> is the writable one) and, when node is found there, <c>~/.nvm</c>.
    /// Only those that exist: a bind of a path that is not there would fail the unit.
    /// </summary>
    public IReadOnlyList<string> ReadOnlyCaches =>
        [
            .. NuGetPackages.Length > 0 && Directory.Exists(NuGetPackages) ? [NuGetPackages] : Array.Empty<string>(),
            .. NvmEntries.Count > 0 && Directory.Exists(NvmRoot) ? [NvmRoot] : Array.Empty<string>(),
        ];
}
