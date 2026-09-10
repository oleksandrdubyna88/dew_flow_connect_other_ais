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
    /// <value>Must be positive: a ceiling of zero would discard every child's output.</value>
    public int MaxOutputChars
    {
        get;
        // Refused rather than clamped, and it is the one number here a caller could plausibly get
        // wrong in the direction that destroys evidence: 0 and -1 are both common spellings of
        // "unlimited" elsewhere, and either would silently return an empty answer for every launch.
        // (gemini, code round.)
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "a stream ceiling must be positive; zero would keep nothing at all");
    } = 8 * 1024 * 1024;

    /// <summary>
    /// How long the streams are still read AFTER the child is gone, before what is left is dropped.
    /// </summary>
    /// <remarks>
    /// A pipe stays open while any process holds its write end, and a grandchild inherits both — so a
    /// handle that outlives the tree kill would hang the launcher rather than the process it belongs
    /// to. In the ordinary case the streams close with the child and this costs nothing; it is a
    /// bound on the pathological case, and the stream says so when it is what dropped the rest.
    /// </remarks>
    public TimeSpan DrainGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A name to record this child under while it runs, or empty to track nothing.
    /// </summary>
    /// <remarks>
    /// Opt-in per request rather than for everything, because most processes here are git commands
    /// that finish in milliseconds and would only add write traffic. The ones worth tracking are
    /// the reviewers: minutes long, expensive, and the ones that get orphaned.
    /// </remarks>
    public string TrackAs { get; init; } = string.Empty;

    /// <summary>
    /// Whether the child starts with this process's whole environment, or with only the names in
    /// <see cref="ProcessEnvironment.Passthrough"/>. <see cref="Environment"/> is applied on top
    /// either way, last, exactly as it always was.
    /// </summary>
    /// <remarks>
    /// <para>The default is the whole environment, and that is not a compromise: the local
    /// <c>coai-mcp</c> runs the developer's own CLIs in the developer's own environment, which is
    /// where their sign-ins, proxies and PATH live. Nothing that already calls this launcher
    /// changes.</para>
    /// <para>False is for a launch that was handed everything it needs on the request — the Team
    /// server's reviewers, whose prompt already carries the diff and whose working directory is an
    /// empty temporary directory. There the inherited environment is the server's own configuration
    /// (<c>/etc/coai-server.env</c>, whatever it holds this month), in the environment of a process
    /// running an employee's arbitrary prompt whose answer is returned to that employee verbatim.
    /// ADDING the request's variables to the parent's was finding 1 of the product audit of
    /// 2026-09-09; starting from the allowlist instead is the fix, and it is an allowlist rather
    /// than a list of names to strip because the thing being kept out is not a known name.</para>
    /// </remarks>
    public bool InheritsEnvironment { get; init; } = true;
}

/// <summary>What a run produced. <see cref="TimedOut"/> true means the tree was killed.</summary>
/// <param name="Cancelled">
/// The tree was killed because the CALLER's token fired, not because this launch ran out of budget.
/// </param>
/// <remarks>
/// <para>Both extra fields are additive and default to false, so every existing construction and
/// every existing reader of <see cref="TimedOut"/> keeps its meaning — "the tree was killed" — and
/// the new ones answer questions that used to need the caller's own bookkeeping.</para>
/// <para><b>Why <see cref="Cancelled"/> is separate rather than a different value of TimedOut.</b>
/// "We ran out of time" and "somebody stopped this" look identical from inside the wait and lead to
/// opposite conclusions outside it: one is a vendor that is too slow for its budget and belongs in
/// the retry ladder, the other is a job the person withdrew and must never be retried or reported as
/// a failure of the model. Two vendors asked for the distinction independently on the code round.</para>
/// <param name="Truncated">
/// At least one stream was cut — by the ceiling or by the drain grace — so what is here is a head
/// rather than the whole. A caller validating machine-readable output can tell that from a vendor
/// that answered badly, without parsing the sentence in the text.
/// </param>
/// </remarks>
public sealed record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Cancelled = false,
    bool Truncated = false);

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
    /// <summary>
    /// How much of a stream is taken per read.
    /// </summary>
    /// <remarks>
    /// Two of these per launch, against a launch that starts an operating-system process — a cost
    /// three orders of magnitude larger — so it is a plain array rather than a pooled one. A rented
    /// buffer would also have to be returned around a read the drain grace is entitled to ABANDON,
    /// which is a use-after-return rather than a saving.
    /// </remarks>
    private const int DrainBufferChars = 8192;

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

        ApplyEnvironment(info.Environment, request);

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
    /// The child's environment: the parent's whole one, or the allowlist alone — and then the
    /// request's own names on top, last, either way.
    /// </summary>
    /// <remarks>
    /// The order is the contract. A caller that hands over <c>HOME</c> or a sign-in token on the
    /// request gets it in the child whichever mode it chose, and a caller that hands over nothing
    /// gets exactly the mode it named. <see cref="ProcessStartInfo.Environment"/> arrives already
    /// filled from this process, which is what a confined launch has to undo before it adds
    /// anything — clearing it AFTER the request's names were applied would drop those too.
    /// </remarks>
    private static void ApplyEnvironment(IDictionary<string, string?> environment, ProcessRequest request)
    {
        if (!request.InheritsEnvironment)
        {
            Confine(environment);
        }

        foreach (var (name, value) in request.Environment)
        {
            environment[name] = value;
        }
    }

    /// <summary>Keeps only the names in <see cref="ProcessEnvironment.Passthrough"/>.</summary>
    /// <remarks>
    /// Read into a copy before the clear, because the dictionary being pruned is the one being read.
    /// The comparison is the dictionary's own — .NET builds it case-insensitive on Windows and
    /// case-sensitive elsewhere, the same rule <see cref="ProcessEnvironment.NameComparer"/> follows,
    /// so a Windows <c>Path</c> matches the list's <c>PATH</c> and a Linux <c>path</c> does not.
    /// </remarks>
    private static void Confine(IDictionary<string, string?> environment)
    {
        var kept = environment.Where(entry => ProcessEnvironment.Passthrough.Contains(entry.Key)).ToArray();
        environment.Clear();
        foreach (var (name, value) in kept)
        {
            environment[name] = value;
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

        // A token of its own, because the drain must OUTLIVE the deadline: the output of a child
        // that was just killed is the evidence of why it was killed, and cancelling the read with
        // the same token would throw that away at the one moment it matters.
        using var draining = new CancellationTokenSource();

        // Started before the write, because a child may answer before it has finished reading — and
        // one that fills ITS pipe while nobody drains ours is a deadlock made of two blocked writes.
        var reading = Task.WhenAll(
            DrainAsync(process.StandardOutput, stdout, draining.Token),
            DrainAsync(process.StandardError, stderr, draining.Token));
        var writing = WriteStdInAsync(process, request.StdIn, deadline.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            // BEFORE the kill, and this is the finding that made it explicit: a write blocked on a
            // full pipe does not honour its token on every platform, and a descendant that survived
            // the tree kill still holding the read end would leave that write blocked for ever —
            // with the launcher awaiting it, and on the Team server an account locked behind it.
            // Closing our own end fails the write at once, whoever else is holding theirs.
            // (codex, code round.)
            Close(process.StandardInput);
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

        // The child is gone, so the streams should end now. The grace is what bounds the case where
        // they do not — and it CANCELS the read rather than walking away from it, so no suspended
        // task is left to fault against a disposed process or to append to text somebody is already
        // reading. (codex and gemini, code round, from three directions.)
        draining.CancelAfter(request.DrainGrace);
        await Task.WhenAll(SettledAsync(writing), SettledAsync(reading));

        return new ProcessResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            timedOut,
            // The token's STATE, never the exception's type — the two arrive as the same
            // OperationCanceledException and mean opposite things. A budget that ran out is a vendor
            // too slow for it; a caller that cancelled is a job somebody withdrew, and retrying that
            // is spending money on an answer nobody is waiting for.
            Cancelled: timedOut && ct.IsCancellationRequested,
            Truncated: stdout.Truncated || stderr.Truncated);
    }

    /// <summary>
    /// Closes our end of the child's stdin, even with a write still blocked inside it.
    /// </summary>
    /// <remarks>
    /// <para><b>The HANDLE, not the writer.</b> <c>StreamWriter.Close()</c> refuses outright while an
    /// async write is in flight — <c>InvalidOperationException: The stream is currently in use by a
    /// previous operation</c> — which is exactly and only the state this is called in. Measured on
    /// the first run of the test that asked for it. Disposing the underlying pipe stream closes the
    /// handle, and the blocked write then fails at once with the <c>IOException</c> that has always
    /// meant "the child is not listening any more".</para>
    /// <para>Every one of these is an ordinary way for it to already be gone, so none of them is an
    /// error here: the point is that after this call nothing is holding our end open.</para>
    /// </remarks>
    private static void Close(StreamWriter stream)
    {
        try
        {
            stream.BaseStream.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Reads one stream to its end, keeping at most what <paramref name="into"/> allows.</summary>
    /// <remarks>
    /// Character by chunk rather than line by line. The line-based reader this replaced could not
    /// enforce a ceiling at all — it delivers nothing until a newline arrives, so a child writing two
    /// hundred megabytes without one had already been buffered by the framework before any callback
    /// ran. It also APPENDED a newline to every line, so a vendor's answer came back with a line
    /// ending it had not written; the raw reader hands over exactly what the child produced.
    /// </remarks>
    /// <param name="ct">
    /// Cancelled at the drain grace, not at the request's deadline — see the caller for why the two
    /// are different clocks. Its cancellation is CAUGHT here, so the launch still returns everything
    /// that did arrive and the text says the rest was dropped.
    /// </param>
    private static async Task DrainAsync(StreamReader stream, BoundedText into, CancellationToken ct)
    {
        var buffer = new char[DrainBufferChars];
        try
        {
            while (await stream.ReadAsync(buffer, ct) is var read && read > 0)
            {
                into.Append(buffer, read);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Something outlived the tree kill and is still holding this pipe. What arrived is kept
            // and the stream says it is a head rather than the whole — an abandoned reader would
            // have said nothing at all, and would still have been suspended when the process it
            // reads from was disposed.
            into.CutShort();
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
            Close(process.StandardInput);
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
    private long _dropped;
    private bool _cutShort;

    /// <summary>True when what this holds is a HEAD rather than the whole stream.</summary>
    public bool Truncated => _dropped > 0 || _cutShort;

    /// <summary>Take up to the ceiling, and count the rest rather than keeping it.</summary>
    /// <remarks>
    /// Reading continues past the ceiling in the caller — a pipe nobody drains blocks the child that
    /// is writing to it — so this goes on being called and goes on counting. The count is what makes
    /// the sentence below worth reading: "cut at 8 Mi" says nothing about whether one character was
    /// lost or two hundred megabytes.
    /// </remarks>
    public void Append(char[] buffer, int count)
    {
        var room = maxChars - _text.Length;
        if (count <= room)
        {
            _text.Append(buffer, 0, count);

            return;
        }

        _text.Append(buffer, 0, Math.Max(room, 0));
        _dropped += count - Math.Max(room, 0);
    }

    /// <summary>The stream was still open when the drain grace ran out.</summary>
    /// <remarks>
    /// A different fact from the ceiling and it deserves a different sentence: the ceiling is this
    /// launcher's decision about a runaway, while this is something that outlived the tree kill and
    /// is still holding the pipe. Reading them as one would send somebody to raise a limit that was
    /// never reached.
    /// </remarks>
    public void CutShort() => _cutShort = true;

    /// <summary>
    /// The text, with one sentence at the end when it is not all of it.
    /// </summary>
    /// <remarks>
    /// Composed HERE rather than appended when the ceiling was crossed, because the number worth
    /// printing — how much was dropped — is not known until the stream ends. It opens with a newline
    /// so it cannot run on from output that ended without one.
    /// </remarks>
    public override string ToString() => Truncated
        ? _text.ToString() + Sentence()
        : _text.ToString();

    private string Sentence() => _dropped > 0
        ? $"\n[coai: output truncated at {maxChars} characters; {_dropped} more were dropped]\n"
        : "\n[coai: output truncated — the stream was still open when the drain grace ran out]\n";
}
