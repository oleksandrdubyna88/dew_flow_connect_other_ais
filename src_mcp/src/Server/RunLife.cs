using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server;

/// <summary>
/// One host's run marker, for as long as the host lives: written, the dead swept once, beaten, then
/// stopped and cleared by its owner.
/// </summary>
/// <remarks>
/// <para><b>One dedicated loop</b>, because the data directory can be a network share and a share can
/// wedge. Every write here is synchronous; on its own thread, a wedged share stalls the BEAT and
/// nothing else — never startup, never a tool call. The lesson <see cref="NoticeWriter"/> already
/// encodes, applied to the other thing this server writes to that directory.</para>
///
/// <para><b>The order is the design.</b> This run's marker is written BEFORE the sweep, because a
/// sweeper with no fresh marker of its own looks dead to a sibling sweeping at the same moment
/// (<see cref="RunMarkers.Sweep"/> says why). And the beat survives a sweep that fails: a sweep that
/// threw and took the loop with it would leave a LIVE run silent, and thirty minutes later a peer would
/// record it as a death it is not. So the sweep has a catch-all of its own — the one
/// <c>reliability.md</c> sanctions at every detached edge.</para>
/// </remarks>
internal sealed class RunLife
{
    /// <summary>How long the end of a run waits for the beat to stop before it goes on without it.</summary>
    internal static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(2);

    private readonly RunMarkers _markers;

    private readonly Serilog.ILogger _log;

    private readonly CancellationTokenSource _stop = new();

    private readonly Task _loop;

    private RunLife(RunMarkers markers, Func<ServerNotice, bool> append, Serilog.ILogger log)
    {
        _markers = markers;
        _log = log;
        _loop = Task.Factory.StartNew(
            () => LivingAsync(append), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .Unwrap();
    }

    /// <summary>Begin: write this run's marker, record the dead, then beat until stopped.</summary>
    internal static RunLife Start(RunMarkers markers, Func<ServerNotice, bool> append, Serilog.ILogger log) =>
        new(markers, append, log);

    /// <summary>
    /// Stop the beat — BEFORE the marker is cleared, or a beat landing after the clear re-creates it.
    /// </summary>
    /// <remarks>
    /// Bounded. A beat stuck on a wedged share cannot be recalled, and it may land after the clear:
    /// that marker would then outlive a run that ended cleanly and be recorded, half an hour later,
    /// as a death. Said here rather than discovered then. (gemini, on the plan round, named the race.)
    /// </remarks>
    /// <returns>Whether the loop is known to have stopped.</returns>
    internal async Task<bool> StopAsync()
    {
        await _stop.CancelAsync();
        var stopped = await Task.WhenAny(_loop, Task.Delay(StopBudget)) == _loop;
        if (!stopped)
        {
            _log.Warning("run marker: a heartbeat is stuck on the share, and this run's marker may outlive it");
        }

        return stopped;
    }

    /// <summary>Clear this run's marker. Only after <see cref="StopAsync"/>.</summary>
    internal void Clear() => _markers.Clear();

    private async Task LivingAsync(Func<ServerNotice, bool> append)
    {
        Beaten();
        Swept(append);

        using var beat = new PeriodicTimer(RunMarkers.Beat);
        try
        {
            while (await beat.WaitForNextTickAsync(_stop.Token))
            {
                Beaten();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped, which is how a run's life ends.
        }
    }

    /// <summary>One beat, which no failure may turn into the last one.</summary>
    /// <remarks>
    /// <see cref="RunMarkers.Write"/> names the disk's own two failures; anything else used to escape
    /// the loop, fault a task nobody observes, and end the beat in silence — a live run whose marker
    /// then goes stale and is recorded, half an hour later, as a death it is not. Found re-reading this
    /// story before its code round, RED first.
    /// </remarks>
    private void Beaten()
    {
        try
        {
            _markers.Write();
        }
        catch (Exception failure)
        {
            // The detached edge's catch-all, as the sweep's: the next beat tries again.
            _log.Warning("run marker: a heartbeat failed, and the next one will try again: {Why}",
                Redaction.SafeText(failure.Message, Redaction.TitleLimit));
        }
    }

    private void Swept(Func<ServerNotice, bool> append)
    {
        try
        {
            _markers.Sweep(append);
        }
        catch (Exception failure)
        {
            // The detached edge's catch-all: the beat must go on whatever the sweep met.
            _log.Warning("run marker: the sweep for runs that never finished failed: {Why}",
                Redaction.SafeText(failure.Message, Redaction.TitleLimit));
        }
    }
}
