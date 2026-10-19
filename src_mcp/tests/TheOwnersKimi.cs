using CoaiMcp.Runners.Kimi;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The tests that start the REAL shim share one precondition beyond the OS: no real <c>kimi</c> of the person's runs on this machine.
/// The shim now waits for the person's own Kimi before it takes the sign-in's hold (decision 74 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>), so
/// with the owner's Kimi open these scenarios would wait out their deadline and report the product working as designed as a failure. They skip, saying so.
/// </summary>
internal static class TheOwnersKimi
{
    public static void SkipUnlessWindowsAndFree(string windowsOnlyReason)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), windowsOnlyReason);
        SkipWhenARealKimiRuns();
    }

    /// <summary>Skips when this machine's process list cannot be read, or holds a real Kimi that is not this process's.</summary>
    public static void SkipWhenARealKimiRuns()
    {
        var listing = HostProcessList.Current.Snapshot();
        Assert.SkipWhen(listing is ProcessListing.Unreadable, $"this machine's process list cannot be read ({(listing as ProcessListing.Unreadable)?.Reason}), so the shim would give the turn up (DL10)");
        var running = ForeignKimiWatch.Foreign(((ProcessListing.Listed)listing).Processes, Environment.ProcessId);
        Assert.SkipWhen(running.Count > 0, $"a real Kimi is running on this machine (pid {running.FirstOrDefault()?.Pid}): the shim waits for the person's own Kimi (decision 74), so this scenario would wait out its deadline");
    }
}
