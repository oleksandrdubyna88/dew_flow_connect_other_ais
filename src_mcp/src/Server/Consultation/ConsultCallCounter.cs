using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server;

/// <summary>What one attempt to take a call from the counter produced.</summary>
/// <param name="Note">Empty ordinarily; a sentence when the counter could not be persisted and is being held in memory.</param>
public sealed record CounterOutcome(bool Allowed, int Used, string Note);

/// <summary>
/// The per-caller call cap: how many consult calls one caller session has made in its window.
/// </summary>
/// <remarks>
/// <para>The <see cref="CallerSessions"/> claim shape — one file per caller, held
/// <see cref="FileShare.None"/> while it is read, decided and rewritten — because two servers share
/// one data directory as a matter of course and a read-then-write would let both count the same call
/// as the first.</para>
/// <para><b>It never fails open.</b> The split-order claim fails open because a duplicate instruction
/// is cheaper than a silently disabled feature; a call cap that fails open is a runaway agent on a
/// paid vendor, which two reviewers called out on the plan round. When the file cannot be written the
/// cap is enforced IN MEMORY for the lifetime of this server process, and the reply says so.</para>
/// </remarks>
public sealed class ConsultCallCounter(string dataDir)
{
    public static readonly TimeSpan Window = CallerSessions.Remembers;

    /// <summary>
    /// The fallback count, held per DATA DIRECTORY rather than per instance or per process.
    /// </summary>
    /// <remarks>
    /// <para>It cannot be an instance field: <see cref="PanelServiceHost"/> rebuilds the service
    /// whenever the panel rewrites its settings file, so an instance-scoped count would reset every
    /// time somebody touched a control — which is the cap quietly turning itself off.</para>
    /// <para>It is keyed by the data directory as well as the caller, so two servers with different
    /// data directories in one process — which is what a test run is — cannot see each other's
    /// counts. The values are immutable records replaced whole under the lock, never mutated in
    /// place. (codex and gemini, code round, from two directions.)</para>
    /// </remarks>
    private static readonly Lock Gate = new();

    private static readonly Dictionary<string, Spent> Memory = new(StringComparer.Ordinal);

    private sealed record Spent(DateTime Start, int Count);

    private string Dir => Path.Combine(dataDir, "consultations", "callers");

    /// <summary>How long to keep trying for the claim when another call in this process holds it.</summary>
    /// <remarks>
    /// Two consult calls from one caller session can overlap — a retry, a second agent thread — and
    /// <see cref="FileShare.None"/> refuses the second one outright. Without this it would fall
    /// straight to the memory count and report the file as unwritable, which is true of the instant
    /// and false about the machine. Milliseconds, because the held section is a read and a write.
    /// (gemini, code round.)
    /// </remarks>
    private static readonly TimeSpan Contention = TimeSpan.FromSeconds(2);

    public CounterOutcome TryTake(string caller, int cap, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        var deadline = DateTime.UtcNow + Contention;
        while (true)
        {
            try
            {
                return TakeFromFile(caller, cap, nowUtc);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(Random.Shared.Next(5, 25));
                    continue;
                }

                return TakeFromMemory(caller, cap, nowUtc) with
                {
                    Note = $"the call counter at {Dir} could not be persisted ({e.Message}); the cap of {cap} is being enforced in memory for this server process",
                };
            }
        }
    }

    private CounterOutcome TakeFromFile(string caller, int cap, DateTime nowUtc)
    {
        Directory.CreateDirectory(Dir);
        using var stream = new FileStream(FileFor(caller), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var (start, count) = Parse(reader.ReadToEnd(), nowUtc);
        var (windowStart, used) = nowUtc - start < Window ? (start, count) : (nowUtc, 0);
        if (used >= cap)
        {
            return new CounterOutcome(false, used, string.Empty);
        }

        stream.SetLength(0);
        using var writer = new StreamWriter(stream);
        writer.Write($"{windowStart.ToString("o", CultureInfo.InvariantCulture)}\t{used + 1}\t{caller}\n");

        return new CounterOutcome(true, used + 1, string.Empty);
    }

    private CounterOutcome TakeFromMemory(string caller, int cap, DateTime nowUtc)
    {
        // The separator is a NUL because neither a path nor a caller id can contain one, so no
        // pair of them can collide by concatenation. Written as an ESCAPE: it was a raw NUL byte
        // in this file, which is the same string and an invisible one — nothing in an editor, a
        // diff or a review shows it, and a re-encoding would change the key without a trace.
        // (SonarCloud, Critical, on the pull request.)
        var key = Path.GetFullPath(dataDir) + "\0" + caller;
        lock (Gate)
        {
            // SEEDED from the file when this process has not counted for this caller yet. Starting
            // at zero was a hole in the fail-closed contract: a write failure made every restart —
            // and every second server on the same data directory — hand out a whole fresh cap of
            // paid calls, while the note said the cap was "being enforced". The bytes are usually
            // still READABLE when the failure is a lock or a read-only directory, which is the
            // common case; when they are not, zero is genuinely all anybody knows.
            // (CodeRabbit, on the pull request.)
            var spent = Memory.TryGetValue(key, out var known) && nowUtc - known.Start < Window
                ? known
                : Persisted(caller, nowUtc);
            if (spent.Count >= cap)
            {
                return new CounterOutcome(false, spent.Count, string.Empty);
            }

            // Replaced whole, never mutated: the value is a record and the dictionary entry is the
            // only thing that moves.
            Memory[key] = spent with { Count = spent.Count + 1 };

            return new CounterOutcome(true, spent.Count + 1, string.Empty);
        }
    }

    /// <summary>What the file last recorded for this caller, or an empty window when it cannot be read.</summary>
    /// <remarks>
    /// Best effort and read-only: this runs precisely because the ordinary path failed, so it must
    /// not throw a second time — but a counter that forgets what is already spent is not a counter.
    /// </remarks>
    private Spent Persisted(string caller, DateTime nowUtc)
    {
        try
        {
            var (start, count) = Parse(File.ReadAllText(FileFor(caller)), nowUtc);

            return nowUtc - start < Window ? new Spent(start, count) : new Spent(nowUtc, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SystemException)
        {
            return new Spent(nowUtc, 0);
        }
    }

    private static (DateTime Start, int Count) Parse(string content, DateTime nowUtc)
    {
        var fields = content.Split('\t');
        return fields.Length >= 2
               && DateTime.TryParse(fields[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
               && int.TryParse(fields[1].Trim(), out var count)
            ? (start.ToUniversalTime(), count)
            : (nowUtc, 0);
    }

    private string FileFor(string caller) =>
        Path.Combine(Dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(caller))) + ".txt");
}
