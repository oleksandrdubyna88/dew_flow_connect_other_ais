using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    /// <summary>How many samples the history keeps per engine before it starts dropping the oldest.</summary>
    private const int HistoryCap = 200;

    /// <summary>How many of those an estimate averages.</summary>
    private const int SampleWindow = 20;

    private readonly FileStream _held;

    private EngineLease(FileStream held) => _held = held;

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
    /// <para>The wait is bounded by the SAME deadline the reviewer has, because a wait that silently
    /// ate a reviewer's whole budget and then reported the engine as slow would be a lie about which
    /// part was slow. The caller is told what it waited for, and what is left.</para>
    /// <para>The waiter file is cleaned up on EVERY exit — acquired, timed out, cancelled or thrown.
    /// Five reviewers of this change's code round found the same leak on the timeout path, one of
    /// them Blocking: an orphaned wait file is counted as a live waiter for ever, so the queue a
    /// person is shown grows by one every time somebody's deadline expires.</para>
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
                    return new EngineLease(held) { Waited = DateTime.UtcNow - started };
                }

                var waited = DateTime.UtcNow - started;
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    return null;
                }
                if (onWaiting is not null && waited - announced >= TimeSpan.FromSeconds(30))
                {
                    announced = waited;
                    // Ahead of THIS caller: its own wait file is not somebody it is waiting for.
                    onWaiting(waited, Ahead(engineKey, waiterPath));
                }

                // Jittered, so two waiters released together do not retry in lockstep for ever.
                await Task.Delay(TimeSpan.FromMilliseconds(200 + Random.Shared.Next(200)), ct);
            }
        }
        finally
        {
            waiter?.Dispose();
            Forget(waiterPath);
        }
    }

    /// <summary>
    /// How many callers are on this engine right now — the holder, plus everyone queued behind it.
    /// </summary>
    /// <param name="exceptWaiter">
    /// A wait file to leave out: the caller's own. Counting yourself as somebody you are waiting for
    /// says "2 ahead" when one reviewer is ahead, and multiplies the estimate by one whole run.
    /// </param>
    /// <remarks>
    /// Counted by trying to OPEN each file: a live waiter holds its own, and a waiter that was killed
    /// leaves one nobody holds, which is deleted here rather than counted. That is the same mechanism
    /// as the lease itself, so there is one liveness rule in this class and not two.
    /// </remarks>
    public static int Ahead(string engineKey, string exceptWaiter = "")
    {
        var count = Busy(LockPath(engineKey)) ? 1 : 0;
        var dir = WaiterDirectory(engineKey);
        if (!System.IO.Directory.Exists(dir))
        {
            return count;
        }
        foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.wait"))
        {
            if (string.Equals(file, exceptWaiter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
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
    public static string WaitNote(string engineKey, string model, string exceptWaiter = "")
    {
        var ahead = Ahead(engineKey, exceptWaiter);
        if (ahead == 0)
        {
            return string.Empty;
        }
        var typical = Typical(engineKey, model);
        var queue = ahead == 1 ? "1 ahead on this engine" : $"{ahead} ahead on this engine";

        return typical is null
            ? queue
            : $"{queue}, about {Rough(TimeSpan.FromSeconds(typical.Value.TotalSeconds * ahead))}";
    }

    /// <summary>Record what this run took, for the estimate the next one is given.</summary>
    /// <remarks>
    /// <para>Written while the lease is still HELD, so two processes cannot interleave lines in it —
    /// the same exclusion that protects the engine protects its history, and nothing else has to.</para>
    /// <para>Called only for a run that SUCCEEDED. An endpoint that answers 404 in three milliseconds
    /// is not a three-millisecond run, and averaging it in would tell the next person their wait is
    /// nearly over. Raised in this change's code round.</para>
    /// </remarks>
    public void Record(string engineKey, string model, TimeSpan took)
    {
        try
        {
            var clean = model.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            var line = string.Create(CultureInfo.InvariantCulture, $"{clean}\t{took.TotalSeconds:F1}");
            var path = HistoryPath(engineKey);
            var kept = File.Exists(path)
                ? File.ReadAllLines(path).TakeLast(HistoryCap - 1).Append(line)
                : [line];
            // Rewritten rather than appended for ever: the file is read by every queued reviewer,
            // and an unbounded one turns an estimate into a scan of a year of history.
            File.WriteAllLines(path, kept);
        }
        catch (IOException)
        {
            // An estimate nobody can write is not a reason to fail a review.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _held.Dispose();

    private static IReadOnlyList<double> History(string engineKey, string model)
    {
        try
        {
            return File.ReadLines(HistoryPath(engineKey))
                .Select(l => l.Split('\t'))
                .Where(parts => parts.Length == 2 && string.Equals(parts[0], model, StringComparison.OrdinalIgnoreCase))
                .Select(parts => double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : -1)
                .Where(s => s > 0)
                .TakeLast(SampleWindow)
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

    /// <summary>
    /// A file name for one engine: readable, and unique.
    /// </summary>
    /// <remarks>
    /// The readable half alone was not enough — folding every non-alphanumeric character to a hyphen
    /// maps <c>http://host/a</c> and <c>http://host-a</c> onto one file, so two different engines
    /// would have shared a lock and a history. Found in this change's code round. The hash of the
    /// exact key is what makes it injective; the prefix is what makes the directory readable when
    /// somebody looks.
    /// </remarks>
    private static string Slug(string engineKey)
    {
        var readable = new string(engineKey.Take(48).Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engineKey)))[..12].ToLowerInvariant();

        return $"{readable}-{hash}";
    }

    private static string LockPath(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.lock");

    private static string HistoryPath(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.history");

    private static string WaiterDirectory(string engineKey) =>
        Path.Combine(Directory, $"{Slug(engineKey)}.waiting");

    private static string Rough(TimeSpan span) =>
        span.TotalSeconds < 90
            ? $"{span.TotalSeconds:F0}s"
            : $"{span.TotalMinutes:F0} min";
}
