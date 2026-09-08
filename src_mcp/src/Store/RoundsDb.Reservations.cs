using CoaiMcp.Core.Rounds;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// What a dispatch attempt was allowed to do.
/// </summary>
/// <remarks>
/// The reason this is not a state: reading the row back after an UPDATE tells the winner and the
/// loser the same thing — <c>running</c> — so both would go on to launch reviewers. Only the caller
/// whose UPDATE actually moved the row out of <c>reserved</c> has the right to run, and the only
/// evidence of that is how many rows it changed.
/// </remarks>
public enum RoundClaim
{
    /// <summary>This call moved the round from reserved to running. It, and only it, may dispatch.</summary>
    Claimed,

    /// <summary>This locator is already running — another caller claimed it, or this one did before.</summary>
    AlreadyRunning,

    /// <summary>Another round of the same session holds the one running slot.</summary>
    SessionBusy,

    /// <summary>Already finished. Its stored answer is the reply.</summary>
    AlreadyCompleted,

    /// <summary>Dispatched once and ended without an answer; it cannot be run again.</summary>
    Spent,

    /// <summary>No such locator here.</summary>
    Unknown,
}

/// <summary>
/// Who is running a round: this machine, this process, this instance of the server.
/// </summary>
/// <remarks>
/// <para>Recorded so a later server can tell whose round it is looking at. It is NOT a lease —
/// nothing here is compared against a clock, and a review that takes an hour is as alive as one
/// that takes a minute.</para>
/// <para><paramref name="Instance"/> is a fresh value per <c>PanelService</c>, because a
/// process id alone is reused: a dead round's pid can belong to something unrelated within the
/// hour, and a reader that trusted the pid would call a stranger's process this round's owner.</para>
/// </remarks>
public sealed record RoundOwner(string Machine, int Pid, string Instance)
{
    public static RoundOwner Here(string instance) =>
        new(Environment.MachineName, Environment.ProcessId, instance);
}

/// <summary>An addressable round to finish in the same transaction as the row it belongs to.</summary>
/// <param name="SessionCommit">
/// The session update this round owes, decided inside the same transaction that stores its answer.
/// Nothing can commit to SQLite and a JSON file at once, so the intention is written down here and
/// applied afterwards — by the normal path immediately, and by <c>open</c> when a crash intervened.
/// </param>
public sealed record RoundCompletion(
    RoundLocator Locator,
    string ResultJson,
    SessionCommit? SessionCommit = null);

/// <summary>Why a reservation could not be resumed under a token that already exists.</summary>
public sealed record ReservationMismatch(string Field, string Reserved, string Requested);

/// <summary>One reserved round: its locator, what it is pinned to, and where it has got to.</summary>
public sealed record ReservedRound(
    RoundLocator Locator,
    string ClientToken,
    string Stage,
    string State,
    string RepoPath,
    string Branch,
    SubjectAttestation Subject,
    string ReservedUtc,
    string StartedUtc,
    string CompletedUtc,
    long? RoundRef,
    string ResultJson,
    string OwnerMachine = "",
    int OwnerPid = 0,
    string OwnerInstance = "")
{
    /// <summary>The state a read-back reports, which is not always the state stored.</summary>
    public string ReadBackState => RoundLifecycle.ReadBackFor(State);
}

/// <summary>
/// The reservation half: a locator handed out before anything runs, and the answer it earned.
/// </summary>
/// <remarks>
/// <para>Unlike the projection in the other half of this class, these writes are NOT best-effort.
/// A reservation that was not stored is a round nobody can ever name, so the caller is told the
/// reservation failed rather than handed a locator this server will not recognise.</para>
/// </remarks>
public sealed partial class RoundsDb
{
    /// <summary>
    /// Reserves a round, or returns the one this caller already reserved under the same token.
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotent by the caller's token, arbitrated by the database.</b> Two clients racing
    /// on one repo+branch both INSERT; the unique index on (session, token) decides. A caller
    /// retrying after a crash sends the token it used before and gets its original locator back,
    /// which is the difference between a safe retry and a second round.</para>
    /// <para>No reviewer runs here and no budget moves: reserving is writing a row.</para>
    /// </remarks>
    /// <returns>
    /// The stored reservation, and whether THIS call created it. A caller told <c>Created: false</c>
    /// is resuming a round it already reserved, which is a different thing to report than a fresh one.
    /// </returns>
    /// <param name="mismatch">
    /// Set when the token names an existing reservation whose repository, branch, base or subject
    /// is not the one being asked for. The reservation comes back unchanged and is NOT presented as
    /// matching: a token is an idempotency key for ONE round, not a name for whatever the caller
    /// now wants. Answering silently with the old pin is how a caller reviews yesterday's commit
    /// while believing it asked about today's.
    /// </param>
    public (ReservedRound Round, bool Created) Reserve(
        string sessionId,
        string clientToken,
        string stage,
        string repoPath,
        string branch,
        SubjectAttestation subject,
        Func<string> newRoundId,
        out ReservationMismatch? mismatch)
    {
        mismatch = null;
        using var transaction = _db.BeginTransaction();
        // The read comes first so the ordinary resume costs one statement, but it is not what makes
        // this safe: two callers can both read nothing and both insert. The index arbitrates, and
        // the loser reads the winner's row below.
        if (Find(sessionId, clientToken) is { } existing)
        {
            transaction.Commit();
            mismatch = Disagreement(existing, repoPath, branch, subject);

            return (existing, false);
        }

        var locator = new RoundLocator(RoundLocator.Provider, sessionId, newRoundId());
        using (var write = _db.CreateCommand())
        {
            write.CommandText = """
                INSERT INTO round_locators (
                    provider_id, session_id, round_id, client_token, stage, state,
                    repo_path, branch, repo_identity, base_ref, base_sha, head_sha, tree_sha,
                    subject_hash, reserved_utc)
                VALUES ($provider, $session, $round, $token, $stage, 'reserved',
                        $repo, $branch, $identity, $baseRef, $baseSha, $headSha, $treeSha,
                        $subjectHash, $reserved)
                ON CONFLICT (session_id, client_token) DO NOTHING
                """;
            Bind(write, "$provider", locator.ProviderId);
            Bind(write, "$session", locator.SessionId);
            Bind(write, "$round", locator.RoundId);
            Bind(write, "$token", clientToken);
            Bind(write, "$stage", stage);
            Bind(write, "$repo", repoPath);
            Bind(write, "$branch", branch);
            Bind(write, "$identity", subject.RepoIdentity);
            Bind(write, "$baseRef", subject.BaseRef);
            Bind(write, "$baseSha", subject.BaseSha);
            Bind(write, "$headSha", subject.HeadSha);
            Bind(write, "$treeSha", subject.TreeSha);
            Bind(write, "$subjectHash", subject.SubjectHash);
            Bind(write, "$reserved", DateTime.UtcNow.ToString("O"));
            write.ExecuteNonQuery();
        }

        // Read back rather than returning what was built: on a lost race the row is the OTHER
        // caller's, and handing this one the locator it failed to insert would give two callers one
        // round under two names.
        var stored = Find(sessionId, clientToken)
            ?? throw new InvalidOperationException("the reservation was neither written nor found");
        transaction.Commit();

        mismatch = Disagreement(stored, repoPath, branch, subject);

        return (stored, stored.Locator.Matches(locator));
    }

    /// <summary>The first field on which an existing reservation is not what is being asked for.</summary>
    /// <remarks>
    /// The subject hash alone would catch every content difference, but naming the FIELD is what
    /// makes the refusal actionable: "you reserved this token against another branch" sends a caller
    /// somewhere, and "the hashes differ" does not.
    /// </remarks>
    private static ReservationMismatch? Disagreement(
        ReservedRound existing, string repoPath, string branch, SubjectAttestation subject)
    {
        if (!string.Equals(existing.RepoPath, repoPath, StringComparison.OrdinalIgnoreCase))
        {
            return new ReservationMismatch("repoPath", existing.RepoPath, repoPath);
        }

        if (existing.Branch != branch)
        {
            return new ReservationMismatch("branch", existing.Branch, branch);
        }

        if (existing.Subject.BaseRef != subject.BaseRef)
        {
            return new ReservationMismatch("baseRef", existing.Subject.BaseRef, subject.BaseRef);
        }

        return existing.Subject.SubjectHash == subject.SubjectHash
            ? null
            : new ReservationMismatch("subject", existing.Subject.SubjectHash, subject.SubjectHash);
    }

    /// <summary>
    /// Claims a reserved round for dispatch. Only the caller that MOVED it may run reviewers.
    /// </summary>
    /// <remarks>
    /// <para>The claim is the row COUNT, not the state read back afterwards. Both the winner and
    /// the loser of a race see <c>running</c> when they re-read, so a decision made on the state
    /// lets both launch a fan-out — a doubled non-idempotent call, which is the exact failure the
    /// locator exists to prevent.</para>
    /// <para>A UNIQUE violation is the session's one running slot already held by ANOTHER round:
    /// the session file is read-modify-write and its round ordinal comes from it, so two rounds in
    /// flight over one session corrupt the trail whichever locators they carry.</para>
    /// </remarks>
    /// <param name="owner">
    /// Who is about to run it. Written in the same statement that claims the round, so a
    /// running row always names its owner and there is no window in which one does not.
    /// </param>
    public (RoundClaim Claim, ReservedRound? Round) Begin(RoundLocator locator, RoundOwner owner)
    {
        using var transaction = _db.BeginTransaction();
        var before = Read(locator);
        if (before is null)
        {
            transaction.Commit();

            return (RoundClaim.Unknown, null);
        }

        int moved;
        try
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                UPDATE round_locators
                SET state = 'running', started_utc = $started,
                    owner_machine = $machine, owner_pid = $pid, owner_instance = $instance
                WHERE provider_id = $provider AND session_id = $session AND round_id = $round
                  AND state = 'reserved'
                """;
            Bind(write, "$started", DateTime.UtcNow.ToString("O"));
            Bind(write, "$machine", owner.Machine);
            Bind(write, "$pid", owner.Pid);
            Bind(write, "$instance", owner.Instance);
            BindLocator(write, locator);
            moved = write.ExecuteNonQuery();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            transaction.Rollback();

            return (RoundClaim.SessionBusy, before);
        }

        var stored = Read(locator);
        transaction.Commit();

        return moved == 1
            ? (RoundClaim.Claimed, stored)
            : (before.State switch
            {
                RoundLifecycle.Running => RoundClaim.AlreadyRunning,
                RoundLifecycle.Completed => RoundClaim.AlreadyCompleted,
                _ => RoundClaim.Spent,
            }, stored);
    }

    /// <summary>
    /// Stores the answer and closes the round — the whole result, in one transaction.
    /// </summary>
    /// <remarks>
    /// The state, the local round it owns and the answer land together or not at all. A completion
    /// that wrote the state and lost the answer would read back as a completed round with nothing
    /// to say, which is the shape a caller cannot distinguish from a review that found nothing.
    /// </remarks>
    public ReservedRound? Complete(RoundLocator locator, long? roundRef, string resultJson)
    {
        using var transaction = _db.BeginTransaction();
        CompleteHere(locator, roundRef, resultJson);
        var stored = Read(locator);
        transaction.Commit();

        return stored;
    }

    /// <summary>
    /// The completion write itself, for a caller that already holds a transaction.
    /// </summary>
    /// <remarks>
    /// Guarded by <c>state &lt;&gt; 'completed'</c>, which is what makes a finished round FINISHED:
    /// a second completion carrying a different answer changes nothing and the first result stands.
    /// The answer is what every later check was made against, so the last writer must not win.
    /// </remarks>
    internal void CompleteHere(RoundLocator locator, long? roundRef, string resultJson)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE round_locators
            SET state = 'completed', completed_utc = $completed, round_ref = $roundRef,
                result_json = $result, owner_machine = '', owner_pid = 0, owner_instance = ''
            WHERE provider_id = $provider AND session_id = $session AND round_id = $round
              AND state <> 'completed'
            """;
        Bind(write, "$completed", DateTime.UtcNow.ToString("O"));
        Bind(write, "$roundRef", roundRef is { } id ? id : DBNull.Value);
        Bind(write, "$result", resultJson);
        BindLocator(write, locator);
        write.ExecuteNonQuery();
    }

    /// <summary>The round was dispatched and provably ended without an answer.</summary>
    public void Fail(RoundLocator locator)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE round_locators
            SET state = 'failed', completed_utc = $completed, owner_machine = '', owner_pid = 0, owner_instance = ''
            WHERE provider_id = $provider AND session_id = $session AND round_id = $round
              AND state <> 'completed'
            """;
        Bind(write, "$completed", DateTime.UtcNow.ToString("O"));
        BindLocator(write, locator);
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// The rounds of this session that still say `running`.
    /// </summary>
    /// <remarks>
    /// Read by the one caller that can prove nothing is running — the holder of the session
    /// claim. This decides nothing on its own; it hands over the rows and their owners so the
    /// caller can refuse to touch another machine's.
    /// </remarks>
    public IReadOnlyList<ReservedRound> RunningRounds(string repoPath, string branch)
    {
        using var read = _db.CreateCommand();
        read.CommandText = $"{SelectColumns} WHERE repo_path = $repo AND branch = $branch AND state = 'running'";
        Bind(read, "$repo", repoPath);
        Bind(read, "$branch", branch);

        var running = new List<ReservedRound>();
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            running.Add(Materialise(rows));
        }

        return running;
    }

    /// <summary>The row for exactly this locator, or null when this server has never issued it.</summary>
    public ReservedRound? Read(RoundLocator locator)
    {
        using var read = _db.CreateCommand();
        read.CommandText = $"{SelectColumns} WHERE provider_id = $provider AND session_id = $session AND round_id = $round";
        BindLocator(read, locator);

        return One(read);
    }

    private ReservedRound? Find(string sessionId, string clientToken)
    {
        using var read = _db.CreateCommand();
        read.CommandText = $"{SelectColumns} WHERE session_id = $session AND client_token = $token";
        Bind(read, "$session", sessionId);
        Bind(read, "$token", clientToken);

        return One(read);
    }

    private const string SelectColumns = """
        SELECT provider_id, session_id, round_id, client_token, stage, state, repo_path, branch,
               repo_identity, base_ref, base_sha, head_sha, tree_sha, reserved_utc, started_utc,
               completed_utc, round_ref, result_json, owner_machine, owner_pid, owner_instance
        FROM round_locators
        """;

    private static ReservedRound? One(SqliteCommand read)
    {
        using var row = read.ExecuteReader();

        return row.Read() ? Materialise(row) : null;
    }

    private static ReservedRound Materialise(SqliteDataReader row)
    {
        return new ReservedRound(
            new RoundLocator(row.GetString(0), row.GetString(1), row.GetString(2)),
            row.GetString(3),
            row.GetString(4),
            row.GetString(5),
            row.GetString(6),
            row.GetString(7),
            new SubjectAttestation(row.GetString(8), row.GetString(9), row.GetString(10), row.GetString(11), row.GetString(12)),
            row.GetString(13),
            row.GetString(14),
            row.GetString(15),
            row.IsDBNull(16) ? null : row.GetInt64(16),
            row.GetString(17),
            row.GetString(18),
            row.GetInt32(19),
            row.GetString(20));
    }

    /// <summary>The id of the local round row for a session's stage+number, if one was written.</summary>
    /// <remarks>
    /// The link between a locator and the round it owns. Looked up rather than passed down, because
    /// the projection writes that row and the reservation must not depend on its return value
    /// reaching another layer intact.
    /// </remarks>
    public long? RoundRowId(string sessionId, string stage, int number)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT id FROM rounds WHERE session_id = $session AND stage = $stage AND number = $number";
        Bind(read, "$session", sessionId);
        Bind(read, "$stage", stage);
        Bind(read, "$number", number);
        var value = read.ExecuteScalar();

        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static void BindLocator(SqliteCommand command, RoundLocator locator)
    {
        Bind(command, "$provider", locator.ProviderId);
        Bind(command, "$session", locator.SessionId);
        Bind(command, "$round", locator.RoundId);
    }
}
