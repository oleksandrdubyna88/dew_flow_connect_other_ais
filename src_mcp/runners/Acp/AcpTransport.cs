using System.Diagnostics;
using System.Text;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The agent as a child process this client OWNS: newline-delimited lines in and out, stderr drained beside
/// them, and one teardown that leaves nothing of its tree alive.
/// </summary>
/// <remarks>
/// <para><b>Not <see cref="ProcessLauncher"/>, and the difference is the shape of the conversation.</b> The
/// launcher writes stdin once, closes it, and waits for the end; an ACP agent asks questions back
/// (<c>session/request_permission</c>, <c>fs/read_text_file</c>) while the prompt is still being answered, so
/// stdin stays open for the replies and both streams are drained while the turn runs. What IS the launcher's is
/// reused rather than copied: the UTF-8-without-BOM stdin, the executable resolution, and the end of a child —
/// <see cref="ProcessLauncher.EndItAsync"/> (stdin closed, the whole tree killed, the exit waited for) and
/// <see cref="ProcessLauncher.Close"/>.</para>
/// <para><b>Every exit path ends the same way</b> (<see cref="EndAsync"/>): a bounded graceful step — the caller's
/// <c>session/cancel</c>, then our end of stdin closed, which is the EOF a well-behaved agent exits on — a short
/// wait, then the tree kill by this process's own handle, then the wait. The grandchild a CLI spawns is the point
/// of the tree kill: a reviewer that outlives its round is a reviewer that never really failed.</para>
/// </remarks>
internal sealed class AcpTransport : IDisposable
{
    /// <summary>How long a graceful end is given before the kill. The probe gave Kimi 1.5 s (acp-probe.mjs).</summary>
    public static readonly TimeSpan GraceWait = TimeSpan.FromSeconds(1.5);

    /// <summary>How long a drain may outlive the kill, for the same reason the launcher's drain grace exists: a grandchild can hold a pipe.</summary>
    public static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    /// <summary>How much of the agent's stderr is kept — its diagnosis lives in the last lines.</summary>
    private const int StdErrChars = 64 * 1024;

    private readonly Process _process;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly BoundedText _stderr = new(StdErrChars);
    private readonly CancellationTokenSource _draining = new();
    private readonly Task _stderrDrain;

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
    public static AcpTransport Start(string executable, IReadOnlyList<string> arguments, string cwd)
    {
        var info = new ProcessStartInfo(ExecutableResolver.Resolve(executable))
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // Without the BOM: three stray bytes in front of the first JSON line would be a parse error on the agent's side.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        process.Start();

        return new AcpTransport(process);
    }

    /// <summary>The next line the agent wrote — or null at the end of its output, which is the one fact a reader must see.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        try
        {
            return await _process.StandardOutput.ReadLineAsync(ct);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The pipe went with the child: the same thing as its end of output.
            return null;
        }
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
    /// Ends the agent whatever state it is in: the graceful step, a bounded wait, then the tree kill and the wait for it.
    /// </summary>
    /// <param name="graceful">What to say before closing stdin — the <c>session/cancel</c> notification when a turn is in flight.</param>
    public async Task EndAsync(Func<Task> graceful)
    {
        await SettledAsync(graceful());
        ProcessLauncher.Close(_process.StandardInput);
        using var grace = new CancellationTokenSource(GraceWait);
        await SettledAsync(_process.WaitForExitAsync(grace.Token));

        // The tree, by this process's own handle — never by a name — then the wait the launcher already does.
        await ProcessLauncher.EndItAsync(_process);

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
