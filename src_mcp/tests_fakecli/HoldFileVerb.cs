using System.Diagnostics;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.FakeCli;

/// <summary>
/// L-N1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c> as a CHILD process: the product's own inheritable take, <c>HeldFile.Take(path, inheritable: true)</c>, on one file.
/// <c>hold</c> keeps it, starts a sleeping grandchild AFTER the take (so the grandchild is the process that may inherit the descriptor), prints <c>held &lt;grandchild pid&gt;</c> and sleeps;
/// <c>try</c> takes it once and prints <c>taken</c>, <c>busy</c> or <c>unusable</c>, then exits. A real second process, because what is measured is what the kernel's lock does between processes.
/// </summary>
internal static class HoldFileVerb
{
    internal static int Run(string mode, string file) => mode switch
    {
        "hold" => KeepHolding(file),
        "try" => TakeOnce(file),
        _ => 64,
    };

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
