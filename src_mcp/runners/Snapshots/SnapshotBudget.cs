using System.Globalization;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// The aggregate cap on what this process's live snapshots hold on disk (D6, plan gate 2026-10-12).
/// </summary>
/// <remarks>
/// <para>A per-snapshot ceiling bounds one copy; nothing bounded the SUM. Fifty consultations a day on a repository
/// this size is 2 GB of copying, and a handful of rounds together hold a few hundred megabytes at once — enough that a
/// temp volume already low on space is the thing to protect. So before a copy starts, the bytes it will take (known
/// from the listing) are reserved against two numbers: a ceiling on the sum this process holds (2 GB by default), and a
/// floor the volume's free space may not fall under (5 GB by default). A refusal names both numbers. A snapshot's
/// disposal gives its bytes back.</para>
/// <para>"Live" here means NOT YET DISPOSED, whichever source mode made the snapshot: a pinned copy holds disk the same
/// way a live-tree copy does.</para>
/// <para><b>During a refresh in place, <see cref="Held"/> transiently counts old + new.</b> The new copy is reserved
/// while the old one is still on disk and still held, so between the reservation and <c>ReviewSnapshot.Replace</c> (or
/// the refusal that releases the new hold) the sum may stand ABOVE the ceiling — which is the truth about the disk at
/// that moment. The ceiling CHECK itself counts the replaced bytes once (<c>replacing</c>), so a refresh that fits is
/// not refused for the copy it is about to retire; a concurrent reservation in that window sees the larger sum and may
/// be refused, which is the conservative side.</para>
/// <para>The volume reader is injected so the floor is tested without filling a disk; the real one asks
/// <see cref="DriveInfo"/>. A reader that cannot answer is a refusal, not a guess: an unmeasured volume is neither full
/// nor empty, and the family's rule is that absent is not zero.</para>
/// </remarks>
public sealed class SnapshotBudget(long ceilingBytes, long freeFloorBytes, Func<string, long> freeBytesOn)
{
    /// <summary>2 GB — the plan's default ceiling on the sum of live snapshots.</summary>
    public const long DefaultCeilingBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>5 GB — the plan's default floor under which the temp volume's free space may not fall.</summary>
    public const long DefaultFreeFloorBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>The one budget the product's snapshots share — process-wide, as the cap is defined.</summary>
    public static SnapshotBudget Shared { get; } = new(DefaultCeilingBytes, DefaultFreeFloorBytes, FreeBytesOn);

    private readonly Lock _lock = new();
    private long _held;
    private int _count;

    /// <summary>The bytes this process's live snapshots hold.</summary>
    public long Held
    {
        get
        {
            lock (_lock)
            {
                return _held;
            }
        }
    }

    /// <summary>How many snapshots hold them.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Room for <paramref name="bytes"/> on the volume holding <paramref name="path"/>, or a refusal naming the numbers.
    /// </summary>
    /// <param name="replacing">
    /// The bytes of a snapshot this one REPLACES (a refresh in place): counted once against the ceiling, not old plus
    /// new. The free-space floor still sees the whole new copy, because the old one is on disk until the swap.
    /// </param>
    public Reserving Reserve(long bytes, string path, long replacing = 0)
    {
        long free;
        try
        {
            free = freeBytesOn(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Reserving.Refused($"the free space of the volume holding {path} could not be read ({e.Message}), so no snapshot is made on it");
        }

        lock (_lock)
        {
            var why = OverTheCeiling(bytes, replacing) + UnderTheFloor(bytes, free, path);
            if (why.Length > 0)
            {
                return new Reserving.Refused(why);
            }

            _held += bytes;
            _count++;

            return new Reserving.Held(new Hold(this, bytes));
        }
    }

    private string OverTheCeiling(long bytes, long replacing)
    {
        var would = _held - replacing + bytes;

        return would > ceilingBytes
            ? $"this process holds {N(_held)} bytes in {_count} snapshot(s); with the {N(bytes)} bytes of this one it would hold {N(would)}, over the ceiling of {N(ceilingBytes)}. "
            : string.Empty;
    }

    private string UnderTheFloor(long bytes, long free, string path) =>
        free - bytes < freeFloorBytes
            ? $"the volume holding {path} has {N(free)} bytes free; after the {N(bytes)} bytes of this snapshot it would have {N(free - bytes)}, under the floor of {N(freeFloorBytes)}. "
            : string.Empty;

    private void Release(long bytes)
    {
        lock (_lock)
        {
            _held -= bytes;
            _count--;
        }
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The free bytes on the volume holding <paramref name="path"/> — what <see cref="Shared"/> asks.</summary>
    /// <remarks>
    /// On Windows <see cref="DriveInfo"/> wants the drive, so the path's root is asked; on Unix it wants a path and
    /// answers for the mount holding it — asking <c>/</c> there would report the root filesystem for a temp directory
    /// mounted elsewhere.
    /// </remarks>
    public static long FreeBytesOn(string path)
    {
        var full = Path.GetFullPath(path);
        var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(full) ?? full : full;

        return new DriveInfo(volume).AvailableFreeSpace;
    }

    /// <summary>Bytes reserved for one snapshot, given back exactly once when disposed.</summary>
    public sealed class Hold : IDisposable
    {
        private readonly SnapshotBudget _budget;
        private int _released;

        internal Hold(SnapshotBudget budget, long bytes)
        {
            _budget = budget;
            Bytes = bytes;
        }

        public long Bytes { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _budget.Release(Bytes);
            }
        }
    }

    /// <summary>What a reservation came to.</summary>
    public abstract record Reserving
    {
        private Reserving()
        {
        }

        public sealed record Held(Hold Hold) : Reserving;

        public sealed record Refused(string Reason) : Reserving;
    }
}
