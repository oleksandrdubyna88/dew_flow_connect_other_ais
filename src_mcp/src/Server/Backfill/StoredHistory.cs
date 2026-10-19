using System.Collections.Immutable;
using System.Globalization;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// One gate session as the backfill sees it: the rows the database holds for its rounds, and the journal the live path would have had — the
/// session FILE's rounds when the file is there, else rounds REBUILT from those rows (E7.S2).
/// </summary>
/// <param name="Journal">
/// The rounds in the order they ran. Mutable on purpose: the run replaces a round's entry with the snapshot and the size it just gave it, so the
/// next round of the session sees them — the earlier caller of its timeline, the predecessor of its delta — exactly as it would have when it ran
/// live, and a DRY run keeps the same overlay in memory so its counts equal the writing run's.
/// </param>
internal sealed record StoredGate(
    string SessionId,
    string RepoPath,
    string Branch,
    SessionFileState FileState,
    DateTime OpenedUtc,
    List<RoundRecord> Journal,
    ImmutableArray<StoredRound> Rounds,
    ImmutableArray<RoundRecord> FromDatabase)
{
    /// <summary>The index of the round in the journal, or -1 when the journal does not list it.</summary>
    public int IndexOf(StoredRound round) => Journal.FindIndex(entry => entry.Stage == round.Stage && entry.Number == round.Number);

    /// <summary>The session as the engine held it BEFORE <paramref name="round"/> was saved: its state, its open time, the rounds that ran earlier.</summary>
    public PersistedSession Before(StoredRound round, int index) =>
        new(new SessionState(SessionId, RepoPath, Branch, new PanelConfig()) { Stage = Stage.CodeReview }, index >= 0 ? [.. Journal.Take(index)] : [.. Journal.Where(entry => entry.StartedUtc < round.StartedUtc)])
        {
            OpenedUtc = OpenedUtc,
        };

    /// <summary>The round's own record in the journal, or one rebuilt from its row when the journal does not list it.</summary>
    public RoundRecord RecordOf(StoredRound round, int index) => index >= 0 ? Journal[index] : FromDatabase[Rounds.IndexOf(round)];
}

/// <summary>
/// What the database and the session files hold, read once at the start of a run: every plan and code round with its gate, in the order the gate
/// ran them (E7.S2). The connection is the mode's READ-ONLY one; the session files are read through <see cref="SessionStore"/> and never written
/// here.
/// </summary>
/// <remarks>
/// <para><b>The open time comes from the session FILE</b> (<c>PersistedSession.OpenedUtc</c>, what <c>open</c> stamped), never from
/// <c>sessions.opened_utc</c>: that column is the clock of the FIRST PROJECTION, later than <c>open</c> by at least one round. Without the file
/// the open time is the default and the mapping reads round 1 as E4's own <c>unobserved: no open time</c>.</para>
/// <para><c>SessionStore.Load</c> answers null for a missing file AND for a torn or busy one, so a gone file is decided by
/// <c>SessionStore.Exists</c> and a file that exists but does not load is <see cref="SessionFileState.Unreadable"/> — deferred, never recorded.</para>
/// </remarks>
internal sealed class StoredHistory(SqliteConnection db, SessionStore store)
{
    private const string Rounds = """
        SELECT r.id, r.session_id, r.stage, r.number, r.status, r.verdict, r.gating, r.caller, r.caller_vendor, r.authors_session,
               r.started_utc, r.completed_utc, r.authors_state, r.authors_id_source, r.authors_reader, r.authors_attribution,
               r.size_state, r.delta_state, r.delta_from, r.base_ref, r.head_sha, r.round_key, s.repo_path, s.branch
          FROM rounds r JOIN sessions s ON s.id = r.session_id
         WHERE r.stage IN ('PlanReview', 'CodeReview')
         ORDER BY r.session_id, r.started_utc, r.id
        """;

    private sealed record Row(StoredRound Stored, RoundRecord Record, string RepoPath, string Branch);

    /// <summary>Every gate session that holds a plan or code round, in a fixed order (by its first round's start, then its id).</summary>
    public ImmutableArray<StoredGate> Gates() =>
    [
        .. ReadRows().GroupBy(row => row.Stored.SessionId, StringComparer.Ordinal)
            .Select(Gate)
            .OrderBy(gate => gate.Rounds[0].StartedUtc)
            .ThenBy(gate => gate.SessionId, StringComparer.Ordinal),
    ];

    private StoredGate Gate(IGrouping<string, Row> rows)
    {
        var first = rows.First();
        var (state, file) = FileOf(first);
        var present = state == SessionFileState.Present;

        return new StoredGate(
            rows.Key, first.RepoPath, first.Branch, state, present ? file!.OpenedUtc : default,
            present ? [.. file!.Rounds] : [.. rows.Select(row => row.Record)],
            [.. rows.Select(row => row.Stored)],
            [.. rows.Select(row => row.Record)]);
    }

    private (SessionFileState State, PersistedSession? File) FileOf(Row row)
    {
        if (!store.Exists(row.RepoPath, row.Branch))
        {
            return (SessionFileState.Gone, null);
        }

        var file = store.Load(row.RepoPath, row.Branch);

        return file is null ? (SessionFileState.Unreadable, null)
            : file.State.SessionId != row.Stored.SessionId ? (SessionFileState.Replaced, null)
            : (SessionFileState.Present, file);
    }

    private List<Row> ReadRows()
    {
        using var read = db.CreateCommand();
        read.CommandText = Rounds;
        using var rows = read.ExecuteReader();
        var all = new List<Row>();
        while (rows.Read())
        {
            all.Add(RowOf(rows));
        }

        return all;
    }

    private static Row RowOf(SqliteDataReader rows)
    {
        var started = Instant(Columns.Text(rows, "started_utc"));
        var stored = new StoredRound(
            Columns.Id(rows, "id"), Columns.Text(rows, "session_id"), Columns.Text(rows, "stage"), Columns.Number(rows, "number"),
            Columns.Text(rows, "status"), Columns.Text(rows, "verdict"), Columns.Text(rows, "caller"), Columns.Text(rows, "caller_vendor"),
            Columns.Text(rows, "authors_session"), started, Columns.Text(rows, "authors_state"), Columns.Text(rows, "authors_id_source"),
            Columns.Number(rows, "authors_reader"), Columns.Number(rows, "authors_attribution"), Columns.Text(rows, "size_state"),
            Columns.Text(rows, "base_ref"), Columns.Text(rows, "head_sha"));
        var size = RebuiltSize(stored, Columns.Text(rows, "delta_state"), Columns.Text(rows, "delta_from"));
        var record = Rebuild(stored, Columns.Text(rows, "round_key"), Columns.Number(rows, "gating"), Columns.Text(rows, "completed_utc")) with { Size = size };

        return new Row(stored, record, Columns.Text(rows, "repo_path"), Columns.Text(rows, "branch"));
    }

    /// <summary>
    /// A round as the journal would hold it, rebuilt from its row: enough for the timeline (stage, number, start, completion, status, verdict, the
    /// caller's vendor) and for the delta's predecessor (the sha). Nothing the engine never needed is invented.
    /// </summary>
    internal static RoundRecord Rebuild(StoredRound round, string key, int gating, string completedUtc) =>
        new(round.Stage, round.Number, round.Verdict, gating, string.Empty, Instant(completedUtc))
        {
            Status = round.Status,
            StartedUtc = round.StartedUtc,
            Sha = round.HeadSha,
            Key = key,
            Caller = new CallerDeclaration(Vendor: round.CallerVendor),
        };

    /// <summary>
    /// A round's size as the predecessor rule reads it — only <c>Whole.From</c> — rebuilt from its row: the base the whole scope was measured against
    /// IS <c>rounds.base_ref</c> (E2 §10 point 9). Null when the round recorded no size: the delta then reads <c>unavailable: base unknown</c>.
    /// </summary>
    private static RoundSize? RebuiltSize(StoredRound round, string deltaState, string deltaFrom) =>
        round.SizeState.Length == 0 ? null : new RoundSize(new ScopeSize(round.SizeState, round.BaseRef, []), new ScopeSize(deltaState, deltaFrom, []));

    /// <summary>The column's own <c>"O"</c> stamp, read as UTC; an empty or unreadable one is <c>default</c> — a round with no recorded instant.</summary>
    private static DateTime Instant(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc) : default;
}
