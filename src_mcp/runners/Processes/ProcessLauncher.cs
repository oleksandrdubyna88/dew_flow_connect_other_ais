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

    /// <summary>
    /// How much of each stream is kept. Past it the rest is dropped and the text says so.
    /// </summary>
    /// <remarks>
    /// <para>CHARACTERS rather than bytes, and the difference is not pedantry: what is bounded is the
    /// memory of a .NET string, which is two bytes per character. The default is therefore
    /// 8 Mi characters — <b>16 MiB</b> per stream, which is the number the plan named.</para>
    /// <para>It has to be enforced while the stream is READ, not per line. A child can write two
    /// hundred megabytes without a single newline; a line-based reader delivers none of it until the
    /// stream closes, so a ceiling checked in a line callback fires only after the allocation it
    /// exists to prevent has already happened. (codex, plan round.)</para>
    /// <para>The child is not killed for reaching it — the rest is read and discarded, so a runaway
    /// cannot block on a pipe nobody is draining either. Its exit code and the head of what it said
    /// are still returned.</para>
    /// </remarks>
    public int MaxOutputChars { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// A name to record this child under while it runs, or empty to track nothing.
    /// </summary>
    /// <remarks>
    /// Opt-in per request rather than for everything, because most processes here are git commands
    /// that finish in milliseconds and would only add write traffic. The ones worth tracking are
    /// the reviewers: minutes long, expensive, and the ones that get orphaned.
    /// </remarks>
    public string TrackAs { get; init; } = string.Empty;
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
/// <param name="tracker">
/// Where a long-running child is written down while it runs, so a later server can collect it if
/// this one dies. Defaults to recording nothing, which is what every test and every git command
/// wants.
/// </param>
public sealed class ProcessLauncher(IProcessTracker? tracker = null) : IProcessLauncher
{
    private readonly IProcessTracker _tracker = tracker ?? NoProcessTracking.Instance;

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
        var stdout = new BoundedText(request.MaxOutputChars);
        var stderr = new BoundedText(request.MaxOutputChars);

        process.Start();

        // Recorded as soon as it exists and forgotten however this method leaves — returned,
        // timed out, cancelled or thrown. The record is what a LATER server reads: the kill below
        // is performed by this process, so it cannot happen if this process is the one that dies,
        // which is exactly how a reviewer ends up outliving its round by ten hours.
        var tracked = request.TrackAs.Length > 0;
        if (tracked)
        {
            _tracker.Record(process.Id, process.StartTime.ToUniversalTime(), request.TrackAs);
        }

        try
        {
            return await RunToCompletionAsync(process, request, stdout, stderr, ct);
        }
        finally
        {
            if (tracked)
            {
                _tracker.Forget(process.Id);
            }
        }
    }

    /// <summary>
    /// One deadline over the WHOLE operation — the write, the wait and the reading alike.
    /// </summary>
    /// <remarks>
    /// <para><b>The write used to happen before the clock started, and that was the defect.</b> A
    /// pipe holds about 4 KiB on Windows and 64 KiB on Linux; a review prompt is a shaped diff of up
    /// to 192 KiB, so every reviewer launch writes past the buffer and blocks until the child reads.
    /// A child that never reads — a sign-in prompt, a TTY check, a CLI that died before its first
    /// read — held the launch for as long as it lived, with no timeout started and the caller's
    /// token unconsulted. Measured 2026-09-09: a ten-second child, a 300 ms budget, and a call that
    /// returned after ten seconds saying <c>TimedOut=false</c>, because by the time the clock was
    /// created the process had already gone.</para>
    /// <para>So everything below runs under ONE linked, timed token created before a byte is
    /// written, and the three concurrent halves — writing, reading, waiting — are all ended by it.
    /// The kill is what actually frees a blocked write: it closes the child's end of the pipe, so a
    /// write that ignored its token (an anonymous pipe on Windows does) still fails at once, with
    /// the <c>IOException</c> this class has always treated as the child's decision.</para>
    /// </remarks>
    private static async Task<ProcessResult> RunToCompletionAsync(
        Process process,
        ProcessRequest request,
        BoundedText stdout,
        BoundedText stderr,
        CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout);

        // Started before the write, because a child may answer before it has finished reading — and
        // one that fills ITS pipe while nobody drains ours is a deadlock made of two blocked writes.
        var reading = Task.WhenAll(
            DrainAsync(process.StandardOutput, stdout),
            DrainAsync(process.StandardError, stderr));
        var writing = WriteStdInAsync(process, request.StdIn, deadline.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the deadline and the kill; the race loser is fine either way.
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }

        await SettledAsync(writing);
        await SettledAsync(DrainedAsync(reading));

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), timedOut);
    }

    /// <summary>Reads one stream to its end, keeping at most what <paramref name="into"/> allows.</summary>
    /// <remarks>
    /// Character by chunk rather than line by line. The line-based reader this replaced could not
    /// enforce a ceiling at all — it delivers nothing until a newline arrives, so a child writing two
    /// hundred megabytes without one had already been buffered by the framework before any callback
    /// ran. It also APPENDED a newline to every line, so a vendor's answer came back with a line
    /// ending it had not written; the raw reader hands over exactly what the child produced.
    /// </remarks>
    private static async Task DrainAsync(StreamReader stream, BoundedText into)
    {
        var buffer = new char[8192];
        while (await stream.ReadAsync(buffer, CancellationToken.None) is var read && read > 0)
        {
            into.Append(buffer, read);
        }
    }

    /// <summary>
    /// Waits for both streams to end, but not for ever.
    /// </summary>
    /// <remarks>
    /// A pipe stays open while ANY process holds its write end, and a grandchild inherits both. The
    /// tree kill takes them, so this normally returns the instant the child is gone — but a handle
    /// that outlives the kill would otherwise hang this method rather than the process it belongs
    /// to, and a launcher that never returns is worse than a truncated stream.
    /// </remarks>
    private static async Task DrainedAsync(Task reading)
    {
        if (await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(5))) == reading)
        {
            await reading;
        }
    }

    /// <summary>
    /// Awaits a half of the operation, treating the child's own departure as an ordinary end.
    /// </summary>
    /// <remarks>
    /// These three are what a dead child leaves behind: a broken pipe, a stream disposed under a
    /// write in flight, and the deadline that ended the write itself. None of them is a failure of
    /// the LAUNCH — the child's exit code and its own words are the answer — and letting one travel
    /// out of here failed a whole round rather than one reviewer with a named outcome, which is a
    /// flake this class has already paid for once.
    /// </remarks>
    private static async Task SettledAsync(Task half)
    {
        try
        {
            await half;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
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
    /// <para>The CLOSE is what a child waiting for EOF is waiting for, so it happens on every path
    /// including a write the deadline ended — otherwise a child that read everything would sit
    /// waiting for an end-of-file that never came.</para>
    /// </remarks>
    private static async Task WriteStdInAsync(Process process, string text, CancellationToken ct)
    {
        try
        {
            if (text.Length > 0)
            {
                await process.StandardInput.WriteAsync(text.AsMemory(), ct);
            }
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // The stream is already gone with the child. There is nothing left to close.
            }
        }
    }
}

/// <summary>
/// One stream's text, up to a ceiling, with the truncation said out loud.
/// </summary>
/// <remarks>
/// Not a <see cref="StringBuilder"/> with a check beside it, because the check has to hold at every
/// call site and there are two of them per launch. A bound that lives in the type cannot be
/// forgotten by the next reader of this file.
/// </remarks>
internal sealed class BoundedText(int maxChars)
{
    private readonly StringBuilder _text = new();
    private bool _truncated;

    public void Append(char[] buffer, int count)
    {
        if (_truncated)
        {
            // Still READ — the caller keeps draining so the child never blocks on a pipe nobody
            // empties — and no longer kept.
            return;
        }

        var room = maxChars - _text.Length;
        if (count <= room)
        {
            _text.Append(buffer, 0, count);

            return;
        }

        _text.Append(buffer, 0, Math.Max(room, 0));
        // Named, once, in the stream itself. A silently cut answer is one a parser fails on for a
        // reason nobody can find; this one fails saying why.
        _text.Append($"\n[coai: output truncated after {maxChars} characters]\n");
        _truncated = true;
    }

    public override string ToString() => _text.ToString();
}
