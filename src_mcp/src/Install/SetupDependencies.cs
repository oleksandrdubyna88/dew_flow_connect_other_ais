using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Install;

/// <summary>
/// Everything <c>--setup-clients</c> takes from outside — the environment, the platform, this binary's own path, the launcher, the clock, the vendor timeout and the
/// fault seam. A test builds one for a scratch home and cannot reach the real one.
/// </summary>
internal sealed record SetupDependencies(
    Func<string, string?> Env,
    bool IsWindows,
    string HostWord,
    string SelfPath,
    string SelfVersion,
    IProcessLauncher Launcher,
    TimeProvider Clock,
    int Pid,
    TimeSpan VendorTimeout,
    InstallFaults Faults)
{
    /// <summary>This process: its environment, platform, path and version. The path is NOT resolved through links — a <c>current</c> link is the stable name, and the target behind it is not.</summary>
    internal static SetupDependencies ForThisProcess(string versionText) => new(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows(),
        HostKinds.Word(HostKinds.Current),
        Environment.ProcessPath ?? string.Empty,
        versionText,
        new ProcessLauncher(),
        TimeProvider.System,
        Environment.ProcessId,
        VendorCommands.DefaultTimeout,
        InstallFaults.None);
}
