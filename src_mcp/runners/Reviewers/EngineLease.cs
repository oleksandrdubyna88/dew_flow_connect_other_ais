using System.Globalization;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// One local engine, one caller at a time — across every process on this machine.
/// </summary>
/// <remarks>
/// <para><b>The in-process cap was only half of it.</b> `BoundedScheduler` serialises the reviewers
/// of ONE server against one engine, and this machine routinely runs several MCP clients at once,
/// each with its own `coai-mcp`. The measured failure is the one that started this: three requests
/// on one card turned a 30-second reviewer into two cancelled at 590 s. So the lease lives where
/// every local reviewer of every server passes through — the `--ask-local` shim — and not in the
/// scheduler.</para>
///
/// <para><b>The lock is the operating system's, not a protocol of ours.</b> A lock FILE held open
/// with <see cref="FileShare.None"/> is exclusive between .NET processes on Windows and on Unix, and
/// the kernel releases it when the holder dies — including a kill, a crash and a power cut. The
/// first design of this class was a pid, a heartbeat and rules for stealing a stale lease, and this
/// change's own gate took it apart: pid reuse makes a dead holder look alive, a partial write makes
/// the metadata unreadable on exactly the kill path it exists for, two waiters race the same delete,
/// and a hung-but-alive holder is indistinguishable from a slow one. None of those exist here,
/// because none of that is written down anywhere: the only state is a handle the kernel owns.</para>
///
/// <para><b>Waiting is counted the same way.</b> A waiter holds its own file open while it queues, so
/// "how many are ahead" is "how many of these files are locked" — a waiter that was killed leaves a
/// file nobody holds, which is a file this class deletes rather than counts.</para>
/// </remarks>
public sealed class EngineLease : IDisposable
{
    private readonly FileStream _held;
    private readonly FileStream? _waiter;
    private readonly string _waiterPath;

    private EngineLease(FileStream held, FileStream? waiter, string waiterPath)
    {
        _held = held;
        _waiter = waiter;
        _waiterPath = waiterPath;
    }

    /// <summary>How long this lease waited before it got the card.</summary>
    public TimeSpan Waited { get; private init; }

    /// <summary>Where the leases live: one directory per user, beside nothing else.</summary>
    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp",
        "engines");

    /// <summary>
    /// Take the card, waiting until the deadline for whoever has it.
    /// </summary>
    /// <returns>The lease, or <c>null</c> when the deadline passed before the card was free.</returns>
    /// <remarks>
    /// The wait is bounded by the SAME deadline the reviewer has, because a wait that silently ate a
    /// reviewer's whole budget and then reported the engine as slow would be a lie about which part
    /// was slow. The caller is told what it waited for, and what is left.
    /// </remarks>
    public static async Task<EngineLease?> AcquireAsync(
        string engineKey,
        DateTime deadlineUtc,
        Action<TimeSpan, int>? onWaiting = null,
        CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(WaiterDirectory(engineKey));
        var started = DateTime.UtcNow;
        var waiterPath = Path.Combine(
            WaiterDirectory(engineKey),
            $"{Environment.ProcessId}-{Guid.NewGuid():N}.wait");
        var waiter = TryOpen(waiterPath);
        var announced = TimeSpan.Zero;
        try
        {
            while (true)
            {
                var held = TryOpen(LockPath(engineKey));
                if (held is not null)
                {
                    // Stop being a waiter the moment you are the holder, or you are counted twice:
                    // once by the lock you hold and once by the queue you have left.
                    waiter?.Dispose();
                    Forget(waiterPath);

                    return new EngineLease(held, null, waiterPath) { Waited = DateTime.UtcNow - started };
                }

                var waited = DateTime.UtcNow - started;
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    return null;
                }
                if (onWaiting is not null && waited - announced >= TimeSpan.FromSeconds(30))
                {
                    announced = waited;
                    onWaiting(waited, Ahead(engineKey));
                }

                // Jittered, so two waiters released together do not retry in lockstep for ever.
                await Task.Delay(TimeSpan.FromMilliseconds(200 + Random.Shared.Next(200)), ct);
            }
        }
        catch
        {
            waiter?.Dispose();
            Forget(waiterPath);
            throw;
        }
    }

    /// <summary>
    /// How many callers are on this engine right now — the holder, plus everyone queued behind it.
    /// </summary>
    /// <remarks>
    /// Counted by trying to OPEN each file: a live waiter holds its own, and a waiter that was killed
    /// leaves one nobody holds, which is deleted here rather than counted. That is the same
    /// mechanism as the lease itself, so there is one liveness rule in this class and not two.
    /// </remarks>
    public static int Ahead(string engineKey)
    {
        var count = Busy(LockPath(engineKey)) ? 1 : 0;
        var dir = WaiterDirectory(engineKey);
        if (!System.IO.Directory.Exists(dir))
        {
            return count;
        }
        foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.wait"))
        {
            if (Busy(file))
            {
                count += 1;
            }
            else
            {
                Forget(file);
            }
        }

        return count;
    }

    /// <summary>
    /// What a run on this engine has been taking, or nothing when too little has been recorded.
    /// </summary>
    /// <remarks>
    /// <para>Per engine AND model, because one average over both is an estimate of nothing: a
    /// ten-second check and a five-hundred-second analysis on the same card average to a number
    /// neither of them will take. Raised in this change's gate round.</para>
    /// <para>Three samples before it says anything at all. Two runs is not a rate, and a confident
    /// wrong number would be worse than the count of reviewers ahead, which is always true.</para>
    /// </remarks>
    public static TimeSpan? Typical(string engineKey, string model)
    {
        var samples = History(engineKey, model);

        return samples.Count < 3 ? null : TimeSpan.FromSeconds(samples.Average());
    }

    /// <summary>What a person waiting is told: how many are ahead, and how long that usually takes.</summary>
    public static string WaitNote(string engineKey, string model)
    {
        var ahead = Ahead(engineKey);
        if (ahead == 0)
        {
            return string.Empty;
        }
        var typical = Typical(engineKey, model);
        var queue = ahead == 1 ? "1 ahead on this engine" : $"{ahead} ahead on this engine";

        return typical is null
            ? queue
            : $"{queue}, about {Minutes(TimeSpan.FromSeconds(typical.Value.TotalSeconds * ahead))}";
    }

    /// <summary>Record what this run took, for the estimate the next one is given.</summary>
    /// <remarks>
    /// Written while the lease is still HELD, so two processes cannot interleave lines in it — the
    /// same exclusion that protects the engine protects its history, and nothing else has to.
    /// </remarks>
    public void Record(string engineKey, string model, TimeSpan took)
    {
        try
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{model.Replace('\t', ' ')}\t{took.TotalSeconds:F1}\n");
            File.AppendAllText(HistoryPath(engineKey), line);
        }
        catch (IOException)
        {
            // An estimate nobody can write is not a reason to fail a review.
        }
    }

    public void Dispose()
    {
        _held.Dispose();
        _waiter?.Dispose();
        Forget(_waiterPath);
    }

    private static IReadOnlyList<double> History(string engineKey, string model)
    {
        try
        {
            return File.ReadLines(HistoryPath(engineKey))
                .Select(l => l.Split('\t'))
                .Where(parts => parts.Length == 2 && string.Equals(parts[0], model, StringComparison.OrdinalIgnoreCase))
                .Select(parts => double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : -1)
                .Where(s => s > 0)
                .TakeLast(20)
                .ToList();
        }
        catch (IOException)
        {
            // No history yet, or somebody is writing it: an estimate is a courtesy, never a gate.
            return [];
        }
    }

    /// <summary>Open a file exclusively, or nothing when somebody already holds it.</summary>
    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Busy(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using var probe = TryOpen(path);

        return probe is null;
    }

    private static void Forget(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Somebody else holds it, which means it is not ours to remove.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Slug(string engineKey)
    {
        var chars = engineKey.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');

        return new string(chars.ToArray());
    }

    private static string LockPath(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.lock");

    private static string HistoryPath(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.history");

    private static string WaiterDirectory(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.waiting");

    private static string Minutes(TimeSpan span) =>
        span.TotalSeconds < 90
            ? $"{span.TotalSeconds:F0}s"
            : $"{span.TotalMinutes:F0} min";
}
