using CoaiMcp.Runners.Kimi;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The tests that start the REAL shim on a Windows host share one precondition beyond the OS: no real <c>kimi</c> of the person's runs on this machine.
/// The shim now waits for the person's own Kimi before it takes the sign-in's hold (decision 74 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>), so
/// with the owner's Kimi open these scenarios would wait out their deadline and report the product working as designed as a failure. They skip, saying so.
/// </summary>
internal static class TheOwnersKimi
{
    public static void SkipUnlessWindowsAndFree(string windowsOnlyReason)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), windowsOnlyReason);
        var running = ForeignKimiWatch.Foreign(HostProcessList.Current.Snapshot(), Environment.ProcessId);
        Assert.SkipWhen(running.Count > 0, $"a real Kimi is running on this machine (pid {running.FirstOrDefault()?.Pid}): the shim waits for the person's own Kimi (decision 74), so this scenario would wait out its deadline");
    }
}
