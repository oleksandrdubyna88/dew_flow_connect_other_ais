using System.Diagnostics;

namespace CoaiServer;

/// <summary>
/// Runs a child attached to THIS process's terminal, and waits for it.
/// </summary>
/// <remarks>
/// <para><b>Why this is not <c>ProcessLauncher</c>.</b> That launcher redirects stdout, stderr AND
/// stdin — which is correct for everything else here, because everything else wants the output as a
/// string. A sign-in is the one thing that does not: <c>codex login --device-auth</c> prints a URL
/// and a code and then waits for the person to finish in a browser. With the streams redirected the
/// operator sees nothing at all and the CLI reads EOF from a closed stdin, so the command that exists
/// to be interactive was the one command that could not be. Raised as Blocking on this change's code
/// round, and it was right.</para>
/// <para>Widening <c>ProcessLauncher</c> with a "do not redirect" flag was the other option and is
/// worse: its whole return type is <c>StdOut</c>/<c>StdErr</c>, and a mode where both are empty by
/// construction is a shape that lies. Two launchers with two honest contracts beat one with a
/// footnote.</para>
/// </remarks>
public static class InteractiveProcess
{
    /// <summary>Started, attached to the terminal, and waited for.</summary>
    /// <returns>The exit code, or null when the timeout ran out and the child was killed.</returns>
    public static async Task<int?> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            // Every redirection off: the child inherits this terminal, so the person sees the URL and
            // can answer the prompts. That is the entire point of this class.
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{executable} did not start");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(limit.Token);

            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // A sign-in nobody finished. Killing the tree matters: these CLIs spawn helpers, and a
            // survivor still holds the slot's HOME open.
            Kill(process);

            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It exited between the check and the kill, or the platform refuses the tree walk.
            // Either way there is nothing further to do and throwing here would replace a timeout
            // message with a stack trace.
        }
    }
}
