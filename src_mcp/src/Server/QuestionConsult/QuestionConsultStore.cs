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
        AtomicJson.Write(PathFor(record.Id), JsonSerializer.Serialize(record, QuestionConsultJsonContext.Default.QuestionConsultRecord));
        projected?.Invoke(record);
    }

    public QuestionConsultRecord? Read(string id) => IsWellFormedId(id) ? ReadFile(PathFor(id)) : null;

    /// <summary>
    /// D14 (a), S4b item 8: the consultation's proof spent on ONE card, atomically — the record read, judged and written
    /// under its turn, before the card is posted. Answers what came of it; on <see cref="SpendOutcome.Spent"/> the record
    /// names the card and says the person was asked.
    /// </summary>
    /// <remarks>
    /// <para>Before this the proof was verified, the card posted, and only then the record marked — so two
    /// <c>ask_human</c> calls with one <c>consultId</c> in flight at once both verified it and both went through.</para>
    /// <para>The turn guards against another process; <see cref="SpendGate"/> against this one, because the turn is a
    /// lock file opened <c>FileShare.None</c> and is not re-entrant — a second call of this process cannot take it, waits
    /// out its attempts and would then go on WITHOUT it. A turn that cannot be had is refused (<see cref="SpendOutcome.Busy"/>),
    /// never spent without it.</para>
    /// </remarks>
    /// <param name="spentAs">What the record says happened next — <see cref="QuestionOutcomes.PersonAsked"/> for a card,
    /// <see cref="QuestionOutcomes.PersonAskedInConversation"/> when the AI asks in its own conversation and no card exists.</param>
    public (SpendOutcome Outcome, string PreviousOutcome) Spend(string id, string escalationId, string spentAs = QuestionOutcomes.PersonAsked)
    {
        var previous = string.Empty;
        var outcome = Changed(id, record =>
        {
            previous = record.Outcome;

            return record.EscalationId.Length > 0 ? null : record with { Outcome = spentAs, EscalationId = escalationId };
        });

        return (outcome, previous);
    }

    /// <summary>A spend undone because its card was never posted: the record forgets the card, and the proof can open the door again.</summary>
    /// <remarks>Fenced by the card's id — a record another card has spent since is left as it is.</remarks>
    public SpendOutcome Release(string id, string escalationId, string previousOutcome) =>
        Changed(id, record => record.EscalationId == escalationId ? record with { Outcome = previousOutcome, EscalationId = string.Empty } : null);

    /// <summary>One record read, changed and written whole under its turn and <see cref="SpendGate"/>; a null change writes nothing.</summary>
    private SpendOutcome Changed(string id, Func<QuestionConsultRecord, QuestionConsultRecord?> change)
    {
        if (!IsWellFormedId(id))
        {
            return SpendOutcome.Gone;
        }

        var path = PathFor(id);
        lock (SpendGate)
        {
            using var turn = SessionTurn.Take(path);

            return turn.Held ? ChangedUnderTurn(path, change) : SpendOutcome.Busy;
        }
    }

    private SpendOutcome ChangedUnderTurn(string path, Func<QuestionConsultRecord, QuestionConsultRecord?> change)
    {
        if (ParsedFile(path) is not { } record)
        {
            return SpendOutcome.Gone;
        }

        if (change(record) is not { } next)
        {
            return SpendOutcome.AlreadyUsed;
        }

        AtomicJson.WriteUnderTurn(path, JsonSerializer.Serialize(next, QuestionConsultJsonContext.Default.QuestionConsultRecord));
        projected?.Invoke(next);

        return SpendOutcome.Spent;
    }

    /// <summary>One gate for every spend of this process — the turn is not re-entrant (<see cref="Spend"/>).</summary>
    private static readonly Lock SpendGate = new();

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
    /// A row's budget (<c>COAI_QCONSULT_ROW_MINUTES</c>): rows run in parallel, so it is the question's deadline too, and
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

    /// <summary>The record in the file, read by a caller that holds its turn — or null: torn, busy, half-written or gone.</summary>
    private static QuestionConsultRecord? ParsedFile(string path) =>
        RecordFiles.ReadHeld(path, QuestionConsultJsonContext.Default.QuestionConsultRecord);
}

/// <summary>What spending (or releasing) a consultation's proof came to (S4b item 8).</summary>
public enum SpendOutcome
{
    /// <summary>The record now names the card — or, on a release, no longer does.</summary>
    Spent,

    /// <summary>Another card already spent it (on a release: a card other than this one holds it).</summary>
    AlreadyUsed,

    /// <summary>Its turn could not be had in time; nothing was written, and nothing is spent without the turn.</summary>
    Busy,

    /// <summary>No such record, or one that cannot be read.</summary>
    Gone,
}
