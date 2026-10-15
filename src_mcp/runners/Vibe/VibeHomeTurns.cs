namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// One Vibe turn at a time per coai-owned home (D17 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>: "turns sharing a home
/// are serialised").
/// </summary>
/// <remarks>
/// <para>A home is validated before every launch, and a validation that runs while another turn on the same home is still
/// writing its session, its caches and its lock file would judge a half-written home — and two Vibe processes on one
/// home writing one <c>.connector_bootstrap_cache.json.lock</c> is a race nobody measured. So the turn is taken FIRST,
/// the home checked inside it, and the turn given back only after the launch has been read.</para>
/// <para>In-process only, and that is enough: a home belongs to one owner — one reviewer of one round, one conversation —
/// inside one coai-mcp. Keyed by the home's full path, compared as the platform compares paths; an entry is dropped when
/// its last holder or waiter leaves, so a server that runs for weeks does not keep one semaphore per round forever.</para>
/// </remarks>
public static class VibeHomeTurns
{
    private static readonly Dictionary<string, Entry> Homes = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The home's turn, once every earlier holder has given it back; disposing it gives it back.</summary>
    public static async Task<IDisposable> TakeAsync(string home, CancellationToken ct)
    {
        var key = Path.GetFullPath(home);
        var entry = Join(key);
        try
        {
            await entry.Gate.WaitAsync(ct);
        }
        catch
        {
            Leave(key);
            throw;
        }

        return new Turn(key, entry);
    }

    /// <summary>
    /// The home's turn if nobody holds it NOW — never a wait. The consultant home sweep's question (D20): a home whose turn
    /// is held is a live launch, judged by the lock and never by a stamp, and is skipped this beat.
    /// </summary>
    /// <remarks>
    /// Vendor-free, like the rest of this class: the sweep asks it of a conversation's folder and of every home in it,
    /// whichever CLI's they are (D19: Kimi's homes share this, never a copy of it).
    /// </remarks>
    public static bool TryTake(string home, out IDisposable turn)
    {
        var key = Path.GetFullPath(home);
        var entry = Join(key);
        if (entry.Gate.Wait(0))
        {
            turn = new Turn(key, entry);

            return true;
        }

        Leave(key);
        turn = NotTaken.Instance;

        return false;
    }

    /// <summary>What <see cref="TryTake"/> hands back when it took nothing: disposing it gives nothing back.</summary>
    private sealed class NotTaken : IDisposable
    {
        public static NotTaken Instance { get; } = new();

        public void Dispose()
        {
            // Nothing was taken.
        }
    }

    /// <summary>Whether <paramref name="home"/> has a holder or a waiter now — for a test that a finished turn leaves nothing behind.</summary>
    internal static bool Held(string home)
    {
        lock (Homes)
        {
            return Homes.ContainsKey(Path.GetFullPath(home));
        }
    }

    private static Entry Join(string key)
    {
        lock (Homes)
        {
            if (!Homes.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                Homes[key] = entry;
            }

            entry.Users++;

            return entry;
        }
    }

    private static void Leave(string key)
    {
        lock (Homes)
        {
            var entry = Homes[key];
            entry.Users--;
            if (entry.Users == 0)
            {
                Homes.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Turn(string key, Entry entry) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 1)
            {
                return;
            }

            entry.Gate.Release();
            Leave(key);
        }
    }
}
