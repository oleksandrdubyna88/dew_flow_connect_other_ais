using System.Diagnostics;
using System.Globalization;

namespace CoaiMcp.FakeCli;

/// <summary>
/// A memory hog and its parent, for the memory ceiling of a role runner's unit (epoch 3 E1 s1, scenario A14). <c>alloc &lt;mb&gt; &lt;log&gt;</c> allocates and touches ten megabytes at a time, writing <c>step N</c> to the log
/// after each and <c>done</c> when it has the lot — a process the kernel killed leaves a log with no <c>done</c>. <c>parent-of-alloc &lt;mb&gt; &lt;log&gt; &lt;result&gt;</c> is the runner's shape: a small parent
/// that starts the hog as its CHILD, waits for it and writes <c>exit=&lt;code&gt;</c> into the result file — so a test can show that the over-limit CHILD is the one that dies and the parent lives to say so.
/// </summary>
internal static class AllocVerbs
{
    private const int StepMegabytes = 10;

    /// <remarks>
    /// NATIVE memory, on purpose: the .NET runtime reads the cgroup's limit and caps its own heap at three quarters of it, so a managed allocation past the ceiling ends in an
    /// <c>OutOfMemoryException</c> (exit 97 here) and never reaches the kernel's kill — measured 2026-11-23. Native pages are the kernel's to account, which is what a vendor CLI's are.
    /// </remarks>
    internal static int Alloc(int megabytes, string log)
    {
        for (var step = 1; step <= megabytes / StepMegabytes; step++)
        {
            const int PageBytes = 4096;
            var size = StepMegabytes * 1024 * 1024;
            var block = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            for (var offset = 0; offset < size; offset += PageBytes)
            {
                System.Runtime.InteropServices.Marshal.WriteByte(block, offset, 1);
            }

            File.AppendAllText(log, $"step {step}\n");
            Thread.Sleep(40);
        }

        File.AppendAllText(log, "done\n");
        Thread.Sleep(1500);

        return 0;
    }

    internal static int ParentOfAlloc(int megabytes, string log, string result)
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["alloc", megabytes.ToString(CultureInfo.InvariantCulture), log]) { UseShellExecute = false })!;
        child.WaitForExit();
        File.WriteAllText(result, $"exit={child.ExitCode}");

        return 0;
    }
}
