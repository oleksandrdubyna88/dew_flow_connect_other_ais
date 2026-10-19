using System.Diagnostics;
using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shim of a Kimi tree, played by a CHILD process (<c>FakeCli tree-hold</c>: the product's own <c>KimiCredentialsLock.HoldTree</c>, inheritable, in a real
/// second process) and the probes that ask what its hold does — shared by the suites that need a second process holding the sign-in
/// (E4.2 S2.43, and L-N1 on Linux), so no suite copies them. The test host itself NEVER holds an inheritable handle (<see cref="NoTestHoldsAnInheritableHandleTests"/>).
/// </summary>
internal static class ShimChild
{
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static string FakeCli => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    /// <summary>The shim as a child: <c>held &lt;grandchild pid&gt;</c> on stdout once it has taken <paramref name="mode"/>'s lock.</summary>
    internal static async Task<(Process Shim, Process Grandchild)> StartAsync(string mode, string locks, string target, string home)
    {
        var shim = Process.Start(new ProcessStartInfo(FakeCli, ["tree-hold", mode, locks, target, home]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var line = await shim.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        if (line is null)
        {
            await shim.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"the child ended (exit {shim.ExitCode}) without taking its lock: {await shim.StandardError.ReadToEndAsync()}");
        }

        line.Should().StartWith("held ", "the child takes its lock before it says so");

        return (shim, Process.GetProcessById(int.Parse(line![5..])));
    }

    /// <summary>The shim as a child that is expected NOT to take its lock: its exit code and what it said on stderr.</summary>
    internal static async Task<(int Exit, string Error)> FailsAsync(string mode, string locks, string target, string home)
    {
        using var shim = Process.Start(new ProcessStartInfo(FakeCli, ["tree-hold", mode, locks, target, home]) { RedirectStandardError = true, UseShellExecute = false })!;
        var error = await shim.StandardError.ReadToEndAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await shim.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (shim.ExitCode, error);
    }

    /// <summary>
    /// Whether the file is Held at EVERY probe for 1.5 s. Never one probe: handle rundown after a process exit takes a moment, so a
    /// single probe right after a kill proves nothing in either direction.
    /// </summary>
    internal static async Task<bool> StaysHeldAsync(string path)
    {
        var until = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < until)
        {
            if (HeldFile.Probe(path) != LockProbe.Held)
            {
                return false;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return true;
    }

    /// <summary>The process ended (killed when it still runs) and waited for.</summary>
    internal static async Task EndAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
        }

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    }
}
