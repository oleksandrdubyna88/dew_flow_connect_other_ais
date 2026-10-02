using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Server;

/// <summary>
/// One JSON file per question in the data directory both halves share — the consultation store's
/// shape (D5): written whole under the turn, read without forbidding a writer, a torn file is nothing,
/// the SQLite table a projection hung off the one write road.
/// </summary>
/// <remarks>
/// <para><b>The sweep is D14 (d).</b> A <c>consulting</c> record is ended as <c>interrupted</c> only when
/// its heartbeat is older than <see cref="HeartbeatStale"/> AND its pid is dead: a recycled Windows pid
/// alone cannot end a live question, and a stale heartbeat alone cannot either — the fan-out rewrites
/// it every <see cref="HeartbeatEvery"/>, so a live server with a wedged heartbeat is a server still
/// running, left to settle its own record. A terminal record is deleted <see cref="Retention"/> after it
/// ended, and the directory is capped at <see cref="MaxFiles"/> — the oldest TERMINAL records go first,
/// never one still consulting (§5).</para>
/// <para>No repository lock, deliberately (D6): the fan-out takes none, so there is none for the sweep
/// to ask. The heartbeat is the fan-out's proof of life instead.</para>
/// </remarks>
public sealed partial class QuestionConsultStore(
    string dataDir,
    Action<string>? warn = null,
    Action<QuestionConsultRecord>? projected = null)
{
    /// <summary>How long a finished question is kept for the log and the card before its file goes (A4's clock, applied here too).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>How often the fan-out rewrites <see cref="QuestionConsultRecord.HeartbeatUtc"/> while rows run.</summary>
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);

    /// <summary>How old a heartbeat must be before a dead pid ends the record (D14 d) — four beats missed.</summary>
    public static readonly TimeSpan HeartbeatStale = TimeSpan.FromMinutes(2);

    /// <summary>The most records the directory holds (§5): ~200 KB each at the worst, 100 MB in all.</summary>
    public const int MaxFiles = 500;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex WellFormedId();

    public string Directory => Path.Combine(dataDir, "question-consults");

    /// <summary>Where a row's launch may write its answer and prompt files — swept on the record's clock.</summary>
    public string AnswersDir => Path.Combine(Directory, "answers");

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool IsWellFormedId(string? id) => id is not null && WellFormedId().IsMatch(id);

    public string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    /// <summary>The record on disk, and the projection after it — the file FIRST, the view second.</summary>
    public void Write(QuestionConsultRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        AtomicJson.Write(PathFor(record.Id), JsonSerializer.Serialize(record, QuestionConsultJsonContext.Default.QuestionConsultRecord));
        projected?.Invoke(record);
    }

    public QuestionConsultRecord? Read(string id) => IsWellFormedId(id) ? ReadFile(PathFor(id)) : null;

    /// <summary>Every question in the directory — and nothing else that happens to be JSON in it. The NAME is the guard.</summary>
    public IReadOnlyList<QuestionConsultRecord> All()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return [.. System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Where(path => IsWellFormedId(Path.GetFileNameWithoutExtension(path)))
            .Select(ReadFile)
            .OfType<QuestionConsultRecord>()
            .Where(record => IsWellFormedId(record.Id))];
    }

    /// <summary>
    /// What a restarted server owes the records a previous one left: a dead server's <c>consulting</c>
    /// record ends <c>interrupted</c>, a finished one past retention goes, the directory is held under
    /// its cap, and the answer files go on the same clock. Returns how many records changed or went.
    /// </summary>
    public int Sweep(Func<int, bool> isAlive, DateTime nowUtc)
    {
        var records = All();
        var changed = records.Sum(record => SweepOne(record, isAlive, nowUtc) ? 1 : 0);
        changed += Capped(records.Where(r => r.IsOver).ToList(), nowUtc);

        return changed + ConsultantArtefactSweep.Sweep(AnswersDir, nowUtc, Retention, warn);
    }

    private bool SweepOne(QuestionConsultRecord record, Func<int, bool> isAlive, DateTime nowUtc) =>
        record.IsOver
            ? nowUtc - Parse(Ended(record), nowUtc) > Retention && Deleted(record)
            : Orphaned(record, isAlive, nowUtc) && Rewritten(Interrupted(record, nowUtc));

    /// <summary>D14 (d): BOTH facts, never one — a stale heartbeat and a dead pid.</summary>
    internal static bool Orphaned(QuestionConsultRecord record, Func<int, bool> isAlive, DateTime nowUtc) =>
        nowUtc - Parse(record.HeartbeatUtc.Length > 0 ? record.HeartbeatUtc : record.StartedUtc, nowUtc) > HeartbeatStale
        && !isAlive(record.RunnerPid);

    /// <summary>The verdict a dead server's question gets: every row still consulting fails naming the fact; the record ends.</summary>
    private static QuestionConsultRecord Interrupted(QuestionConsultRecord record, DateTime nowUtc)
    {
        var stamp = Stamp(nowUtc);

        return record with
        {
            Status = QuestionConsultStatuses.Interrupted,
            Rows = [.. record.Rows.Select(row => row.IsOver
                ? row
                : row with { Status = RowOutcomes.Failed, Reason = Died, EndedUtc = stamp })],
            UpdatedUtc = stamp,
            EndedUtc = stamp,
        };
    }

    private const string Died = "the server running this question died before the row answered";

    /// <summary>Over the cap, the oldest TERMINAL records go first — one still consulting is never the price of the cap.</summary>
    private int Capped(List<QuestionConsultRecord> over, DateTime nowUtc)
    {
        var total = All().Count;
        if (total <= MaxFiles)
        {
            return 0;
        }

        return over
            .OrderBy(record => Parse(Ended(record), nowUtc))
            .Take(total - MaxFiles)
            .Count(Deleted);
    }

    private bool Rewritten(QuestionConsultRecord record)
    {
        Write(record);

        return true;
    }

    private bool Deleted(QuestionConsultRecord record)
    {
        try
        {
            File.Delete(PathFor(record.Id));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retried on the next sweep — but SAID, because a retention that silently never runs is a
            // directory that grows for ever with nothing anywhere reporting it.
            warn?.Invoke($"question {record.Id} is past retention and could not be removed: {e.Message}");

            return false;
        }
    }

    private static string Ended(QuestionConsultRecord record) =>
        record.EndedUtc.Length > 0 ? record.EndedUtc : record.UpdatedUtc.Length > 0 ? record.UpdatedUtc : record.StartedUtc;

    public static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string stamp, DateTime fallback) =>
        DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;

    private static QuestionConsultRecord? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var turn = SessionTurn.Take(path);

            return JsonSerializer.Deserialize(SharedRead.Text(path), QuestionConsultJsonContext.Default.QuestionConsultRecord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // torn, busy, or half-written: not a question, and the next read may find it whole
        }
    }
}
