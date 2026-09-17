using System.Globalization;

namespace CoaiBugs;

/// <summary>
/// What the admin surface asks the corpus: the listings, the counts, and one configuration check.
/// </summary>
/// <remarks>
/// <para><b>A partial of <see cref="Corpus"/>, not a separate type.</b> These reads need the one
/// guarded connection — the same <c>_gate</c> and the same transaction semantics as every write —
/// so extracting them into a class of their own would mean handing that connection out, which is
/// the property the gate exists to keep. What they ARE is a separate concern: nothing here is on
/// the ingest path, every one of them answers a question only an administrator asks, and together
/// they were what took `Corpus.cs` past this repository's 800-line maximum.</para>
/// <para>The two paging rules live here and differ for a reason about the TABLES rather than about
/// paging: <see cref="KeysPage"/> pages by <c>(created_utc, id)</c> because `api_keys` has an
/// implicit rowid that a <c>VACUUM</c> renumbers, while <see cref="AuditTrail"/> pages by
/// <c>admin_audit.id</c>, which is a declared <c>INTEGER PRIMARY KEY</c> and is preserved. See
/// <see cref="KeysCursor"/>.</para>
/// </remarks>
public sealed partial class Corpus
{
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

    /// <summary>The id of the key stored under this hash, or empty when no row holds it.</summary>
    /// <remarks>
    /// <para>For the startup check that refuses a credential configured as BOTH an administrator and
    /// a contributor key. It takes a HASH and not a key, because the caller is
    /// <see cref="AdminKeys"/>, which holds no keys.</para>
    /// <para>An ordinary indexed lookup and deliberately NOT the fixed-time walk
    /// <see cref="KeyFor"/> does: this compares two values the operator already holds, once, at
    /// startup, with no request and no attacker in the loop. Using the constant-time path here would
    /// suggest a threat that is not present and hide the one that is — a misconfiguration nobody
    /// notices.</para>
    /// </remarks>
    internal string KeyWithHash(string keyHash)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT id FROM api_keys WHERE key_hash = $hash";
            Bind(read, "$hash", keyHash);

            return read.ExecuteScalar() as string ?? string.Empty;
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

    /// <summary>The newest page of keys — the first request a reader makes.</summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter, because the default would have to be a null
    /// <see cref="KeysCursor"/> and the doctrine forbids one in business logic.
    /// <see cref="KeysCursor.Newest"/> says what it means at the call site.
    /// </remarks>
    public IReadOnlyList<KeyRow> KeysPage(int limit) => KeysPage(limit, KeysCursor.Newest);

    /// <summary>One page of keys, newest first, paged by the cursor of the row to read BEFORE.</summary>
    /// <param name="limit">Rows wanted; clamped to <see cref="MostAuditPage"/>.</param>
    /// <param name="before">Exclusive cursor: the page after this one starts at its last row's <c>Cursor</c>.</param>
    /// <remarks>
    /// <para><b>Keyset, not `OFFSET`, and the reason is not performance here.</b> With tens of rows an
    /// offset scan would cost nothing — but it is not INSERT-STABLE, which a reviewer caught: issuing
    /// a key between the first page and the second shifts every boundary after it, so a row is
    /// duplicated or hidden. The same defect the audit's paging was changed for, on a table small
    /// enough that the performance argument never applies.</para>
    /// <para><b>The cursor is <c>(created_utc, id)</c>, not `rowid`.</b> See
    /// <see cref="KeysCursor"/>: `api_keys` is keyed by a TEXT primary key, so its rowid is implicit
    /// and a `VACUUM` renumbers it — the order survives and the numbers do not, which silently moves
    /// a cursor a client is holding. `created_utc` sorts chronologically as ISO-8601 text and `id`
    /// breaks a tie inside one instant.</para>
    /// <para><b>The waiting count is a GROUPED join, not a per-row subquery.</b> It was
    /// <c>(SELECT COUNT(*) FROM quarantine q WHERE q.key_id = k.id)</c>, evaluated once per returned
    /// row while holding <see cref="_gate"/> — and quarantine's bound is 20 000 rows, so a page of
    /// fifty keys could visit a million rows with every ingest blocked behind it. One grouped pass
    /// plus the index step 4 adds answers the same question once. (Code round, gemini.)</para>
    /// </remarks>
    public IReadOnlyList<KeyRow> KeysPage(int limit, KeysCursor before)
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = """
                SELECT k.id, k.note, k.created_utc, k.revoked_utc, k.submissions,
                       k.last_seen_month, COALESCE(q.waiting, 0)
                  FROM api_keys k
                  LEFT JOIN (SELECT key_id, COUNT(*) AS waiting FROM quarantine GROUP BY key_id) q
                         ON q.key_id = k.id
                 WHERE $newest = 1
                    OR k.created_utc < $created
                    OR (k.created_utc = $created AND k.id < $id)
                 ORDER BY k.created_utc DESC, k.id DESC
                 LIMIT $limit
                """;
            Bind(read, "$newest", before.FromTheNewest ? 1 : 0);
            Bind(read, "$created", before.Created);
            Bind(read, "$id", before.Id);
            Bind(read, "$limit", Math.Clamp(limit, 1, MostAuditPage));
            using var rows = read.ExecuteReader();
            var page = new List<KeyRow>();
            while (rows.Read())
            {
                page.Add(new KeyRow(
                    new KeysCursor(rows.GetString(2), rows.GetString(0)),
                    new KeyId(rows.GetString(0)),
                    rows.GetString(1),
                    UtcInstant.Read(rows.GetString(2)),
                    rows.GetString(3) is { Length: > 0 } revoked ? UtcInstant.Read(revoked) : null,
                    new SubmissionCount(rows.GetInt32(4)),
                    UtcMonth.Read(rows.GetString(5)),
                    rows.GetInt32(6)));
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
}
