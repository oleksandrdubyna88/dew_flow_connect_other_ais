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
internal sealed record AdminPaging(int Limit, long Before)
{
    /// <summary>What a page is when the caller asks for nothing in particular.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Reads the query, or says which parameter was wrong and what would have been right.</summary>
    public static Read From(IQueryCollection query) =>
        Whole(query, "limit", DefaultLimit) switch
        {
            Number<int>.Bad bad => new Read.Refused(bad.Why),
            Number<int>.Good { Value: var limit } when limit < 1 || limit > Corpus.MostAuditPage =>
                new Read.Refused(
                    $"limit is '{limit}'; it must be from 1 to {Corpus.MostAuditPage} — 0 is refused "
                    + "rather than meaning everything"),
            Number<int>.Good { Value: var limit } => Cursor(query) switch
            {
                Number<long>.Bad bad => new Read.Refused(bad.Why),
                Number<long>.Good { Value: var before } => new Read.Page(new AdminPaging(limit, before)),
                _ => throw new InvalidOperationException("Number<long> has no third case"),
            },
            _ => throw new InvalidOperationException("Number<int> has no third case"),
        };

    private static Number<long> Cursor(IQueryCollection query)
    {
        if (query["before"].ToString() is not { Length: > 0 } raw)
        {
            // Absent means "from the newest", which is the first page.
            return new Number<long>.Good(long.MaxValue);
        }

        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? new Number<long>.Good(value)
            : new Number<long>.Bad($"before is '{raw}'; it must be a whole number from a page's nextBefore");
    }

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

        /// <summary>Usable paging.</summary>
        public sealed record Page(AdminPaging Paging) : Read;

        /// <summary>A 400, and the sentence the caller gets.</summary>
        public sealed record Refused(string Why) : Read;
    }
}
