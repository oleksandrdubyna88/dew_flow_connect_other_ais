using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The read behind <c>--round-authors</c> (E6.S1 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md</c>): every round of the
/// gate sessions the window touches, as text-free facts, from ONE read-only open of <c>coai.db</c>.
/// </summary>
/// <remarks>
/// <para><b>Read-only, and one snapshot.</b> The open is <see cref="RoundCounts"/>'s — <c>Mode=ReadOnly</c>, unpooled — and every query below,
/// <see cref="RoundChangesLog.Read"/> included, runs inside ONE deferred read transaction, so a <c>RecordRound</c> landing between two of them
/// (it writes a round's rows in one transaction) is seen whole or not at all: the rollback-journal reader holds the shared lock, the writer
/// waits for it. A database from before step 24 (no <c>rounds.authors_state</c>) reads every round <see cref="AuthorsStateKind.NotRecorded"/>;
/// before step 22 (no <c>rounds.size_state</c>) it has no size and no reviewer effort; before step 20 no reviewer model. Each is ASKED
/// (<see cref="RoundsQuery.HasColumn"/>, never a version guess), and a missing file is an empty answer that creates nothing.</para>
/// <para><b>Which rounds, which facts.</b> (a) the rounds of the asked stage that started in <c>[since, until)</c> name the touched sessions; (b) EVERY
/// round of those sessions, any stage, any time, comes back with <c>InWindow</c> set from (a) — D1 and D2 need them for cohorts and contributors.
/// Reviewers, findings and size are read for the in-window rounds only; a context round carries the empty value of each, since the report never
/// counts it. Ids go in as ONE bound JSON array (<see cref="PageIds.Bind"/>), so no thousand of rounds reaches an SQLite variable limit.</para>
/// <para><b>No text column is selected, ever</b> — not <c>plan_text</c>, <c>agent_log</c>, <c>subject</c>, <c>note</c>, <c>commands</c>,
/// <c>plan_source</c>, nor any finding's <c>title</c>, <c>why</c>, <c>fix</c>, <c>reason</c>, <c>file</c>, <c>category</c>, <c>security_evidence</c>.
/// Names of tables and columns are literals; every value is a parameter.</para>
/// </remarks>
public static class RoundAuthorsQuery
{
    private const string RoundHead = """
        SELECT r.id, r.session_id, s.repo_path, s.branch, r.stage, r.number, r.status, r.verdict, r.started_utc, r.gating,
               (r.accepted <> -1) AS resolved,
               (r.stage = $stage AND r.started_utc >= $since AND r.started_utc < $until) AS in_window,
        """;

    private const string AuthorsPresent = """
               r.authors_state, r.authors_session, r.uncovered_writes, r.ambiguous_agents, r.ambiguous_launches, r.ambiguous_turns,
               r.ambiguous_input_tokens, r.ambiguous_output_tokens, r.ambiguous_cache_creation_tokens, r.ambiguous_cache_read_tokens
        """;

    private const string AuthorsAbsent = """
               '' AS authors_state, '' AS authors_session, 0 AS uncovered_writes, 0 AS ambiguous_agents, 0 AS ambiguous_launches,
               0 AS ambiguous_turns, 0 AS ambiguous_input_tokens, 0 AS ambiguous_output_tokens, 0 AS ambiguous_cache_creation_tokens,
               0 AS ambiguous_cache_read_tokens
        """;

    private const string RoundTail = """
          FROM rounds r JOIN sessions s ON s.id = r.session_id
         WHERE r.session_id IN (SELECT session_id FROM rounds WHERE stage = $stage AND started_utc >= $since AND started_utc < $until)
         ORDER BY r.session_id, r.started_utc, r.id
        """;

    private sealed record Window(string Stage, string Since, string Until);

    private sealed record RoundRow(
        long Id, string SessionId, string RepoPath, string Branch, string Stage, int Number, string Status, string Verdict, string StartedUtc,
        int Gating, bool Resolved, bool InWindow, string AuthorsState, string AuthorsSession, int Uncovered, AmbiguousFacts Ambiguous);

    private sealed record Parts(
        ILookup<long, ReviewerFact> Reviewers,
        IReadOnlyDictionary<long, FindingFacts> Findings,
        IReadOnlyDictionary<long, RoundSize> Sizes,
        ILookup<long, StoredAuthorRow> AuthorRows);

    /// <summary>The rounds of every session the window touches; empty when the database does not exist yet.</summary>
    public static IReadOnlyList<RoundFacts> Read(string dataDir, string stage, string sinceUtc, string untilUtc) =>
        Read(dataDir, stage, sinceUtc, untilUtc, () => { });

    /// <param name="between">A test seam, a no-op in production: called after the rounds are read and before anything else, so a test can write in the middle of the snapshot.</param>
    internal static IReadOnlyList<RoundFacts> Read(string dataDir, string stage, string sinceUtc, string untilUtc, Action between)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return [];
        }

        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();
        using var snapshot = db.BeginTransaction(deferred: true);
        var facts = Gather(db, new Window(stage, sinceUtc, untilUtc), between);
        snapshot.Commit();

        return facts;
    }

    private static List<RoundFacts> Gather(SqliteConnection db, Window window, Action between)
    {
        var rows = Rounds(db, window);
        between();
        var parts = PartsOf(db, rows);

        return [.. rows.Select(row => FactsOf(row, parts))];
    }

    private static Parts PartsOf(SqliteConnection db, List<RoundRow> rows)
    {
        var inWindow = rows.Where(row => row.InWindow).Select(row => row.Id).ToList();
        var withState = rows.Where(row => row.AuthorsState.Length > 0).Select(row => row.Id).ToList();

        return new Parts(Reviewers(db, inWindow), Findings(db, inWindow, rows), RoundChangesLog.Read(db, inWindow), AuthorRows(db, withState));
    }

    private static List<RoundRow> Rounds(SqliteConnection db, Window window)
    {
        var authors = RoundsQuery.HasColumn(db, "rounds", "authors_state");

        return Select(
            db,
            RoundHead + (authors ? AuthorsPresent : AuthorsAbsent) + RoundTail,
            read =>
            {
                read.Parameters.AddWithValue("$stage", window.Stage);
                read.Parameters.AddWithValue("$since", window.Since);
                read.Parameters.AddWithValue("$until", window.Until);
            },
            RoundOf);
    }

    private static RoundRow RoundOf(SqliteDataReader rows) => new(
        Columns.Id(rows, "id"), Columns.Text(rows, "session_id"), Columns.Text(rows, "repo_path"), Columns.Text(rows, "branch"),
        Columns.Text(rows, "stage"), Columns.Number(rows, "number"), Columns.Text(rows, "status"), Columns.Text(rows, "verdict"),
        Columns.Text(rows, "started_utc"), Columns.Number(rows, "gating"), Columns.Number(rows, "resolved") == 1, Columns.Number(rows, "in_window") == 1,
        Columns.Text(rows, "authors_state"), Columns.Text(rows, "authors_session"), Columns.Number(rows, "uncovered_writes"), AmbiguousOf(rows));

    /// <summary>The seven ambiguous columns as facts — E5's <c>-1</c> (an unknown total) becomes <see cref="AmbiguousFacts.Unknown"/>, so no minus one reaches a sum (D17).</summary>
    private static AmbiguousFacts AmbiguousOf(SqliteDataReader rows) =>
        Columns.Number(rows, "ambiguous_agents") < 0
            ? AmbiguousFacts.Unknown
            : new AmbiguousFacts(
                true, Columns.Number(rows, "ambiguous_agents"), Columns.Number(rows, "ambiguous_launches"), Columns.Number(rows, "ambiguous_turns"),
                Columns.Id(rows, "ambiguous_input_tokens"), Columns.Id(rows, "ambiguous_output_tokens"),
                Columns.Id(rows, "ambiguous_cache_creation_tokens"), Columns.Id(rows, "ambiguous_cache_read_tokens"));

    private static ILookup<long, ReviewerFact> Reviewers(SqliteConnection db, List<long> ids)
    {
        var effort = RoundsQuery.HasColumn(db, "reviewers", "effort");
        var model = RoundsQuery.HasColumn(db, "reviewers", "model") ? "v.model" : "''";
        var sql = $"SELECT v.round_id, v.provider, v.status, {model} AS model, {(effort ? "v.effort" : "''")} AS effort "
            + "FROM reviewers v WHERE v.round_id IN (SELECT value FROM json_each($ids)) ORDER BY v.round_id, v.rowid";

        return Select(db, sql, read => PageIds.Bind(read, ids), rows => (
                Columns.Id(rows, "round_id"),
                new ReviewerFact(Columns.Text(rows, "provider"), Columns.Text(rows, "model"), Columns.Text(rows, "effort"), effort, Columns.Text(rows, "status"))))
            .ToLookup(row => row.Item1, row => row.Item2);
    }

    private sealed record Counted(long RoundId, int All, int Severe, int ReRaised, int Accepted, int Unresolved);

    private static Dictionary<long, FindingFacts> Findings(SqliteConnection db, List<long> ids, List<RoundRow> rows)
    {
        var grouped = Select(
            db,
            """
            SELECT round_id, COUNT(*) AS n, COALESCE(SUM(is_gating), 0) AS severe, COALESCE(SUM(re_raised), 0) AS re_raised,
                   COALESCE(SUM(resolution = 'accept'), 0) AS accepted, COALESCE(SUM(resolution = ''), 0) AS unresolved
            FROM findings WHERE round_id IN (SELECT value FROM json_each($ids)) GROUP BY round_id
            """,
            read => PageIds.Bind(read, ids),
            found => new Counted(
                Columns.Id(found, "round_id"), Columns.Number(found, "n"), Columns.Number(found, "severe"), Columns.Number(found, "re_raised"),
                Columns.Number(found, "accepted"), Columns.Number(found, "unresolved")))
            .ToDictionary(found => found.RoundId);

        return rows.Where(row => row.InWindow).ToDictionary(row => row.Id, row => FindingFactsOf(row, grouped));
    }

    private static FindingFacts FindingFactsOf(RoundRow row, Dictionary<long, Counted> grouped) =>
        grouped.TryGetValue(row.Id, out var found)
            ? new FindingFacts(found.All, found.Severe, row.Gating, found.ReRaised, found.Accepted, found.Unresolved, row.Resolved)
            : FindingFacts.None(row.Gating, row.Resolved);

    private static ILookup<long, StoredAuthorRow> AuthorRows(SqliteConnection db, List<long> ids)
    {
        if (ids.Count == 0 || !RoundsQuery.HasColumn(db, "rounds", "authors_state"))
        {
            return Enumerable.Empty<(long, StoredAuthorRow)>().ToLookup(row => row.Item1, row => row.Item2);
        }

        return Select(
                db,
                """
                SELECT round_id, ordinal, who, agent, requested, model, effort, agent_type, association, window, launches, turns, write_calls
                FROM round_authors WHERE round_id IN (SELECT value FROM json_each($ids)) ORDER BY round_id, ordinal
                """,
                read => PageIds.Bind(read, ids),
                rows => (Columns.Id(rows, "round_id"), AuthorRowOf(rows)))
            .ToLookup(row => row.Item1, row => row.Item2);
    }

    private static StoredAuthorRow AuthorRowOf(SqliteDataReader rows) => new(
        Columns.Number(rows, "ordinal"), Columns.Text(rows, "who"), Columns.Text(rows, "agent"), Columns.Text(rows, "requested"),
        Columns.Text(rows, "model"), Columns.Text(rows, "effort"), Columns.Text(rows, "agent_type"), Columns.Text(rows, "association"),
        Columns.Text(rows, "window"), Columns.Number(rows, "launches"), Columns.Number(rows, "turns"), Columns.Number(rows, "write_calls"));

    private static RoundFacts FactsOf(RoundRow row, Parts parts) => new(
        row.Id, row.SessionId, row.RepoPath, row.Branch, row.Stage, row.Number, row.Status, row.Verdict, row.StartedUtc, row.InWindow,
        AuthorsOf(row, parts),
        parts.Sizes.TryGetValue(row.Id, out var size) ? new SizeFacts(true, size) : SizeFacts.NotRecorded,
        [.. parts.Reviewers[row.Id]],
        parts.Findings.TryGetValue(row.Id, out var found) ? found : FindingFacts.None(row.Gating, row.Resolved));

    /// <summary>
    /// A round's authors: <c>NotRecorded</c> for an empty state AND for a state the shared rule refuses — judged per ROW, never by a catch
    /// around a query, which would hide <i>no such column</i> (the defect the <c>HasColumn</c> guard exists to prevent).
    /// </summary>
    private static AuthorsFacts AuthorsOf(RoundRow row, Parts parts) =>
        row.AuthorsState.Length > 0 && AuthorsSnapshot.IsWellFormed(row.AuthorsState)
            ? new AuthorsFacts(KindOf(row.AuthorsState), ReasonsOf(row.AuthorsState), row.AuthorsSession, [.. parts.AuthorRows[row.Id]], row.Ambiguous, row.Uncovered)
            : AuthorsFacts.NotRecorded;

    private static AuthorsStateKind KindOf(string state) =>
        state[..(state.IndexOf(':') is var at and >= 0 ? at : state.Length)] switch
        {
            ObservationKinds.Observed => AuthorsStateKind.Observed,
            ObservationKinds.Partial => AuthorsStateKind.Partial,
            _ => AuthorsStateKind.Unobserved,
        };

    private static string[] ReasonsOf(string state) =>
        state.IndexOf(": ", StringComparison.Ordinal) is var at and >= 0 ? state[(at + 2)..].Split(", ") : [];

    /// <summary>One query, its parameters bound by <paramref name="bind"/>, every row mapped.</summary>
    private static List<T> Select<T>(SqliteConnection db, string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> map)
    {
        using var read = db.CreateCommand();
        read.CommandText = sql;
        bind(read);
        using var rows = read.ExecuteReader();
        var found = new List<T>();
        while (rows.Read())
        {
            found.Add(map(rows));
        }

        return found;
    }
}
