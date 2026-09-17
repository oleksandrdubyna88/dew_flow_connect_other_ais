using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Storage;
using Microsoft.Data.Sqlite;

namespace CoaiBugs;

/// <summary>What storing a pair came to.</summary>
/// <remarks>
/// Two outcomes and not three: a refusal is decided by the alphabet BEFORE anything is stored, so
/// it is never an answer this type can give. <c>AlreadyHeld</c> is a success — the corpus holds the
/// pair, in quarantine or promoted, and the client may stop sending it.
/// </remarks>
public enum Kept
{
    /// <summary>Written to quarantine, waiting for a person.</summary>
    Stored,

    /// <summary>Already here. Nothing was written and nothing is wrong.</summary>
    AlreadyHeld,
}

/// <summary>
/// The corpus: quarantine, the keys that may write to it, and the index a person promotes into.
/// </summary>
/// <remarks>
/// <para><b>Quarantine is the first schema, not a later one.</b> The alphabet check stops leaked
/// identifiers; it cannot tell a real skeleton from a crafted one, and a corpus shown to people as
/// precedent is worth poisoning. Retrofitting quarantine after an index is live means re-auditing
/// everything already in it — so it exists before there is anything to audit.</para>
/// <para><b>The entry id is DERIVED here, not sent.</b> The plan first promised idempotency on a
/// client-generated id AND that only three fields cross; two reviewers said both cannot hold. They
/// both do if the id is a pure function of the payload: the server computes it, nothing extra
/// crosses, identical pairs from different people deduplicate for free, and there is no second copy
/// to disagree with the first.</para>
/// <para><b>Two kinds of write, and the difference is the transaction.</b> <see cref="Keep"/>,
/// <see cref="Issue"/>, <see cref="Revoke"/> and <see cref="Promote"/> each open and commit their own.
/// <see cref="Accept"/> opens ONE for a whole batch and hands the callee an <see cref="IngestScope"/>
/// that writes inside it, so the pairs, the key's count and the key's month are one commit — the
/// atomicity a reviewer asked for, made explicit rather than ambient.</para>
/// <para><b>Every write transaction is IMMEDIATE.</b> In WAL a deferred transaction that reads and
/// then writes can find its snapshot stale when another connection — a one-shot beside the running
/// server — committed in between, and SQLite refuses the upgrade with <c>SQLITE_BUSY_SNAPSHOT</c>,
/// which no busy timeout retries. Taking the write lock first makes every read inside the
/// transaction a read of the latest commit, which is also what lets <see cref="Accept"/> re-check a
/// key with nothing able to revoke it in between.</para>
/// </remarks>
public sealed class Corpus : IDisposable
{
    /// <summary>How many unreviewed pairs may wait before the door closes.</summary>
    /// <remarks>
    /// <b>This, and not `submissions`, is what bounds a key holder.</b> The body cap and the batch
    /// cap bound one REQUEST; nothing bounded ten thousand of them, and quarantine has no timer and
    /// no retention rule because a person reads every row. So the room has a size: past it the server
    /// refuses new pairs and says why, which is a queue a person must work through rather than a disk
    /// that fills. (Code round, codex.)
    /// </remarks>
    public const int MostWaiting = 20_000;

    /// <summary>How many administrative actions the audit keeps: the newest this many.</summary>
    /// <remarks>
    /// One row per administrative action at ~150 bytes is ~7 MB here, and decades of hand-driven
    /// administration. The trim runs inside the transaction of every write that could cross the
    /// mark, so the table is bounded from its first row rather than by a mode nobody will ask for.
    /// </remarks>
    public const int MostAudit = 50_000;

    /// <summary>The most audit rows one page answers, whatever a caller asks for.</summary>
    /// <remarks>
    /// The trail is read under <see cref="_gate"/>, and an unbounded page is a request that holds
    /// the server's one connection for as long as the table is long. Two hundred is the admin API's
    /// own ceiling (story 2), so the two numbers are one.
    /// </remarks>
    public const int MostAuditPage = 200;

    /// <summary>
    /// One connection, one server, many requests — so the connection is guarded.
    /// </summary>
    /// <remarks>
    /// <c>SqliteConnection</c> is not thread-safe and the route holds ONE for the process lifetime,
    /// so two concurrent uploads would use it at once. A lock rather than a connection per request:
    /// SQLite serialises writers anyway, this server is not a hot path, and one connection is what
    /// makes a transaction here mean what it says.
    /// </remarks>
    private readonly Lock _gate = new();

    private readonly SqliteConnection _db;

    private Corpus(SqliteConnection db) => _db = db;

    /// <summary>Opens the file and brings it up to date.</summary>
    /// <remarks>
    /// <para>Through the shared <see cref="SqliteMigrator"/>: <see cref="CorpusSchema.Steps"/> in
    /// order, <c>user_version</c> recording how far the file has come. The file <c>bugs-v0.1.0</c>
    /// created on the first host has every table of step 1 and <c>user_version = 0</c> — it was made
    /// by a build that ran the schema as one statement and never stamped it — so on this build's first
    /// open step 1 runs as a no-op (<c>IF NOT EXISTS</c>), the file is stamped 1, and the later steps
    /// add what is new. A test migrates exactly that shape, because a fresh file proves nothing.</para>
    /// <para>The connection's timeout comes from the runner too, so the pragma it sets and the
    /// provider's own retry are one number: a one-shot mode opening this file while the server is
    /// mid-write WAITS rather than failing with <c>SQLITE_BUSY</c>.</para>
    /// <para>A file that is not a database throws <see cref="SqliteException"/> from here. That is an
    /// infrastructure fault, not an expected answer, and the two process edges — the server's start
    /// and a one-shot's run — catch it and say what and where.</para>
    /// </remarks>
    public static Corpus Open(string path)
    {
        var db = new SqliteConnection(
            $"Data Source={path};Pooling=False;{SqliteMigrator.DefaultTimeoutFragment}");
        db.Open();
        SqliteMigrator.Migrate(db, CorpusSchema.Steps);

        return new Corpus(db);
    }

    /// <summary>The id of a pair: a pure function of what the pair IS.</summary>
    /// <remarks>
    /// <see cref="PairId"/> lives in the shared core because the CLIENT derives it too — an
    /// acknowledgement is matched to a local pair by this id rather than by the answer's order. This
    /// stays as the name the server reads at its own boundary.
    /// </remarks>
    public static string IdOf(string language, string before, string after) =>
        PairId.Of(language, before, after);

    /// <summary>
    /// Stores a pair on its own, or says it was already held.
    /// </summary>
    /// <remarks>
    /// <para>Its own transaction. Inside a batch the same write goes through
    /// <see cref="IngestScope.Keep"/>, which is the batch's transaction; this is for a caller that
    /// has no batch — a test, an import — and there is deliberately no way to ask the corpus whether
    /// a batch happens to be open.</para>
    /// <para><b>The corpus is asked BEFORE the insert, in one transaction.</b> It inserted first and
    /// asked afterwards — so a pair that had already been promoted (and therefore deleted from
    /// quarantine) was written back into quarantine, answered <c>duplicate</c>, and then sat in
    /// `--waiting` for ever: promoting it again is a no-op, because the corpus already holds that
    /// id. Three reviewers found the same row, from three directions. (Code round, codex/gemini.)</para>
    /// </remarks>
    /// <returns>Whether it was stored, and the id it is stored under.</returns>
    public (Kept Kept, string EntryId) Keep(
        string language, string before, string after, KeyId key, UtcMonth month)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction(deferred: false);
            var kept = KeepInside(language, before, after, key, month);
            transaction.Commit();

            return kept;
        }
    }

    /// <summary>The write itself, inside whatever transaction the caller holds.</summary>
    /// <remarks>
    /// <para>The entry id is DERIVED HERE, not taken from the caller. It was a parameter, and a
    /// parameter is a way for an importer or a replay to store the same three fields under two
    /// different ids — which is precisely the idempotency this table exists to have. (Code round,
    /// codex.)</para>
    /// <para><b><c>received_utc</c> is written EMPTY, for ever.</b> It is an exact instant beside
    /// <c>key_id</c>, which the promise forbids; step 1 is frozen so the column cannot go, and it is
    /// <c>NOT NULL</c> without a default so it must be written. What a pair carries about its arrival
    /// is <c>received_month</c>; what orders the queue is <c>rowid</c>.</para>
    /// </remarks>
    internal (Kept Kept, string EntryId) KeepInside(
        string language, string before, string after, KeyId key, UtcMonth month)
    {
        var entryId = IdOf(language, before, after);
        if (Promoted(entryId))
        {
            return (Kept.AlreadyHeld, entryId);
        }

        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO quarantine
                (entry_id, language, skeleton_before, skeleton_after, received_utc, received_month, key_id)
            VALUES ($id, $language, $before, $after, '', $month, $key)
            ON CONFLICT(entry_id) DO NOTHING
            """;
        Bind(write, "$id", entryId);
        Bind(write, "$language", language);
        Bind(write, "$before", before);
        Bind(write, "$after", after);
        Bind(write, "$month", month.Value);
        Bind(write, "$key", key.Value);

        // DO NOTHING answers zero rows for a pair quarantine already holds. That is a success for
        // the caller: it may stop sending it.
        var stored = write.ExecuteNonQuery() == 1;

        return (stored ? Kept.Stored : Kept.AlreadyHeld, entryId);
    }

    /// <summary>How many pairs are waiting for a person right now.</summary>
    public int WaitingCount()
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM quarantine";

            return Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    private bool Promoted(string entryId)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT 1 FROM corpus WHERE entry_id = $id";
        Bind(read, "$id", entryId);

        return read.ExecuteScalar() is not null;
    }

    /// <summary>
    /// One accepted ingest: the pairs it stores, the key's count and the key's month — one commit.
    /// </summary>
    /// <remarks>
    /// <para><b>The key is checked again, inside the transaction.</b> The gate authenticated it a
    /// moment ago, and a <c>--revoke</c> can commit in that moment; the transaction is IMMEDIATE, so
    /// once this check passes nothing can revoke the key before the writes commit. A key found not in
    /// force answers <see cref="Accepted{T}.KeyNotInForce"/> with nothing written and nothing counted,
    /// and the caller answers 401. (Code round, codex.)</para>
    /// <para><b>One UPDATE moves the count and the month.</b> They were two — the month written only
    /// when it differed, "so a busy key rewrites its row at most once a month" — and a reviewer
    /// pointed out that the counter rewrites the same row on every ingest, so the conditional saved
    /// nothing and its justification was false. <c>submissions = submissions + 1</c> stays in SQL,
    /// never read-then-write, which loses an increment when two ingests race.</para>
    /// <para><b>Only an ACCEPTED ingest reaches here.</b> A 401, a 429 and a malformed body are
    /// answered before it, and an administrative one-shot never calls it — each is a test, because
    /// the month is a promise about what the server records and not only a column.</para>
    /// </remarks>
    public Accepted<T> Accept<T>(KeyId key, UtcMonth month, Func<IngestScope, T> take)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction(deferred: false);

            // CHECKED AGAIN, inside the transaction. The gate authenticated this key a moment ago
            // and a `--revoke` one-shot can commit in that moment — a separate process, on the same
            // file. Trusting the gate's answer here would store pairs, count a submission and stamp
            // a month for a key the operator had already stopped. (Code round, codex.)
            if (!InForce(key))
            {
                transaction.Commit();

                return new Accepted<T>.KeyNotInForce();
            }

            // SPENT on the way out, whichever way that is. The scope holds this corpus, the key and
            // the month, so a caller who kept it could write through it after the commit — outside
            // the transaction, without the key being re-checked and without the counter moving.
            // A `finally` rather than a line after the call, because a throwing callback escapes
            // here too and leaves the same live capability behind. (Code round, codex.)
            var scope = new IngestScope(this, key, month);
            T answer;
            try
            {
                answer = take(scope);
            }
            finally
            {
                scope.Spend();
            }

            Record(key, month);
            transaction.Commit();

            return new Accepted<T>.Stored(answer);
        }
    }

    /// <summary>Does this key exist and remain unrevoked?</summary>
    /// <remarks>
    /// <c>revoked_utc = ''</c> and not <c>IS NULL</c>, because the column ships
    /// <c>NOT NULL DEFAULT ''</c>: an unrevoked key holds the empty string and a revoked one holds a
    /// time, so there is no NULL to test for and a <c>IS NULL</c> check would match nothing at all.
    /// </remarks>
    private bool InForce(KeyId key)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT 1 FROM api_keys WHERE id = $id AND revoked_utc = ''";
        Bind(read, "$id", key.Value);

        return read.ExecuteScalar() is not null;
    }

    private void Record(KeyId key, UtcMonth month)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE api_keys SET submissions = submissions + 1, last_seen_month = $month WHERE id = $id
            """;
        Bind(write, "$month", month.Value);
        Bind(write, "$id", key.Value);
        write.ExecuteNonQuery();
    }

    /// <summary>The key this request may write as, or empty.</summary>
    /// <remarks>
    /// <para><b>Hashed with a server secret and compared in constant time.</b> A bare hash makes a
    /// stolen database a rainbow-table exercise; an ordinary string comparison leaks the prefix
    /// through timing. Both were named by the plan round.</para>
    /// <para><b>A revoked key and an unknown key answer the same thing</b>, so the endpoint cannot be
    /// used to discover which keys exist. A revoked key is one whose <c>revoked_utc</c> is non-empty,
    /// and this never answers one — which is what makes revoking stop an ingest, at the gate; the
    /// check is repeated inside <see cref="Accept"/> for the moment between.</para>
    /// </remarks>
    public string KeyFor(string presented, string secret)
    {
        if (presented.Length == 0)
        {
            return string.Empty;
        }

        var wanted = HashOf(presented, secret);
        lock (_gate)
        {
            return Matching(wanted);
        }
    }

    private string Matching(string wanted)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT id, key_hash FROM api_keys WHERE revoked_utc = ''";
        using var rows = read.ExecuteReader();
        var found = string.Empty;
        while (rows.Read())
        {
            // Every row is compared, and the loop does not stop early: returning on the first match
            // would make the answer's TIMING depend on where in the table the key sits.
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(rows.GetString(1)), Encoding.UTF8.GetBytes(wanted)))
            {
                found = rows.GetString(0);
            }
        }

        return found;
    }

    /// <summary>What is stored for a key. Never the key.</summary>
    public static string HashOf(string key, string secret) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(key)));

    /// <summary>
    /// Records a new key and the administrator who minted it, in one transaction. The key itself is
    /// the caller's to print once.
    /// </summary>
    /// <remarks>
    /// The audit row and the trim that bounds the table commit with the key, so the caller can never
    /// report an issuance that was not audited — nor an audit of a key that was not issued. The key's
    /// creation time is the audit's instant: one clock, one stamp, two columns.
    /// </remarks>
    public void Issue(KeyId id, string keyHash, string note, Audit by)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction(deferred: false);
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO api_keys (id, key_hash, created_utc, note) VALUES ($id, $hash, $now, $note)
                """;
            Bind(write, "$id", id.Value);
            Bind(write, "$hash", keyHash);
            Bind(write, "$now", by.At.Stored);
            Bind(write, "$note", note);
            write.ExecuteNonQuery();
            Audited(AuditAction.Issue, id, by);
            transaction.Commit();
        }
    }

    /// <summary>
    /// Stops a key, and says which of the three things happened rather than whether one did.
    /// </summary>
    /// <remarks>
    /// <para><b>A <c>bool</c> could not carry this.</b> The admin route owes a 404 for a key that
    /// never existed and a 200 with <c>changed: false</c> for one already revoked, and `false` meant
    /// both. Asking a second question to tell them apart would be a second query with a race between
    /// it and this one — a key revoked in the gap would answer 404 for a key that exists. So the read
    /// and the write happen in ONE transaction and the answer names the case. (Plan round, gemini.)
    /// </para>
    /// <para>An already-revoked key reports the time of its ORIGINAL revocation, not the time of this
    /// attempt: that is the fact an administrator is asking for, and the attempt is in the audit
    /// only if it changed something.</para>
    /// </remarks>
    public Revoked Revoke(KeyId id, Audit by)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction(deferred: false);
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT revoked_utc FROM api_keys WHERE id = $id";
            Bind(read, "$id", id.Value);
            if (read.ExecuteScalar() is not string already)
            {
                transaction.Commit();

                return new Revoked.NoSuchKey();
            }

            if (already.Length > 0)
            {
                transaction.Commit();

                return new Revoked.Already(UtcInstant.Read(already));
            }

            using var write = _db.CreateCommand();
            write.CommandText = "UPDATE api_keys SET revoked_utc = $now WHERE id = $id AND revoked_utc = ''";
            Bind(write, "$now", by.At.Stored);
            Bind(write, "$id", id.Value);
            write.ExecuteNonQuery();
            Audited(AuditAction.Revoke, id, by);
            transaction.Commit();

            return new Revoked.Now(by.At);
        }
    }

    /// <summary>
    /// The audit row for a mutation, and the trim that keeps the table bounded — inside the caller's
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>target</c> is the key's id and nothing else: not its note, never the key, never its
    /// hash. That is the privacy boundary of this table and a test pins it.</para>
    /// <para><b>The trim is inside the transaction on purpose.</b> The write that crosses the bound and
    /// the deletion that restores it commit together, so the table is never observed over the bound
    /// and a crash between them cannot leave it there.</para>
    /// </remarks>
    private void Audited(AuditAction action, KeyId target, Audit by)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO admin_audit (admin_id, action, target, at_utc)
            VALUES ($admin, $action, $target, $at)
            """;
        Bind(write, "$admin", by.Who.Value);
        Bind(write, "$action", action.Word());
        Bind(write, "$target", target.Value);
        Bind(write, "$at", by.At.Stored);
        write.ExecuteNonQuery();
        TrimAudit(MostAudit);
    }

    /// <summary>Keeps the newest <paramref name="keep"/> audit rows and deletes the rest.</summary>
    /// <remarks>
    /// Internal, with the bound as a parameter, so a test can cross a small one and watch exactly the
    /// newest rows survive; the real bound is crossed too, from a seeded table. The production path
    /// is <see cref="Audited"/>, which always passes <see cref="MostAudit"/>.
    /// </remarks>
    internal void TrimAuditTo(int keep)
    {
        lock (_gate)
        {
            TrimAudit(keep);
        }
    }

    private void TrimAudit(int keep)
    {
        // An INDEXED cutoff: the one id at the boundary is found through the primary key, and the
        // delete is a range below it. `DELETE … WHERE id NOT IN (SELECT …)` is a latency spike and a
        // lock risk inside a request. With fewer rows than `keep` the subquery is NULL, the
        // comparison is NULL, and nothing is deleted.
        using var trim = _db.CreateCommand();
        trim.CommandText = """
            DELETE FROM admin_audit
             WHERE id <= (SELECT id FROM admin_audit ORDER BY id DESC LIMIT 1 OFFSET $keep)
            """;
        Bind(trim, "$keep", keep);
        trim.ExecuteNonQuery();
    }

    /// <summary>What is waiting for a person to look at it, in order of arrival.</summary>
    /// <remarks>
    /// <para><paramref name="skip"/> exists because the view showed the first fifty and said "50
    /// waiting", which is indistinguishable from fifty being all of them — and nothing could reach row
    /// 51 without disposing of the first fifty one at a time. (Code round, codex/local.)</para>
    /// <para>Ordered by <c>rowid</c>, never by a time: the only thing the order is for is a person
    /// reading the queue oldest-first, and insertion order among live rows is exactly that.</para>
    /// </remarks>
    public IReadOnlyList<(string EntryId, string Language, string Before, string After)> Waiting(
        int limit, int skip = 0)
    {
        lock (_gate)
        {
            return Reading(limit, skip);
        }
    }

    /// <summary>One waiting pair, or empty strings when nothing is waiting under that id.</summary>
    /// <remarks>
    /// `--promote` reads the row BEFORE moving it, so it can print what it moved rather than only
    /// that it moved something — the one defence against a mistyped id, since there is no undo.
    /// </remarks>
    public (string EntryId, string Language, string Before, string After) Find(string entryId)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = """
                SELECT entry_id, language, skeleton_before, skeleton_after
                  FROM quarantine WHERE entry_id = $id
                """;
            Bind(read, "$id", entryId);
            using var rows = read.ExecuteReader();

            return rows.Read()
                ? (rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3))
                : (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private IReadOnlyList<(string EntryId, string Language, string Before, string After)> Reading(
        int limit, int skip)
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT entry_id, language, skeleton_before, skeleton_after
              FROM quarantine ORDER BY rowid LIMIT $limit OFFSET $skip
            """;
        Bind(read, "$limit", limit);
        Bind(read, "$skip", skip);
        using var rows = read.ExecuteReader();
        var waiting = new List<(string, string, string, string)>();
        while (rows.Read())
        {
            waiting.Add((rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3)));
        }

        return waiting;
    }

    /// <summary>Moves one pair out of quarantine and into the corpus.</summary>
    /// <remarks>
    /// <para>One transaction, so an interrupted promotion leaves the row in quarantine — the state it
    /// started in — rather than in both places or in neither.</para>
    /// <para><b>The quarantine row goes whether or not the insert wrote anything.</b> It was only
    /// deleted when the INSERT reported a row, so a pair the corpus already held stayed in the queue
    /// and no command could ever clear it. What this promises is "the corpus holds it and quarantine
    /// does not", and that is true in both cases. (Code round, gemini.)</para>
    /// <para><paramref name="at"/> is an exact time and may be: <c>corpus</c> carries no <c>key_id</c>,
    /// so a promotion is attributable to nobody who contributed.</para>
    /// </remarks>
    public bool Promote(string entryId, UtcInstant at)
    {
        lock (_gate)
        {
            return Moving(entryId, at);
        }
    }

    private bool Moving(string entryId, UtcInstant at)
    {
        using var transaction = _db.BeginTransaction(deferred: false);
        using var here = _db.CreateCommand();
        here.CommandText = "SELECT 1 FROM quarantine WHERE entry_id = $id";
        Bind(here, "$id", entryId);
        if (here.ExecuteScalar() is null)
        {
            transaction.Commit();

            return false;
        }

        using var move = _db.CreateCommand();
        move.CommandText = """
            INSERT INTO corpus (entry_id, language, skeleton_before, skeleton_after, promoted_utc)
            SELECT entry_id, language, skeleton_before, skeleton_after, $now
              FROM quarantine WHERE entry_id = $id
            ON CONFLICT(entry_id) DO NOTHING
            """;
        Bind(move, "$now", at.Stored);
        Bind(move, "$id", entryId);
        move.ExecuteNonQuery();

        using var drop = _db.CreateCommand();
        drop.CommandText = "DELETE FROM quarantine WHERE entry_id = $id";
        Bind(drop, "$id", entryId);
        drop.ExecuteNonQuery();

        transaction.Commit();

        return true;
    }

    /// <summary>Throws a submission away without promoting it.</summary>
    public bool Reject(string entryId)
    {
        lock (_gate)
        {
            using var drop = _db.CreateCommand();
            drop.CommandText = "DELETE FROM quarantine WHERE entry_id = $id";
            Bind(drop, "$id", entryId);

            return drop.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>How many pairs the corpus holds.</summary>
    public int Held()
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM corpus";

            return Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>What a key has done — or that there is no such key, which is not the same as nothing.</summary>
    /// <remarks>
    /// It answered zero and empty for a key that does not exist, and a reviewer was right that absent
    /// is not zero: an operator reading "0 submissions, never used" about a mistyped id would believe
    /// the key exists and is idle.
    /// </remarks>
    public Usage UsageOf(KeyId key)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT submissions, last_seen_month FROM api_keys WHERE id = $id";
            Bind(read, "$id", key.Value);
            using var rows = read.ExecuteReader();

            return rows.Read()
                ? new Usage.Known(new SubmissionCount(rows.GetInt32(0)), UtcMonth.Read(rows.GetString(1)))
                : new Usage.NoSuchKey();
        }
    }

    /// <summary>
    /// A batch from an ADMINISTRATOR: stored and attributed, but counted against no key.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is a separate path and not <see cref="Accept{T}"/> with a different id.</b>
    /// `Accept` re-checks `InForce` inside its transaction against `api_keys`, and an administrator
    /// is deliberately not a row there — so the plan's "an admin key may also upload" was, as
    /// written, either a rejection, a silent weakening of that guard, or an uncounted write. All
    /// three reviewers refused to let it stand. This is the resolution the operator took.</para>
    /// <para><b>The in-force re-check is not bypassed; it does not apply.</b> That check exists for
    /// a race a contributor key really has: a `--revoke` one-shot can commit between the gate and
    /// the write, in another process, against the same file. An administrator's credentials live in
    /// <see cref="AdminKeys"/>, built once at startup and immutable for the process — rotation means
    /// editing the secret and redeploying, which RESTARTS this server. There is no window between
    /// the gate and the write for the set to change in, so there is nothing here to guard.</para>
    /// <para><b>No counter and no month on any key row</b>, because there is no row: an
    /// administrator's uploads are invisible to the Users tab, which the plan accepts and story 3
    /// must say out loud. The quarantine row still carries the derived `admin-…` id in `key_id`, so
    /// one administrator's mistake can be undone in bulk exactly like anybody else's — and it still
    /// carries `received_month` and no clock, because the rule is about the column beside a key id,
    /// not about whose key it is.</para>
    /// </remarks>
    public T AcceptAdmin<T>(AdminId admin, UtcMonth month, Func<IngestScope, T> take)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction(deferred: false);

            // The derived id goes in `key_id`: that column records WHICH CREDENTIAL sent the pair,
            // and for an administrator that is this. It is not a contributor key and no row in
            // `api_keys` will ever match it, which is exactly why nothing is counted below.
            var scope = new IngestScope(this, new KeyId(admin.Value), month);
            T answer;
            try
            {
                answer = take(scope);
            }
            finally
            {
                scope.Spend();
            }

            transaction.Commit();

            return answer;
        }
    }

    /// <summary>How many keys exist, revoked ones included.</summary>
    /// <remarks>
    /// Cheap enough to answer on every page, unlike the audit's: the growth budget puts `api_keys`
    /// at tens of rows a year, hand-issued, and revoked rows are the audit trail so none are deleted.
    /// This is why `/admin/keys` carries `total` and `/admin/audit` does not.
    /// </remarks>
    public int KeysTotal()
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM api_keys";

            return Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One page of keys, newest first, paged by the cursor of the row to read BEFORE.</summary>
    /// <param name="limit">Rows wanted; clamped to <see cref="MostAuditPage"/>.</param>
    /// <param name="before">Exclusive cursor: the page after this one starts at its last row's <c>Cursor</c>.</param>
    /// <remarks>
    /// <para><b>Keyset, not `OFFSET`, and the reason is not performance here.</b> With tens of rows an
    /// offset scan would cost nothing — but it is not INSERT-STABLE, which a reviewer caught: issuing
    /// a key between the first page and the second shifts every boundary after it, so a row is
    /// duplicated or hidden. The same defect the audit's paging was changed for, on a table small
    /// enough that the performance argument never applies.</para>
    /// <para><b>The cursor is `rowid`</b>, because `api_keys.id` is a hex string and sorts
    /// lexicographically rather than chronologically, so it cannot order "newest first". `rowid` is
    /// monotonic for inserts and this table never deletes — retirement is deliberately none, revoked
    /// rows being the trail — so it is never reused. That dependency is why it is safe, and why it is
    /// written down.</para>
    /// </remarks>
    public IReadOnlyList<KeyRow> KeysPage(int limit, long before = long.MaxValue)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = """
                SELECT k.rowid, k.id, k.note, k.created_utc, k.revoked_utc, k.submissions,
                       k.last_seen_month,
                       (SELECT COUNT(*) FROM quarantine q WHERE q.key_id = k.id)
                  FROM api_keys k
                 WHERE k.rowid < $before
                 ORDER BY k.rowid DESC
                 LIMIT $limit
                """;
            Bind(read, "$before", before);
            Bind(read, "$limit", Math.Clamp(limit, 1, MostAuditPage));
            using var rows = read.ExecuteReader();
            var page = new List<KeyRow>();
            while (rows.Read())
            {
                page.Add(new KeyRow(
                    rows.GetInt64(0),
                    new KeyId(rows.GetString(1)),
                    rows.GetString(2),
                    UtcInstant.Read(rows.GetString(3)),
                    rows.GetString(4) is { Length: > 0 } revoked ? UtcInstant.Read(revoked) : null,
                    new SubmissionCount(rows.GetInt32(5)),
                    UtcMonth.Read(rows.GetString(6)),
                    rows.GetInt32(7)));
            }

            return page;
        }
    }

    /// <summary>The audit, newest first, one indexed range per page.</summary>
    /// <param name="limit">Rows wanted; clamped to <see cref="MostAuditPage"/>.</param>
    /// <param name="before">Read rows older than this id; the default reads from the newest.</param>
    /// <remarks>
    /// Oldest-first paging by <c>OFFSET</c> made page one the first day of the deployment and recent
    /// history an <c>O(N)</c> scan while holding the gate. Keyset paging by id — the page after this
    /// one starts at its last row's <c>Id</c> — costs one indexed range whatever the table has grown
    /// to, and the newest actions are the ones an administrator opens the trail for.
    /// </remarks>
    public IReadOnlyList<AuditRow> AuditTrail(int limit, long before = long.MaxValue)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = """
                SELECT id, admin_id, action, target, at_utc
                  FROM admin_audit WHERE id < $before ORDER BY id DESC LIMIT $limit
                """;
            Bind(read, "$before", before);
            Bind(read, "$limit", Math.Clamp(limit, 1, MostAuditPage));
            using var rows = read.ExecuteReader();
            var trail = new List<AuditRow>();
            while (rows.Read())
            {
                trail.Add(new AuditRow(
                    rows.GetInt64(0),
                    AdminId.Stored(rows.GetString(1)),
                    AuditActions.Parse(rows.GetString(2)),
                    new KeyId(rows.GetString(3)),
                    UtcInstant.Read(rows.GetString(4))));
            }

            return trail;
        }
    }

    /// <summary>How many audit rows there are.</summary>
    public int AuditCount()
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM admin_audit";

            return Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    public void Dispose() => _db.Dispose();
}
