using System.Collections.Concurrent;
using System.Threading.Channels;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// One scripted launch of a <see cref="ClockedLauncher"/>: how long the "process" would run, and what it says if it is allowed to finish.
/// </summary>
/// <param name="Turn">How long the launch runs on the clock. Past the request's timeout the stand-in answers <c>TimedOut</c> instead.</param>
/// <param name="StdOut">What it writes to stdout (the usage a codex adapter reads there, or a gemini answer).</param>
/// <param name="OutFile">What it writes to the file named by the request's <c>-o</c> argument (a codex answer); nothing when empty.</param>
/// <param name="Exit">Its exit code.</param>
internal sealed record ClockedStep(TimeSpan Turn, string StdOut = "", string OutFile = "", int Exit = 0);

/// <summary>
/// An in-process launcher that costs no time and spends none: it never advances the clock (E4 plan §5.4 point 6 — an advance from the
/// launcher's thread would run the clock's callbacks on it). Each launch records the request's timeout, signals ITS OWN handshake
/// (a <see cref="ClockHandshake"/> is single-shot) and waits; the test's driver advances by what the launch would have been allowed to
/// run, <c>min(turn, timeout)</c>, and releases it. Released, it answers the scripted text when the turn fits its timeout and
/// <c>TimedOut</c> when it does not. Past <c>ceiling</c> launches it answers a failure naming the count, so a loop nothing ends
/// finishes red instead of hanging.
/// </summary>
/// <remarks>
/// <para>Two shapes. <c>new ClockedLauncher(turn, stdout)</c> answers every launch alike — the cap tests' shape. <c>new ClockedLauncher(script)</c>
/// answers the n-th launch with the n-th <see cref="ClockedStep"/> (E5 S8, C30/C35): a turn that answers, then a turn that "runs" ten seconds
/// against a two-second request; a junk launch and a repair that times out. A script that runs out answers a failure naming the launch, as the
/// ceiling does.</para>
/// <para><b>What it proves.</b> Budget ALLOCATION — what the executor and the loop hand each launch — and not the production launcher's timer
/// ENFORCEMENT: the real <c>ProcessLauncher</c> deadline tests (<c>ProcessLauncherTests</c>, <c>AChildThatNeverReadsItsStdinIsStillKilledOnTime</c>)
/// stay as that proof.</para>
/// </remarks>
internal sealed class ClockedLauncher : IProcessLauncher
{
    private sealed record Arrival(ClockHandshake Handshake, TimeSpan Timeout, TimeSpan Turn);

    private readonly IReadOnlyList<ClockedStep> _script;
    private readonly bool _repeatsTheLast;
    private readonly int _ceiling;
    private readonly Channel<Arrival> _arrivals = Channel.CreateUnbounded<Arrival>();
    private readonly ConcurrentQueue<ClockHandshake> _handshakes = [];
    private readonly ConcurrentQueue<TimeSpan> _timeouts = [];
    private int _count;

    /// <summary>Every launch alike: <paramref name="turn"/> long, writing <paramref name="stdout"/>.</summary>
    public ClockedLauncher(TimeSpan turn, string stdout, int ceiling = 20)
    {
        _script = [new ClockedStep(turn, stdout)];
        _repeatsTheLast = true;
        _ceiling = ceiling;
    }

    /// <summary>The n-th launch answers with the n-th entry of <paramref name="script"/>, in launch order.</summary>
    public ClockedLauncher(IReadOnlyList<ClockedStep> script, int ceiling = 20)
    {
        _script = script;
        _repeatsTheLast = false;
        _ceiling = ceiling;
    }

    public int Count => Volatile.Read(ref _count);

    /// <summary>The timeout every launch was given, in launch order.</summary>
    public IReadOnlyList<TimeSpan> Timeouts => [.. _timeouts];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var launches = Interlocked.Increment(ref _count);
        if (launches > _ceiling)
        {
            return new ProcessResult(1, string.Empty, $"the stand-in was launched {launches} times, past its ceiling of {_ceiling}: nothing ended the conversation", TimedOut: false);
        }

        if (!TryStep(launches, out var step))
        {
            return new ProcessResult(1, string.Empty, $"the stand-in was launched {launches} times but its script holds {_script.Count}", TimedOut: false);
        }

        var handshake = new ClockHandshake();
        _handshakes.Enqueue(handshake);
        _timeouts.Enqueue(request.Timeout);
        _arrivals.Writer.TryWrite(new Arrival(handshake, request.Timeout, step.Turn));
        handshake.Signal();
        await handshake.WaitForReleaseAsync(ct);

        return step.Turn <= request.Timeout
            ? await Answered(request, step)
            : new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);
    }

    /// <summary>The scripted step of the n-th launch (1-based); false when the script has run out.</summary>
    private bool TryStep(int launch, out ClockedStep step)
    {
        var at = _repeatsTheLast ? Math.Min(launch, _script.Count) : launch;
        var found = at >= 1 && at <= _script.Count;
        step = found ? _script[at - 1] : new ClockedStep(TimeSpan.Zero);

        return found;
    }

    /// <summary>The launch finished in time: its answer file written where the request's <c>-o</c> names it, and what it said.</summary>
    private static async Task<ProcessResult> Answered(ProcessRequest request, ClockedStep step)
    {
        if (step.OutFile.Length > 0 && OutputFileOf(request) is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, step.OutFile);
        }

        return new ProcessResult(step.Exit, step.StdOut, string.Empty, TimedOut: false);
    }

    /// <summary>The path after <c>-o</c> in a codex-shaped argv, or empty when there is none.</summary>
    private static string OutputFileOf(ProcessRequest request)
    {
        var at = request.Arguments.ToList().IndexOf("-o");

        return at >= 0 && at + 1 < request.Arguments.Count ? request.Arguments[at + 1] : string.Empty;
    }

    /// <summary>The test's side: for each launch, wait until it is there, move the clock by what it was allowed to run, release it — until the run is over.</summary>
    public async Task DriveAsync(Task run, ManualClock clock, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            while (!run.IsCompleted)
            {
                var next = _arrivals.Reader.ReadAsync(stop.Token).AsTask();
                if (await Task.WhenAny(run, next) == run)
                {
                    return;
                }

                var arrival = await next;
                await arrival.Handshake.Reached;
                clock.Advance(TimeSpan.FromTicks(Math.Min(arrival.Turn.Ticks, arrival.Timeout.Ticks)));
                arrival.Handshake.Release();
            }
        }
        finally
        {
            await stop.CancelAsync();
        }
    }

    /// <summary>Lets every parked launch go — in the test's <c>finally</c>, so a failing test never leaves a product thread waiting.</summary>
    public void ReleaseAll()
    {
        foreach (var handshake in _handshakes)
        {
            handshake.Release();
        }
    }
}
