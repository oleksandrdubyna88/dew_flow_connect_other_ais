using System.Collections.Concurrent;

namespace CoaiBugs;

/// <summary>A sliding one-minute window per key, in memory, for ONE process.</summary>
/// <remarks>
/// <para><b>Per key only.</b> See <see cref="LimiterSubject"/> for why an address is not, and cannot
/// be, a subject here.</para>
/// <para><b>Thread-safe by immutability, and the atomic operation is named.</b> The dictionary is
/// concurrent, which protects the dictionary and nothing inside it — two findings said the same
/// thing: a request appending a stamp while the sweep evicts that window, and eleven simultaneous
/// requests all reading one window and all passing a limit of ten. So a <see cref="Window"/> is
/// IMMUTABLE and replaced by compare-and-swap: an admit reads the window, computes the next one, and
/// <c>TryUpdate</c> installs it only if the entry still holds the one it read — a loser re-reads and
/// re-decides. Eviction is <c>TryRemove</c> of the exact instance the sweep judged idle, so an admit
/// that swapped a stamp in meanwhile keeps its window. Both rest on the dictionary's own atomic
/// compare-and-swap; the residual is a retry, never a lost stamp or an over-admit, and a test races
/// N requests against a limit of L and asserts exactly L admitted.</para>
/// <para><b>Bounded.</b> A window holds at most <c>limit</c> stamps, and the dictionary holds one
/// window per subject that sent within the last minute. Subjects are AUTHENTICATED key ids — the 401
/// comes first — so the dictionary's size is bounded by the keys that exist, and <see cref="Sweep"/>
/// drops idle windows every minute. The worst case is in the plan's growth budget:
/// <c>keys × limit × 8 bytes</c>, a few megabytes at a thousand keys and the maximum rate.</para>
/// <para><b>One process, and it resets on restart, by design.</b> <see cref="ServeLock"/> is what
/// makes the first half true rather than assumed; the second is stated rather than discovered — a
/// deploy rolls the limiter back too, which is acceptable here.</para>
/// </remarks>
public sealed class RateLimiter(RatePerMinute limit, TimeProvider clock)
{
    /// <summary>The window every stamp lives in.</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    /// <summary>The setting in force.</summary>
    public RatePerMinute Limit => limit;

    /// <summary>How many subjects hold a window right now — for the tests and, later, <c>/admin/active</c>.</summary>
    public int Tracked => _windows.Count;

    /// <summary>Decides one request: through, or wait.</summary>
    public Admission Admit(LimiterSubject subject) =>
        limit.Disabled ? Admission.Through : Admitting(subject.Key, clock.GetUtcNow().UtcTicks);

    private Admission Admitting(string key, long now)
    {
        while (true)
        {
            var current = _windows.GetOrAdd(key, Window.Empty);
            var (next, admission) = current.Admit(now, limit.Value, WindowLength.Ticks);

            // A refusal needs no write: stamps only leave a window with time, so a window that was
            // full at this instant is not made emptier by anything a concurrent request could do.
            // An admission is installed only if nobody swapped the window between the read and the
            // write — a concurrent admit or the sweep — and otherwise decided again over the new one.
            if (!admission.Admitted || _windows.TryUpdate(key, next, current))
            {
                return admission;
            }
        }
    }

    /// <summary>Drops every window with no stamp inside the last minute. Answers how many it dropped.</summary>
    /// <remarks>
    /// <c>TryRemove</c> of the pair, not of the key: it removes only if the entry still holds the
    /// instance judged idle, so a window that gained a stamp since the judgement survives. That is
    /// the whole of "the sweep must not race an admit".
    /// </remarks>
    public int Sweep()
    {
        var now = clock.GetUtcNow().UtcTicks;
        var dropped = 0;
        foreach (var entry in _windows)
        {
            if (entry.Value.IsIdle(now, WindowLength.Ticks) && _windows.TryRemove(entry))
            {
                dropped++;
            }
        }

        return dropped;
    }

    /// <summary>How many stamps a subject holds, expired or not — for the tests.</summary>
    internal int StampsOf(LimiterSubject subject) =>
        _windows.TryGetValue(subject.Key, out var window) ? window.Count : 0;
}
