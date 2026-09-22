using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Storage;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The rounds database: every round, every reviewer, and every FINDING with what was decided.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A session file records that a reviewer produced four findings. It does
/// not record the findings. Their text went into the reply to the calling agent and, for the ones
/// that were rejected, into a list kept so they are not raised again — everything else was gone the
/// moment the round closed. So the log page could show counts and nothing else, and "every finding
/// that ever mentioned FileShare" was a question with no answer anywhere on this machine.</para>
/// <para><b>A projection, never the source of truth.</b> The session files stay exactly as they are
/// and a running round still reads and writes them; this is written alongside. Every call here is
/// best-effort: a database that cannot be written must never take a round down, because the round
/// is the thing somebody is waiting for and this is a record of it.</para>
/// <para><b>Opened per write, not held.</b> A round takes minutes and produces two or three writes,
/// so a connection that lives for the process buys nothing and costs the file handle — which is
/// exactly what five servers sharing one data directory must not fight over.</para>
/// <para><b>Concurrency.</b> WAL, because the five-window case is five servers sharing one data
/// directory — WAL lets them write in turn while a reader (the panel) sees a consistent snapshot
/// rather than a torn page. Writes are short and single-statement-ish for the same reason.</para>
/// </remarks>
public sealed class RoundsDb : IDisposable
{
    /// <summary>The file, in the data directory beside the sessions it projects.</summary>
    public const string FileName = "coai.db";

    private readonly SqliteConnection _db;

    /// <summary>
    /// Where a stamp comes from.
    /// </summary>
    /// <remarks>
    /// Injected rather than read off the machine, per `.agents/conventions/common/utc-timestamps.md`:
    /// a clock a test cannot control is a column a test cannot assert. It was ambient until the code
    /// round said so, and the story after this one turns `resolved_utc` into a duration on screen —
    /// which is exactly when an uncontrollable clock stops being a style question.
    /// </remarks>
    private readonly TimeProvider _time;

    private RoundsDb(SqliteConnection db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>Opens (and creates or migrates) the database, or answers null when it cannot.</summary>
    /// <remarks>
    /// Null rather than an exception: every caller's correct behaviour on a database that will not
    /// open is to carry on without one, and a method that can only be used inside a try/catch says
    /// that badly.
    /// </remarks>
    public static RoundsDb? Open(string dataDir, Serilog.ILogger log, TimeProvider? time = null)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            // Pooling off: a pooled connection keeps the file handle open after Dispose, and this
            // opens for one write and closes. It cost a test suite nine red cleanups to learn.
            //
            // The busy timeout is stated rather than assumed, and what it is worth was MEASURED
            // rather than argued: the gate said a concurrent write would return SQLITE_BUSY at once
            // and a best-effort write would swallow it. It does not — with a second server holding
            // a write transaction, the test passes with this setting and without it, because
            // Microsoft.Data.Sqlite already retries a busy database until CommandTimeout (30 s).
            // So this is a statement of intent that outlives that default, not the fix it looked
            // like; the loss the gate feared does not reproduce.
            var db = new SqliteConnection(
                $"Data Source={Path.Combine(dataDir, FileName)};Pooling=False;{SqliteMigrator.DefaultTimeoutFragment}");
            db.Open();
            Migrate(db);

            return new RoundsDb(db, time ?? TimeProvider.System);
        }
        catch (Exception e)
        {
            // ANY exception, not a named few: the contract here is that a database which will not
            // open changes nothing about the round, and a migration step throwing something
            // unlisted would otherwise take down the review it is only supposed to record.
            log.Warning(e, "the rounds database could not be opened; rounds are recorded in their session files only");

            return null;
        }
    }

    /// <summary>
    /// Brings the file up to the schema this build expects.
    /// </summary>
    /// <remarks>
    /// <para>Through <see cref="SqliteMigrator"/>, the one runner every database in this repository
    /// shares. It was private here — <c>CREATE TABLE IF NOT EXISTS</c> alone is not a migration, the
    /// gate was right to say so, and the runner is what makes each step run once, in order, as ONE
    /// transaction with its <c>user_version</c> bump. It moved out the day <c>coai-bugs.db</c>
    /// reached a host and needed the same discipline, because the gate ruled against a second copy.
    /// What the runner guarantees, and why a step and its bump must be atomic, is written on it; the
    /// test that proves the transaction is real (<c>AStepThatFailsPartWay…</c>) drives the runner
    /// directly through its step seam, exactly as it drove this class's own overload before.</para>
    /// <para>A step must be additive. Adding a column belongs in <see cref="Schema.Steps"/>; anything
    /// that cannot be expressed as one is a reason to delete the file instead — it is a projection,
    /// and the sessions it projects are still on disk.</para>
    /// </remarks>
    private static void Migrate(SqliteConnection db) => SqliteMigrator.Migrate(db, Schema.Steps);

    /// <summary>
    /// One finished round, with its reviewers and the findings it produced.
    /// </summary>
    /// <remarks>
    /// The findings arrive in the order <c>resolve</c> numbers them, and that ordinal is stored: it
    /// is how a decision made in a later call finds the finding it was about.
    /// </remarks>
    public void RecordRound(
        SessionState state,
        RoundRecord round,
        IReadOnlyList<Finding> findings,
        RoundContext context = default)
    {
        using var transaction = _db.BeginTransaction();
        RecordSession(state);
        var roundId = RecordRoundRow(state, round, context);
        RecordReviewers(roundId, round);
        RecordFindings(roundId, findings, context);
        transaction.Commit();
    }

    /// <summary>
    /// One consultation, as it stands right now: written when it opens and again on every change.
    /// </summary>
    /// <remarks>
    /// <para>An upsert rather than an insert, because a consultation is a CONVERSATION — it is
    /// written `asking`, becomes `open` with a turn, may be `interrupted` and resumed, and ends
    /// `closed` or `failed`. One row that advances says what happened; a row per state would make
    /// the log page count one consultation five times.</para>
    /// <para>The totals are summed from the turns rather than stored per turn: what the log answers
    /// is "what did this consultation cost", and the turn-by-turn detail is in the record file the
    /// panel reads while it is still running. <c>problem</c> is the FIRST turn's — what it is about —
    /// and <c>advice</c> the LAST one's, which is the answer in force. A consultation still in its
    /// first launch has neither, and that is a real state: it is `asking`.</para>
    /// </remarks>
    public void RecordConsultation(ConsultationRow row)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO consultations (
                id, caller, caller_kind, repo_path, branch, head_sha, vendor, model, turns, status,
                reason, outcome, outcome_by, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd, problem, advice, alert)
            VALUES (
                $id, $caller, $kind, $repo, $branch, $sha, $vendor, $model, $turns, $status,
                $reason, $outcome, $outcomeBy, $started, $ended, $seconds, $in, $out, $cost, $problem, $advice, $alert)
            ON CONFLICT(id) DO UPDATE SET
                turns = excluded.turns, status = excluded.status, reason = excluded.reason,
                outcome = excluded.outcome, outcome_by = excluded.outcome_by,
                ended_utc = excluded.ended_utc, seconds = excluded.seconds,
                tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out,
                cost_usd = excluded.cost_usd, problem = excluded.problem,
                advice = excluded.advice, alert = excluded.alert
            """;
        Bind(write, "$id", row.Id);
        Bind(write, "$caller", row.Caller);
        Bind(write, "$kind", row.CallerKind);
        Bind(write, "$repo", row.RepoPath);
        Bind(write, "$branch", row.Branch);
        Bind(write, "$sha", row.HeadSha);
        Bind(write, "$vendor", row.Vendor);
        Bind(write, "$model", row.Model);
        Bind(write, "$turns", row.Turns);
        Bind(write, "$status", row.Status);
        Bind(write, "$reason", row.Reason);
        Bind(write, "$outcome", row.Outcome);
        Bind(write, "$outcomeBy", row.OutcomeBy);
        Bind(write, "$started", row.StartedUtc);
        Bind(write, "$ended", row.EndedUtc);
        Bind(write, "$seconds", row.Seconds);
        Bind(write, "$in", row.TokensIn);
        Bind(write, "$out", row.TokensOut);
        Bind(write, "$cost", row.CostUsd is { } usd ? usd : DBNull.Value);
        Bind(write, "$problem", row.Problem);
        Bind(write, "$advice", row.Advice);
        Bind(write, "$alert", row.Alert);
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// What this caller DECIDED in earlier rounds of the same session and stage, oldest first.
    /// </summary>
    /// <remarks>
    /// <para>The input to story 6's counter. Same STAGE, because a plan-stage remark has no file and
    /// a code-stage one usually does — comparing across them would match on category alone and count
    /// coincidences.</para>
    /// <para>The WHOLE finding is hydrated, not the four fields the current predicate happens to
    /// read. A partial reconstruction is an undercount waiting to happen: the day
    /// <see cref="FindingDedup.SameDefect"/> reads a fifth field — the remark, say — every row out of
    /// here would compare empty against it, silently, and the counter would quietly stop counting.
    /// The columns are already on the table and the reader is already open. (codex, third code
    /// round.) The vendor list is the one thing left behind: it is a separate table, it says who
    /// SAID the defect rather than what the defect is, and no similarity rule can be written over
    /// it.</para>
    /// <para>Both kinds of decision travel, ordered oldest first, because the LATEST word about a
    /// defect is what decides: one accepted in round 1 and rejected in round 2 is a disagreement the
    /// caller is defending by the time it comes back. An UNRESOLVED finding is neither and is left
    /// out — it is a round the caller has not answered, not a decision.</para>
    /// </remarks>
    public IReadOnlyList<EarlierDecision> DecidedEarlier(string sessionId, string stage, int number)
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT r.number, f.severity, f.category, f.file, f.line, f.title, f.resolution, f.why, f.fix
            FROM findings f JOIN rounds r ON r.id = f.round_id
            WHERE r.session_id = $session AND r.stage = $stage AND r.number < $number
              AND f.resolution IN ('accept', 'reject')
            ORDER BY r.number, f.ordinal
            """;
        BindRound(read, sessionId, stage, number);

        var decided = new List<EarlierDecision>();
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            decided.Add(new EarlierDecision(
                rows.GetInt32(0),
                new Finding(
                    Enum.TryParse<Severity>(rows.GetString(1), out var severity) ? severity : Severity.Major,
                    Enum.TryParse<Category>(rows.GetString(2), out var category) ? category : Category.Reliability,
                    rows.GetString(3),
                    rows.GetInt32(4),
                    rows.GetString(5),
                    rows.GetString(7),
                    rows.GetString(8),
                    []),
                rows.GetString(6) == "accept"));
        }

        return decided;
    }

    /// <summary>
    /// How many accepted findings this round was handed back. <c>-1</c> until something counted.
    /// </summary>
    /// <remarks>
    /// The <c>accepted</c>/<c>rejected</c> convention, for the same reason: a round the projection
    /// could not ask about must not read as a round where nothing survived. Zero is a measurement and
    /// -1 is the absence of one.
    /// </remarks>
    public void RecordConsultMissed(string sessionId, string stage, int number, int missed)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE rounds SET consult_missed = $missed
            WHERE session_id = $session AND stage = $stage AND number = $number
            """;
        Bind(write, "$missed", missed);
        BindRound(write, sessionId, stage, number);
        write.ExecuteNonQuery();
    }

    /// <summary>What a collector run made of one candidate.</summary>
    /// <remarks>
    /// <para>All four columns in ONE statement, keyed by the finding's own id. A run that wrote the
    /// state and then died before the reason would leave a row saying it was skipped and refusing to
    /// say why — and the next run would pass over it, because the state is no longer empty. Written
    /// once, after every piece of evidence is in hand. (Plan round, codex.)</para>
    /// <para><b>A compare-and-swap on the state the run READ, not on emptiness.</b> Two collector
    /// runs can read the same finding; an unconditional UPDATE let the slower one overwrite a
    /// `collected` row and its fix commit with its own later verdict. The first guard was written as
    /// `collect_state = ''` — which is the same thing only for a run that takes unprocessed rows, and
    /// `--all` exists precisely to revisit decided ones. It recomputed every candidate and persisted
    /// NOTHING, silently: the feature that makes a repaired walk worth having did not work at all.
    /// Swapping against <paramref name="was"/> keeps the race guard exactly (a pending row swaps from
    /// `''`) and lets a revisit through. (Code round, gemini and codex, independently, twice.)</para>
    /// <para>The id, not the ordinal: a candidate is identified here by the row it came from, and
    /// `BugsQuery` hands that id over for exactly this.</para>
    /// </remarks>
    /// <returns>Whether this run was the one that claimed the row.</returns>
    /// <param name="was">
    /// The state the row carried when this run read it — empty for an unprocessed candidate. The
    /// write lands only if the row still says that, so a run that lost the race is told.
    /// </param>
    /// <param name="pair">
    /// The before/after skeletons, when the candidate was collected. Written in the SAME
    /// transaction as the outcome: a kill between the two would leave a database whose outcome says
    /// `collected` and whose pairs table has nothing to show for it, which is the one disagreement
    /// this pair of writes exists to make impossible. (Plan round, codex.)
    /// </param>
    public bool RecordCollect(
        long findingId, string was, string state, string reason, string fixSha, string runId,
        CollectedPair? pair = null)
    {
        using var transaction = _db.BeginTransaction();
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE findings
               SET collect_state = $state, collect_reason = $reason,
                   fix_sha = $fix, collect_run_id = $run
             WHERE id = $id AND collect_state = $was
            """;
        Bind(write, "$state", state);
        Bind(write, "$reason", reason);
        Bind(write, "$fix", fixSha);
        Bind(write, "$run", runId);
        Bind(write, "$id", findingId);
        Bind(write, "$was", was);

        // The swap, and the caller is told whether it won. Two runs can read the same finding; an
        // unconditional write let the slower one replace a `collected` row and its fix_sha with its
        // own later verdict, so durable state depended on timing. (Code round, codex.)
        var claimed = write.ExecuteNonQuery() == 1;
        if (claimed)
        {
            // A pair belongs to a COLLECTED finding and to no other kind. `--all` can revisit a
            // finding that was collected and decide it is now skipped — a repaired walk that finds
            // the earlier fix commit was wrong — and the pair written last time would otherwise
            // survive, with `Pairs()` still offering it for review. The row follows its outcome, in
            // the same transaction as the outcome. (CodeRabbit.)
            if (pair is { } collected)
            {
                WritePair(findingId, collected);
            }
            else
            {
                ForgetPair(findingId);
            }
        }

        transaction.Commit();

        return claimed;
    }

    /// <summary>The pair, written beside the outcome that produced it.</summary>
    /// <remarks>
    /// <b>The upsert replaces the pair and leaves <c>keep</c> exactly where it was.</b> This is the
    /// sharpest failure the plan round found, and two reviewers found it independently: a person
    /// reviews two hundred pairs, reruns `--all` to pick up a repaired walk, and an ordinary upsert
    /// takes every decision back to `-1` without saying anything. `0` and `1` are BOTH decisions, so
    /// the column is simply not in the update list — a guard written `WHERE keep = -1` would have
    /// preserved the kept rows and quietly un-dropped the dropped ones.
    /// </remarks>
    private void WritePair(long findingId, CollectedPair pair)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO collect_pairs
                (finding_id, symbol_name, language, skeleton_before, skeleton_after, written_utc)
            VALUES ($id, $symbol, $language, $before, $after, $now)
            ON CONFLICT(finding_id) DO UPDATE SET
                symbol_name = excluded.symbol_name, language = excluded.language,
                skeleton_before = excluded.skeleton_before,
                skeleton_after = excluded.skeleton_after,
                written_utc = excluded.written_utc
            """;
        Bind(write, "$id", findingId);
        Bind(write, "$symbol", pair.SymbolName);
        Bind(write, "$language", pair.Language);
        Bind(write, "$before", pair.SkeletonBefore);
        Bind(write, "$after", pair.SkeletonAfter);
        Bind(write, "$now", Now());
        write.ExecuteNonQuery();
    }

    /// <summary>Drops the pair of a finding that is no longer collected.</summary>
    /// <remarks>
    /// The decision goes with it, and that is right: a pair a later run says was never the fix is not
    /// a pair somebody decided about. Deleting nothing is the ordinary case — most outcomes never had
    /// a pair — so this is silent rather than counted.
    /// </remarks>
    private void ForgetPair(long findingId)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "DELETE FROM collect_pairs WHERE finding_id = $id";
        Bind(write, "$id", findingId);
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// The collected pairs, newest first, with whatever a person has decided about them — and where
    /// each one was, and what the reviewers said about it.
    /// </summary>
    /// <remarks>
    /// <para><b>A wider row than <see cref="Sendable"/>'s, into a record of its own.</b> The send
    /// projects <see cref="StoredPair"/> and nothing else, and <c>OnlyFourFieldsLeaveTests</c>
    /// constructs that record by name to say so; widening it would have routed the repository path
    /// and the reviewers' prose through the type the upload reads. The page reads
    /// <see cref="ReviewPair"/>, and the two share their first nine columns and nothing else.</para>
    /// <para><b>The round and the session are LEFT JOINed.</b> A pair belongs to a finding, and the
    /// finding's round and session are what say where the code was — but a pair whose round or
    /// session row has gone is still a pair somebody has to decide about. An inner join would drop
    /// it from the page in silence; an empty repository path says something true instead. The
    /// finding itself stays an inner join: a pair keyed on a finding that is not there is a pair of
    /// nothing, and it was never offered before either.</para>
    /// <para>Read by column NAME, through <see cref="Columns"/>, rather than by ordinal as
    /// <see cref="Sendable"/> still does: at sixteen columns an ordinal reader answers the wrong
    /// field the day the SELECT is reordered, and does it silently.</para>
    /// </remarks>
    public IReadOnlyList<ReviewPair> Pairs(int limit)
    {
        using var read = _db.CreateCommand();
        read.CommandText = ThePagesRow + """

             ORDER BY p.written_utc DESC, p.finding_id
             LIMIT $limit
            """;
        Bind(read, "$limit", limit);
        using var rows = read.ExecuteReader();
        var pairs = new List<ReviewPair>();
        while (rows.Read())
        {
            pairs.Add(ReviewPairFrom(rows));
        }

        return pairs;
    }

    /// <summary>ONE pair's row, by finding — the same projection as <see cref="Pairs"/>, or nothing.</summary>
    /// <remarks>
    /// <para>For <c>--real-method</c>, which needs the checkout, the two commits, the path, the line
    /// and the stored name of one pair and must not read the whole page to get them: a pair past the
    /// page's limit would then be unreadable, and two hundred rows for one is the wrong shape.</para>
    /// <para>Null is the legitimate "not found" the C# doctrine allows — a pair recollected out from
    /// under the page, or an id nobody ever had — and the mode turns it into an ANSWER naming that,
    /// not an exit code.</para>
    /// </remarks>
    public ReviewPair? Pair(long findingId)
    {
        using var read = _db.CreateCommand();
        read.CommandText = ThePagesRow + """

             WHERE p.finding_id = $id
            """;
        Bind(read, "$id", findingId);
        using var rows = read.ExecuteReader();

        return rows.Read() ? ReviewPairFrom(rows) : null;
    }

    /// <summary>The page's projection, without its ORDER or its WHERE — written once for the two readers of it.</summary>
    private const string ThePagesRow = """
        SELECT p.finding_id, p.symbol_name, p.language, p.skeleton_before, p.skeleton_after,
               p.keep, f.severity, f.category, f.title,
               COALESCE(s.repo_path, '') AS repo_path, COALESCE(r.head_sha, '') AS head_sha,
               f.fix_sha, f.file, f.line, f.why, f.fix,
               p.comment, p.sent_utc, p.comment_lost
          FROM collect_pairs p
          JOIN findings f ON f.id = p.finding_id
          LEFT JOIN rounds r ON r.id = f.round_id
          LEFT JOIN sessions s ON s.id = r.session_id
        """;

    /// <summary>One row of <see cref="Pairs"/>, by column name.</summary>
    private static ReviewPair ReviewPairFrom(SqliteDataReader rows) =>
        new(
            FindingId: Columns.Id(rows, "finding_id"),
            SymbolName: Columns.Text(rows, "symbol_name"),
            Language: Columns.Text(rows, "language"),
            SkeletonBefore: Columns.Text(rows, "skeleton_before"),
            SkeletonAfter: Columns.Text(rows, "skeleton_after"),
            Keep: Columns.Number(rows, "keep"),
            Severity: Columns.Text(rows, "severity"),
            Category: Columns.Text(rows, "category"),
            Title: Columns.Text(rows, "title"),
            RepoPath: Columns.Text(rows, "repo_path"),
            HeadSha: Columns.Text(rows, "head_sha"),
            FixSha: Columns.Text(rows, "fix_sha"),
            File: Columns.Text(rows, "file"),
            Line: Columns.Number(rows, "line"),
            Why: Columns.Text(rows, "why"),
            Fix: Columns.Text(rows, "fix"),
            Comment: Columns.Text(rows, "comment"),
            SentUtc: Columns.Text(rows, "sent_utc"),
            CommentLost: Columns.Text(rows, "comment_lost"));

    /// <summary>The kept pairs nobody has sent yet, and nobody has been refused for.</summary>
    /// <remarks>
    /// Three conditions and each is a decision: <c>keep = 1</c> because a person said yes, no
    /// <c>sent_utc</c> because sending twice is waste, and no <c>send_refusal</c> because a pair the
    /// server would not take is a defect in our normaliser and retrying it blindly hides that.
    /// </remarks>
    public IReadOnlyList<StoredPair> Sendable(int limit)
    {
        using var read = _db.CreateCommand();
        // The predicate comes from SendablePairs, which is the one place it is written: it was here
        // and in the button's count, the same three conditions twice, which is how a button comes to
        // show a number the run does not use. (Code round 2, gemini and codex.)
        read.CommandText = """
            SELECT p.finding_id, p.symbol_name, p.language, p.skeleton_before, p.skeleton_after,
                   p.keep, f.severity, f.category, f.title, p.comment
              FROM collect_pairs p JOIN findings f ON f.id = p.finding_id
             WHERE
            """
            + " " + SendablePairs.Conditions
            + """

             ORDER BY p.written_utc, p.finding_id
             LIMIT $limit
            """;
        Bind(read, "$limit", limit);
        using var rows = read.ExecuteReader();
        var pairs = new List<StoredPair>();
        while (rows.Read())
        {
            // The comment is LAST, at 9, because this reader is ordinal: appended rather than
            // inserted, every column before it keeps the number it had.
            pairs.Add(new StoredPair(
                rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3),
                rows.GetString(4), rows.GetInt32(5), rows.GetString(6), rows.GetString(7),
                rows.GetString(8), rows.GetString(9)));
        }

        return pairs;
    }

    /// <summary>
    /// Writes down what a whole batch came to, in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>It was two hundred separate updates. A kill halfway through left half the batch marked
    /// sent and half not — a local state no retry can reason about, because nothing records that the
    /// batch was ever partial. One transaction means the client's picture of a batch is the
    /// server's. (Code round, local.)</para>
    /// <para>Called ONLY on an acknowledgement, never when the batch left: a pair marked before the
    /// server answered is a pair this client would skip for ever.</para>
    /// </remarks>
    public void RecordSendOutcome(IReadOnlyList<SendOutcome> outcomes)
    {
        using var transaction = _db.BeginTransaction();
        foreach (var outcome in outcomes)
        {
            using var write = outcome.WasRefused ? RefusalOf(outcome) : AcknowledgementOf(outcome);
            write.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>What <c>comment_lost</c> says when the text changed while its batch was in the air.</summary>
    internal const string EditedWhileSending =
        "the comment was changed while it was being sent, so the server holds the words from before the change";

    private SqliteCommand RefusalOf(SendOutcome outcome)
    {
        var write = _db.CreateCommand();
        write.CommandText = "UPDATE collect_pairs SET send_refusal = $why WHERE finding_id = $id";
        Bind(write, "$why", outcome.Why);
        Bind(write, "$id", outcome.FindingId);

        return write;
    }

    /// <summary>A pair the server took — and whether the words a person sees are the words it has.</summary>
    /// <remarks>
    /// <para><c>comment_lost</c> is the server's own sentence when it took the pair and not the
    /// comment (somebody else's was there first; the pair was already promoted), written in THIS
    /// transaction so a pair is never marked sent while the page still implies its words went.</para>
    /// <para><b>And the text is compared with what crossed.</b> A batch is read, the POST takes its
    /// time, and a person can edit the box meanwhile: the pair is then acknowledged with words the
    /// server never saw. Marking it sent with the NEW text on screen would be the silent divergence
    /// the read-only box exists to prevent, so the row says what happened instead. (Plan round of
    /// 4.2, the local reviewer.)</para>
    /// </remarks>
    private SqliteCommand AcknowledgementOf(SendOutcome outcome)
    {
        var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE collect_pairs
               SET sent_utc = $now,
                   comment_lost = CASE WHEN comment = $sent THEN $lost ELSE $edited END
             WHERE finding_id = $id
            """;
        Bind(write, "$now", Now());
        Bind(write, "$sent", outcome.Comment);
        Bind(write, "$lost", outcome.Why);
        Bind(write, "$edited", EditedWhileSending);
        Bind(write, "$id", outcome.FindingId);

        return write;
    }

    /// <summary>
    /// Clears every refusal, so a repaired normaliser can offer those pairs again.
    /// </summary>
    /// <remarks>
    /// <b>Without this a refusal is permanent.</b> `Sendable` excludes anything with a
    /// <c>send_refusal</c>, and the refusals worth having are precisely the ones OUR normaliser
    /// caused — two were found in the alphabet check by running it over this repository, and both
    /// refused perfectly good work. Fixing the normaliser and having no way to re-offer what it
    /// spoiled is fixing nothing. (Code round, codex.)
    /// </remarks>
    /// <returns>How many pairs are sendable again.</returns>
    public int RequeueRefused()
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE collect_pairs SET send_refusal = '' WHERE send_refusal != ''";

        return write.ExecuteNonQuery();
    }

    /// <summary>Marks a pair the server acknowledged.</summary>
    /// <remarks>
    /// Called ONLY on an acknowledgement — accepted or already held — never when the batch left.
    /// A pair marked before the server answered is a pair the client would skip for ever.
    /// </remarks>
    public void Sent(long findingId)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE collect_pairs SET sent_utc = $now WHERE finding_id = $id";
        Bind(write, "$now", Now());
        Bind(write, "$id", findingId);
        write.ExecuteNonQuery();
    }

    /// <summary>Marks a pair the server would not take, and why.</summary>
    /// <remarks>
    /// Not retried: the refusal is about the pair itself, so sending it again produces the same
    /// answer. The reason is kept because it names a defect in OUR normaliser, and a later run with a
    /// repaired one can clear the column.
    /// </remarks>
    public void Refused(long findingId, string why)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE collect_pairs SET send_refusal = $why WHERE finding_id = $id";
        Bind(write, "$why", why);
        Bind(write, "$id", findingId);
        write.ExecuteNonQuery();
    }

    /// <summary>Records what a person decided about a batch of pairs.</summary>
    /// <remarks>
    /// One transaction for the batch, because a review is a batch: two hundred decisions arriving as
    /// two hundred spawns is the shape `--findings-many` was created to end.
    /// </remarks>
    /// <returns>How many rows were actually decided.</returns>
    public int RecordKeep(IReadOnlyList<KeepDecision> decisions)
    {
        using var transaction = _db.BeginTransaction();
        var decided = 0;
        foreach (var decision in decisions)
        {
            using var write = _db.CreateCommand();
            write.CommandText =
                "UPDATE collect_pairs SET keep = $keep WHERE finding_id = $id";
            Bind(write, "$keep", decision.Keep);
            Bind(write, "$id", decision.FindingId);
            decided += write.ExecuteNonQuery();
        }

        transaction.Commit();

        return decided;
    }

    /// <summary>Records keeps AND comments, or refuses the batch — see <see cref="PairDecisions"/>.</summary>
    public (int Decided, string Refusal) RecordDecide(IReadOnlyList<CommentedDecision> decisions) =>
        PairDecisions.Record(_db, decisions);

    /// <summary>What the caller decided about each finding of the round it last answered.</summary>
    /// <remarks>
    /// Each decision carries the finding NUMBER it was made by (see <see cref="DecisionAt"/>). This
    /// used to read the number off the decision's POSITION in the list, which is the same thing only
    /// for a caller that resolves top to bottom — and nothing requires one to, so an out-of-order or
    /// partial set wrote every mark onto the wrong finding.
    /// </remarks>
    public void RecordDecisions(string sessionId, string stage, int number, IReadOnlyList<DecisionAt> decisions)
    {
        using var transaction = _db.BeginTransaction();
        var when = _time.GetUtcNow().UtcDateTime.ToString("O");
        // Property access, not a deconstruction and emphatically not a loop counter: three reviewers
        // of the code round read `ordinal` as an iteration index after it had stopped being one.
        // `decided.Ordinal` is the number the caller sent and cannot be read as anything else.
        foreach (var decided in decisions)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                UPDATE findings SET resolution = $resolution, reason = $reason, resolved_utc = $when
                WHERE ordinal = $ordinal AND round_id = (
                    SELECT id FROM rounds WHERE session_id = $session AND stage = $stage AND number = $number)
                """;
            Bind(write, "$resolution", decided.Decision is Decision.Accepted ? "accept" : "reject");
            Bind(write, "$reason", decided.Decision is Decision.Rejected rejected ? rejected.Reason : string.Empty);
            Bind(write, "$when", when);
            Bind(write, "$ordinal", decided.Ordinal);
            BindRound(write, sessionId, stage, number);
            write.ExecuteNonQuery();
        }

        RecordClosing(sessionId, stage, number);
        transaction.Commit();
    }

    /// <summary>
    /// How the caller closed the gate: how many findings it took, and how many it argued with.
    /// </summary>
    /// <remarks>
    /// Asked for on 2026-09-05, and it is the reason the rest of this table is worth keeping. An
    /// ACCEPTED finding is, by definition, something the caller had not seen and then agreed was
    /// worth having — a blind spot, admitted. A rejection is a disagreement, and one a later round
    /// raises again (see <c>re_raised</c>) is a disagreement the caller is defending. Counting them
    /// per round makes "what does this model habitually miss" a query rather than an afternoon.
    /// </remarks>
    /// <remarks>
    /// <para><b>Counted from the findings TABLE, never from the call that triggered it.</b> It used
    /// to count the decisions in the batch it was handed, which is right only when every finding is
    /// decided in one go. A caller that accepts one finding now and rejects another later — which
    /// <c>RecordDecisions</c> explicitly supports, and which has a test — had the round's summary
    /// OVERWRITTEN by the second call: accepted fell back to nought while the findings table plainly
    /// said otherwise. This is the same defect the ordinal fix removed, one level up: a number
    /// derived from the shape of one call rather than from the thing it describes. (Code round,
    /// gemini.)</para>
    /// <para>The <c>-1</c> convention survives it: this runs only when a decision is recorded, so a
    /// round nobody has resolved keeps the -1 that says "nobody has said yet" rather than counting
    /// nought accepted.</para>
    /// </remarks>
    private void RecordClosing(string sessionId, string stage, int number)
    {
        using var write = _db.CreateCommand();
        write.CommandText = SQL_CLOSING;
        BindRound(write, sessionId, stage, number);
        write.ExecuteNonQuery();
    }

    private const string SQL_CLOSING = """
        UPDATE rounds SET
            accepted = (SELECT COUNT(*) FROM findings f WHERE f.round_id = rounds.id AND f.resolution = 'accept'),
            rejected = (SELECT COUNT(*) FROM findings f WHERE f.round_id = rounds.id AND f.resolution = 'reject')
        WHERE session_id = $session AND stage = $stage AND number = $number
        """;

    private void RecordSession(SessionState state)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ($id, $repo, $branch, $opened)
            ON CONFLICT(id) DO UPDATE SET repo_path = excluded.repo_path, branch = excluded.branch
            """;
        Bind(write, "$id", state.SessionId);
        Bind(write, "$repo", state.RepoPath);
        Bind(write, "$branch", state.Branch);
        Bind(write, "$opened", DateTime.UtcNow.ToString("O"));
        write.ExecuteNonQuery();
    }

    private long RecordRoundRow(SessionState state, RoundRecord round, RoundContext context)
    {
        using var write = _db.CreateCommand();
        // A round is written once when it finishes; a re-run of the same number replaces it rather
        // than doubling it, which is what a repeated round after a failed save would otherwise do.
        write.CommandText = """
            INSERT INTO rounds (session_id, stage, number, subject, status, verdict, gating,
                                started_utc, completed_utc, tokens_in, tokens_out, cost_usd,
                                plan_text, head_sha, base_ref, caller, agent_log,
                                caller_vendor, caller_client, caller_client_version, caller_model)
            VALUES ($session, $stage, $number, $subject, $status, $verdict, $gating,
                    $started, $completed, $tokensIn, $tokensOut, $cost,
                    $plan, $sha, $baseRef, $caller, $agentLog,
                    $vendor, $client, $clientVersion, $model)
            ON CONFLICT(session_id, stage, number) DO UPDATE SET
                subject = excluded.subject, status = excluded.status, verdict = excluded.verdict,
                gating = excluded.gating, completed_utc = excluded.completed_utc,
                tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out, cost_usd = excluded.cost_usd,
                plan_text = excluded.plan_text, head_sha = excluded.head_sha, base_ref = excluded.base_ref,
                caller = excluded.caller, agent_log = excluded.agent_log,
                caller_vendor = excluded.caller_vendor, caller_client = excluded.caller_client,
                caller_client_version = excluded.caller_client_version, caller_model = excluded.caller_model
            RETURNING id
            """;
        BindRound(write, state.SessionId, round.Stage, round.Number);
        Bind(write, "$subject", round.Subject);
        Bind(write, "$status", round.Status);
        Bind(write, "$verdict", round.Verdict);
        Bind(write, "$gating", round.GatingCount);
        Bind(write, "$started", round.StartedUtc.ToString("O"));
        Bind(write, "$completed", round.CompletedUtc.ToString("O"));
        Bind(write, "$tokensIn", round.TokensIn);
        Bind(write, "$tokensOut", round.TokensOut);
        Bind(write, "$cost", round.CostUsd is { } usd ? usd : DBNull.Value);
        // Coalesced HERE because `default(RoundContext)` runs no field initialiser: a caller that
        // passes nothing hands over a struct whose strings are null, and these columns are NOT NULL.
        Bind(write, "$plan", context.PlanText ?? string.Empty);
        Bind(write, "$sha", context.HeadSha ?? string.Empty);
        Bind(write, "$baseRef", context.BaseRef ?? string.Empty);
        Bind(write, "$caller", context.Caller ?? string.Empty);
        Bind(write, "$agentLog", context.AgentLog ?? string.Empty);
        // From the ROUND, which is the only thing that owns this: RoundRecord.Caller is the copy
        // taken when the round started, so a second client opening the same repo and branch cannot
        // change what an already-running round records. A second copy on RoundContext would have
        // been a second source of truth for one fact, and a caller that filled one and not the
        // other would have written 'unknown' over a perfectly good declaration. (gemini, round 2.)
        //
        // Coalesced here because the round's own field is nullable and absence is meaningful there:
        // a round from before the field said NOTHING, while these columns are NOT NULL and an
        // unidentified caller is 'unknown'. The database keeps the distinction as empty against
        // 'unknown'; the session file keeps it as absent against present.
        var calledBy = round.Caller ?? new Server.CallerDeclaration(Vendor: string.Empty);
        Bind(write, "$vendor", calledBy.Vendor);
        Bind(write, "$client", calledBy.Client);
        Bind(write, "$clientVersion", calledBy.ClientVersion);
        Bind(write, "$model", calledBy.Model);

        return (long)(write.ExecuteScalar() ?? 0L);
    }

    private void RecordReviewers(long roundId, RoundRecord round)
    {
        Run(_db, "DELETE FROM reviewers WHERE round_id = " + roundId);
        foreach (var reviewer in round.ReviewerStates)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO reviewers (round_id, provider, role, status, findings, seconds, note)
                VALUES ($round, $provider, $role, $status, $findings, $seconds, $note)
                """;
            Bind(write, "$round", roundId);
            Bind(write, "$provider", reviewer.Provider);
            Bind(write, "$role", reviewer.Role);
            Bind(write, "$status", reviewer.Status);
            Bind(write, "$findings", reviewer.Findings);
            Bind(write, "$seconds", reviewer.Seconds);
            Bind(write, "$note", reviewer.Note);
            write.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The round's findings, without losing what was already decided about them.
    /// </summary>
    /// <remarks>
    /// It deleted and re-inserted, and the gate caught what that costs: a round re-recorded after
    /// its decisions were made would take the resolutions with it. An upsert on (round, ordinal)
    /// leaves `resolution`, `reason` and `resolved_utc` exactly where they were. Findings BEYOND the
    /// new count are still removed — a round that answered fewer findings than last time must not
    /// keep the extra ones.
    /// </remarks>
    private void RecordFindings(long roundId, IReadOnlyList<Finding> findings, RoundContext context)
    {
        Run(_db, $"DELETE FROM findings WHERE round_id = {roundId} AND ordinal >= {findings.Count}");
        for (var ordinal = 0; ordinal < findings.Count; ordinal++)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO findings (round_id, ordinal, severity, category, file, line, title, why, fix,
                                      role, is_gating, providers, re_raised)
                VALUES ($round, $ordinal, $severity, $category, $file, $line, $title, $why, $fix,
                        $role, $gating, $providers, $reRaised)
                ON CONFLICT(round_id, ordinal) DO UPDATE SET
                    severity = excluded.severity, category = excluded.category, file = excluded.file,
                    line = excluded.line, title = excluded.title, why = excluded.why, fix = excluded.fix,
                    role = excluded.role, is_gating = excluded.is_gating, providers = excluded.providers,
                    re_raised = excluded.re_raised,
                    -- An ordinal is a POSITION in one reply, not an identity. When the finding at
                    -- this position is a different one, its predecessor's decision must not stay
                    -- attached to it — the gate called that out, and attaching a rejection to a
                    -- defect nobody rejected is worse than losing the record of it.
                    resolution   = CASE WHEN findings.title = excluded.title AND findings.file = excluded.file
                                        THEN findings.resolution ELSE '' END,
                    reason       = CASE WHEN findings.title = excluded.title AND findings.file = excluded.file
                                        THEN findings.reason ELSE '' END,
                    resolved_utc = CASE WHEN findings.title = excluded.title AND findings.file = excluded.file
                                        THEN findings.resolved_utc ELSE '' END
                """;
            var finding = findings[ordinal];
            Bind(write, "$round", roundId);
            Bind(write, "$ordinal", ordinal);
            Bind(write, "$severity", finding.Severity.ToString());
            Bind(write, "$category", finding.Category.ToString());
            Bind(write, "$file", finding.File);
            Bind(write, "$line", finding.Line);
            Bind(write, "$title", finding.Title);
            Bind(write, "$why", finding.Why);
            Bind(write, "$fix", finding.Fix);
            Bind(write, "$role", finding.Role);
            Bind(write, "$gating", finding.IsGating ? 1 : 0);
            Bind(write, "$providers", string.Join(",", finding.Providers));
            Bind(write, "$reRaised", context.WasReRaised(finding) ? 1 : 0);
            write.ExecuteNonQuery();
        }
    }

    /// <summary>Opens the run, before a single candidate is decided.</summary>
    /// <remarks>
    /// Before, not after. One write at the finish is the shape that cannot represent <i>running</i>,
    /// and a button whose whole job is to say what is happening needs exactly that state to exist
    /// while it happens. (Plan round, gemini and codex.)
    /// </remarks>
    public void StartCollectRun(string runId, string model)
    {
        var now = Now();
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO collect_runs (id, started_utc, heartbeat_utc, state, model)
            VALUES ($id, $now, $now, 'running', $model)
            ON CONFLICT(id) DO NOTHING
            """;
        Bind(write, "$id", runId);
        Bind(write, "$now", now);
        Bind(write, "$model", model);
        write.ExecuteNonQuery();
    }

    /// <summary>The run is alive, and this is how far it has got.</summary>
    /// <remarks>
    /// Written as each candidate is decided, on the beat that already happens there. Progress that is
    /// only written twice is not progress, and a heartbeat that is only written twice cannot tell a
    /// live run from an abandoned one — which is the difference the sweep depends on.
    /// </remarks>
    public void BeatCollectRun(string runId, int candidates, int picked, CollectTally tally)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE collect_runs
               SET heartbeat_utc = $now, state = 'running',
                   candidates = $candidates, picked = $picked,
                   collected = $collected, skipped = $skipped, failed = $failed
             -- A beat is PROOF OF LIFE, so it takes a swept run back. The sweep presumes a run
             -- gone after thirty silent minutes, and a laptop that slept longer than that is a
             -- run that was never gone at all: without this line it would keep working, keep
             -- claiming findings, and show `interrupted` for ever. Only a FINISHED run is out of
             -- reach, which is what the terminal guard below is for. (Code round 2, local.)
             WHERE id = $id AND finished_utc = ''
            """;
        BindTally(write, runId, candidates, picked, tally);
        write.ExecuteNonQuery();
    }

    /// <summary>The run is over, however it ended.</summary>
    /// <remarks>
    /// Called from a <c>finally</c>, so a throw on candidate three still writes a terminal state. It
    /// used to be reachable only by the happy path, which left the row — and the button — in flight
    /// for ever. (Plan round, codex.)
    /// </remarks>
    public void FinishCollectRun(
        string runId, string state, int candidates, int picked, CollectTally tally, string reasons)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE collect_runs
               SET heartbeat_utc = $now, finished_utc = $now, state = $state,
                   candidates = $candidates, picked = $picked,
                   collected = $collected, skipped = $skipped, failed = $failed, reasons = $reasons
             -- Only a run still believed to be RUNNING may end itself. Process A stops beating,
             -- process B sweeps its row to `interrupted`, and A then wakes and finishes: without
             -- this it would overwrite the sweep and the row would claim to be `done` after
             -- another process had already recorded that nobody knew. (Code round, codex.)
             WHERE id = $id AND state = 'running'
            """;
        BindTally(write, runId, candidates, picked, tally);
        Bind(write, "$state", state);
        Bind(write, "$reasons", reasons);
        write.ExecuteNonQuery();
    }

    private void BindTally(
        SqliteCommand write, string runId, int candidates, int picked, CollectTally tally)
    {
        Bind(write, "$id", runId);
        Bind(write, "$now", Now());
        Bind(write, "$candidates", candidates);
        Bind(write, "$picked", picked);
        Bind(write, "$collected", tally.Collected);
        Bind(write, "$skipped", tally.Skipped);
        Bind(write, "$failed", tally.Failed);
    }

    /// <summary>Ends the runs whose owner stopped saying it was alive.</summary>
    /// <remarks>
    /// <para><b>Marked, never deleted.</b> The id is a foreign key in all but name — every finding the
    /// run claimed carries it — so deleting the row would leave those rows naming a run that cannot be
    /// looked up. An interrupted run is a fact about the run, not an absence of one.</para>
    /// <para><b>And only a STALE one.</b> The panel reaches this database through one-shot
    /// invocations, so a sweep that took every row with no <c>finished_utc</c> would end the run that
    /// is happening right now, from the process that was asked to display it. The heartbeat is what
    /// tells the two apart. (Plan round, gemini — it caught this before it shipped.)</para>
    /// </remarks>
    /// <returns>How many runs were ended.</returns>
    public int SweepStaleCollectRuns(TimeSpan staleAfter)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE collect_runs
               SET state = 'interrupted', finished_utc = $now
             WHERE state = 'running' AND heartbeat_utc < $cutoff
            """;
        Bind(write, "$now", Now());
        Bind(write, "$cutoff", _time.GetUtcNow().UtcDateTime.Subtract(staleAfter).ToString("O"));

        return write.ExecuteNonQuery();
    }

    /// <summary>The most recent run, or an empty row when none has ever started.</summary>
    /// <remarks>Through <see cref="CollectRuns"/>, which is the one place this query lives.</remarks>
    public CollectRunRow LastCollectRun() => CollectRuns.Last(_db);

    /// <summary>How long a send may go without a beat before it is presumed gone.</summary>
    /// <remarks>
    /// <para>The collector's number, deliberately. It was ten minutes, on the reasoning that a send
    /// is bounded by a network request rather than by a model — and four code-round findings said the
    /// same thing back: a cutoff shorter than the work can legitimately take is a fence around a live
    /// process. Thirty minutes is longer than the panel's own cap on a send, so a run this sweeps has
    /// outlived the only thing that starts one.</para>
    /// <para>Being wrong in this direction costs nothing: a swept run's pairs are simply offered
    /// again, and the server is idempotent on the derived pair id. Being wrong in the other direction
    /// leaves a button that says "Sending…" for ever, with the only thing that would clear it being
    /// the send the button will not start.</para>
    /// </remarks>
    public static readonly TimeSpan SendPresumedGoneAfter = TimeSpan.FromMinutes(30);

    /// <summary>Opens the send, before the first request leaves.</summary>
    /// <remarks>
    /// Before, not after: one write at the finish is the shape that cannot represent <i>sending</i>,
    /// and a panel whose whole job is to say what is happening needs that state to exist while it
    /// happens. The pairs cannot carry it — they are marked only on an acknowledgement — which is
    /// exactly why this table exists. (Plan round, all three reviewers.)
    /// </remarks>
    /// <returns>
    /// Whether this call TOOK the lease. False means another send is already running and this one
    /// must not start.
    /// </returns>
    public bool StartUploadRun(string runId, string server, int offered)
    {
        var now = Now();
        using var write = _db.CreateCommand();

        // ONE STATEMENT, because two were a race. It read the last run, found it idle, and then
        // inserted — and two processes inside that gap both read idle and both inserted, each with
        // its own id, and both then offered the same waiting pairs. One connection is not a lock;
        // `WHERE NOT EXISTS` inside the insert is. (Code round 2, gemini and codex, independently.)
        //
        // `ON CONFLICT(id) DO NOTHING` stays for the case it was always for: the SAME run started
        // twice must not erase what the first one achieved.
        write.CommandText = """
            INSERT INTO upload_runs (id, started_utc, heartbeat_utc, state, server, offered)
            SELECT $id, $now, $now, 'running', $server, $offered
             WHERE NOT EXISTS (
                   SELECT 1 FROM upload_runs WHERE state = 'running' AND finished_utc = ''
             )
            ON CONFLICT(id) DO NOTHING
            """;
        Bind(write, "$id", runId);
        Bind(write, "$now", now);
        Bind(write, "$server", server);
        Bind(write, "$offered", offered);

        return write.ExecuteNonQuery() > 0;
    }

    /// <summary>The send is alive, and this is how far it has got.</summary>
    /// <remarks>
    /// A beat is PROOF OF LIFE, so it takes a swept run back — a machine that slept longer than the
    /// cutoff is a send that was never gone. Only a FINISHED send is out of reach, which the
    /// <c>finished_utc = ''</c> guard is for; the collector's beat carries the same line for the
    /// same reason.
    /// </remarks>
    public void BeatUploadRun(string runId, int offered, int sent, int duplicate, int refused)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE upload_runs
               SET heartbeat_utc = $now, state = 'running', offered = $offered,
                   sent = $sent, duplicate = $duplicate, refused = $refused
             WHERE id = $id AND finished_utc = ''
            """;
        Bind(write, "$now", Now());
        Bind(write, "$id", runId);
        Bind(write, "$offered", offered);
        Bind(write, "$sent", sent);
        Bind(write, "$duplicate", duplicate);
        Bind(write, "$refused", refused);
        write.ExecuteNonQuery();
    }

    /// <summary>The send is over, and this is what it came to.</summary>
    /// <remarks>
    /// <paramref name="trouble"/> is the CLI's own sentence rather than a flag, because the panel has
    /// to render it: "the server answered 502" sends a person somewhere and "failed" sends them
    /// nowhere.
    /// </remarks>
    public void EndUploadRun(
        string runId, string state, int offered, int sent, int duplicate, int refused, string trouble)
    {
        var now = Now();
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE upload_runs
               SET heartbeat_utc = $now, finished_utc = $now, state = $state, offered = $offered,
                   sent = $sent, duplicate = $duplicate, refused = $refused, trouble = $trouble
             WHERE id = $id
            """;
        Bind(write, "$now", now);
        Bind(write, "$id", runId);
        Bind(write, "$offered", offered);
        Bind(write, "$state", state);
        Bind(write, "$sent", sent);
        Bind(write, "$duplicate", duplicate);
        Bind(write, "$refused", refused);
        Bind(write, "$trouble", trouble);
        write.ExecuteNonQuery();
    }

    /// <summary>Ends the sends whose owner stopped saying it was alive.</summary>
    /// <remarks>
    /// Marked, never deleted, and only a STALE one — the same two rules
    /// <see cref="SweepStaleCollectRuns"/> carries, and for the same reasons: the panel reaches this
    /// database through one-shot invocations, so a sweep that took every unfinished row would end
    /// the send that is happening right now, from the process asked to display it.
    /// </remarks>
    /// <returns>How many sends were ended.</returns>
    public int SweepAbandonedUploads() => SweepAbandonedUploads(SendPresumedGoneAfter);

    /// <summary>The same sweep, with the cutoff named — which is what a test can drive.</summary>
    public int SweepAbandonedUploads(TimeSpan staleAfter)
    {
        using var write = _db.CreateCommand();
        // BOTH halves of "unfinished". `EndUploadRun` writes the state and `finished_utc` in one
        // statement, so `state = 'running'` is enough today — and only while that stays true, and
        // while nobody adds a state this sweep has never heard of. Naming the column costs nothing
        // and depends on neither. (Code round 2, local.)
        write.CommandText = """
            UPDATE upload_runs
               SET state = 'interrupted', finished_utc = $now
             WHERE state = 'running' AND finished_utc = '' AND heartbeat_utc < $cutoff
            """;
        Bind(write, "$now", Now());
        Bind(write, "$cutoff", _time.GetUtcNow().UtcDateTime.Subtract(staleAfter).ToString("O"));

        return write.ExecuteNonQuery();
    }

    /// <summary>The most recent send, or an empty row when none has ever started.</summary>
    /// <remarks>Through <see cref="UploadRuns"/>, which is the one place this query lives.</remarks>
    public UploadRunRow LastUploadRun() => UploadRuns.Last(_db);

    private string Now() => _time.GetUtcNow().UtcDateTime.ToString("O");

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    /// <summary>
    /// The three parameters that name ONE round — bound together, because they never travel apart.
    /// </summary>
    /// <remarks>
    /// Five statements in this class select or update by exactly this key and each repeated the three
    /// names. One helper is the reason they cannot drift, and it removes the repeated literals
    /// SonarCloud counted on the pull request.
    /// </remarks>
    private static void BindRound(SqliteCommand command, string sessionId, string stage, int number)
    {
        Bind(command, "$session", sessionId);
        Bind(command, "$stage", stage);
        Bind(command, "$number", number);
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
