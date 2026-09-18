using CoaiMcp.Core.Collecting;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>One accepted finding, offered as material for the defect corpus.</summary>
/// <remarks>
/// It carries where the defect WAS — repository, branch, the commit the reviewers read, the file and
/// the line — because the collector's first job is to read that method back out of git and its
/// second is to find the commit that fixed it. The reviewer's own words ride along because they are
/// what a ranking pass reads; they are NOT anonymous and never leave the machine.
/// </remarks>
public sealed record BugCandidate(
    long Id,
    string RepoPath,
    string Branch,
    string SessionId,
    string Stage,
    int RoundNumber,
    string HeadSha,
    string File,
    int Line,
    string Severity,
    string Category,
    string Title,
    string Why,
    string Fix,
    string CollectState,
    string CollectRunId,
    string CollectReason,
    string FixSha,
    /// <summary>The commit of the NEXT code round in the same session, or empty.</summary>
    /// <remarks>
    /// By construction that is the state AFTER this round's fixes — the gate's own loop guarantees
    /// it — so it bounds the search for the fix to one interval instead of a whole branch. 37 % of
    /// measured candidates have one, and for an orphaned commit it is the only interval there is.
    /// </remarks>
    string LaterSha = "");

/// <summary>How many findings each step of the filter leaves standing.</summary>
/// <remarks>
/// <para>The point of the shape is that a skip rate is only readable as a FUNNEL. "30 % were not
/// collectable" says nothing when one step drops everything written in a language nobody parses and
/// the next drops the fixes that cannot be found: a flat percentage lets the first mask the second,
/// and the second is the one with a decision attached to it.</para>
/// <para>Every number is the count after that step AND all the steps before it, so the last one is
/// exactly what <see cref="BugCorpus.Candidates"/> would be without a limit.</para>
/// </remarks>
public sealed record BugFunnel(
    int All = 0,
    int OnCode = 0,
    int Accepted = 0,
    int Gating = 0,
    int Runtime = 0,
    int Located = 0,
    int Unprocessed = 0,
    /// <summary>How many findings have a collected pair, across every run there has ever been.</summary>
    /// <remarks>
    /// The corpus's own size, and deliberately not the last run's tally. The Review button was gated
    /// on `lastRun.collected`, so a second, incremental run that collected nothing new — every
    /// remaining candidate skipped — disabled the button over the ten pairs the first run had already
    /// collected. A batch delta is not a corpus. (Code round 2, gemini.)
    /// </remarks>
    int Collected = 0);

/// <summary>What the corpus has to offer, and what each step of the filter cost to get there.</summary>
/// <param name="Candidates">
/// Capped by the caller's limit. <see cref="BugFunnel.Unprocessed"/> is the true total and is never
/// capped — a caller that sized the work off the list would size it off the cap.
/// </param>
public sealed record BugCorpus(BugFunnel Funnel, IReadOnlyList<BugCandidate> Candidates)
{
    /// <summary>The most recent collector run, or an empty row when none has ever started.</summary>
    /// <remarks>
    /// <para><b>An init property with a default, so it is always present and never null.</b> The panel
    /// renders three different things — no run, a run happening now, a run that ended — and an absent
    /// value has to mean the first of those rather than "this build cannot tell you".</para>
    /// <para><b>And it rides HERE rather than in a mode of its own.</b> The panel owns no SQLite and
    /// reaches this database by spawning a process; a second question would be a second spawn for an
    /// answer the first one already had the file open for.</para>
    /// <para>An older binary emits no <c>lastRun</c> key at all. A reader must treat that as "no run",
    /// which is the same state an empty id means — the two halves of this product have shipped out of
    /// step before. (Plan round, codex.)</para>
    /// </remarks>
    public CollectRunRow LastRun { get; init; } = new();

    /// <summary>The most recent SEND, or an empty row when none has ever started.</summary>
    /// <remarks>
    /// <para>It rides here for the reason <see cref="LastRun"/> does — one spawn answers both — and
    /// it has to exist for the reason the funnel cannot answer: a pair is marked sent only on the
    /// server's acknowledgement, so while a send runs the counts say what they said before it began.
    /// Without this the panel cannot tell "nothing to send" from "sending right now".</para>
    /// <para>An older binary emits no <c>lastSend</c> key. A reader must treat that as "no send",
    /// which is the same state an empty id means.</para>
    /// </remarks>
    public UploadRunRow LastSend { get; init; } = new();

    /// <summary>
    /// How many kept pairs are actually UNSENT — the number a send would offer.
    /// </summary>
    /// <remarks>
    /// <para><c>Funnel.Collected</c> counts every pair this machine has ever kept, which is the
    /// right number for "is there a corpus" and the wrong one for "is there anything to send": after
    /// a successful send it is unchanged, so a button reading it offers to send what has already
    /// gone. Seven code-round findings across three providers, all of them the same sentence.</para>
    /// <para>The predicate is <see cref="RoundsDb.Sendable"/>'s, and it has to stay that way — a
    /// count that describes a different set from the one the run takes is a button that lies in
    /// whichever direction the two differ.</para>
    /// </remarks>
    public int Sendable { get; init; }

    /// <summary>The vendors that may be shown a finding's own words.</summary>
    /// <remarks>
    /// <para><b>On the wire, so the picker reads the list that ENFORCES rather than a copy of it.</b>
    /// Both sides asserting `shared/ranking-vendors.txt` stops them drifting in the repository — it
    /// does not stop an installed extension and an installed server of different ages disagreeing,
    /// and this product ships its two halves out of step. A panel that reads the list from the
    /// server it is actually talking to cannot offer a model that server will refuse.</para>
    /// <para>The panel keeps its own list as the FALLBACK, for a server too old to send this.</para>
    /// </remarks>
    public IReadOnlyList<string> RankingVendors { get; init; } = RankingModels.Local;
}

/// <summary>
/// The accepted findings, read back as material rather than as a log.
/// </summary>
/// <remarks>
/// <para>A sibling of <see cref="RoundsQuery"/> and deliberately not a method on it: that reader
/// answers "what happened in this round", this one answers "which defects are worth keeping", and
/// the second question filters on things the first has no opinion about. Read-only, like its
/// sibling and for the same reason — a reader must never be able to disturb a round.</para>
/// <para><b>It does not migrate.</b> A reader that writes is not a reader; the one-shot mode that
/// calls this opens the database properly first. A schema too old to answer is an empty corpus, not
/// a crash.</para>
/// <para>The panel owns no SQLite, so this is reached through <c>coai-mcp --bugs-json</c>.</para>
/// </remarks>
public static class BugsQuery
{
    public const int DefaultLimit = 200;

    /// <summary>
    /// The same ceiling <see cref="RoundsQuery.MaxLimit"/> uses, and the same number on purpose.
    /// </summary>
    /// <remarks>
    /// The one-shot mode parses <c>--limit</c> with the shared helper, which clamps to that reader's
    /// maximum before this one ever sees it. A larger number here would be unreachable through the
    /// only caller there is, which is worse than a smaller ceiling: it would read as a promise the
    /// binary does not keep.
    /// </remarks>
    public const int MaxLimit = 1_000;

    private const int SqliteError = 1;

    /// <summary>
    /// The six steps, counted cumulatively in one pass.
    /// </summary>
    /// <remarks>
    /// <para><b>Written out, not composed.</b> The first version built each prefix by joining an
    /// array of predicates, which is genuinely safe — every element was a literal in this file — and
    /// reads to any scanner, and to a person skimming, as SQL assembled at runtime. This repository
    /// has already had that argument once, on <c>RoundsQuery</c>'s paired statements, and settled it
    /// the same way: a query that needs a paragraph to prove it is safe costs more than the lines it
    /// saves.</para>
    /// <para>The multiplications are what make it a funnel rather than six independent tallies: each
    /// column includes every flag before it, so the numbers narrow the way the filter does.</para>
    /// <para>Why these six — measured over a live database on 2026-09-15, 8 687 findings narrowing
    /// to 462 usable candidates. A PLAN round's remarks are prose about a document and carry no file
    /// at all. A rejection is a disagreement rather than a defect, and an undecided finding is
    /// neither yet. A Nit somebody accepted to be agreeable is not a defect worth teaching.
    /// Architecture, Ux and Convention are judgements about shape, and this corpus is for runtime
    /// failures. A finding naming no file cannot be read back out of git — though in practice that
    /// step costs nothing, because every runtime finding measured had both a file and a line.</para>
    /// </remarks>
    private const string SqlFunnel = """
        WITH flagged AS (
            SELECT
                CASE WHEN r.stage = 'CodeReview'                                   THEN 1 ELSE 0 END AS on_code,
                CASE WHEN f.resolution = 'accept'                                  THEN 1 ELSE 0 END AS accepted,
                CASE WHEN f.is_gating = 1                                          THEN 1 ELSE 0 END AS gating,
                CASE WHEN f.category IN ('Reliability', 'Performance', 'Security') THEN 1 ELSE 0 END AS runtime,
                CASE WHEN f.file != '' AND f.line != 0                             THEN 1 ELSE 0 END AS located,
                CASE WHEN f.collect_state = ''                                     THEN 1 ELSE 0 END AS unhandled,
                CASE WHEN f.collect_state = 'collected'                            THEN 1 ELSE 0 END AS collected
            FROM findings f JOIN rounds r ON r.id = f.round_id
        )
        SELECT
            COUNT(*)                                                              AS n_all,
            SUM(on_code)                                                          AS n_on_code,
            SUM(on_code * accepted)                                               AS n_accepted,
            SUM(on_code * accepted * gating)                                      AS n_gating,
            SUM(on_code * accepted * gating * runtime)                            AS n_runtime,
            SUM(on_code * accepted * gating * runtime * located)                  AS n_located,
            SUM(on_code * accepted * gating * runtime * located * unhandled)      AS n_unprocessed,
            SUM(on_code * accepted * gating * runtime * located * collected)      AS n_collected
        FROM flagged
        """;

    /// <summary>
    /// The candidates themselves, in the two shapes a caller can ask for.
    /// </summary>
    /// <remarks>
    /// <para>Two texts differing in ONE line, for the reason above and on the precedent of
    /// <c>RoundsQuery</c>'s pair. That line is the cost, and it is on its own line in each so that a
    /// change to one is visibly a change the other needs.</para>
    /// <para>The WHERE here must stay the same six conditions <see cref="SqlFunnel"/> counts, or the
    /// funnel describes a set nobody is offered. That is held by a test rather than by the compiler:
    /// <c>TheFunnelCountsEachStep_AndItsLastStepIsTheListItself</c>.</para>
    /// </remarks>
    private const string SqlUnhandled = """
        SELECT f.id, f.file, f.line, f.severity, f.category, f.title, f.why, f.fix,
               f.collect_state, f.collect_run_id, f.collect_reason, f.fix_sha,
               r.head_sha, r.stage, r.number, r.session_id, s.repo_path, s.branch,
               COALESCE((SELECT later.head_sha FROM rounds later
                         WHERE later.session_id = r.session_id AND later.stage = r.stage
                           AND later.number > r.number AND later.head_sha != ''
                           -- and it must have MOVED: a re-review that committed nothing carries the
                           -- same head_sha, and head..head is an empty interval that finds no fix.
                           AND later.head_sha != r.head_sha
                         ORDER BY later.number LIMIT 1), '') AS later_sha
        FROM findings f
            JOIN rounds r ON r.id = f.round_id
            JOIN sessions s ON s.id = r.session_id
        WHERE r.stage = 'CodeReview' AND f.resolution = 'accept' AND f.is_gating = 1
          AND f.category IN ('Reliability', 'Performance', 'Security')
          AND f.file != '' AND f.line != 0
          AND f.collect_state = ''
        ORDER BY r.started_utc DESC, f.id
        LIMIT $limit
        """;

    /// <summary>As <see cref="SqlUnhandled"/>, including what a run has already handled.</summary>
    private const string SqlEverything = """
        SELECT f.id, f.file, f.line, f.severity, f.category, f.title, f.why, f.fix,
               f.collect_state, f.collect_run_id, f.collect_reason, f.fix_sha,
               r.head_sha, r.stage, r.number, r.session_id, s.repo_path, s.branch,
               COALESCE((SELECT later.head_sha FROM rounds later
                         WHERE later.session_id = r.session_id AND later.stage = r.stage
                           AND later.number > r.number AND later.head_sha != ''
                           -- and it must have MOVED: a re-review that committed nothing carries the
                           -- same head_sha, and head..head is an empty interval that finds no fix.
                           AND later.head_sha != r.head_sha
                         ORDER BY later.number LIMIT 1), '') AS later_sha
        FROM findings f
            JOIN rounds r ON r.id = f.round_id
            JOIN sessions s ON s.id = r.session_id
        WHERE r.stage = 'CodeReview' AND f.resolution = 'accept' AND f.is_gating = 1
          AND f.category IN ('Reliability', 'Performance', 'Security')
          AND f.file != '' AND f.line != 0
        ORDER BY r.started_utc DESC, f.id
        LIMIT $limit
        """;

    /// <summary>What the corpus can offer from the database in <paramref name="dataDir"/>.</summary>
    /// <param name="limit">How many candidates to return. The funnel is unaffected by it.</param>
    /// <param name="all">
    /// Include findings a run has already handled. The default excludes them, which is what makes a
    /// second run additive rather than a repeat.
    /// </param>
    public static BugCorpus Read(string dataDir, int limit = DefaultLimit, bool all = false)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return Empty;
        }

        try
        {
            // Read-only, unpooled, and with a busy timeout rather than none: a reader never blocks a
            // WAL writer, but the open itself can still meet a checkpoint. RoundsQuery's words.
            using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
            db.Open();

            return new BugCorpus(Funnel(db), Candidates(db, Math.Clamp(limit, 1, MaxLimit), all))
            {
                LastRun = LastRun(db),
                LastSend = LastSend(db),
                Sendable = Sendable(db),
            };
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteError && TooOld(e))
        {
            // A database this build has never opened for writing has no collector columns yet. That
            // is a schema that cannot answer, not a failure: the one-shot mode migrates before it
            // reads, so the only way to arrive here is a reader pointed at somebody else's file.
            return Empty;
        }
    }

    private static BugCorpus Empty => new(new BugFunnel(), []);

    private static bool TooOld(SqliteException e) =>
        e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase);

    /// <summary>The most recent run, through the one place that query lives.</summary>
    /// <remarks>
    /// It was a second copy of <see cref="RoundsDb.LastCollectRun"/> for one commit — same twelve
    /// columns, same twelve-argument mapping — which is how the CLI and the panel come to report
    /// different run states from one table. (Code round, codex, twice.)
    /// </remarks>
    private static CollectRunRow LastRun(SqliteConnection db) => CollectRuns.Last(db);

    /// <summary>The most recent send, through the one place that query lives.</summary>
    private static UploadRunRow LastSend(SqliteConnection db) => UploadRuns.Last(db);

    /// <summary>How many pairs a send would offer right now.</summary>
    /// <remarks>
    /// Through <see cref="SendablePairs"/>, which is where the predicate lives. It was spelled out
    /// here as well for one commit — the same three conditions, written twice — and that is how a
    /// button comes to show a number the run does not use. (Code round 2, gemini and codex.)
    /// </remarks>
    private static int Sendable(SqliteConnection db) => SendablePairs.Count(db);

    private static BugFunnel Funnel(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = SqlFunnel;
        using var rows = read.ExecuteReader();
        return rows.Read()
            ? new BugFunnel(
                All: Count(rows, "n_all"),
                OnCode: Count(rows, "n_on_code"),
                Accepted: Count(rows, "n_accepted"),
                Gating: Count(rows, "n_gating"),
                Runtime: Count(rows, "n_runtime"),
                Located: Count(rows, "n_located"),
                Unprocessed: Count(rows, "n_unprocessed"),
                Collected: Count(rows, "n_collected"))
            : new BugFunnel();
    }

    private static List<BugCandidate> Candidates(SqliteConnection db, int limit, bool all)
    {
        using var read = db.CreateCommand();
        read.CommandText = all ? SqlEverything : SqlUnhandled;
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();

        var candidates = new List<BugCandidate>();
        while (rows.Read())
        {
            candidates.Add(CandidateFrom(rows));
        }

        return candidates;
    }

    private static BugCandidate CandidateFrom(SqliteDataReader rows) =>
        new(
            Id: rows.GetInt64(rows.GetOrdinal("id")),
            RepoPath: Text(rows, "repo_path"),
            Branch: Text(rows, "branch"),
            SessionId: Text(rows, "session_id"),
            Stage: Text(rows, "stage"),
            RoundNumber: Number(rows, "number"),
            HeadSha: Text(rows, "head_sha"),
            File: Text(rows, "file"),
            Line: Number(rows, "line"),
            Severity: Text(rows, "severity"),
            Category: Text(rows, "category"),
            Title: Text(rows, "title"),
            Why: Text(rows, "why"),
            Fix: Text(rows, "fix"),
            CollectState: Text(rows, "collect_state"),
            CollectRunId: Text(rows, "collect_run_id"),
            CollectReason: Text(rows, "collect_reason"),
            FixSha: Text(rows, "fix_sha"),
            LaterSha: Text(rows, "later_sha"));

    /// <summary>A SUM over no rows is NULL in SQLite, and no findings is nought rather than absent.</summary>
    private static int Count(SqliteDataReader rows, string column)
    {
        var at = rows.GetOrdinal(column);
        return rows.IsDBNull(at) ? 0 : Convert.ToInt32(rows.GetValue(at), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Text(SqliteDataReader rows, string column) => rows.GetString(rows.GetOrdinal(column));

    private static int Number(SqliteDataReader rows, string column) => rows.GetInt32(rows.GetOrdinal(column));
}
