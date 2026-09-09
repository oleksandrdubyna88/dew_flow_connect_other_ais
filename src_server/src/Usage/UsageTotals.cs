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

    /// <summary>One pass over the group, because three were three passes for no reason.</summary>
    /// <remarks>
    /// The first version called <c>Where().ToList()</c> and then <c>Count</c>, <c>Sum</c>, <c>Sum</c>
    /// on the original grouping — four traversals and a copy per vendor, on every company request.
    /// (Two reviewers, code round.)
    /// </remarks>
    private static VendorTotal Fold(IGrouping<string, UsageLine> group)
    {
        var runs = 0;
        var failed = 0;
        long tokensIn = 0;
        long tokensOut = 0;
        var seconds = 0d;
        var priced = 0;
        var cost = 0d;

        foreach (var line in group)
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

        return new VendorTotal(
            group.Key,
            runs,
            failed,
            tokensIn,
            tokensOut,
            Math.Round(seconds, 1),
            priced > 0 ? Math.Round(cost, 4) : null,
            priced > 0 && unpriced > 0,
            unpriced);
    }

    /// <summary>
    /// Per kind — the gate against asking — over the same lines.
    /// </summary>
    /// <remarks>
    /// <para>Folded through <see cref="Fold"/> so the arithmetic is the SAME arithmetic: an unknown
    /// price is never zero here either, and a failed run is counted rather than filtered, because a
    /// conversation that burned ninety seconds and answered nothing spent exactly what one that
    /// answered did.</para>
    /// <para>Only the kinds that actually occur appear. A window with no conversations in it says so
    /// by having one row, not by carrying a row of zeroes that reads as a measurement.</para>
    /// </remarks>
    public static IReadOnlyList<KindTotal> ByKind(IEnumerable<UsageLine> lines) =>
        [.. lines
            .GroupBy(l => JobKinds.Wire(l.Kind), StringComparer.Ordinal)
            .Select(Fold)
            .Select(AsKind)
            .OrderByDescending(k => k.Runs)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)];

    /// <summary>The same numbers, named for a kind rather than a vendor.</summary>
    /// <remarks>
    /// <see cref="Fold"/> folds a GROUP and names the result by that group's key, whatever the key
    /// means; here it means a kind. Re-labelling it beats a second copy of the arithmetic, which is
    /// how two totals on one page come to disagree about what a failed run costs.
    /// </remarks>
    private static KindTotal AsKind(VendorTotal folded) =>
        new(folded.Vendor, folded.Runs, folded.Failed, folded.TokensIn, folded.TokensOut,
            folded.Seconds, folded.CostUsd, folded.CostIsFloor, folded.UnpricedRuns);

    /// <summary>Per kind, for one person.</summary>
    public static IReadOnlyList<KindTotal> ByKindFor(IEnumerable<UsageLine> lines, string email) =>
        ByKind(lines.Where(l => Same(l.Email, email)));
}

/// <summary>One person's spending, for the admin view.</summary>
public sealed record PersonTotal(string Email, IReadOnlyList<VendorTotal> Vendors);
