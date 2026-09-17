namespace CoaiBugs;

/// <summary>Runs the limiter's sweep every minute for the life of the host.</summary>
/// <remarks>
/// <para>A <see cref="PeriodicTimer"/> rather than a <c>System.Threading.Timer</c>, per the doctrine;
/// the whole body inside its <c>try</c> with a catch-all that logs, and each tick inside its own,
/// per the reliability rule — a sweep that dies silently is a dictionary that grows silently. The
/// decision of what to drop is the limiter's compare-and-swap; this is only the clock that calls it.</para>
/// <para>The timer is thin wiring around <see cref="RateLimiter.Sweep"/>, which is what the tests
/// drive with a frozen clock. What this class adds — a tick a minute, a caught exception — has no
/// behaviour worth a fixture that waits sixty seconds.</para>
/// </remarks>
internal sealed class LimiterSweep(RateLimiter limiter, ILogger<LimiterSweep> log) : BackgroundService
{
    /// <summary>How often idle windows are dropped.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            using var timer = new PeriodicTimer(Every);
            while (await timer.WaitForNextTickAsync(stopping))
            {
                Tick();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // A planned stop, which is the only way this loop is meant to end.
        }
        catch (Exception e)
        {
            log.LogError(
                e, "the rate limiter's sweep stopped; idle windows are kept until the next start");
        }
    }

    private void Tick()
    {
        try
        {
            var dropped = limiter.Sweep();
            if (dropped > 0 && log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("the rate limiter dropped {Dropped} idle window(s)", dropped);
            }
        }
        catch (Exception e)
        {
            log.LogWarning(e, "one sweep of the rate limiter failed; the next runs in a minute");
        }
    }
}
