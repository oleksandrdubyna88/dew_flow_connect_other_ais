using System.Text.Json;
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
public sealed class QuestionConsultStore(
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

    /// <summary>The write road — <see cref="AtomicJson.Write"/> (path, whole text). A seam so a test can refuse a write on every OS; the file is never touched when it throws.</summary>
    internal Action<string, string> FileWriter { get; init; } = AtomicJson.Write;

    public string Directory => Path.Combine(dataDir, "question-consults");

    /// <summary>Where a row's launch may write its answer and prompt files — swept on the record's clock.</summary>
    public string AnswersDir => Path.Combine(Directory, "answers");

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool IsWellFormedId(string? id) => RecordFiles.IsRecordId(id);

    public string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    /// <summary>The record on disk, and the projection after it — the file FIRST, the view second.</summary>
    public void Write(QuestionConsultRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        FileWriter(PathFor(record.Id), JsonSerializer.Serialize(record, QuestionConsultJsonContext.Default.QuestionConsultRecord));
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
    /// <param name="deadline">
    /// A row's budget (<c>COAI_CONSASK_ROW_MINUTES</c>): rows run in parallel, so it is the question's deadline too, and
    /// a <c>consulting</c> record past TWICE it is ended whatever its pid says (§5, S4b item 9).
    /// </param>
    public int Sweep(Func<int, bool> isAlive, DateTime nowUtc, TimeSpan deadline)
    {
        var records = All();
        var changed = records.Sum(record => SweepOne(record, isAlive, nowUtc, deadline) ? 1 : 0);
        changed += Capped(records.Where(r => r.IsOver).ToList(), nowUtc);

        return changed + ConsultantArtefactSweep.Sweep(AnswersDir, nowUtc, Retention, warn);
    }

    private bool SweepOne(QuestionConsultRecord record, Func<int, bool> isAlive, DateTime nowUtc, TimeSpan deadline) =>
        record.IsOver
            ? nowUtc - Parse(Ended(record), nowUtc) > Retention && Deleted(record)
            : WhyEnded(record, isAlive, nowUtc, deadline) is { Length: > 0 } why && Rewritten(Interrupted(record, nowUtc, why));

    /// <summary>
    /// Why a <c>consulting</c> record is over though nothing settled it, or empty: its server died (D14 d — a stale
    /// heartbeat AND a dead pid), or it is past twice its deadline (S4b item 9 — a live server whose fan-out beats but
    /// never settles: a wedged launch, a final write that failed).
    /// </summary>
    private static string WhyEnded(QuestionConsultRecord record, Func<int, bool> isAlive, DateTime nowUtc, TimeSpan deadline) =>
        Orphaned(record, isAlive, nowUtc) ? Died
        : nowUtc - Parse(record.StartedUtc, nowUtc) > 2 * deadline ? PastDeadline
        : string.Empty;

    /// <summary>D14 (d): BOTH facts, never one — a stale heartbeat and a dead pid.</summary>
    internal static bool Orphaned(QuestionConsultRecord record, Func<int, bool> isAlive, DateTime nowUtc) =>
        nowUtc - Parse(record.HeartbeatUtc.Length > 0 ? record.HeartbeatUtc : record.StartedUtc, nowUtc) > HeartbeatStale
        && !isAlive(record.RunnerPid);

    /// <summary>The verdict a question nobody settled gets: every row still consulting fails naming the fact; the record ends.</summary>
    private static QuestionConsultRecord Interrupted(QuestionConsultRecord record, DateTime nowUtc, string why)
    {
        var stamp = Stamp(nowUtc);

        return record with
        {
            Status = QuestionConsultStatuses.Interrupted,
            Rows = [.. record.Rows.Select(row => row.IsOver
                ? row
                : row with { Status = RowOutcomes.Failed, Reason = why, EndedUtc = stamp })],
            UpdatedUtc = stamp,
            EndedUtc = stamp,
        };
    }

    private const string Died = "the server running this question died before the row answered";

    private const string PastDeadline = "the question ran past twice its deadline without settling — its server left the record behind";

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

    private bool Deleted(QuestionConsultRecord record) => RecordFiles.Delete(PathFor(record.Id), warn, $"question {record.Id}");

    private static string Ended(QuestionConsultRecord record) =>
        record.EndedUtc.Length > 0 ? record.EndedUtc : record.UpdatedUtc.Length > 0 ? record.UpdatedUtc : record.StartedUtc;

    public static string Stamp(DateTime utc) => RecordFiles.Stamp(utc);

    private static DateTime Parse(string stamp, DateTime fallback) => RecordFiles.Parse(stamp, fallback);

    private static QuestionConsultRecord? ReadFile(string path) =>
        RecordFiles.Read(path, QuestionConsultJsonContext.Default.QuestionConsultRecord);
}
