namespace CoaiServer;

/// <summary>
/// A window of time, half-open: <c>[FromUtc, ToUtc)</c>.
/// </summary>
/// <remarks>
/// Half-open on purpose. With both ends inclusive, a run stamped exactly on a boundary belongs to two
/// adjacent windows and is counted twice; with both exclusive it belongs to neither and vanishes. The
/// plan round asked for this to be stated rather than left to whoever writes the comparison, and it is
/// tested at both edges.
/// </remarks>
public sealed record UsageRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public bool Contains(DateTimeOffset at) => at >= FromUtc && at < ToUtc;

    /// <summary>The smallest range that holds both — what one scan is asked for when two answers are built from it.</summary>
    /// <remarks>
    /// The company answer carries a summary over the SELECTED window and a chart over its own thirty
    /// days, and the ledger is parsed once for both: the reader is handed this, and each answer applies
    /// its own range to what came back. Two ranges that do not touch leave a gap in the middle that
    /// nobody asked about and nobody reads; it costs the lines between, which is cheaper than a second
    /// parse of the whole file.
    /// </remarks>
    public UsageRange Union(UsageRange other) =>
        new(FromUtc <= other.FromUtc ? FromUtc : other.FromUtc, ToUtc >= other.ToUtc ? ToUtc : other.ToUtc);
}

/// <summary>The windows a caller may ask for.</summary>
/// <remarks>
/// <para>Trailing spans rather than calendar boxes: <c>week</c> is the last seven days, not the current
/// ISO week. A calendar week shows a near-empty box every Monday morning, which reads as "we spent
/// almost nothing" at exactly the moment somebody looks. The cost of the choice is that the NAME is
/// approximate, so the answer always carries the actual range it used and no client has to guess.</para>
/// <para>UTC throughout, because the deployment rule fixes the machine to UTC and the ledger's stamps
/// are UTC. A local-time window over UTC stamps is an off-by-one-day nobody can see.</para>
/// </remarks>
public static class UsageWindow
{
    public static IReadOnlyList<string> Names { get; } = ["today", "week", "month", "year"];

    /// <summary>The range a name means at <paramref name="nowUtc"/>, or null when the name is not one.</summary>
    /// <remarks>
    /// Null rather than a default, so the endpoint can refuse an unknown name and say what the legal
    /// ones are. A silent fall back to "today" would answer a question nobody asked.
    /// </remarks>
    public static UsageRange? Range(string? name, DateTimeOffset nowUtc)
    {
        // The exclusive upper bound is the next second, not `nowUtc`: a run that finished during this
        // very request must be in "today", and a half-open range ending exactly at now would drop it.
        var end = nowUtc.AddSeconds(1);

        // `?window=` sends an empty string, which is a client saying nothing rather than a client
        // naming something wrong — the same as leaving the parameter off. (local, code round.)
        return (string.IsNullOrWhiteSpace(name) ? "today" : name).ToLowerInvariant() switch
        {
            "today" => new UsageRange(new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero), end),
            "week" => new UsageRange(end.AddDays(-7), end),
            "month" => new UsageRange(end.AddDays(-30), end),
            "year" => new UsageRange(end.AddDays(-365), end),
            _ => null,
        };
    }
}

/// <summary>What one vendor cost, over a window.</summary>
/// <param name="CostUsd">
/// The sum of the prices that were KNOWN, or null when none were. Read it with
/// <paramref name="CostIsFloor"/>.
/// </param>
/// <param name="CostIsFloor">
/// True when at least one run in this group had no price. The number is then a LOWER BOUND, not a
/// total: some of what was spent is not in it.
/// </param>
/// <param name="UnpricedRuns">
/// How many runs had no price, so a reader can tell "one of forty is missing" from "thirty-nine of
/// forty are". A bare flag cannot, and the plan round was right that a boolean alone is not enough to
/// act on.
/// </param>
public sealed record VendorTotal(
    string Vendor,
    int Runs,
    int Failed,
    long TokensIn,
    long TokensOut,
    double Seconds,
    double? CostUsd,
    bool CostIsFloor,
    int UnpricedRuns,
    /// <summary>The same runs, by the model that ran them.</summary>
    /// <remarks>
    /// <para>Story 2.2 of <c>PLAN_team_usage_by_person.md</c> (D4): the client prices each MODEL at its
    /// public list price, because a vendor's current model is not what ran, and a vendor total cannot
    /// be priced at all. Trailing and defaulted like <see cref="UsageDto.Kinds"/>, for the same reason —
    /// a client older than the field deserialises the answer unchanged — and computed for BOTH scopes by
    /// the shared fold: the JSON context writes nulls, so a "company only" breakdown would have put
    /// <c>"models": null</c> into every personal answer.</para>
    /// <para>An older server is recognised by the absence of <c>daily</c> on the company answer, never by
    /// an empty <c>models</c>, which a new server answers too for a vendor with no runs.</para>
    /// </remarks>
    IReadOnlyList<ModelTotal> Models = null!)
{
    /// <summary>Never null, whatever a deserialiser did with the property — see <see cref="UsageDto.Kinds"/>.</summary>
    public IReadOnlyList<ModelTotal> Models { get; init; } = Models ?? [];
}

/// <summary>What one MODEL of a vendor cost, over a window — a row under its vendor.</summary>
/// <remarks>
/// Grouped by the model id ORDINALLY: the id is the vendor's own string and the client prices it by
/// exact lookup, so two casings would be two lookups there too. A line with no model — every line
/// written before the column existed — is kept under <c>""</c>, which the client labels <i>unknown</i>:
/// its spending is still spending, and dropping the row would hide it. The cost trio (<see cref="CostUsd"/>,
/// <see cref="CostIsFloor"/>, <see cref="UnpricedRuns"/>) means exactly what it means on
/// <see cref="VendorTotal"/>, and is folded by the same function so the two cannot disagree.
/// </remarks>
public sealed record ModelTotal(
    string Model,
    int Runs,
    int Failed,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    bool CostIsFloor,
    int UnpricedRuns);

/// <summary>
/// What one KIND of work cost, over a window: the gate, or asking.
/// </summary>
/// <remarks>
/// <para>The owner asked for this on 2026-09-08 — *"счиатть, отделять"* — and the reason is that
/// "what did the gate cost me" and "what did asking cost me" are two questions about the same
/// vendors, which a single total answers neither of.</para>
/// <para><b>A second small block, not a second row per vendor.</b> Splitting the vendor table by kind
/// doubles it and leaves a reader adding pairs of rows together to get back the number they had
/// before. The vendor rows stay what they were — everything that vendor cost — and this answers the
/// one extra question beside them.</para>
/// </remarks>
public sealed record KindTotal(
    string Kind,
    int Runs,
    int Failed,
    long TokensIn,
    long TokensOut,
    double Seconds,
    double? CostUsd,
    bool CostIsFloor,
    int UnpricedRuns);

/// <summary>Aggregation, as pure functions over parsed lines.</summary>
/// <remarks>
/// <para><b>Failed runs are counted, never filtered.</b> A review that burned ninety seconds and
/// answered nothing spent exactly the same as one that answered — hiding it is the one thing a
/// spending record must not do. They appear in <c>Runs</c>, in <c>Failed</c>, and in the tokens.</para>
/// <para><b>An unknown price is never zero.</b> Most lines here have no price at all, because these are
/// subscription CLIs rather than metered APIs. Summing null as zero would render "we do not know" as
/// "this was free", which is the most expensive possible lie for a page about money.</para>
/// </remarks>
public static class UsageTotals
{
    /// <summary>Per vendor, for everyone in <paramref name="lines"/>.</summary>
    public static IReadOnlyList<VendorTotal> ByVendor(IEnumerable<UsageLine> lines) =>
        [.. lines
            .GroupBy(l => l.Vendor, StringComparer.OrdinalIgnoreCase)
            .Select(Fold)
            .OrderByDescending(v => v.Runs)
            .ThenBy(v => v.Vendor, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Per vendor, for one person.</summary>
    /// <remarks>
    /// Case-insensitive, because an identity provider may hand back <c>Alice@Example.com</c> where the
    /// ledger holds <c>alice@example.com</c> — the same person, and matching them exactly would show
    /// somebody an empty page and split them in two in the company view. (codex, plan round.)
    /// </remarks>
    public static IReadOnlyList<VendorTotal> ByVendorFor(IEnumerable<UsageLine> lines, string email) =>
        ByVendor(lines.Where(l => Same(l.Email, email)));

    /// <summary>Per person, each with their own vendor breakdown.</summary>
    /// <remarks>
    /// Lines with no email are left out of the people list rather than grouped under a blank name: they
    /// come from local runs that predate a Team server and belong to nobody here. They still count in
    /// the company's vendor totals, because the money was spent.
    /// </remarks>
    public static IReadOnlyList<PersonTotal> ByPerson(IEnumerable<UsageLine> lines) =>
        [.. lines
            .Where(l => l.Email.Length > 0)
            .GroupBy(l => l.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PersonTotal(g.Key, ByVendor(g)))
            .OrderBy(p => p.Email, StringComparer.OrdinalIgnoreCase)];

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A vendor's row: its own sum, and the same lines summed again per model beneath it.</summary>
    /// <remarks>
    /// Two passes over the group — the vendor's sum and the per-model grouping — where the first
    /// version made four and a copy (two reviewers, code round) and story 2.2 would have made it one
    /// with a mutable accumulator per model. Two passes over a few thousand lines is milliseconds;
    /// one arithmetic function for every row on the page is the property worth keeping.
    /// </remarks>
    private static VendorTotal Fold(IGrouping<string, UsageLine> group)
    {
        var sum = Add(group);

        return new VendorTotal(
            group.Key,
            sum.Runs,
            sum.Failed,
            sum.TokensIn,
            sum.TokensOut,
            Math.Round(sum.Seconds, 1),
            sum.CostUsd,
            sum.CostIsFloor,
            sum.UnpricedRuns,
            ByModel(group));
    }

    /// <summary>Per model, within one vendor's lines. Ordinal on the id — see <see cref="ModelTotal"/>.</summary>
    private static IReadOnlyList<ModelTotal> ByModel(IEnumerable<UsageLine> lines) =>
        [.. lines
            .GroupBy(l => l.Model, StringComparer.Ordinal)
            .Select(g => AsModel(g.Key, Add(g)))
            .OrderByDescending(m => m.Runs)
            .ThenBy(m => m.Model, StringComparer.Ordinal)];

    private static ModelTotal AsModel(string model, Sum sum) =>
        new(model, sum.Runs, sum.Failed, sum.TokensIn, sum.TokensOut, sum.CostUsd, sum.CostIsFloor, sum.UnpricedRuns);

    /// <summary>
    /// The ONE arithmetic every row here is built from: a vendor's, a model's and a kind's.
    /// </summary>
    /// <param name="Seconds">Raw; the vendor row rounds it, the others do not carry it.</param>
    /// <param name="CostUsd">The sum of the KNOWN prices, rounded, or null when none were known.</param>
    private sealed record Sum(
        int Runs,
        int Failed,
        long TokensIn,
        long TokensOut,
        double Seconds,
        double? CostUsd,
        bool CostIsFloor,
        int UnpricedRuns);

    /// <summary>One pass over the lines, because three were three passes for no reason.</summary>
    /// <remarks>
    /// The first version called <c>Where().ToList()</c> and then <c>Count</c>, <c>Sum</c>, <c>Sum</c>
    /// on the original grouping — four traversals and a copy per vendor, on every company request.
    /// (Two reviewers, code round.)
    /// </remarks>
    private static Sum Add(IEnumerable<UsageLine> lines)
    {
        var runs = 0;
        var failed = 0;
        long tokensIn = 0;
        long tokensOut = 0;
        var seconds = 0d;
        var priced = 0;
        var cost = 0d;

        foreach (var line in lines)
        {
            runs += 1;
            failed += line.Failed ? 1 : 0;
            tokensIn += line.TokensIn;
            tokensOut += line.TokensOut;
            seconds += line.Seconds;
            if (line.CostUsd is { } known)
            {
                priced += 1;
                cost += known;
            }
        }

        var unpriced = runs - priced;

        return new Sum(
            runs,
            failed,
            tokensIn,
            tokensOut,
            seconds,
            priced > 0 ? Math.Round(cost, 4) : null,
            priced > 0 && unpriced > 0,
            unpriced);
    }

    /// <summary>
    /// Per kind — the gate against asking — over the same lines.
    /// </summary>
    /// <remarks>
    /// <para>Summed by <see cref="Add"/> so the arithmetic is the SAME arithmetic: an unknown price is
    /// never zero here either, and a failed run is counted rather than filtered, because a conversation
    /// that burned ninety seconds and answered nothing spent exactly what one that answered did.</para>
    /// <para>Only the kinds that actually occur appear. A window with no conversations in it says so
    /// by having one row, not by carrying a row of zeroes that reads as a measurement.</para>
    /// </remarks>
    public static IReadOnlyList<KindTotal> ByKind(IEnumerable<UsageLine> lines) =>
        [.. lines
            .GroupBy(l => JobKinds.Wire(l.Kind), StringComparer.Ordinal)
            .Select(g => AsKind(g.Key, Add(g)))
            .OrderByDescending(k => k.Runs)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)];

    /// <summary>The same numbers, named for a kind rather than a vendor.</summary>
    /// <remarks>
    /// <see cref="Add"/> sums LINES and knows nothing of what grouped them; here the key means a kind.
    /// Re-labelling one sum beats a second copy of the arithmetic, which is how two totals on one
    /// page come to disagree about what a failed run costs.
    /// </remarks>
    private static KindTotal AsKind(string kind, Sum sum) =>
        new(kind, sum.Runs, sum.Failed, sum.TokensIn, sum.TokensOut,
            Math.Round(sum.Seconds, 1), sum.CostUsd, sum.CostIsFloor, sum.UnpricedRuns);

    /// <summary>Per kind, for one person.</summary>
    public static IReadOnlyList<KindTotal> ByKindFor(IEnumerable<UsageLine> lines, string email) =>
        ByKind(lines.Where(l => Same(l.Email, email)));
}

/// <summary>One person's spending, for the admin view.</summary>
public sealed record PersonTotal(string Email, IReadOnlyList<VendorTotal> Vendors);
