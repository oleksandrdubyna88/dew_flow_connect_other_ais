using System.Diagnostics;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The shim as a CHILD process (E4.2 S2.43 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): it takes the product's own sign-in
/// hold, starts a sleeping grandchild — the stand-in for <c>kimi</c>, which inherits the hold — prints <c>held &lt;pid&gt;</c> and
/// sleeps. Run in a real second process, because what is measured is what a handle does when its opener dies and a child lives on.
/// </summary>
internal static class TreeHoldVerb
{
    internal static int Run(string mode, string locks, string target, string home)
    {
        using var _ = KimiCredentialsLock.RootFor(locks);
        var held = mode.StartsWith("tree", StringComparison.Ordinal) ? TakeTree(mode, target, home) : TakeGate(target);
        if (held is null)
        {
            return 69;
        }

        var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["sleep", "30000"]) { UseShellExecute = false })!;
        Console.Out.WriteLine($"held {grandchild.Id}");
        Console.Out.Flush();
        Thread.Sleep(60_000);
        GC.KeepAlive(held);

        return 0;
    }

    /// <summary>The tree hold, taken for the sleeping grandchild to inherit; null (and said) when it could not be.</summary>
    private static IDisposable? TakeTree(string mode, string target, string home)
    {
        if (mode == "tree-probed")
        {
            // A disposal's probe meets the shim: the per-home file is held for a moment between the two takes (M1).
            KimiCredentialsLock.BeforeTheHomeHold = MeetADisposalsProbe;
        }

        var holding = KimiCredentialsLock.HoldTree(target, home, TimeSpan.FromSeconds(1));
        if (holding is TreeHolding.Taken taken)
        {
            return taken.Hold;
        }

        Console.Error.WriteLine(holding is TreeHolding.Unusable unusable
            ? $"fake-cli: the tree hold was not taken: Unusable: {unusable.Cause.Message}"
            : $"fake-cli: the tree hold was not taken: {holding.GetType().Name}");

        return null;
    }

    /// <summary>Holds the per-home file for a quarter of a second, as a probe does, and returns once it is held.</summary>
    private static void MeetADisposalsProbe(string path)
    {
        var probe = (Hold.Taken)HeldFile.Take(path);
        _ = Task.Delay(250).ContinueWith(_ => probe.Stream.Dispose(), TaskScheduler.Default);
    }

    /// <summary>The server's gate alone — NOT inheritable — the control arm.</summary>
    private static IDisposable? TakeGate(string target) =>
        KimiCredentialsLock.TakeAsync(target, CancellationToken.None).GetAwaiter().GetResult() is Hold.Taken taken ? taken.Stream : null;
}
