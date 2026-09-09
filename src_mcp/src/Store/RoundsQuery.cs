using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>One finding as the log page shows it — what it said, and what was decided about it.</summary>
public sealed record LoggedFinding(
    int Ordinal,
    string Severity,
    string Category,
    string File,
    int Line,
    string Title,
    string Why,
    string Fix,
    string Role,
    bool IsGating,
    string Providers,
    string Resolution,
    string Reason,
    bool ReRaised);

/// <summary>One round, with the findings it produced. Keyed the way the page keys its own rows.</summary>
public sealed record LoggedRound(
    string RepoPath,
    string Branch,
    string Stage,
    int Number,
    string StartedUtc,
    int Accepted,
    int Rejected,
    /// <summary>
    /// Which session it belonged to — the half of the key that makes it unique.
    /// </summary>
    /// <remarks>
    /// Round numbers restart per session, so one repository and branch reviewed twice has two
    /// "CodeReview round 1" records; without this the second overwrote the first and a row showed
    /// another review's findings. Found by the code gate, two vendors independently.
    /// </remarks>
    string SessionId,
    IReadOnlyList<LoggedFinding> Findings,
    /// <summary>Where the next page starts: <c>started_utc|id</c>, the pair the list is ordered by.</summary>
    string Cursor = "",
    /// <summary>
    /// HOW MANY findings this round produced, when the list no longer carries them.
    /// </summary>
    /// <remarks>
    /// One integer per row, and it is load-bearing: the page tells a gate still open from a round
    /// that raised nothing, and the only evidence for the second is that no finding exists. Without
    /// this the list could no longer say which, and every clean round would read as awaiting a
    /// resolve nobody owes.
    /// </remarks>
    int FoundCount = 0);

/// <summary>How often one kind of thing was accepted — a category, a role, or a vendor.</summary>
public sealed record BlindSpot(string Kind, string Name, int Accepted, int Total);

/// <summary>What the whole table adds up to, counted by SQL rather than by the page.</summary>
public sealed record LoggedTotals(
    int Rounds = 0,
    int Findings = 0,
    int Accepted = 0,
    int Rejected = 0,
    int Gating = 0,
    long TokensIn = 0,
    long TokensOut = 0,
    double CostUsd = 0);

/// <summary>One round's findings, and whether the database has ever heard of that round.</summary>
public sealed record LoggedRoundFindings(bool Known, IReadOnlyList<LoggedFinding> Findings);

/// <summary>What the page asks for in one go.</summary>
public sealed record LoggedLog(
    IReadOnlyList<LoggedRound> Rounds,
    IReadOnlyList<BlindSpot> BlindSpots,
    IReadOnlyList<LoggedFinding> Defended,
    LoggedTotals Totals);

/// <summary>
/// Reading the rounds database, for the page and for the two questions it exists to answer.
/// </summary>
/// <remarks>
/// <para><b>Why the server reads it and not the extension.</b> The extension would need SQLite of its
/// own — a WebAssembly build in the VSIX, or a native module per platform — to ask questions of a
/// file this binary already has open and whose schema it owns. One read-only mode here costs sixty
/// lines and no dependency, and it keeps every query beside the table it queries.</para>
/// <para><b>The two questions</b> come from what the data is for (operator, 2026-09-05): finding the
/// blind spots in an AI's own reasoning. An ACCEPTED finding is something the caller had not seen
/// and then agreed was worth having, so accepted-by-category, by-role and by-vendor is the shape of
/// what it habitually misses. A REJECTED finding that a later round raised again is a disagreement
/// the caller is defending, which is the more interesting kind and a much shorter list.</para>
/// </remarks>
public static class RoundsQuery
{
    /// <summary>How many rounds the page is given, newest first.</summary>
    /// <remarks>
    /// Two hundred, from the operator on 2026-09-09: "у нас есть пагинаций. 200 на стр достаточно."
    /// It used to be three hundred and every one of them carried its findings.
    /// </remarks>
    public const int DefaultLimit = 200;

    /// <summary>The most a caller may ask for in one page, however it asks.</summary>
    /// <remarks>
    /// The CLI is a boundary and a boundary that trusts its input is not one — the plan round said
    /// so. A limit is clamped rather than refused: somebody typing `--limit 100000` wants as much as
    /// they can have, and an error would answer a reasonable wish with nothing.
    /// </remarks>
    public const int MaxLimit = 1000;

    /// <summary>The separator inside a page cursor. Never appears in an ISO instant or in an id.</summary>
    private const char CursorSeparator = '|';

    /// <summary>
    /// One round's findings, asked for when a row is OPENED.
    /// </summary>
    /// <remarks>
    /// <para>The reason this exists is a measurement: `--log --limit 300` answered <b>3.83 MB</b>, of
    /// which the 236 round rows were <b>0.05 MB</b>. The other 98.7 % was 3 484 findings shipped for
    /// every round although the page draws them one at a time, on a click.</para>
    /// <para><b>Known is not the same as empty</b>, and the page's states depend on the difference: a
    /// round the database holds and that found nothing is CLEAN, a round it has never heard of had
    /// its findings recorded nowhere. An empty list cannot say which, so this says it instead of
    /// leaving the renderer to guess. (Plan round, codex — Blocking.)</para>
    /// </remarks>
    public static LoggedRoundFindings FindingsOf(string dataDir, string sessionId, string stage, int number)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return new LoggedRoundFindings(false, []);
        }

        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();

        var id = RoundId(db, sessionId, stage, number);

        return id is null ? new LoggedRoundFindings(false, []) : new LoggedRoundFindings(true, FindingsFor(db, id.Value));
    }

    /// <summary>The row id of one round, or nothing when the database has never heard of it.</summary>
    private static long? RoundId(SqliteConnection db, string sessionId, string stage, int number)
    {
        using var read = db.CreateCommand();
        read.CommandText =
            "SELECT id FROM rounds WHERE session_id = $session AND stage = $stage AND number = $number";
        read.Parameters.AddWithValue("$session", sessionId);
        read.Parameters.AddWithValue("$stage", stage);
        read.Parameters.AddWithValue("$number", number);

        return read.ExecuteScalar() is long id ? id : null;
    }

    /// <summary>
    /// A page of the log.
    /// </summary>
    /// <param name="withFindings">
    /// The OLD shape, for an extension that predates paging: every listed round carries its findings
    /// inline, as `--log` answered until 2026-09-09. A new binary must keep answering it, because the
    /// two halves of this product update separately and an old extension reading a new binary would
    /// otherwise show a log with every findings list silently empty.
    /// </param>
    public static LoggedLog Read(
        string dataDir, int limit = DefaultLimit, string before = "", bool withFindings = false)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return new LoggedLog([], [], [], new LoggedTotals());
        }

        // Read-only, unpooled, and with a busy timeout rather than the default of none: a reader
        // never blocks a WAL writer, but the open itself can still meet a checkpoint.
        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();

        return new LoggedLog(
            Rounds(db, Math.Clamp(limit, 1, MaxLimit), Cursor(before), withFindings),
            BlindSpots(db),
            Defended(db),
            Totals(db));
    }

    /// <summary>
    /// A page cursor as the two values the list is ordered by, or nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>A pair, not a timestamp.</b> `started_utc` is not unique — a review running three
    /// rounds in a burst writes three rows in one second — so a cursor that is only the time either
    /// skips the rest of that second or hands it back for ever. The id breaks the tie, and the
    /// ordering carries it too.</para>
    /// <para>Anything unreadable is treated as ABSENT rather than refused: an unreadable cursor asks
    /// for the first page, which is a page. Refusing would answer a typo with an empty log.</para>
    /// </remarks>
    private static (string StartedUtc, long Id)? Cursor(string before)
    {
        var at = before.IndexOf(CursorSeparator);

        return at > 0 && long.TryParse(before[(at + 1)..], out var id)
            ? (before[..at], id)
            : null;
    }

    private static List<LoggedRound> Rounds(
        SqliteConnection db, int limit, (string StartedUtc, long Id)? before, bool withFindings)
    {
        // ONE query for the whole page's findings, not one per row. The paged shape asks for none of
        // them; the old shape asks for all of them, and a thousand round-trips where one grouped
        // read would do is what the code round caught here. (codex, Major.)
        var inline = withFindings ? FindingsByRound(db, limit, before) : [];
        using var read = db.CreateCommand();
        // Keyed, never OFFSET. Rounds are inserted at the TOP of this ordering, so a round finishing
        // while somebody is on page two shifts every later page by one and a row is seen twice or
        // never. (Plan round, local — Major.)
        read.CommandText = """
            SELECT r.id, s.repo_path, s.branch, r.stage, r.number, r.started_utc, r.accepted, r.rejected,
                   r.session_id, (SELECT COUNT(*) FROM findings f WHERE f.round_id = r.id)
            FROM rounds r JOIN sessions s ON s.id = r.session_id
            WHERE $unbounded = 1
               OR r.started_utc < $started
               OR (r.started_utc = $started AND r.id < $id)
            ORDER BY r.started_utc DESC, r.id DESC LIMIT $limit
            """;
        read.Parameters.AddWithValue("$unbounded", before is null ? 1 : 0);
        read.Parameters.AddWithValue("$started", before?.StartedUtc ?? string.Empty);
        read.Parameters.AddWithValue("$id", before?.Id ?? 0L);
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();
        var rounds = new List<LoggedRound>();
        while (rows.Read())
        {
            var id = rows.GetInt64(0);
            rounds.Add(new LoggedRound(
                rows.GetString(1),
                rows.GetString(2),
                rows.GetString(3),
                rows.GetInt32(4),
                rows.GetString(5),
                rows.GetInt32(6),
                rows.GetInt32(7),
                rows.GetString(8),
                inline.TryGetValue(id, out var mine) ? mine : [],
                rows.GetString(5) + CursorSeparator + id,
                rows.GetInt32(9)));
        }

        return rounds;
    }

    /// <summary>
    /// What the whole table adds up to, counted where the table is.
    /// </summary>
    /// <remarks>
    /// From the operator, 2026-09-09: "суммы - скл счиатть". Two statements and one scan each,
    /// rather than the eight scalar subqueries the plan first had — five `COUNT(*) … WHERE` over one
    /// table are five passes where conditional sums are one. (Plan round, gemini.)
    /// </remarks>
    private static LoggedTotals Totals(SqliteConnection db)
    {
        using var overRounds = db.CreateCommand();
        overRounds.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(tokens_in), 0), COALESCE(SUM(tokens_out), 0),
                   COALESCE(SUM(cost_usd), 0)
            FROM rounds
            """;
        using var rounds = overRounds.ExecuteReader();
        rounds.Read();

        using var overFindings = db.CreateCommand();
        overFindings.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(resolution = 'accept'), 0), COALESCE(SUM(resolution = 'reject'), 0),
                   COALESCE(SUM(is_gating = 1), 0)
            FROM findings
            """;
        using var findings = overFindings.ExecuteReader();
        findings.Read();

        return new LoggedTotals(
            rounds.GetInt32(0),
            findings.GetInt32(0),
            findings.GetInt32(1),
            findings.GetInt32(2),
            findings.GetInt32(3),
            rounds.GetInt64(1),
            rounds.GetInt64(2),
            rounds.GetDouble(3));
    }

    /// <summary>
    /// Every finding of one PAGE of rounds, grouped — the old shape's read, and only its read.
    /// </summary>
    /// <remarks>
    /// The paged shape never calls this: it is the 3.78 MB. It exists so that a new binary asked
    /// without <c>--paged</c> answers exactly what it answered yesterday, at yesterday's cost.
    /// </remarks>
    private static Dictionary<long, List<LoggedFinding>> FindingsByRound(
        SqliteConnection db, int limit, (string StartedUtc, long Id)? before)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT f.round_id, f.ordinal, f.severity, f.category, f.file, f.line, f.title, f.why, f.fix,
                   f.role, f.is_gating, f.providers, f.resolution, f.reason, f.re_raised
            FROM findings f
            WHERE f.round_id IN (
                SELECT id FROM rounds
                WHERE $unbounded = 1
                   OR started_utc < $started
                   OR (started_utc = $started AND id < $id)
                ORDER BY started_utc DESC, id DESC LIMIT $limit)
            ORDER BY f.round_id, f.ordinal
            """;
        read.Parameters.AddWithValue("$unbounded", before is null ? 1 : 0);
        read.Parameters.AddWithValue("$started", before?.StartedUtc ?? string.Empty);
        read.Parameters.AddWithValue("$id", before?.Id ?? 0L);
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();
        var byRound = new Dictionary<long, List<LoggedFinding>>();
        while (rows.Read())
        {
            var round = rows.GetInt64(0);
            if (!byRound.TryGetValue(round, out var mine))
            {
                byRound[round] = mine = [];
            }

            mine.Add(FindingFrom(rows, 1));
        }

        return byRound;
    }

    /// <summary>One round's findings, in the order the reviewers produced them.</summary>
    private static List<LoggedFinding> FindingsFor(SqliteConnection db, long roundId)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT f.ordinal, f.severity, f.category, f.file, f.line, f.title, f.why, f.fix,
                   f.role, f.is_gating, f.providers, f.resolution, f.reason, f.re_raised
            FROM findings f WHERE f.round_id = $round ORDER BY f.ordinal
            """;
        read.Parameters.AddWithValue("$round", roundId);
        using var rows = read.ExecuteReader();
        var findings = new List<LoggedFinding>();
        while (rows.Read())
        {
            findings.Add(FindingFrom(rows, 0));
        }

        return findings;
    }

    private static LoggedFinding FindingFrom(SqliteDataReader rows, int at) =>
        new(rows.GetInt32(at),
            rows.GetString(at + 1),
            rows.GetString(at + 2),
            rows.GetString(at + 3),
            rows.GetInt32(at + 4),
            rows.GetString(at + 5),
            rows.GetString(at + 6),
            rows.GetString(at + 7),
            rows.GetString(at + 8),
            rows.GetInt32(at + 9) == 1,
            rows.GetString(at + 10),
            rows.GetString(at + 11),
            rows.GetString(at + 12),
            rows.GetInt32(at + 13) == 1);

    /// <summary>
    /// What the caller accepts, grouped three ways.
    /// </summary>
    /// <remarks>
    /// Accepted over TOTAL rather than accepted alone, because a category that produces fifty
    /// findings and gets two accepted says something different from one that produces two and gets
    /// both — and only the second is a blind spot worth acting on.
    /// </remarks>
    private static List<BlindSpot> BlindSpots(SqliteConnection db)
    {
        var spots = new List<BlindSpot>();
        spots.AddRange(GroupedBy(db, "category"));
        spots.AddRange(GroupedBy(db, "role"));
        spots.AddRange(GroupedBy(db, "providers"));

        return spots;
    }

    private static List<BlindSpot> GroupedBy(SqliteConnection db, string column)
    {
        using var read = db.CreateCommand();
        // The column is one of three names written HERE, never anything a caller sends — and it is
        // quoted anyway, so the shape cannot become an injection the day somebody makes it dynamic.
        read.CommandText = $"""
            SELECT "{column}", SUM(resolution = 'accept'), COUNT(*)
            FROM findings WHERE resolution <> '' GROUP BY "{column}" ORDER BY 2 DESC
            """;
        using var rows = read.ExecuteReader();
        var spots = new List<BlindSpot>();
        while (rows.Read())
        {
            spots.Add(new BlindSpot(column, rows.GetString(0), rows.GetInt32(1), rows.GetInt32(2)));
        }

        return spots;
    }

    /// <summary>How many defended disagreements are listed before the list says it was cut.</summary>
    private const int DefendedCap = 200;

    /// <summary>
    /// Findings a reviewer raised again over a rejection that still stood.
    /// </summary>
    /// <remarks>
    /// <para>Rejected AGAIN, not merely raised again: a finding the caller rejected once, had put to
    /// it a second time, and rejected once more. Without that condition the list also held the ones
    /// it was persuaded by — a repeat that was then ACCEPTED is the opposite of a defended
    /// disagreement, and it is the case this tab exists to tell apart. Found by the code gate.</para>
    /// <para>Capped, and the cap is REPORTED rather than silent: a list that was cut and looks whole
    /// is worse than no list, because somebody counts it later and reads the cap as the measurement.
    /// One extra row is fetched so the caller can tell "exactly two hundred" from "more than that".</para>
    /// </remarks>
    private static List<LoggedFinding> Defended(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT ordinal, severity, category, file, line, title, why, fix, role, is_gating,
                   providers, resolution, reason, re_raised
            FROM findings WHERE re_raised = 1 AND resolution = 'reject' ORDER BY id DESC LIMIT $limit
            """;
        read.Parameters.AddWithValue("$limit", DefendedCap + 1);
        using var rows = read.ExecuteReader();
        var defended = new List<LoggedFinding>();
        while (rows.Read())
        {
            defended.Add(FindingFrom(rows, 0));
        }

        return defended;
    }
}
