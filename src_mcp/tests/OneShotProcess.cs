using System.Diagnostics;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Runs <c>coai-mcp</c> in one of its one-shot modes exactly as the panel runs it — a child process
/// with its own data directory — and hands back what the panel reads: the exit code and stdout.
/// </summary>
/// <remarks>
/// <para>The ONE one-shot runner of this suite. Written for the round counter's scenario, then the
/// three older copies were folded onto it: <c>LogCliScenarioTests</c> and
/// <c>CloseConsultCliScenarioTests</c> call <see cref="Run(string, string[])"/> as they are, and
/// <c>ConsultantModesCliScenarioTests</c> shapes its child's environment (the fake CLI's steering,
/// inherited <c>COAI_*</c>/<c>FAKECLI_*</c> removed, its own temp directory) on top of
/// <see cref="StartInfo"/> and runs it through <see cref="Run(ProcessStartInfo, TimeSpan)"/> with its
/// own, longer deadline. Its two tests that start a LONG-LIVED child — a stdio server it ends by
/// closing stdin, and a holder it kills mid-turn on purpose — use <see cref="StartInfo"/> only: they
/// are not one-shot runs, and a deadline that kills them would be the wrong contract.</para>
/// <para>BOTH pipes are drained, and concurrently. Reading stdout to the end while stderr fills its
/// buffer is the classic deadlock: the child blocks writing, this blocks reading, and the timeout never
/// gets a chance because nothing is timing the READ. And a process that outlives its deadline is KILLED
/// rather than left behind holding the temp directory the test is about to delete — by its own
/// handle, never by image name.</para>
/// </remarks>
internal static class OneShotProcess
{
    /// <summary>How long a one-shot mode with no CLI behind it gets to answer and exit.</summary>
    internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(1);

    /// <summary>The exit code and both streams of one run of the built binary against <paramref name="dataDir"/>.</summary>
    internal static (int Code, string Out, string Err) Run(string dataDir, params string[] args) =>
        Run(StartInfo(dataDir, args), DefaultDeadline);

    /// <summary>
    /// The built binary with <paramref name="args"/>, both output pipes redirected and
    /// <c>COAI_DATA_DIR</c> set to <paramref name="dataDir"/> — for a caller that shapes the rest of
    /// the child's environment itself before starting it.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string dataDir, params string[] args)
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
        return info;
    }

    /// <summary>
    /// Starts <paramref name="info"/> (both output pipes redirected), drains both pipes, and kills the
    /// child by its own handle if it has not exited within <paramref name="deadline"/>.
    /// </summary>
    internal static (int Code, string Out, string Err) Run(ProcessStartInfo info, TimeSpan deadline)
    {
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(deadline))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException(
                $"`{string.Join(' ', info.ArgumentList)}` did not answer and exit within {deadline.TotalSeconds:0} s");
        }

        Task.WaitAll([stdout, stderr], 10_000).Should().BeTrue("the pipes close when the child does");

        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
