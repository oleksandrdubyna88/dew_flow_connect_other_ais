using CoaiMcp.Core.Findings;
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
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
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
    /// <para>A step must be idempotent and additive. Adding a column belongs here; anything that
    /// cannot be expressed as one is a reason to delete the file instead — it is a projection, and
    /// the sessions it projects are still on disk.</para>
    /// </remarks>
    private static void Migrate(SqliteConnection db)
    {
        Run(db, "PRAGMA journal_mode=WAL");
        Run(db, "PRAGMA busy_timeout=5000");
        var version = Version(db);
        for (var step = version; step < Schema.Steps.Length; step++)
        {
            Run(db, Schema.Steps[step]);
            Run(db, $"PRAGMA user_version={step + 1}");
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

    /// <summary>What the caller decided about each finding of the round it last answered.</summary>
    public void RecordDecisions(string sessionId, string stage, int number, IReadOnlyList<Decision> decisions)
    {
        using var transaction = _db.BeginTransaction();
        var when = DateTime.UtcNow.ToString("O");
        for (var ordinal = 0; ordinal < decisions.Count; ordinal++)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                UPDATE findings SET resolution = $resolution, reason = $reason, resolved_utc = $when
                WHERE ordinal = $ordinal AND round_id = (
                    SELECT id FROM rounds WHERE session_id = $session AND stage = $stage AND number = $number)
                """;
            Bind(write, "$resolution", decisions[ordinal] is Decision.Accepted ? "accept" : "reject");
            Bind(write, "$reason", decisions[ordinal] is Decision.Rejected rejected ? rejected.Reason : string.Empty);
            Bind(write, "$when", when);
            Bind(write, "$ordinal", ordinal);
            Bind(write, "$session", sessionId);
            Bind(write, "$stage", stage);
            Bind(write, "$number", number);
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
    private void RecordClosing(string sessionId, string stage, int number, IReadOnlyList<Decision> decisions)
    {
        using var write = _db.CreateCommand();
        write.CommandText = SQL_CLOSING;
        Bind(write, "$accepted", decisions.Count(d => d is Decision.Accepted));
        Bind(write, "$rejected", decisions.Count(d => d is Decision.Rejected));
        Bind(write, "$session", sessionId);
        Bind(write, "$stage", stage);
        Bind(write, "$number", number);
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
                                plan_text, head_sha, caller, agent_log)
            VALUES ($session, $stage, $number, $subject, $status, $verdict, $gating,
                    $started, $completed, $tokensIn, $tokensOut, $cost,
                    $plan, $sha, $caller, $agentLog)
            ON CONFLICT(session_id, stage, number) DO UPDATE SET
                subject = excluded.subject, status = excluded.status, verdict = excluded.verdict,
                gating = excluded.gating, completed_utc = excluded.completed_utc,
                tokens_in = excluded.tokens_in, tokens_out = excluded.tokens_out, cost_usd = excluded.cost_usd,
                plan_text = excluded.plan_text, head_sha = excluded.head_sha, caller = excluded.caller,
                agent_log = excluded.agent_log
            RETURNING id
            """;
        Bind(write, "$session", state.SessionId);
        Bind(write, "$stage", round.Stage);
        Bind(write, "$number", round.Number);
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

    private void RecordFindings(long roundId, IReadOnlyList<Finding> findings, RoundContext context)
    {
        Run(_db, "DELETE FROM findings WHERE round_id = " + roundId);
        for (var ordinal = 0; ordinal < findings.Count; ordinal++)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO findings (round_id, ordinal, severity, category, file, line, title, why, fix,
                                      role, is_gating, providers, re_raised)
                VALUES ($round, $ordinal, $severity, $category, $file, $line, $title, $why, $fix,
                        $role, $gating, $providers, $reRaised)
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

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
