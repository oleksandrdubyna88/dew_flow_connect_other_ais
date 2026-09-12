using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server;

/// <summary>
/// One JSON file per consultation in the data directory both halves share — the escalation channel's
/// shape: written whole under the turn, read without forbidding a writer, a torn file is nothing.
/// </summary>
public sealed partial class ConsultationStore(string dataDir, Action<string>? warn = null)
{
    /// <summary>How long a finished consultation is kept for the log and the card before its file goes.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex WellFormedId();

    public string Directory => Path.Combine(dataDir, "consultations");

    /// <summary>A fresh id: a GUID's 32 hex digits, which is also a safe file name.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>An id a CALLER handed us is a file name only when it is shaped like one of ours.</summary>
    public static bool IsWellFormedId(string id) => WellFormedId().IsMatch(id);

    public string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    public void Write(ConsultationRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        AtomicJson.Write(PathFor(record.Id), JsonSerializer.Serialize(record, ConsultationJsonContext.Default.ConsultationRecord));
    }

    /// <summary>The record, or null for an id nobody wrote, a torn file, or one being replaced right now.</summary>
    public ConsultationRecord? Read(string id) =>
        IsWellFormedId(id) ? ReadFile(PathFor(id)) : null;

    public IReadOnlyList<ConsultationRecord> All()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return [.. System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(ReadFile)
            .OfType<ConsultationRecord>()];
    }

    /// <summary>
    /// What a restarted server owes the records a previous one left: an <c>asking</c> record whose pid
    /// is gone becomes <c>interrupted</c> when it holds a handle and <c>failed</c> otherwise; an open
    /// record idle past its budget is closed and its handle dropped; a finished one past retention is
    /// deleted. Returns how many records changed or went.
    /// </summary>
    /// <remarks>
    /// The pid check is what keeps a SECOND server sharing this directory from killing the first
    /// one's live consultation — the same rule <c>SessionStore.SweepOrphanedRounds</c> follows.
    /// </remarks>
    public int Sweep(Func<int, bool> isAlive, DateTime nowUtc, TimeSpan idle, TimeSpan retention)
    {
        var changed = 0;
        foreach (var record in All())
        {
            changed += SweepOne(record, isAlive, nowUtc, idle, retention) ? 1 : 0;
        }

        return changed;
    }

    private bool SweepOne(ConsultationRecord record, Func<int, bool> isAlive, DateTime nowUtc, TimeSpan idle, TimeSpan retention)
    {
        if (record.IsOver)
        {
            return Expire(record, nowUtc, retention);
        }

        if (record.Status == ConsultationStatuses.Asking && !isAlive(record.RunnerPid))
        {
            Write(record.Handle.Length > 0
                ? record with { Status = ConsultationStatuses.Interrupted, Reason = "the server running this turn died before the answer was read", UpdatedUtc = Stamp(nowUtc) }
                : record with { Status = ConsultationStatuses.Failed, Reason = "the server running this turn died before the answer was read", EndedUtc = Stamp(nowUtc), UpdatedUtc = Stamp(nowUtc) });

            return true;
        }

        if (record.Status != ConsultationStatuses.Asking && IdleFor(record, nowUtc) > idle)
        {
            Write(record with
            {
                Status = ConsultationStatuses.Closed,
                Reason = $"idle for {idle.TotalMinutes:0} minutes — the vendor's conversation handle was dropped",
                Handle = string.Empty,
                EndedUtc = Stamp(nowUtc),
                UpdatedUtc = Stamp(nowUtc),
            });

            return true;
        }

        return false;
    }

    private bool Expire(ConsultationRecord record, DateTime nowUtc, TimeSpan retention)
    {
        if (nowUtc - Parse(record.EndedUtc.Length > 0 ? record.EndedUtc : record.UpdatedUtc, nowUtc) <= retention)
        {
            return false;
        }

        try
        {
            File.Delete(PathFor(record.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retried on the next sweep — but SAID, because a retention that silently never runs is
            // a directory that grows for ever with nothing anywhere reporting it. (codex, code round.)
            warn?.Invoke($"consultation {record.Id} is past retention and could not be removed: {e.Message}");

            return false;
        }

        return true;
    }

    /// <summary>Since the last turn was answered — or since it started, for a record with no stamp at all.</summary>
    public static TimeSpan IdleFor(ConsultationRecord record, DateTime nowUtc) =>
        nowUtc - Parse(record.UpdatedUtc.Length > 0 ? record.UpdatedUtc : record.StartedUtc, nowUtc);

    public static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string stamp, DateTime fallback) =>
        DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;

    private static ConsultationRecord? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var turn = SessionTurn.Take(path);
            return JsonSerializer.Deserialize(SharedRead.Text(path), ConsultationJsonContext.Default.ConsultationRecord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // torn, busy, or half-written: not a consultation, and the next read may find it whole
        }
    }
}
