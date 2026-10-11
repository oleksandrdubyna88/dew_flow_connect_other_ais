using System.Diagnostics;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Runs <c>coai-mcp</c> in one of its one-shot modes exactly as the panel runs it — a child process
/// with its own data directory — and hands back what the panel reads: the exit code and stdout.
/// </summary>
/// <remarks>
/// <para>The shape <c>LogCliScenarioTests</c>, <c>CloseConsultCliScenarioTests</c> and
/// <c>ConsultantModesCliScenarioTests</c> each carry as a private <c>Run</c>; written once here for
/// the round counter's scenario rather than copied a fourth time (reuse-first: the shared half is
/// extracted, the three existing copies are left as they are and named in the task's summary).</para>
/// <para>BOTH pipes are drained, and concurrently. Reading stdout to the end while stderr fills its
/// buffer is the classic deadlock: the child blocks writing, this blocks reading, and the timeout never
/// gets a chance because nothing is timing the READ. And a process that outlives its deadline is KILLED
/// rather than left behind holding the temp directory the test is about to delete — by its own
/// handle, never by image name.</para>
/// </remarks>
internal static class OneShotProcess
{
    /// <summary>The exit code and both streams of one run of the built binary against <paramref name="dataDir"/>.</summary>
    internal static (int Code, string Out, string Err) Run(string dataDir, params string[] args)
    {
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["COAI_DATA_DIR"] = dataDir;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException($"`{string.Join(' ', args)}` did not answer and exit within a minute");
        }

        Task.WaitAll([stdout, stderr], 10_000).Should().BeTrue("the pipes close when the child does");

        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
