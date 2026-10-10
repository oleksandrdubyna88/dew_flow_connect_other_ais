namespace CoaiServer;

/// <summary>
/// Keeps <c>sessions/</c> to the sessions that are alive: a sweep at boot, and one every hour after.
/// </summary>
/// <remarks>
/// <para>Until 2026-10-09 the only sweep ran at startup (<c>Program.cs</c>), and <see cref="SessionStore.Validate"/>
/// removed an expired file only when its token was presented again. A server that is never restarted
/// therefore kept every session whose owner simply stopped — a week of files per person per window —
/// and <see cref="SessionStore.Active"/>, which the admin roster reads about once a minute per open
/// page, walked all of them to list the few that were alive. Found by the own review of the people
/// endpoint.</para>
/// <para><b>On a clock, like <see cref="JobPump"/>'s deadline sweep</b>, and for the same reason: an
/// event-driven sweep runs only when something happens, and the case this exists for is nothing
/// happening. An hour, because the deadline is seven days and a listing of a few dozen dead files is
/// milliseconds — the sweep is hygiene, not a limit.</para>
/// <para>Every tick is its own unit: a filesystem that refuses one sweep is logged and the next tick
/// runs, because a sweeper that dies silently is the audit's most instructive find — a sweep fully
/// implemented and invoked by nothing.</para>
/// </remarks>
public sealed class SessionSweeper(SessionStore sessions, ILogger<SessionSweeper> log, TimeSpan? period = null) : BackgroundService
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromHours(1);

    private readonly TimeSpan _period = period ?? DefaultPeriod;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once at boot, synchronously, before the host takes a request: the files a crash left
        // expired are gone before the first roster is asked for.
        Sweep();

        using var timer = new PeriodicTimer(_period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Sweep();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A planned stop, not a failure.
        }
    }

    private void Sweep()
    {
        try
        {
            var swept = sessions.Sweep(DateTimeOffset.UtcNow);
            if (swept > 0)
            {
                log.LogInformation("swept {Count} expired session(s)", swept);
            }
        }
        catch (Exception e)
        {
            log.LogError(e, "the session sweep failed and will try again next period");
        }
    }
}
