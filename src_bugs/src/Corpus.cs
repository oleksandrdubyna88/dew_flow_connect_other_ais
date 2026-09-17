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
    /// administration. The sweep runs inside the transaction of every write that could cross the
    /// mark, so the table is bounded from its first row rather than by a mode nobody will ask for.
    /// </remarks>
    public const int MostAudit = 50_000;

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

    /// <summary>
    /// Whether an <see cref="Ingesting"/> batch holds the transaction, so the writes inside it join
    /// it rather than open one of their own.
    /// </summary>
    /// <remarks>
    /// <c>SqliteConnection</c> refuses a nested transaction, and the batch is what makes the accepted
    /// pairs, the counter and the month ONE commit. Set and cleared under <see cref="_gate"/>, which
    /// is reentrant, so a <see cref="Keep"/> called from inside the batch sees it.
    /// </remarks>
    private bool _batching;

    private Corpus(SqliteConnection db) => _db = db;

    /// <summary>Opens the file and brings it up to date.</summary>
    /// <remarks>
    /// <para>Through the shared <see cref="SqliteMigrator"/>: <see cref="CorpusSchema.Steps"/> in
    /// order, <c>user_version</c> recording how far the file has come. The file <c>bugs-v0.1.0</c>
    /// created on the first host has every table of step 1 and <c>user_version = 0</c> — it was made
    /// by a build that ran the schema as one statement and never stamped it — so on this build's first
    /// open step 1 runs as a no-op (<c>IF NOT EXISTS</c>), the file is stamped 1, and step 2 adds
    /// what is new. A test migrates exactly that shape, because a fresh file proves nothing.</para>
    /// <para>The connection's timeout comes from the runner too, so the pragma it sets and the
    /// provider's own retry are one number: a one-shot mode opening this file while the server is
    /// mid-write WAITS rather than failing with <c>SQLITE_BUSY</c>.</para>
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
    /// Stores a pair, or says it was already held.
    /// </summary>
    /// <remarks>
    /// <b>The corpus is asked BEFORE the insert, in one transaction.</b> It inserted first and asked
    /// afterwards — so a pair that had already been promoted (and therefore deleted from quarantine)
    /// was written back into quarantine, answered <c>duplicate</c>, and then sat in `--waiting` for
    /// ever: promoting it again is a no-op, because the corpus already holds that id. Three reviewers
    /// found the same row, from three directions. The check and the insert are now one statement's
    /// worth of truth. (Code round, codex/gemini.)
    /// </remarks>
    /// <returns>Whether it was stored, and the id it is stored under.</returns>
    public (Kept Kept, string EntryId) Keep(
        string language, string before, string after, string keyId, string nowUtc)
    {
        // DERIVED HERE, not taken from the caller. It was a parameter, and a parameter is a way for
        // an importer or a replay to store the same three fields under two different ids — which is
        // precisely the idempotency this table exists to have. The identity of a pair belongs to the
        // boundary that persists it. (Code round, codex.)
        var entryId = IdOf(language, before, after);
        lock (_gate)
        {
            // Its own transaction when called alone; the batch's when called from `Ingesting`,
            // which is how the pairs and the key's row become one commit.
            return Transacting(() => Keeping(entryId, language, before, after, keyId, nowUtc));
        }
    }

    private (Kept Kept, string EntryId) Keeping(
        string entryId, string language, string before, string after, string keyId, string nowUtc)
    {
        if (Promoted(entryId))
        {
            return (Kept.AlreadyHeld, entryId);
        }

        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO quarantine
                (entry_id, language, skeleton_before, skeleton_after, received_utc, key_id)
            VALUES ($id, $language, $before, $after, $now, $key)
            ON CONFLICT(entry_id) DO NOTHING
            """;
        Bind(write, "$id", entryId);
        Bind(write, "$language", language);
        Bind(write, "$before", before);
        Bind(write, "$after", after);
        Bind(write, "$now", nowUtc);
        Bind(write, "$key", keyId);

        // DO NOTHING answers zero rows for a pair quarantine already holds. That is a success for
        // the caller: it may stop sending it.
        var stored = write.ExecuteNonQuery() == 1;

        return (stored ? Kept.Stored : Kept.AlreadyHeld, entryId);
    }

    /// <summary>Runs <paramref name="body"/> in a transaction: its own, or the open batch's.</summary>
    private T Transacting<T>(Func<T> body)
    {
        if (_batching)
        {
            return body();
        }

        using var transaction = _db.BeginTransaction();
        var result = body();
        transaction.Commit();

        return result;
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

    /// <summary>The key this request may write as, or empty.</summary>
    /// <remarks>
    /// <para><b>Hashed with a server secret and compared in constant time.</b> A bare hash makes a
    /// stolen database a rainbow-table exercise; an ordinary string comparison leaks the prefix
    /// through timing. Both were named by the plan round.</para>
    /// <para><b>A revoked key and an unknown key answer the same thing</b>, so the endpoint cannot be
    /// used to discover which keys exist.</para>
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
    /// One accepted ingest: the pairs it stores, the key's count, and the key's month — one commit.
    /// </summary>
    /// <remarks>
    /// <para><b>One transaction</b>, so a kill between the writes cannot leave an accepted batch
    /// with a stale month or an uncounted key. <see cref="Keep"/>, called from inside
    /// <paramref name="take"/>, joins this transaction rather than opening its own.</para>
    /// <para><b>The counter is <c>submissions = submissions + 1</c> in SQL</b> — never read-then-write,
    /// which loses an increment when two ingests race. <b>The month is conditional</b>
    /// (<c>AND last_seen_month &lt;&gt; $month</c>), so a busy key rewrites its row at most once a
    /// month; whether it did is answered, so a test can see a same-month row left alone.</para>
    /// <para><b>Only an ACCEPTED ingest reaches here.</b> A 401, a 429 and a malformed body are
    /// answered before it, and an administrative one-shot never calls it — each of those is a test,
    /// because the month is a promise about what the server records and not only a column.</para>
    /// </remarks>
    public Ingested<T> Ingesting<T>(string keyId, LastSeenMonth month, Func<T> take)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction();
            _batching = true;
            try
            {
                var answer = take();
                Count(keyId);
                var advanced = Touch(keyId, month);
                transaction.Commit();

                return new Ingested<T>(answer, advanced);
            }
            finally
            {
                _batching = false;
            }
        }
    }

    private void Count(string keyId)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE api_keys SET submissions = submissions + 1 WHERE id = $id";
        Bind(write, "$id", keyId);
        write.ExecuteNonQuery();
    }

    private bool Touch(string keyId, LastSeenMonth month)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE api_keys SET last_seen_month = $month
             WHERE id = $id AND last_seen_month <> $month
            """;
        Bind(write, "$month", month.Value);
        Bind(write, "$id", keyId);

        return write.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// Records a new key and the administrator who minted it, in one transaction. The key itself is
    /// the caller's to print once.
    /// </summary>
    /// <remarks>
    /// The audit row and the sweep that bounds the table commit with the key, so the caller can never
    /// report an issuance that was not audited — nor an audit of a key that was not issued.
    /// </remarks>
    public void Issue(string id, string keyHash, string note, Audit by)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction();
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO api_keys (id, key_hash, created_utc, note) VALUES ($id, $hash, $now, $note)
                """;
            Bind(write, "$id", id);
            Bind(write, "$hash", keyHash);
            Bind(write, "$now", by.AtUtc);
            Bind(write, "$note", note);
            write.ExecuteNonQuery();
            Audited(AuditAction.Issue, id, by);
            transaction.Commit();
        }
    }

    /// <summary>Ends a key, audited. Answers whether there was one to end.</summary>
    /// <remarks>
    /// <b>A revoked key is one whose <c>revoked_utc</c> is non-empty</b>, and <see cref="KeyFor"/>
    /// never answers one — so the refusal happens before the rate limiter and before any write, and
    /// a test proves that revoking actually stops an ingest. A revoke that changed nothing (no such
    /// key, or already revoked) writes no audit row: there was no mutation to leave unaudited.
    /// </remarks>
    public bool Revoke(string id, Audit by)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction();
            using var write = _db.CreateCommand();
            write.CommandText = "UPDATE api_keys SET revoked_utc = $now WHERE id = $id AND revoked_utc = ''";
            Bind(write, "$now", by.AtUtc);
            Bind(write, "$id", id);
            var changed = write.ExecuteNonQuery() == 1;
            if (changed)
            {
                Audited(AuditAction.Revoke, id, by);
            }

            transaction.Commit();

            return changed;
        }
    }

    /// <summary>
    /// The audit row for a mutation, and the sweep that keeps the table bounded — inside the
    /// caller's transaction.
    /// </summary>
    /// <remarks>
    /// <c>target</c> is the key's id and nothing else: not its note, never the key, never its hash.
    /// That is the privacy boundary of this table and a test pins it.
    /// </remarks>
    private void Audited(AuditAction action, string target, Audit by)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            INSERT INTO admin_audit (admin_id, action, target, at_utc)
            VALUES ($admin, $action, $target, $at)
            """;
        Bind(write, "$admin", by.AdminId);
        Bind(write, "$action", action.Word());
        Bind(write, "$target", target);
        Bind(write, "$at", by.AtUtc);
        write.ExecuteNonQuery();
        Sweeping(MostAudit);
    }

    /// <summary>Keeps the newest <paramref name="keep"/> audit rows and deletes the rest.</summary>
    /// <remarks>
    /// Internal, with the bound as a parameter, so a test can cross a small one and watch exactly the
    /// newest rows survive; the real bound is crossed too, from a seeded table. The production path
    /// is <see cref="Audited"/>, which always passes <see cref="MostAudit"/>.
    /// </remarks>
    internal void SweepAudit(int keep)
    {
        lock (_gate)
        {
            Sweeping(keep);
        }
    }

    private void Sweeping(int keep)
    {
        // An INDEXED cutoff: the one id at the boundary is found through the primary key, and the
        // delete is a range below it. `DELETE … WHERE id NOT IN (SELECT …)` is a latency spike and a
        // lock risk inside a request. With fewer rows than `keep` the subquery is NULL, the
        // comparison is NULL, and nothing is deleted.
        using var sweep = _db.CreateCommand();
        sweep.CommandText = """
            DELETE FROM admin_audit
             WHERE id <= (SELECT id FROM admin_audit ORDER BY id DESC LIMIT 1 OFFSET $keep)
            """;
        Bind(sweep, "$keep", keep);
        sweep.ExecuteNonQuery();
    }

    /// <summary>What is waiting for a person to look at it.</summary>
    /// <remarks>
    /// <paramref name="skip"/> exists because the view showed the first fifty and said "50 waiting",
    /// which is indistinguishable from fifty being all of them — and nothing could reach row 51
    /// without disposing of the first fifty one at a time. (Code round, codex/local.)
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
              FROM quarantine ORDER BY received_utc, entry_id LIMIT $limit OFFSET $skip
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
    /// </remarks>
    public bool Promote(string entryId, string nowUtc)
    {
        lock (_gate)
        {
            return Moving(entryId, nowUtc);
        }
    }

    private bool Moving(string entryId, string nowUtc)
    {
        using var transaction = _db.BeginTransaction();
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
        Bind(move, "$now", nowUtc);
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

    /// <summary>What a key has done: its lifetime count and the month it was last used.</summary>
    /// <remarks>Zero and empty for a key that does not exist, which is also what "never used" reads as.</remarks>
    public KeyUsage UsageOf(string keyId)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT submissions, last_seen_month FROM api_keys WHERE id = $id";
            Bind(read, "$id", keyId);
            using var rows = read.ExecuteReader();

            return rows.Read()
                ? new KeyUsage(rows.GetInt32(0), rows.GetString(1))
                : new KeyUsage(0, string.Empty);
        }
    }

    /// <summary>The audit, oldest first, paged.</summary>
    public IReadOnlyList<AuditRow> AuditTrail(int limit, int skip = 0)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = """
                SELECT id, admin_id, action, target, at_utc
                  FROM admin_audit ORDER BY id LIMIT $limit OFFSET $skip
                """;
            Bind(read, "$limit", limit);
            Bind(read, "$skip", skip);
            using var rows = read.ExecuteReader();
            var trail = new List<AuditRow>();
            while (rows.Read())
            {
                trail.Add(new AuditRow(
                    rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4)));
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

/// <summary>What an accepted ingest came to: the answer, and whether the key's month moved.</summary>
public sealed record Ingested<T>(T Answer, bool MonthAdvanced);

/// <summary>A key's lifetime count and the month it was last used — empty for never.</summary>
public sealed record KeyUsage(int Submissions, string LastSeenMonth);

/// <summary>One administrative action, as the audit holds it.</summary>
public sealed record AuditRow(long Id, string AdminId, string Action, string Target, string AtUtc);
