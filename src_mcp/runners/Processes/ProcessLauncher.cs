using System.Diagnostics;
using System.Text;

namespace CoaiMcp.Runners.Processes;

public sealed record ProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>What a run produced. <see cref="TimedOut"/> true means the tree was killed.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>The seam every process in this repository goes through — one launcher, injectable
/// everywhere, so a test hands in a fake and the suite touches no vendor.</summary>
public interface IProcessLauncher
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default);
}

/// <summary>
/// The real launcher. On timeout the WHOLE process tree is killed — a reviewer CLI spawns its own
/// children, and an orphaned child that keeps running is a reviewer that never really failed.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        // Resolved here, at the one place a process is started, so every caller — reviewers, git,
        // the creds probe — gets the npm-shim fix without knowing it exists.
        var info = new ProcessStartInfo(ExecutableResolver.Resolve(request.Executable))
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        using var process = new Process { StartInfo = info };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) { stdout.AppendLine(line); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) { stderr.AppendLine(line); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(request.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the timeout and the kill; the race loser is fine either way.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
    }
}
