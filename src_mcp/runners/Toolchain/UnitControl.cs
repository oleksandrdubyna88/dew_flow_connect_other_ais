using System.ComponentModel;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>What <see cref="UnitControl.StartAsync"/> answers: the client's result, or the sentence that says there is no <c>systemd-run --user</c> to start a unit with.</summary>
internal sealed record UnitStart(ProcessResult Result, string Problem);

/// <summary>
/// The user manager, as the OS boundaries ask it: start the <c>systemd-run --wait --pipe</c> client through the one launcher, ask whether a unit is still there, and stop it when it is. The check boundary
/// (<see cref="CheckBoundary"/>) and the coder's boundary (<see cref="CoderBoundary"/>) share it, so the rule that killing the CLIENT does not stop the UNIT is written once.
/// </summary>
/// <remarks>
/// Killing the <c>systemd-run</c> client does NOT stop the unit — the process tree belongs to the user manager — so every start is followed by <see cref="EnsureGoneAsync"/>: on a timeout, on a cancel and on a client that died.
/// </remarks>
internal sealed class UnitControl(IProcessLauncher launcher, BoundaryHost host)
{
    private static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(20);

    /// <summary>How long past the step's own deadline systemd lets the unit live if nobody stops it.</summary>
    internal static readonly TimeSpan Backstop = TimeSpan.FromMinutes(1);

    /// <summary>What the <c>systemd-run</c> CLIENT is started with. The unit's own environment is set by <c>env -i</c> inside it; the client only needs to find the user manager.</summary>
    internal static readonly IReadOnlySet<string> Client = new HashSet<string>(["PATH", "HOME", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS", "LANG"], StringComparer.Ordinal);

    /// <summary>Starts the client described by <paramref name="request"/> through the launcher. A <c>systemd-run</c> that is not there is a sentence, not an exception.</summary>
    internal async Task<UnitStart> StartAsync(ProcessRequest request, CancellationToken ct)
    {
        try
        {
            return new UnitStart(await launcher.RunAsync(request, ct), string.Empty);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new UnitStart(new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, string.Empty, TimedOut: false), $"there is no systemd-run --user on this machine ({e.Message})");
        }
    }

    /// <summary>Asks the manager whether the unit is still there and stops it when it is. True when no process of it is left — the unit is not active now.</summary>
    internal async Task<bool> EnsureGoneAsync(string unit)
    {
        if (!await IsActiveAsync(unit))
        {
            return true;
        }

        await ControlAsync(["--user", "stop", $"{unit}.service"]);

        return !await IsActiveAsync(unit);
    }

    private async Task<bool> IsActiveAsync(string unit)
    {
        var shown = await ControlAsync(["--user", "show", "--property=ActiveState", "--value", $"{unit}.service"]);
        var state = shown.StdOut.Trim();

        // An answer that is not a state — the manager did not answer — counts as "still there": a unit that cannot be shown cannot be called gone.
        return shown.ExitCode != 0 || state is "active" or "activating" or "deactivating" or "reloading" or "";
    }

    private async Task<ProcessResult> ControlAsync(string[] arguments)
    {
        try
        {
            return await launcher.RunAsync(new ProcessRequest(host.Systemctl, arguments, Path.GetTempPath()) { Timeout = StopBudget, InheritsEnvironment = false, Passthrough = Client }, CancellationToken.None);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, e.Message, TimedOut: false);
        }
    }
}
