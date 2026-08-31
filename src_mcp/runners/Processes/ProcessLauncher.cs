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

    /// <summary>
    /// Written to the child's stdin, then closed. Empty means "close immediately".
    /// </summary>
    /// <remarks>
    /// This is how every long or multi-line input travels. On Windows the vendor CLIs are npm
    /// <c>.cmd</c> shims, which cmd.exe parses — and cmd.exe truncates an argument at its first
    /// newline. The first real run (2026-08-31) passed a full review prompt in argv and the model
    /// answered "No implementation plan was provided": it had received the first line and nothing
    /// else, silently. stdin has no such parser between us and the process.
    /// </remarks>
    public string StdIn { get; init; } = string.Empty;

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
            // UTF-8 on every stream, explicitly. Without this .NET decodes a child's output with
            // the console's ANSI code page, and on Windows that turns every non-ASCII character
            // into '?' — silently. Found by a translation test whose Ukrainian came back as
            // "??? ?????", but it was never only translation: a finding written in any language
            // but English was being corrupted on its way in.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            // UTF-8 WITHOUT the byte-order mark, and the difference is not cosmetic.
            // `Encoding.UTF8` carries a preamble, and .NET flushes it into the child's stdin
            // inside Process.Start() — which means every prompt this product has ever sent began
            // with three stray bytes, and a child that exited quickly (git, mostly) took the whole
            // launch down with a broken pipe from INSIDE Start. Found in WSL, where the race is
            // lost reliably rather than once in a hundred runs: five tests, all in Process.Start.
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
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
        await WriteStdInAsync(process, request.StdIn);

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

    /// <summary>
    /// Feeds the child its input, and treats a closed pipe as the child's decision rather than
    /// our failure.
    /// </summary>
    /// <remarks>
    /// A process that exits before reading — a git command that needed no input, a CLI that
    /// refused at once, one that read the first line and stopped — closes its end of the pipe, and
    /// the write then throws. Left unguarded that exception came out of the launcher instead of a
    /// <see cref="ProcessResult"/>, so an early exit failed the whole ROUND rather than one
    /// reviewer with a named outcome. It surfaced as a rare cross-platform test flake for exactly
    /// as long as it took the race to be lost twice.
    /// <para>Nothing is hidden: the child's exit code, stdout and stderr are still returned, so a
    /// CLI that exited early because it was broken is still reported as broken.</para>
    /// </remarks>
    private static async Task WriteStdInAsync(Process process, string text)
    {
        try
        {
            if (text.Length > 0)
            {
                await process.StandardInput.WriteAsync(text);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child is not listening any more. Its output is what matters now.
        }
        catch (ObjectDisposedException)
        {
            // Same race, one step later: the stream went away while we were writing to it.
        }
    }
}
