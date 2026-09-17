namespace CoaiBugs;

/// <summary>What the limiter said: through, or wait this long.</summary>
public sealed record Admission(bool Admitted, int RetryAfterSeconds)
{
    /// <summary>Through.</summary>
    public static readonly Admission Through = new(true, 0);

    /// <summary>Refused, with the wait rounded up and never below one second.</summary>
    /// <remarks>A 429 with no number is a client that retries immediately for ever.</remarks>
    public static Admission Refused(int retryAfterSeconds) => new(false, Math.Max(1, retryAfterSeconds));
}

/// <summary>The stamps one subject holds inside the sliding window — IMMUTABLE.</summary>
/// <remarks>
/// <para>A new window replaces the old one; nothing here is ever mutated. That is what lets
/// <see cref="RateLimiter"/> decide with a compare-and-swap instead of a lock: two requests reading
/// the same window compute two candidates, and the dictionary installs exactly one.</para>
/// <para>At most <c>limit</c> stamps, by construction — a key cannot hold more stamps than it is
/// allowed requests, so the memory a subject costs is bounded by the setting, not by traffic.</para>
/// </remarks>
internal sealed class Window
{
    /// <summary>No stamps; the window every subject starts from.</summary>
    public static readonly Window Empty = new([]);

    /// <summary>UTC ticks, ascending.</summary>
    private readonly long[] _stamps;

    private Window(long[] stamps) => _stamps = stamps;

    /// <summary>How many stamps it holds, expired or not — for the tests.</summary>
    public int Count => _stamps.Length;

    /// <summary>Decides one request, answering the window that would result if it is admitted.</summary>
    /// <param name="now">UTC ticks.</param>
    /// <param name="limit">Requests allowed inside one window.</param>
    /// <param name="length">The window, in ticks.</param>
    public (Window Next, Admission Admission) Admit(long now, int limit, long length)
    {
        var live = Live(now, length);
        if (live.Length >= limit)
        {
            // The oldest live stamp is the first to leave the window; that is when a slot opens.
            var untilASlotOpens = live[0] + length - now;

            return (this, Admission.Refused((int)Math.Ceiling(untilASlotOpens / (double)TimeSpan.TicksPerSecond)));
        }

        return (new Window([.. live, now]), Admission.Through);
    }

    /// <summary>Whether no stamp is inside the window any more — what the sweep evicts on.</summary>
    public bool IsIdle(long now, long length) => Live(now, length).Length == 0;

    /// <summary>The stamps still inside the window, i.e. newer than <c>now - length</c>.</summary>
    private long[] Live(long now, long length)
    {
        var floor = now - length;
        var first = 0;
        while (first < _stamps.Length && _stamps[first] <= floor)
        {
            first++;
        }

        return first == 0 ? _stamps : _stamps[first..];
    }
}
