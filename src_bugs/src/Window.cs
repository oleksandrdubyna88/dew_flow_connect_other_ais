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
    /// <remarks>
    /// The newest stamp is last, so ONE comparison answers for all of them. It sliced the live stamps
    /// to count them — an allocation per key per sweep, on the call the sweep makes for every key
    /// every minute — and a code round asked why.
    /// </remarks>
    public bool IsIdle(long now, long length) => _stamps.Length == 0 || _stamps[^1] <= now - length;

    /// <summary>The window without its newest stamp — for a request admitted and then refused by the corpus.</summary>
    public Window WithoutNewest() => _stamps.Length == 0 ? this : new Window(_stamps[..^1]);

    /// <summary>How many stamps are still inside the window — counted, never sliced.</summary>
    /// <remarks>
    /// `/admin/active` asks this for every tracked subject on every call, so it walks the array and
    /// returns a number rather than reusing <see cref="Live"/>, which allocates the surviving slice.
    /// The same reasoning a code round applied to <see cref="IsIdle"/>: a read that only needs a
    /// count should not pay for a copy.
    /// </remarks>
    public int Since(long floor) => _stamps.Length - FirstLive(floor);

    /// <summary>The stamps still inside the window, i.e. newer than <c>now - length</c>.</summary>
    private long[] Live(long now, long length)
    {
        var first = FirstLive(now - length);

        return first == 0 ? _stamps : _stamps[first..];
    }

    /// <summary>Where the stamps still inside the window begin: the first one NEWER than the floor.</summary>
    /// <remarks>
    /// One scan for both readers. <see cref="Live"/> takes the slice from here and
    /// <see cref="Since"/> takes the count, and they must agree on the boundary — a window that
    /// admitted a request must report that request as in-window on `/admin/active`, and two copies
    /// of `&lt;=` are two places for that to stop being true. The stamps are appended in time order,
    /// so a walk from the oldest stops at the first survivor.
    /// </remarks>
    private int FirstLive(long floor)
    {
        var first = 0;
        while (first < _stamps.Length && _stamps[first] <= floor)
        {
            first++;
        }

        return first;
    }
}
