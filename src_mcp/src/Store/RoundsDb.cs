using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
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

    private RoundsDb(SqliteConnection db) => _db = db;

    /// <summary>Opens (and creates or migrates) the database, or answers null when it cannot.</summary>
    /// <remarks>
    /// Null rather than an exception: every caller's correct behaviour on a database that will not
    /// open is to carry on without one, and a method that can only be used inside a try/catch says
    /// that badly.
    /// </remarks>
    public static RoundsDb? Open(string dataDir, Serilog.ILogger log)
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
                $"Data Source={Path.Combine(dataDir, FileName)};Pooling=False;Default Timeout=5");
            db.Open();
            Migrate(db);

            return new RoundsDb(db);
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
    /// <para><c>CREATE TABLE IF NOT EXISTS</c> alone is not a migration and the gate was right to
    /// say so: it creates nothing when the table already exists, so a column added later is missing
    /// on every database an older build created — and a best-effort writer would swallow the error
    /// for ever. <c>user_version</c> records where the file has got to, and each step is applied in
    /// order, once.</para>
    /// <para>A step must be additive. Adding a column belongs here; anything that cannot be
    /// expressed as one is a reason to delete the file instead — it is a projection, and the
    /// sessions it projects are still on disk.</para>
    /// <para><b>A step and the number it bumps to are ONE transaction.</b> SQLite makes DDL
    /// transactional, and an <c>ALTER TABLE ADD COLUMN</c> is not idempotent the way
    /// <c>CREATE TABLE IF NOT EXISTS</c> is: a process killed between the alter and the
    /// <c>user_version</c> bump would re-run the step on the next open, answer
    /// <c>duplicate column name</c>, and — because <see cref="Open"/> answers null to ANY
    /// exception — leave a database that never opens again, with nothing here able to repair it.
    /// Raised by codex on the #174 plan round.</para>
    /// </remarks>
    private static void Migrate(SqliteConnection db) => Migrate(db, Schema.Steps);

    /// <param name="steps">
    /// The schema, as ordered steps — <see cref="Schema.Steps"/> everywhere but the test that
    /// proves the transaction is real.
    /// </param>
    /// <remarks>
    /// The seam exists because the claim above cannot be tested without a step that FAILS after an
    /// earlier statement in the same step has succeeded, and every real step succeeds. CodeRabbit
    /// asked for the defect-reproducing test on the pull request and was right that
    /// <c>TheMigrationRunsOnce…</c> does not detect a missing transaction: it passes either way.
    /// </remarks>
    internal static void Migrate(SqliteConnection db, IReadOnlyList<string> steps)
    {
        Run(db, "PRAGMA journal_mode=WAL"); // outside: a journal mode cannot be set in a transaction
        Run(db, "PRAGMA busy_timeout=5000");
        var version = Version(db);
        for (var step = version; step < steps.Count; step++)
        {
            using var applying = db.BeginTransaction();
            Run(db, steps[step]);
            Run(db, $"PRAGMA user_version={step + 1}");
            applying.Commit();
        }
    }

    private static int Version(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = "PRAGMA user_version";

        return Convert.ToInt32(read.ExecuteScalar() ?? 0);
    }

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
                reason, started_utc, ended_utc, seconds, tokens_in, tokens_out, cost_usd, problem, advice, alert)
            VALUES (
                $id, $caller, $kind, $repo, $branch, $sha, $vendor, $model, $turns, $status,
                $reason, $started, $ended, $seconds, $in, $out, $cost, $problem, $advice, $alert)
            ON CONFLICT(id) DO UPDATE SET
                turns = excluded.turns, status = excluded.status, reason = excluded.reason,
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
        var when = DateTime.UtcNow.ToString("O");
        foreach (var (ordinal, decision) in decisions)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                UPDATE findings SET resolution = $resolution, reason = $reason, resolved_utc = $when
                WHERE ordinal = $ordinal AND round_id = (
                    SELECT id FROM rounds WHERE session_id = $session AND stage = $stage AND number = $number)
                """;
            Bind(write, "$resolution", decision is Decision.Accepted ? "accept" : "reject");
            Bind(write, "$reason", decision is Decision.Rejected rejected ? rejected.Reason : string.Empty);
            Bind(write, "$when", when);
            Bind(write, "$ordinal", ordinal);
            BindRound(write, sessionId, stage, number);
            write.ExecuteNonQuery();
        }

        RecordClosing(sessionId, stage, number, decisions);
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
    private void RecordClosing(string sessionId, string stage, int number, IReadOnlyList<DecisionAt> decisions)
    {
        using var write = _db.CreateCommand();
        write.CommandText = SQL_CLOSING;
        Bind(write, "$accepted", decisions.Count(d => d.Decision is Decision.Accepted));
        Bind(write, "$rejected", decisions.Count(d => d.Decision is Decision.Rejected));
        BindRound(write, sessionId, stage, number);
        write.ExecuteNonQuery();
    }

    private const string SQL_CLOSING = """
        UPDATE rounds SET accepted = $accepted, rejected = $rejected
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
                                plan_text, head_sha, caller, agent_log,
                                caller_vendor, caller_client, caller_client_version, caller_model)
            VALUES ($session, $stage, $number, $subject, $status, $verdict, $gating,
                    $started, $completed, $tokensIn, $tokensOut, $cost,
                    $plan, $sha, $caller, $agentLog,
                    $vendor, $client, $clientVersion, $model)
            ON CONFLICT(session_id, stage, number) DO UPDATE SET
                subject = excluded.subject, status = excluded.status, verdict = excluded.verdict,
                gating = excluded.gating, completed_utc = excluded.completed_utc,
                tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out, cost_usd = excluded.cost_usd,
                plan_text = excluded.plan_text, head_sha = excluded.head_sha, caller = excluded.caller,
                agent_log = excluded.agent_log,
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
