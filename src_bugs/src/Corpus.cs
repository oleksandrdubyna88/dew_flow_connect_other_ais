using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Collecting;
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
    public static Corpus Open(string path)
    {
        var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = Schema;
        make.ExecuteNonQuery();

        return new Corpus(db);
    }

    /// <summary>
    /// Everything, in one statement, because this database is young enough to have no history.
    /// </summary>
    /// <remarks>
    /// <c>coai.db</c> migrates by appended steps because it has shipped and its files are in the
    /// field. This one has not; when it does, it gains the same discipline. Saying so here is the
    /// difference between a decision and an oversight.
    /// </remarks>
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS quarantine (
            entry_id        TEXT PRIMARY KEY,
            language        TEXT NOT NULL,
            skeleton_before TEXT NOT NULL,
            skeleton_after  TEXT NOT NULL,
            received_utc    TEXT NOT NULL,
            -- WHICH key sent it, never who holds the key. It is here so one contributor's mistake
            -- can be undone in bulk without touching anybody else's work.
            key_id          TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS corpus (
            entry_id        TEXT PRIMARY KEY,
            language        TEXT NOT NULL,
            skeleton_before TEXT NOT NULL,
            skeleton_after  TEXT NOT NULL,
            promoted_utc    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS api_keys (
            id           TEXT PRIMARY KEY,
            key_hash     TEXT NOT NULL UNIQUE,
            created_utc  TEXT NOT NULL,
            revoked_utc  TEXT NOT NULL DEFAULT '',
            -- OUR record of why a key exists, not the holder's data. "for the tuesday workshop",
            -- never a name or an address.
            note         TEXT NOT NULL DEFAULT '',
            -- A counter WITHOUT a clock, deliberately. With timestamps it would be a record of when
            -- a person we handed a key to was working. It is not a rate limit either, and the plan
            -- said so after a reviewer pointed out that a lifetime count has no window and no reset.
            submissions  INTEGER NOT NULL DEFAULT 0
        );
        """;

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
    public Kept Keep(string entryId, string language, string before, string after, string keyId, string nowUtc)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction();
            if (Promoted(entryId))
            {
                transaction.Commit();

                return Kept.AlreadyHeld;
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
            transaction.Commit();

            return stored ? Kept.Stored : Kept.AlreadyHeld;
        }
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

    /// <summary>Writes down that a key was used, without writing down when.</summary>
    /// <remarks>
    /// It was called <c>Counted</c>, which reads as a question. It increments a counter, and a
    /// counter that is deliberately clockless is exactly the thing whose name should say what it
    /// does. (Code round, local.)
    /// </remarks>
    public void RecordSubmission(string keyId)
    {
        lock (_gate)
        {
            using var write = _db.CreateCommand();
            write.CommandText = "UPDATE api_keys SET submissions = submissions + 1 WHERE id = $id";
            Bind(write, "$id", keyId);
            write.ExecuteNonQuery();
        }
    }

    /// <summary>Records a new key and answers its id. The key itself is the caller's to print once.</summary>
    public void Issue(string id, string keyHash, string note, string nowUtc)
    {
        lock (_gate)
        {
            using var write = _db.CreateCommand();
            write.CommandText = """
                INSERT INTO api_keys (id, key_hash, created_utc, note) VALUES ($id, $hash, $now, $note)
                """;
            Bind(write, "$id", id);
            Bind(write, "$hash", keyHash);
            Bind(write, "$now", nowUtc);
            Bind(write, "$note", note);
            write.ExecuteNonQuery();
        }
    }

    /// <summary>Ends a key. Answers whether there was one to end.</summary>
    public bool Revoke(string id, string nowUtc)
    {
        lock (_gate)
        {
            using var write = _db.CreateCommand();
            write.CommandText = "UPDATE api_keys SET revoked_utc = $now WHERE id = $id AND revoked_utc = ''";
            Bind(write, "$now", nowUtc);
            Bind(write, "$id", id);

            return write.ExecuteNonQuery() == 1;
        }
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

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    public void Dispose() => _db.Dispose();
}
