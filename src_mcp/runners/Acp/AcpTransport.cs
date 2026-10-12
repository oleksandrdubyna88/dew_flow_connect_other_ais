using System.Diagnostics;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The agent as a child process this client OWNS: newline-delimited lines in and out, stderr drained beside
/// them, and one teardown that leaves nothing of its tree alive.
/// </summary>
/// <remarks>
/// <para><b>Started by <see cref="ProcessLauncher"/>, not run by it — the difference is the shape of the
/// conversation.</b> <see cref="ProcessLauncher.RunAsync"/> writes stdin once, closes it, and waits for the end; an ACP
/// agent asks questions back (<c>session/request_permission</c>, <c>fs/read_text_file</c>) while the prompt is still being
/// answered, so stdin stays open for the replies and both streams are drained while the turn runs. The START is the
/// launcher's (<see cref="ProcessLauncher.Started"/>: the executable resolution, the UTF-8-without-BOM stdin, the
/// environment rule — the code gate of epic 4 found a copy here that had left the environment rule out), and so is the
/// end of a child —
/// <see cref="ProcessLauncher.EndTreeFirstAsync"/> (the whole tree killed while the agent still lives, then stdin
/// closed, then the exit waited for) and <see cref="ProcessLauncher.Close"/>.</para>
/// <para><b>Every exit path ends the same way</b> (<see cref="EndAsync"/>): the caller's bounded graceful step — the
/// <c>session/cancel</c> and a short wait for the agent to answer it — then the tree kill by this process's own
/// handle WHILE THE AGENT IS STILL ALIVE, then the wait. Not "close stdin and let it leave first": a process that has
/// exited can no longer name its children, so an agent that left on EOF took its grandchild out of the kill's reach
/// (finding 8 of the security review — green only by the order of two timers). The grandchild a CLI spawns is the
/// point of the tree kill: a reviewer that outlives its round is a reviewer that never really failed.</para>
/// <para><b>Lines are read against a cap</b> (<see cref="MaxLineChars"/>): a line reader with no bound buffers a
/// whole runaway before delivering anything, which is the launcher's own finding about ceilings; past the cap the
/// stream is judged broken and the turn ends, the agent with it.</para>
/// </remarks>
internal sealed class AcpTransport : IDisposable
{
    /// <summary>How long a graceful end is given before the kill. The probe gave Kimi 1.5 s (acp-probe.mjs).</summary>
    public static readonly TimeSpan GraceWait = TimeSpan.FromSeconds(1.5);

    /// <summary>How long a drain may outlive the kill, for the same reason the launcher's drain grace exists: a grandchild can hold a pipe.</summary>
    public static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest one line may be. A message is kilobytes — a served file rides inside one, under the boundary's
    /// 4 MiB — so a line past this is not a message, and reading it further is holding a runaway in memory.
    /// </summary>
    public const int MaxLineChars = 16 * 1024 * 1024;

    /// <summary>How much of the agent's stderr is kept — its diagnosis lives in the last lines.</summary>
    private const int StdErrChars = 64 * 1024;

    private const int ChunkChars = 8192;

    private readonly Process _process;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly BoundedText _stderr = new(StdErrChars);
    private readonly CancellationTokenSource _draining = new();
    private readonly Task _stderrDrain;
    private char[] _unread = new char[ChunkChars];
    private int _length;
    private int _scanned;

    private AcpTransport(Process process)
    {
        _process = process;
        _stderrDrain = DrainStdErrAsync();
        Exited = process.WaitForExitAsync(CancellationToken.None);
    }

    /// <summary>The child's pid — what a test checks is gone, and what a tracker would record.</summary>
    public int Pid => _process.Id;

    public bool HasExited => _process.HasExited;

    /// <summary>
    /// Completes when the agent PROCESS is gone — which is not when its output ends. A grandchild inherits
    /// the stdout pipe's write end, so an agent that exits mid-turn leaving one behind never delivers EOF;
    /// measured here by the fake agent's heartbeat grandchild, which held the pipe until the deadline.
    /// </summary>
    public Task Exited { get; }

    /// <summary>The exit code once <see cref="Exited"/> has completed; 0 before.</summary>
    public int ExitCode => _process.HasExited ? _process.ExitCode : 0;

    /// <summary>What the agent wrote on stderr, bounded.</summary>
    public string StdErr => _stderr.ToString();

    /// <summary>Starts the agent. Throws what <see cref="Process.Start()"/> throws when it cannot — the caller names that.</summary>
    /// <remarks>Started by <see cref="ProcessLauncher.Started"/>, the one place a child is built — its resolver, its
    /// UTF-8 streams without a byte-order mark (three stray bytes before the first JSON line would be a parse error on
    /// the agent's side) and its environment rule are the launcher's, not a copy.</remarks>
    public static AcpTransport Start(string executable, IReadOnlyList<string> arguments, string cwd) =>
        new(ProcessLauncher.Started(new ProcessRequest(executable, arguments, cwd)));

    /// <summary>
    /// The next line the agent wrote — or null at the end of its output, which is the one fact a reader must see.
    /// Throws <see cref="AcpProtocolException"/> once a line passes <see cref="MaxLineChars"/> without ending.
    /// </summary>
    /// <remarks>
    /// Its own reader rather than <c>StreamReader.ReadLineAsync</c>, which has no ceiling: it returns nothing until
    /// the newline arrives, by which time the whole line is in memory. Here the unread text is scanned only where it
    /// has not been scanned before, so a long line costs one pass, not one per chunk.
    /// </remarks>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var at = Array.IndexOf(_unread, '\n', _scanned, _length - _scanned);
            if (at > MaxLineChars || (at < 0 && _length > MaxLineChars))
            {
                // Over the cap whether its newline has arrived or not: the first run only caught the line that never ended.
                throw new AcpProtocolException($"the agent wrote a line longer than {MaxLineChars / (1024 * 1024)} Mi characters — not a message, not read further");
            }

            if (at >= 0)
            {
                return TakeLine(at, newline: true);
            }

            _scanned = _length;

            var read = await ReadChunkAsync(ct);
            if (read == 0)
            {
                // The pipe closed: the tail without a newline is the last line, and nothing at all is the end.
                return _length == 0 ? null : TakeLine(_length, newline: false);
            }

            _length += read;
        }
    }

    /// <summary>More of the stream into the unread buffer, grown when full; zero at the end or when the pipe went with the child.</summary>
    private async Task<int> ReadChunkAsync(CancellationToken ct)
    {
        if (_length == _unread.Length)
        {
            Array.Resize(ref _unread, _unread.Length * 2);
        }

        try
        {
            return await _process.StandardOutput.ReadAsync(_unread.AsMemory(_length), ct);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return 0;
        }
    }

    /// <summary>The line up to <paramref name="at"/> (a trailing CR dropped), the rest kept for the next call.</summary>
    private string TakeLine(int at, bool newline)
    {
        var end = at > 0 && _unread[at - 1] == '\r' ? at - 1 : at;
        var line = new string(_unread, 0, end);
        var consumed = newline ? at + 1 : at;
        var rest = _length - consumed;
        if (rest > 0)
        {
            Array.Copy(_unread, consumed, _unread, 0, rest);
        }

        _length = rest;
        _scanned = 0;

        return line;
    }

    /// <summary>Writes one line, serialised against the other writers — a reply to the agent can cross a request to it.</summary>
    /// <remarks>
    /// The await is bounded by the token even where the pipe ignores it — an anonymous pipe on Windows does, which is
    /// the launcher's own finding — so a hung agent that never reads its stdin cannot hold a 33 KB prompt write past
    /// the deadline: the caller returns on cancellation and the teardown's <see cref="ProcessLauncher.Close"/> fails
    /// the write still in flight. A writer that then finds the stream busy gets an <c>InvalidOperationException</c>,
    /// which reads as "not listening" like the others.
    /// </remarks>
    /// <returns>False when the agent is no longer listening: a closed pipe is its decision, not this side's failure.</returns>
    public async Task<bool> WriteLineAsync(string json, CancellationToken ct)
    {
        await _writing.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteAsync((json + "\n").AsMemory(), ct).WaitAsync(ct);
            await _process.StandardInput.FlushAsync(ct).WaitAsync(ct);

            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>
    /// Ends the agent whatever state it is in: the caller's bounded graceful step, then the tree kill — while the
    /// agent still lives, so its children are still its — and the wait for it.
    /// </summary>
    /// <param name="graceful">
    /// What to do before the kill — the <c>session/cancel</c> notification and a short wait for its answer when a
    /// turn is in flight. Bounded by the caller; whatever it does, the kill follows. Stdin is NOT closed first: EOF is
    /// what a well-behaved agent exits on, and an agent that has exited can no longer name its children.
    /// </param>
    public async Task EndAsync(Func<Task> graceful)
    {
        await SettledAsync(graceful());

        // The tree FIRST, by this process's own handle — never by a name — then stdin, then the wait. Not the
        // launcher's own order (stdin first): closing stdin is the EOF a well-behaved agent exits on, and an agent that
        // has exited can no longer name its children — on Linux they are reparented to init the moment it goes, and the
        // kill that follows finds an empty tree (finding G of the risk consultations, 2026-10-12).
        await ProcessLauncher.EndTreeFirstAsync(_process);

        _draining.CancelAfter(DrainGrace);
        await SettledAsync(_stderrDrain);
    }

    public void Dispose()
    {
        _draining.Dispose();
        _writing.Dispose();
        _process.Dispose();
    }

    private async Task DrainStdErrAsync()
    {
        var buffer = new char[8192];
        try
        {
            while (await _process.StandardError.ReadAsync(buffer, _draining.Token) is var read && read > 0)
            {
                _stderr.Append(buffer, read);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
        {
            _stderr.CutShort();
        }
    }

    /// <summary>Awaits a step of the teardown, treating the child's own departure as an ordinary end — the launcher's rule.</summary>
    private static async Task SettledAsync(Task step)
    {
        try
        {
            await step;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException or TimeoutException)
        {
            // Already gone, already closed, or not answering inside the grace: each is a state the kill below handles.
        }
    }
}
