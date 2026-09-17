using System.Globalization;

namespace CoaiBugs;

/// <summary>The two paging parameters, validated at the boundary rather than clamped inside it.</summary>
/// <remarks>
/// <para><b>An illegal value is a 400, never a silent clamp.</b> The doctrine says input fails at the
/// boundary naming what was legal; a clamp answers a question nobody asked and hides the caller's
/// bug until it matters.</para>
/// <para><b><c>limit=0</c> is refused</b> and does NOT mean "everything". A limit nobody can exceed
/// is how a listing endpoint quietly becomes a full-table read, and on the audit that is 50 000 rows
/// while holding the corpus gate.</para>
/// <para><b>A <c>before</c> past the end is not an error</b> — it is an empty page. Paging off the
/// end of a list is what a client doing the right thing looks like on its last request.</para>
/// </remarks>
internal sealed record AdminPaging(int Limit, string Before)
{
    /// <summary>What a page is when the caller asks for nothing in particular.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Reads the query, or says which parameter was wrong and what would have been right.</summary>
    /// <remarks>
    /// <b>The cursor is carried through as an OPAQUE token and validated by the route</b>, because
    /// the two listings have different cursors for a reason that is about their tables, not about
    /// paging: the audit's is its own <c>INTEGER PRIMARY KEY</c>, which a <c>VACUUM</c> preserves,
    /// while the keys page needs <c>(created_utc, id)</c> because its rowid is implicit and gets
    /// renumbered. Parsing it here would mean one of them being parsed wrongly. See
    /// <see cref="KeysCursor"/>.
    /// </remarks>
    public static Read From(IQueryCollection query) =>
        Whole(query, "limit", DefaultLimit) switch
        {
            Number<int>.Bad bad => new Read.Refused(bad.Why),
            Number<int>.Good { Value: var limit } when limit < 1 || limit > Corpus.MostAuditPage =>
                new Read.Refused(
                    $"limit is '{limit}'; it must be from 1 to {Corpus.MostAuditPage} — 0 is refused "
                    + "rather than meaning everything"),
            Number<int>.Good { Value: var limit } =>
                new Read.Page(new AdminPaging(limit, query["before"].ToString())),
            _ => throw new InvalidOperationException("Number<int> has no third case"),
        };

    private static Number<int> Whole(IQueryCollection query, string name, int whenAbsent)
    {
        if (query[name].ToString() is not { Length: > 0 } raw)
        {
            return new Number<int>.Good(whenAbsent);
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? new Number<int>.Good(value)
            : new Number<int>.Bad($"{name} is '{raw}'; it must be a whole number");
    }

    /// <summary>A parsed number, or the sentence saying why it is not one.</summary>
    private abstract record Number<T>
    {
        private Number()
        {
        }

        public sealed record Good(T Value) : Number<T>;

        public sealed record Bad(string Why) : Number<T>;
    }

    /// <summary>What reading the query came to.</summary>
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

        /// <summary>Usable paging.</summary>
        public sealed record Page(AdminPaging Paging) : Read;

        /// <summary>A 400, and the sentence the caller gets.</summary>
        public sealed record Refused(string Why) : Read;
    }
}
