using Microsoft.Data.Sqlite;
using System.Text.Json;

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
    int FoundCount = 0,
    /// <summary>
    /// Which AI asked for this round, and which model it declared — issue #174.
    /// </summary>
    /// <remarks>
    /// <para>The same shape the session file uses rather than four loose strings beside each other,
    /// so one reader can handle both projections and the `--log` JSON does not disagree with the
    /// file it projects. (gemini, second code round — the house rule against primitive obsession
    /// applied to a wire contract.)</para>
    /// <para>Null for a round recorded before the columns existed: it was never asked, which is a
    /// different fact from a caller that could not be identified.</para>
    /// </remarks>
    Server.CallerDeclaration? Caller = null,

    /// <summary>
    /// How many findings this round handed back that the caller had already ACCEPTED.
    /// </summary>
    /// <remarks>
    /// Story 6's counter, and <c>-1</c> means the projection could not answer — the same convention
    /// <c>accepted</c>/<c>rejected</c> use, because a round nobody could measure must not read as a
    /// round where nothing survived. Carried on the log so the phase-2 question can be asked of
    /// `--log` rather than of SQL; no page shows it yet, deliberately, since what it is for is a
    /// decision about whether an automatic consultation should fire at all.
    /// </remarks>
    int ConsultMissed = -1,

    /// <summary>
    /// When the caller LAST decided something about this round, or empty when it never has.
    /// </summary>
    /// <remarks>
    /// <para>The stamp has been written on every finding since <c>resolve</c> was first implemented
    /// and read by nothing at all. It is the second half of what a round costs: <c>started_utc</c>
    /// to <c>completed_utc</c> is the reviewers running, and this is how long the deciding took.</para>
    /// <para>The LAST decision, because one <c>resolve</c> call stamps every finding it touches with
    /// one instant — so for the ordinary round this is that call, and for one returned to later it
    /// is the later visit. Empty rather than null for a round nobody has decided, and for a round
    /// with no findings at all: <c>MAX()</c> over no rows is SQL <c>NULL</c>, which is not a string.
    /// (Code round, codex.)</para>
    /// </remarks>
    string ResolvedUtc = "",

    /// <summary>
    /// Why a SKIPPED round did not run, with a repeat count when consecutive identical skips
    /// coalesced into this row — empty for every round that ran, and for every row a database from
    /// before schema step 16 holds (S2.1 of the feature-review plan).
    /// </summary>
    string Note = "");


/// <summary>
/// One consultation as the log page lists it: who asked, who answered, and how it ended.
/// </summary>
/// <remarks>
/// The turn-by-turn conversation is deliberately NOT here. The sidebar shows a consultation while it
/// is running, from the record file; this list is the history, and what a person asks of history is
/// what it was about, what it cost and whether it was worth it. The advice in force is carried
/// because that is the one line worth reading back.
/// </remarks>
public sealed record LoggedConsultation(
    string Id,
    string CallerKind,
    string RepoPath,
    string Branch,
    string Vendor,
    string Model,
    int Turns,
    string Status,
    string Reason,
    /// <summary>How it ended. Empty for a row written before the column existed — not a verdict.</summary>
    string Outcome,
    /// <summary>Who said so — <c>caller</c>, <c>person</c>, the server's own <c>server</c>, or nobody.</summary>
    string OutcomeBy,
    string StartedUtc,
    string EndedUtc,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Problem,
    string Advice,
    string Alert,
    /// <summary>What it was FOR — <c>stuck</c>, <c>cadence</c> or <c>risk</c>; <c>stuck</c> for a row from before the kinds.</summary>
    string Kind = "stuck",
    /// <summary>The plan a cadence or risk consultation was about, as the caller wrote it; empty for a stuck one.</summary>
    string Plan = "",
    /// <summary>The group (<c>4-6</c>) or the item (<c>7/7.2</c>) it covered; empty for a stuck one.</summary>
    string Epics = "");

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
public sealed record LoggedRoundFindings(bool Known, IReadOnlyList<LoggedFinding> Findings, RoundOrders? Orders = null);

/// <summary>What a round ordered its caller to do, and the size it measured (issue #131).</summary>
/// <remarks>
/// Absent from a <see cref="LoggedRoundFindings"/> when the round was recorded before the columns
/// existed, or by a database not yet migrated: that is NOT RECORDED, a different fact from a round that
/// gave no orders, which carries an empty list.
/// </remarks>
public sealed record RoundOrders(IReadOnlyList<string> Commands, string PlanShape);

/// <summary>One round of a BATCH answer: the key it was asked for, and what was found.</summary>
/// <remarks>
/// The key is echoed back rather than left to positional pairing. A caller matching an answer to a
/// question by index has to trust that nothing was dropped or reordered, and the one thing a batch
/// read must never do is attach one round's findings to another round's row.
/// </remarks>
public sealed record LoggedRoundOfMany(
    string SessionId,
    string Stage,
    int Number,
    bool Known,
    IReadOnlyList<LoggedFinding> Findings);

/// <summary>What a batch read answers: one entry per round asked about, in the order asked.</summary>
public sealed record LoggedManyFindings(IReadOnlyList<LoggedRoundOfMany> Rounds);

/// <summary>One round a batch read is asked about.</summary>
public sealed record RoundKeyAsked(string SessionId, string Stage, int Number);

/// <summary>What the page asks for in one go.</summary>
public sealed record LoggedLog(
    IReadOnlyList<LoggedRound> Rounds,
    IReadOnlyList<BlindSpot> BlindSpots,
    IReadOnlyList<LoggedFinding> Defended,
    LoggedTotals Totals)
{
    /// <summary>
    /// The consultations, newest first. Defaulted, and that is the compatibility promise: an
    /// extension too old to know about them reads a list it ignores, and a binary too old to have
    /// the table answers none — an absent list is EMPTY on both sides, never an error.
    /// </summary>
    public IReadOnlyList<LoggedConsultation> Consultations { get; init; } = [];

    /// <summary>
    /// The instant the blind spots and the defended list were counted from, or empty for all time.
    /// </summary>
    public string Since { get; init; } = string.Empty;
}

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

    /// <summary>
    /// What a caller that cannot page gets when it names no limit.
    /// </summary>
    /// <remarks>
    /// Three hundred, unchanged since the log page shipped. An extension too old to send `--paged`
    /// is also too old to ask for a second page, so shrinking ITS default to 200 would simply have
    /// taken a hundred rounds off the only list it can show. (CodeRabbit, on the pull request.)
    /// </remarks>
    public const int LegacyLimit = 300;

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
        // NO fallback for a missing file, exactly as the batch read has none. This used to answer
        // `Known: false`, which the mode turns into exit 69 — "its findings were never recorded" —
        // a claim about content nobody could read, because the file was not there and nothing was
        // asked of anything. The operator ruled on 2026-09-14 that the two reads align on the
        // honest answer and that masking a database failure as "not recorded" is unacceptable even
        // as the older behaviour. `Open` throws; the mode answers 74; 69 keeps the only meaning it
        // can support, which is an open database with no such round.
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();

        var id = RoundId(db, sessionId, stage, number);

        return id is null
            ? new LoggedRoundFindings(false, [])
            : new LoggedRoundFindings(true, FindingsFor(db, id.Value), OrdersOf(db, id.Value));
    }

    /// <summary>
    /// What the round ordered — or nothing when it was never written down (issue #131).
    /// </summary>
    /// <remarks>
    /// Asked of the schema first, because this read is read-only and a database last written by an
    /// older binary has no such column. The empty string is the column's default and means the same
    /// thing: a round from before the orders were recorded.
    /// </remarks>
    private static RoundOrders? OrdersOf(SqliteConnection db, long roundId)
    {
        if (!HasColumn(db, "rounds", "commands"))
        {
            return null;
        }
        using var read = db.CreateCommand();
        read.CommandText = "SELECT commands, plan_shape FROM rounds WHERE id = $id";
        read.Parameters.AddWithValue("$id", roundId);
        using var row = read.ExecuteReader();

        return row.Read() ? OrdersFrom(Text(row, "commands"), Text(row, "plan_shape")) : null;
    }

    /// <summary>The stored pair as orders, or nothing for a round that recorded none — or unreadable text.</summary>
    internal static RoundOrders? OrdersFrom(string commands, string planShape)
    {
        try
        {
            return commands.Length == 0
                ? null
                : new RoundOrders(
                    JsonSerializer.Deserialize(commands, Server.ServerJsonContext.Default.ListString) ?? [],
                    planShape);
        }
        catch (JsonException)
        {
            // A column this binary wrote and cannot read back is a defect, but not one worth the
            // round's findings: the page shows the findings and says nothing about orders.
            return null;
        }
    }

    /// <summary>
    /// The findings of MANY rounds, in ONE open of the database.
    /// </summary>
    /// <remarks>
    /// <para>The point of this is the process, not the query: the extension spawned this binary once
    /// per round, and a person exporting two hundred rounds paid two hundred process start-ups and
    /// two hundred opens of the same file. This opens it once.</para>
    /// <para>Every round asked about gets an entry, in the order asked, carrying its own key — a
    /// caller must never have to pair an answer to a question by position. A round the database has
    /// never heard of answers <c>Known: false</c> rather than being left out, because a missing
    /// entry and a round with no findings would otherwise be the same thing.</para>
    /// </remarks>
    /// <summary>Whether there is a rounds database at all in this data directory.</summary>
    /// <remarks>
    /// Asked BEFORE a batch read, because "there is no database" and "the database has no such
    /// round" are different answers and only one of them is about the rounds. A caller asking about
    /// specific rounds has just listed them from this database; if it is gone, the honest answer is
    /// that the read failed, not that five hundred rounds were never recorded. (Code round, codex.)
    /// </remarks>
    public static bool DatabaseExists(string dataDir) =>
        File.Exists(Path.Combine(dataDir, RoundsDb.FileName));

    public static LoggedManyFindings FindingsOfMany(string dataDir, IReadOnlyList<RoundKeyAsked> asked)
    {
        // NO silent fallback for a missing file, and that is the point. This used to answer "not
        // known" for every round when the database was not there — a statement about content nobody
        // could read, and the opposite of what the mode in front of it now says. `Open` throws
        // instead, the mode turns it into EX_IOERR, and there is one answer to "the database is
        // gone" rather than two that disagree. (Code round, gemini.)
        if (asked.Count == 0)
        {
            return new LoggedManyFindings([]);
        }
        var file = Path.Combine(dataDir, RoundsDb.FileName);

        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();

        var answered = new List<LoggedRoundOfMany>(asked.Count);
        foreach (var one in asked)
        {
            // One statement per round against an OPEN connection. The saving that matters is the
            // process and the file open, both of which happen once now; a prepared read against a
            // warm page cache is microseconds, and a single IN-clause over a composite key would
            // cost more in query building than it returns.
            var id = RoundId(db, one.SessionId, one.Stage, one.Number);
            answered.Add(id is null
                ? new LoggedRoundOfMany(one.SessionId, one.Stage, one.Number, false, [])
                : new LoggedRoundOfMany(one.SessionId, one.Stage, one.Number, true, FindingsFor(db, id.Value)));
        }

        return new LoggedManyFindings(answered);
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
        string dataDir, int limit = DefaultLimit, string before = "", bool withFindings = false, string since = "")
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return new LoggedLog([], [], [], new LoggedTotals()) { Since = since };
        }

        // Read-only, unpooled, and with a busy timeout rather than the default of none: a reader
        // never blocks a WAL writer, but the open itself can still meet a checkpoint.
        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        db.Open();

        return new LoggedLog(
            Rounds(db, Math.Clamp(limit, 1, MaxLimit), Cursor(before), withFindings),
            BlindSpots(db, since),
            Defended(db, since),
            Totals(db))
        {
            Consultations = Consultations(db, Math.Clamp(limit, 1, MaxLimit)),
            // The echo is the proof it was applied: a coai-mcp 0.36.0 given `--since` was measured to
            // ignore it and answer all time with exit 0, so nothing else could tell the two apart.
            Since = since,
        };
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
        if (at <= 0 || !long.TryParse(before[(at + 1)..], out var id))
        {
            return null;
        }

        // The TIMESTAMP is checked too, not just the number after it. `0000|1` parsed happily and
        // then compared `0000` against `started_utc`, which matches nothing — so a malformed cursor
        // answered with an EMPTY page instead of the documented first one, which is the opposite of
        // treating it as absent. (CodeRabbit, on the pull request.)
        var started = before[..at];

        return DateTime.TryParseExact(
            started, "O", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out _)
            ? (started, id)
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
        // The counter is SELECTED only when the file has it. A database last written by a binary
        // without schema step 2 has no such column, and naming it would fail the whole rounds list —
        // which is the page, not a section of it. The consultations list can answer "none" for the
        // same skew; this one cannot answer anything. (Caught by story 4's own compatibility test.)
        // Three shapes, asked of the schema in the order the steps arrived: a file with `note` (step 16)
        // has `consult_missed` (step 4) as well, so the ladder is note → counter → neither.
        read.CommandText = HasColumn(db, "rounds", "note") ? SqlRoundsNoted
            : HasColumn(db, "rounds", "consult_missed") ? SqlRoundsCounted
            : SqlRoundsPlain;
        read.Parameters.AddWithValue("$unbounded", before is null ? 1 : 0);
        read.Parameters.AddWithValue("$started", before?.StartedUtc ?? string.Empty);
        read.Parameters.AddWithValue("$id", before?.Id ?? 0L);
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();
        var rounds = new List<LoggedRound>();
        while (rows.Read())
        {
            var id = Big(rows, "id");
            var started = Text(rows, "started_utc");
            var vendor = Text(rows, "caller_vendor");
            var client = Text(rows, "caller_client");
            var model = Text(rows, "caller_model");
            rounds.Add(new LoggedRound(
                Text(rows, "repo_path"),
                Text(rows, "branch"),
                Text(rows, "stage"),
                Number(rows, "number"),
                started,
                Number(rows, "accepted"),
                Number(rows, "rejected"),
                Text(rows, "session_id"),
                inline.TryGetValue(id, out var mine) ? mine : [],
                started + CursorSeparator + id,
                Number(rows, "finding_count"),
                // An empty vendor is a round from before the columns: never asked, so it answers
                // nothing rather than "unknown", which is what a caller we could not identify says.
                vendor.Length + client.Length + model.Length == 0
                    ? null
                    : new Server.CallerDeclaration(vendor, client, Text(rows, "caller_client_version"), model),
                // A database stepped by an older binary has no column at all, and the query that ran
                // then selects the literal instead — either way it arrives under this name, and the
                // reader answers the convention's own "not measured" rather than refusing the row.
                NumberOr(rows, "consult_missed", -1),
                Text(rows, "resolved_utc"),
                // Selected as the literal '' by the two older query texts, so a database from before
                // step 16 answers the one spelling of no note rather than failing the whole list.
                Text(rows, "note")));
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
            var round = Big(rows, "round_id");
            if (!byRound.TryGetValue(round, out var mine))
            {
                byRound[round] = mine = [];
            }

            mine.Add(FindingFrom(rows));
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
            findings.Add(FindingFrom(rows));
        }

        return findings;
    }

    /// <summary>One finding, from whichever of the three queries produced the row.</summary>
    /// <remarks>
    /// It took a BASE ORDINAL and read <c>at + n</c>, which made the three callers agree only by
    /// arithmetic: a column added to one of those queries rotated that one's fields and left the
    /// other two right, so the wrong page would have been the only symptom. By name they are
    /// genuinely interchangeable, which is what they were always supposed to be.
    /// </remarks>
    private static LoggedFinding FindingFrom(SqliteDataReader rows) =>
        new(Number(rows, "ordinal"),
            Text(rows, "severity"),
            Text(rows, "category"),
            Text(rows, "file"),
            Number(rows, "line"),
            Text(rows, "title"),
            Text(rows, "why"),
            Text(rows, "fix"),
            Text(rows, "role"),
            Number(rows, "is_gating") == 1,
            Text(rows, "providers"),
            Text(rows, "resolution"),
            Text(rows, "reason"),
            Number(rows, "re_raised") == 1);

    /// <summary>
    /// The consultations, newest first, bounded by the same limit the rounds are.
    /// </summary>
    /// <remarks>
    /// <para>Not paged, deliberately: a consultation is rarer than a round by an order of magnitude —
    /// one is an agent admitting it is stuck — so the page that holds two hundred rounds holds every
    /// consultation anybody has had. A cursor for a list that short would be machinery with nothing
    /// to do.</para>
    /// <para>A database written by an older binary has no table. It answers an EMPTY list rather than
    /// throwing, because the two halves of this product update separately and a log page that refuses
    /// to draw is a worse answer than a log page with one section missing.</para>
    /// </remarks>
    private static List<LoggedConsultation> Consultations(SqliteConnection db, int limit)
    {
        var consultations = new List<LoggedConsultation>();
        try
        {
            using var read = db.CreateCommand();
            // THE SHAPE IS ASKED FOR, never assumed. This connection is READ-ONLY, so the schema
            // steps do not run for it: a data directory whose last writer was an older release has
            // the table and not these columns, and naming one answered "no such column" — which the
            // guard below deliberately does NOT catch, and which takes the rounds, the blind spots
            // and the totals down with it, because this composes the whole page. The rounds half has
            // carried two texts for exactly this since story 6's counter; this half had one.
            // (codex, the code round.)
            read.CommandText = ConsultationsShape(db);
            read.Parameters.AddWithValue("$limit", limit);
            using var rows = read.ExecuteReader();
            while (rows.Read())
            {
                consultations.Add(new LoggedConsultation(
                    Text(rows, "id"), Text(rows, "caller_kind"), Text(rows, "repo_path"), Text(rows, "branch"),
                    Text(rows, "vendor"), Text(rows, "model"), Number(rows, "turns"), Text(rows, "status"),
                    Text(rows, "reason"), Text(rows, "outcome"), Text(rows, "outcome_by"), Text(rows, "started_utc"), Text(rows, "ended_utc"),
                    Real(rows, "seconds"), Big(rows, "tokens_in"), Big(rows, "tokens_out"),
                    MaybeReal(rows, "cost_usd"),
                    Text(rows, "problem"), Text(rows, "advice"), Text(rows, "alert"),
                    Text(rows, "kind"), Text(rows, "plan"), Text(rows, "epics")));
            }
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteNoSuchTable && Missing(e))
        {
            // A file this binary has not stepped yet: the schema runs on OPEN, and this reader opens
            // read-only, so a database last touched by an older build genuinely has no table here.
            //
            // ONLY that — and the conjunction is the whole guard. It was `||`, which made the code
            // check alone sufficient: `SqliteNoSuchTable` IS SQLite's generic `SQLITE_ERROR`, as the
            // constant below says, so "no such COLUMN" from a half-stepped schema also matched and
            // the tab answered empty. That is exactly the wrong answer that looks like a true one
            // which the paragraph above says must not happen — the comment was right and the code
            // beside it was not. (CodeRabbit, on the pull request.)
            return [];
        }

        return consultations;
    }

    /// <summary>The newest shape this file has, asked rather than assumed — the steps are ordered, so one column each tells.</summary>
    private static string ConsultationsShape(SqliteConnection db) =>
        HasColumn(db, "consultations", "kind") ? SqlConsultationsKinded
        : HasColumn(db, "consultations", "outcome_by") ? SqlConsultationsAuthored
        : HasColumn(db, "consultations", "outcome") ? SqlConsultationsEnded
        : SqlConsultationsPlain;

    /// <summary>
    /// The consultations page, in the four shapes a database can be in — written out, not composed.
    /// </summary>
    /// <remarks>
    /// <para>Three literals for the same reason the rounds page has two, and the reason is in the
    /// comment above those: one text with the columns spliced in reads as SQL built at runtime to
    /// every scanner and to a person skimming, and a query that needs a paragraph to prove it is
    /// safe costs more than the duplicated lines that need none.</para>
    /// <para>Four rather than eight, because the steps are ORDERED: a file with <c>outcome_by</c>
    /// has <c>outcome</c>, since the step that adds the second ran after the step that adds the
    /// first, and a file with <c>kind</c> has both. The missing columns are substituted as what they
    /// mean — nobody recorded a verdict, and nobody is not <c>solved</c>; and before the kinds every
    /// consultation was an agent that was stuck, so <c>stuck</c> is the truth, not a guess.</para>
    /// </remarks>
    private const string SqlConsultationsKinded = """
        SELECT id, caller_kind, repo_path, branch, vendor, model, turns, status, reason,
               outcome, outcome_by, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd,
               problem, advice, alert, kind, plan, epics
        FROM consultations ORDER BY started_utc DESC, id DESC LIMIT $limit
        """;

    /// <summary>The same page against a file written before a consultation could say what it was for.</summary>
    private const string SqlConsultationsAuthored = """
        SELECT id, caller_kind, repo_path, branch, vendor, model, turns, status, reason,
               outcome, outcome_by, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd,
               problem, advice, alert, 'stuck' AS kind, '' AS plan, '' AS epics
        FROM consultations ORDER BY started_utc DESC, id DESC LIMIT $limit
        """;

    /// <summary>The same page against a file written before the author column was appended.</summary>
    private const string SqlConsultationsEnded = """
        SELECT id, caller_kind, repo_path, branch, vendor, model, turns, status, reason,
               outcome, '' AS outcome_by, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd,
               problem, advice, alert, 'stuck' AS kind, '' AS plan, '' AS epics
        FROM consultations ORDER BY started_utc DESC, id DESC LIMIT $limit
        """;

    /// <summary>And against one written before a consultation could be said to have ended at all.</summary>
    private const string SqlConsultationsPlain = """
        SELECT id, caller_kind, repo_path, branch, vendor, model, turns, status, reason,
               '' AS outcome, '' AS outcome_by, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd,
               problem, advice, alert, 'stuck' AS kind, '' AS plan, '' AS epics
        FROM consultations ORDER BY started_utc DESC, id DESC LIMIT $limit
        """;

    /// <summary>SQLite's generic error code, which is what a missing table arrives as.</summary>
    private const int SqliteNoSuchTable = 1;

    /// <summary>Whether the message names a missing table, since the code alone is SQLite's catch-all.</summary>
    private static bool Missing(SqliteException e) =>
        e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <summary>
    /// The rounds page, in the two shapes a database can be in — written out rather than composed.
    /// </summary>
    /// <remarks>
    /// <para>One text with the counter column spliced in was the obvious version and it reads, to any
    /// scanner and to a person skimming, as SQL built at runtime. It was not — the two halves were
    /// literals in this file — but a query that needs a paragraph to prove it is safe is worth more
    /// than the eight duplicated lines that need none. (SonarCloud, Major, on the pull request.)</para>
    /// <para>They differ in ONE token, and a change to either must be made to both. That is the cost,
    /// and it is why the difference is on its own line in each.</para>
    /// </remarks>
    /// <summary>The newest shape: the counter (step 4) and the note (step 16) both present.</summary>
    private const string SqlRoundsNoted = """
        SELECT r.id, s.repo_path, s.branch, r.stage, r.number, r.started_utc, r.accepted, r.rejected,
               r.session_id, (SELECT COUNT(*) FROM findings f WHERE f.round_id = r.id) AS finding_count,
               r.caller_vendor, r.caller_client, r.caller_client_version, r.caller_model,
               r.consult_missed, r.note,
               COALESCE((SELECT MAX(f.resolved_utc) FROM findings f WHERE f.round_id = r.id), '') AS resolved_utc
        FROM rounds r JOIN sessions s ON s.id = r.session_id
        WHERE $unbounded = 1
           OR r.started_utc < $started
           OR (r.started_utc = $started AND r.id < $id)
        ORDER BY r.started_utc DESC, r.id DESC LIMIT $limit
        """;

    /// <summary>A file with the counter and no note: written by a build between steps 4 and 16.</summary>
    private const string SqlRoundsCounted = """
        SELECT r.id, s.repo_path, s.branch, r.stage, r.number, r.started_utc, r.accepted, r.rejected,
               r.session_id, (SELECT COUNT(*) FROM findings f WHERE f.round_id = r.id) AS finding_count,
               r.caller_vendor, r.caller_client, r.caller_client_version, r.caller_model,
               r.consult_missed, '' AS note,
               COALESCE((SELECT MAX(f.resolved_utc) FROM findings f WHERE f.round_id = r.id), '') AS resolved_utc
        FROM rounds r JOIN sessions s ON s.id = r.session_id
        WHERE $unbounded = 1
           OR r.started_utc < $started
           OR (r.started_utc = $started AND r.id < $id)
        ORDER BY r.started_utc DESC, r.id DESC LIMIT $limit
        """;

    /// <summary>The same page against a database written before schema step 2 added the column.</summary>
    private const string SqlRoundsPlain = """
        SELECT r.id, s.repo_path, s.branch, r.stage, r.number, r.started_utc, r.accepted, r.rejected,
               r.session_id, (SELECT COUNT(*) FROM findings f WHERE f.round_id = r.id) AS finding_count,
               r.caller_vendor, r.caller_client, r.caller_client_version, r.caller_model,
               -1 AS consult_missed, '' AS note,
               COALESCE((SELECT MAX(f.resolved_utc) FROM findings f WHERE f.round_id = r.id), '') AS resolved_utc
        FROM rounds r JOIN sessions s ON s.id = r.session_id
        WHERE $unbounded = 1
           OR r.started_utc < $started
           OR (r.started_utc = $started AND r.id < $id)
        ORDER BY r.started_utc DESC, r.id DESC LIMIT $limit
        """;

    /// <summary>
    /// A column read by NAME, because ordinals are what broke and they broke quietly.
    /// </summary>
    /// <remarks>
    /// <para>A merge put four columns into the middle of one SELECT and every read after them moved
    /// by four. Nothing objected: <c>GetInt32(14)</c> is valid whatever sits at 14, so the symptom
    /// was a counter reading a vendor string — a WRONG NUMBER rather than an exception, on a page
    /// nobody would think to re-check after a rebase. It was caught by a test, once. The next merge
    /// would have had the same chance.</para>
    /// <para>Names cannot shift, so every computed column is aliased to have one. That also makes
    /// the two rounds queries one contract: the counted variant selects the column and the plain one
    /// selects <c>-1 AS consult_missed</c>, and the reader no longer knows which ran.</para>
    /// <para><c>GetOrdinal</c> is a name lookup on the reader's own schema, once per value. For a
    /// page of two hundred rounds that is a few thousand dictionary hits against a query that has
    /// already crossed a disk — the cost is not measurable, and the ordinal it replaces was free
    /// only until it was wrong.</para>
    /// </remarks>
    private static string Text(SqliteDataReader rows, string column) => rows.GetString(rows.GetOrdinal(column));

    private static int Number(SqliteDataReader rows, string column) => rows.GetInt32(rows.GetOrdinal(column));

    private static long Big(SqliteDataReader rows, string column) => rows.GetInt64(rows.GetOrdinal(column));

    private static double Real(SqliteDataReader rows, string column) => rows.GetDouble(rows.GetOrdinal(column));

    /// <summary>A nullable REAL — an unpriced consultation is a real state, not a zero.</summary>
    private static double? MaybeReal(SqliteDataReader rows, string column)
    {
        var at = rows.GetOrdinal(column);

        return rows.IsDBNull(at) ? null : rows.GetDouble(at);
    }

    /// <summary>An integer that may be NULL, with the sentinel its column's convention uses.</summary>
    private static int NumberOr(SqliteDataReader rows, string column, int whenNull)
    {
        var at = rows.GetOrdinal(column);

        return rows.IsDBNull(at) ? whenNull : rows.GetInt32(at);
    }

    /// <summary>
    /// Whether this file's table has that column — asked of the schema, never guessed from a version.
    /// </summary>
    /// <remarks>
    /// BOTH names are parameters. The table used to be interpolated, under a remark of mine claiming
    /// `pragma` takes no parameter in that position — which is simply wrong: SQLite's table-valued
    /// pragma functions bind like any other, and it was measured rather than argued about. Nothing a
    /// caller sends could reach the SQL either way (both call sites pass literals written here), but
    /// a query with no string building in it needs no such argument. (SonarCloud, Major, on the pull
    /// request.)
    /// </remarks>
    internal static bool HasColumn(SqliteConnection db, string table, string column)
    {
        using var ask = db.CreateCommand();
        ask.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column";
        ask.Parameters.AddWithValue("$table", table);
        ask.Parameters.AddWithValue("$column", column);

        return Convert.ToInt64(ask.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// What the caller accepts, grouped three ways.
    /// </summary>
    /// <remarks>
    /// Accepted over TOTAL rather than accepted alone, because a category that produces fifty
    /// findings and gets two accepted says something different from one that produces two and gets
    /// both — and only the second is a blind spot worth acting on.
    /// </remarks>
    private static List<BlindSpot> BlindSpots(SqliteConnection db, string since)
    {
        var spots = new List<BlindSpot>();
        spots.AddRange(GroupedBy(db, "category", since));
        spots.AddRange(GroupedBy(db, "role", since));
        spots.AddRange(GroupedBy(db, "providers", since));

        return spots;
    }

    /// <summary>
    /// Only the findings of rounds that started at or after `$since` — every finding when it is empty.
    /// The period switch on the page's *What it keeps missing* (2026-09-25).
    /// </summary>
    /// <remarks>
    /// A parameter, never interpolated; and compared as TEXT against `started_utc`, which is sound only
    /// because <c>Program.SinceOf</c> writes the instant exactly the way the column is stored (`"O"`,
    /// UTC). An empty value matches every round — every string is at least `''` and the column is
    /// NOT NULL — so all time is the same query, with no `OR` to keep SQLite off `rounds_by_time`.
    /// (Our own code reviewer, the code round.)
    /// </remarks>
    private const string InPeriod =
        "round_id IN (SELECT r.id FROM rounds r WHERE r.started_utc >= $since)";

    private static List<BlindSpot> GroupedBy(SqliteConnection db, string column, string since)
    {
        using var read = db.CreateCommand();
        // The column is one of three names written HERE, never anything a caller sends — and it is
        // quoted anyway, so the shape cannot become an injection the day somebody makes it dynamic.
        read.CommandText = $"""
            SELECT "{column}" AS grouped, SUM(resolution = 'accept') AS accepted, COUNT(*) AS total
            FROM findings WHERE resolution <> '' AND {InPeriod} GROUP BY "{column}" ORDER BY accepted DESC
            """;
        read.Parameters.AddWithValue("$since", since);
        using var rows = read.ExecuteReader();
        var spots = new List<BlindSpot>();
        while (rows.Read())
        {
            spots.Add(new BlindSpot(column, Text(rows, "grouped"), Number(rows, "accepted"), Number(rows, "total")));
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
    private static List<LoggedFinding> Defended(SqliteConnection db, string since)
    {
        using var read = db.CreateCommand();
        read.CommandText = $"""
            SELECT ordinal, severity, category, file, line, title, why, fix, role, is_gating,
                   providers, resolution, reason, re_raised
            FROM findings WHERE re_raised = 1 AND resolution = 'reject' AND {InPeriod} ORDER BY id DESC LIMIT $limit
            """;
        read.Parameters.AddWithValue("$limit", DefendedCap + 1);
        read.Parameters.AddWithValue("$since", since);
        using var rows = read.ExecuteReader();
        var defended = new List<LoggedFinding>();
        while (rows.Read())
        {
            defended.Add(FindingFrom(rows));
        }

        return defended;
    }
}
