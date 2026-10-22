using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.FakeCli;

/// <summary>
/// L-N1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c> as a CHILD process: the product's own inheritable take, <c>HeldFile.Take(path, inheritable: true)</c>, on one file.
/// <c>hold</c> keeps it, starts a sleeping grandchild AFTER the take (so the grandchild is the process that may inherit the descriptor), prints <c>held &lt;grandchild pid&gt;</c> and sleeps;
/// <c>try</c> takes it once and prints <c>taken</c>, <c>busy</c> or <c>unusable</c>, then exits. A real second process, because what is measured is what the kernel's lock does between processes.
/// <c>fork-fail</c> (C26, Linux): takes the native inheritable hold, and in the place where the stream over the descriptor is made it <c>fork</c>s a child that stays BEFORE any exec, then fails the stream —
/// printing <c>forked &lt;pid&gt;</c> and then <c>unusable</c>. The child is what a sibling thread's fork is in the test host: a copy of the descriptor that no exec has closed.
/// </summary>
internal static class HoldFileVerb
{
    internal static int Run(string mode, string file) => mode switch
    {
        "hold" => KeepHolding(file),
        "try" => TakeOnce(file),
        "fork-fail" => TakeThenForkThenFail(file),
        _ => 64,
    };

    [DllImport("libc", EntryPoint = "fork")]
    private static extern int Fork();

    [DllImport("libc", EntryPoint = "pause")]
    private static extern int Pause();

    /// <summary>The take whose stream cannot be made, with a PRE-EXEC child alive at the moment of failure. The child never execs and never exits by itself: it sits in <c>pause()</c> until the test kills it.</summary>
    /// <remarks>
    /// <c>pause</c> is bound BEFORE the fork (<see cref="Marshal.Prelink(MethodInfo)"/>), so the child — which has one thread and a runtime that was running others — does no symbol lookup between the fork and the wait.
    /// What the child does if the runtime misbehaves there is "stay" or "die"; the test reads the child's pid and fails with a sentence if it is not alive when the probe runs.
    /// </remarks>
    private static int TakeThenForkThenFail(string file)
    {
        if (!OperatingSystem.IsLinux())
        {
            return 64;
        }

        Marshal.Prelink(typeof(HoldFileVerb).GetMethod(nameof(Pause), BindingFlags.NonPublic | BindingFlags.Static)!);
        var hold = LinuxHeldFile.TakeInheritable(file, handle =>
        {
            GC.KeepAlive(handle);
            var child = Fork();
            while (child == 0)
            {
                _ = Pause();
            }

            Console.Out.WriteLine($"forked {child}");
            Console.Out.Flush();

            throw new IOException("the stream could not be made");
        });
        Console.Out.WriteLine(hold is Hold.Unusable ? "unusable" : "other");
        Console.Out.Flush();

        return 0;
    }

    private static int KeepHolding(string file)
    {
        if (HeldFile.Take(file, inheritable: true) is not Hold.Taken taken)
        {
            Console.Error.WriteLine($"fake-cli: the inheritable hold was not taken on {file}");

            return 69;
        }

        var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["sleep", "30000"]) { UseShellExecute = false })!;
        Console.Out.WriteLine($"held {grandchild.Id}");
        Console.Out.Flush();
        Thread.Sleep(60_000);
        GC.KeepAlive(taken);

        return 0;
    }

    private static int TakeOnce(string file)
    {
        var answer = HeldFile.Take(file, inheritable: true) switch
        {
            Hold.Taken taken => Release(taken),
            Hold.Busy => "busy",
            _ => "unusable",
        };
        Console.Out.WriteLine(answer);

        return 0;
    }

    private static string Release(Hold.Taken taken)
    {
        taken.Stream.Dispose();

        return "taken";
    }
}
