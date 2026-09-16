using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CoaiBugs;

/// <summary>What became of one submitted pair.</summary>
/// <remarks>
/// Three outcomes and not two, because a batch answers per item. <c>Duplicate</c> is a SUCCESS — the
/// pair is already held, the client may mark it sent, and nothing is wrong — while <c>Refused</c> is
/// the client's own defect coming back with the word that caused it.
/// </remarks>
public enum Took
{
    /// <summary>Stored in quarantine, waiting for a person.</summary>
    Accepted,

    /// <summary>Already held. The client may stop sending it.</summary>
    Duplicate,

    /// <summary>The alphabet refused it. Something upstream leaked.</summary>
    Refused,
}

/// <summary>One item's fate, with the reason when there is one.</summary>
public readonly record struct Fate(string EntryId, Took Took, string Why);

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
    /// Stable across machines and across runs, carries nothing, and makes two people who found the
    /// same defect in the same library one row rather than two.
    /// </remarks>
    public static string IdOf(string language, string before, string after)
    {
        var bytes = Encoding.UTF8.GetBytes($"{language}\0{before}\0{after}");

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Stores a pair, or says it was already held.</summary>
    public Took Keep(string entryId, string language, string before, string after, string keyId, string nowUtc)
    {
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

        // DO NOTHING answers zero rows for a pair already held — in quarantine OR promoted, since a
        // promoted row keeps its id. That is a success for the caller: it may stop sending it.
        return write.ExecuteNonQuery() == 1 && !Promoted(entryId) ? Took.Accepted : Took.Duplicate;
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
    public void Counted(string keyId)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE api_keys SET submissions = submissions + 1 WHERE id = $id";
        Bind(write, "$id", keyId);
        write.ExecuteNonQuery();
    }

    /// <summary>Records a new key and answers its id. The key itself is the caller's to print once.</summary>
    public void Issue(string id, string keyHash, string note, string nowUtc)
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

    /// <summary>Ends a key. Answers whether there was one to end.</summary>
    public bool Revoke(string id, string nowUtc)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE api_keys SET revoked_utc = $now WHERE id = $id AND revoked_utc = ''";
        Bind(write, "$now", nowUtc);
        Bind(write, "$id", id);

        return write.ExecuteNonQuery() == 1;
    }

    /// <summary>What is waiting for a person to look at it.</summary>
    public IReadOnlyList<(string EntryId, string Language, string Before, string After)> Waiting(int limit)
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT entry_id, language, skeleton_before, skeleton_after
              FROM quarantine ORDER BY received_utc, entry_id LIMIT $limit
            """;
        Bind(read, "$limit", limit);
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
    /// One transaction, so an interrupted promotion leaves the row in quarantine — the state it
    /// started in — rather than in both places or in neither.
    /// </remarks>
    public bool Promote(string entryId, string nowUtc)
    {
        using var transaction = _db.BeginTransaction();
        using var move = _db.CreateCommand();
        move.CommandText = """
            INSERT INTO corpus (entry_id, language, skeleton_before, skeleton_after, promoted_utc)
            SELECT entry_id, language, skeleton_before, skeleton_after, $now
              FROM quarantine WHERE entry_id = $id
            ON CONFLICT(entry_id) DO NOTHING
            """;
        Bind(move, "$now", nowUtc);
        Bind(move, "$id", entryId);
        var moved = move.ExecuteNonQuery() == 1;

        if (moved)
        {
            using var drop = _db.CreateCommand();
            drop.CommandText = "DELETE FROM quarantine WHERE entry_id = $id";
            Bind(drop, "$id", entryId);
            drop.ExecuteNonQuery();
        }

        transaction.Commit();

        return moved;
    }

    /// <summary>Throws a submission away without promoting it.</summary>
    public bool Reject(string entryId)
    {
        using var drop = _db.CreateCommand();
        drop.CommandText = "DELETE FROM quarantine WHERE entry_id = $id";
        Bind(drop, "$id", entryId);

        return drop.ExecuteNonQuery() == 1;
    }

    /// <summary>How many pairs the corpus holds.</summary>
    public int Held()
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM corpus";

        return Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    public void Dispose() => _db.Dispose();
}
