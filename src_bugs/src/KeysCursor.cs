using System.Globalization;

namespace CoaiBugs;

/// <summary>
/// Where a page of keys resumes: the <c>(created_utc, id)</c> of the row to read BEFORE.
/// </summary>
/// <remarks>
/// <para><b>It was the row's <c>rowid</c>, and that was wrong for a reason the first version's own
/// comment argued around.</b> The comment said `rowid` is safe because `api_keys` never deletes, so
/// a number is never reused — true, and not the risk. `api_keys` is keyed by `id TEXT PRIMARY KEY`,
/// so its `rowid` is implicit, and SQLite RENUMBERS implicit rowids on `VACUUM` and on any future
/// step that rebuilds the table. The order survives; the NUMBERS do not. A client holding a
/// `nextBefore` across a maintenance window then resumes at a row that is no longer where its
/// cursor points, and silently skips or repeats keys. The same file's step 3 already noted that a
/// VACUUM renumbers — this code was arguing against a fact stated three hundred lines above it.
/// (Code round, codex.)</para>
/// <para><b>So the cursor is made of columns the table actually stores.</b> `created_utc` is
/// ISO-8601 and sorts chronologically as text; `id` breaks the tie for two keys issued in the same
/// instant, which the admin API can produce because nothing rate-limits an administrator to one
/// issuance per tick. Together they are unique — `id` alone is the primary key — so the pair is a
/// total order and paging by it cannot skip or repeat a row whatever storage does underneath.</para>
/// <para><b>On the wire it is an OPAQUE token.</b> A client passes back exactly what it was given
/// and never composes one; that is why a malformed token is a 400 naming what a cursor is, rather
/// than an empty page that reads like the end of the list. An earlier version accepted any
/// integer, so `?before=-1` and `?before=0` answered 200 with no items and hid every key behind
/// what looked like a finished listing.</para>
/// </remarks>
/// <param name="Created">The row's <c>created_utc</c>, exactly as stored.</param>
/// <param name="Id">The row's key id, which breaks a tie within one instant.</param>
public sealed record KeysCursor(string Created, string Id)
{
    /// <summary>The separator between the two halves.</summary>
    /// <remarks>
    /// <para><b>Unreserved in RFC 3986</b>, so the whole token is legal in a query string exactly as
    /// it was given and a client never has to percent-encode it. It was `|`, which is NOT a legal
    /// query character — the tests that followed a cursor were the first thing to notice, and a
    /// token that has to be encoded before it can be echoed is not an opaque token.</para>
    /// <para>Neither half can contain it: one is ISO-8601 (digits, `-`, `:`, `.`, `T`, `Z`) and the
    /// other is lowercase hex.</para>
    /// </remarks>
    private const char Between = '~';

    /// <summary>Read from the newest row, which is what an absent cursor means.</summary>
    /// <remarks>
    /// A sentinel rather than a second SQL statement, and NOT a string chosen to sort above every
    /// real value — that trick depends on the collation and on nobody ever storing a stranger date.
    /// <see cref="Corpus.KeysPage"/> binds <see cref="FromTheNewest"/> as a flag and skips the
    /// comparison entirely.
    /// </remarks>
    public static readonly KeysCursor Newest = new(string.Empty, string.Empty);

    /// <summary>Whether this is the first page, i.e. no row to read before.</summary>
    public bool FromTheNewest => Created.Length == 0;

    /// <summary>What a client is handed and hands back.</summary>
    public string Token => Created + Between + Id;

    /// <summary>Reads a token, or says what a cursor is.</summary>
    /// <param name="raw">The <c>before</c> parameter; empty means the first page.</param>
    public static Read From(string raw)
    {
        if (raw.Length == 0)
        {
            return new Read.Page(Newest);
        }

        var halves = raw.Split(Between);

        return halves is [{ Length: > 0 } created, { Length: > 0 } id] && UtcInstant.Reads(created)
            ? new Read.Page(new KeysCursor(created, id))
            : new Read.Refused(
                $"before is '{raw}'; a cursor is not composed by a caller — pass back the "
                + "`nextBefore` a page gave you, or leave it out for the newest keys");
    }

    /// <summary>What reading a token came to.</summary>
    public abstract record Read
    {
        private Read()
        {
        }

        /// <summary>Why this cannot be used, or empty when it can.</summary>
        /// <remarks>
        /// So a caller can read the sentence off the union without a cast and without parsing
        /// twice. The pattern `is not Page(var x)` gives the happy value and nothing else, and
        /// asking the refusal for its sentence in the other branch used to mean calling the parser
        /// a second time.
        /// </remarks>
        public string Refusal => this is Refused refused ? refused.Why : string.Empty;

        /// <summary>A usable cursor, or <see cref="Newest"/> for the first page.</summary>
        public sealed record Page(KeysCursor Cursor) : Read;

        /// <summary>A 400, and the sentence the caller gets.</summary>
        public sealed record Refused(string Why) : Read;
    }

    /// <summary>The audit's cursor is its own row id, which IS stable — and must be positive.</summary>
    /// <remarks>
    /// <para><c>admin_audit.id</c> is declared <c>INTEGER PRIMARY KEY</c>, so it is the rowid itself
    /// rather than an implicit one, and SQLite preserves it across a <c>VACUUM</c>. That is the
    /// whole difference between the two listings, and it is why only the keys page needed a
    /// composite cursor.</para>
    /// <para>It is still refused when it is not positive: ids start at 1, so `0` and `-1` are a
    /// client computing a cursor instead of echoing one, and answering them with an empty page is
    /// how a broken pager looks like a finished one.</para>
    /// </remarks>
    public static Read<long> Audit(string raw)
    {
        if (raw.Length == 0)
        {
            return new Read<long>.Page(long.MaxValue);
        }

        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? new Read<long>.Page(id)
            : new Read<long>.Refused(
                $"before is '{raw}'; a cursor is not composed by a caller — pass back the "
                + "`nextBefore` a page gave you, or leave it out for the newest actions");
    }

    /// <summary>What reading a cursor of some other shape came to.</summary>
    public abstract record Read<T>
    {
        private Read()
        {
        }

        /// <summary>Why this cannot be used, or empty when it can.</summary>
        /// <remarks>
        /// So a caller can read the sentence off the union without a cast and without parsing
        /// twice. The pattern `is not Page(var x)` gives the happy value and nothing else, and
        /// asking the refusal for its sentence in the other branch used to mean calling the parser
        /// a second time.
        /// </remarks>
        public string Refusal => this is Refused refused ? refused.Why : string.Empty;

        /// <summary>A usable cursor.</summary>
        public sealed record Page(T Cursor) : Read<T>;

        /// <summary>A 400, and the sentence the caller gets.</summary>
        public sealed record Refused(string Why) : Read<T>;
    }
}
