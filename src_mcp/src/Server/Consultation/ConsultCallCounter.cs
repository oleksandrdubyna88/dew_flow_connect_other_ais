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

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (DateTime Start, int Count)> Memory = new(StringComparer.Ordinal);

    private string Dir => Path.Combine(dataDir, "consultations", "callers");

    public CounterOutcome TryTake(string caller, int cap, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        try
        {
            return TakeFromFile(caller, cap, nowUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var outcome = TakeFromMemory(caller, cap, nowUtc);
            return outcome with
            {
                Note = $"the call counter at {Dir} could not be persisted ({e.Message}); the cap of {cap} is being enforced in memory for this server process",
            };
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

    private static CounterOutcome TakeFromMemory(string caller, int cap, DateTime nowUtc)
    {
        lock (Gate)
        {
            var (start, count) = Memory.TryGetValue(caller, out var known) && nowUtc - known.Start < Window
                ? known
                : (nowUtc, 0);
            if (count >= cap)
            {
                return new CounterOutcome(false, count, string.Empty);
            }

            Memory[caller] = (start, count + 1);
            return new CounterOutcome(true, count + 1, string.Empty);
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
