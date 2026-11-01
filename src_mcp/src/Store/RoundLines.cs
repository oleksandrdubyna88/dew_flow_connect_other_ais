using CoaiMcp.Core.Context;

namespace CoaiMcp.Store;

/// <summary>
/// What a scope's cells add up to in the report's terms (D10). The denominator is what the reviewers READ — <c>shown</c> and <c>elided</c> cells of
/// the selected kinds; <c>excluded</c> and <c>binary</c> are reported apart and never in it; a kind outside <c>--kinds</c> is printed as <i>not
/// counted</i>, never dropped. Pure.
/// </summary>
internal static class RoundLines
{
    /// <summary>The whole scope of one round in the report's terms.</summary>
    internal sealed record Counted(long Selected, long Elided, long Excluded, long BinaryFiles, IReadOnlyList<NamedCount> NotCounted, bool AnyElided, bool BinaryOnly);

    public static Counted Of(IReadOnlyList<SizeCell> cells, IReadOnlyList<string> kinds) => new(
        Sum(Selected(cells, kinds)),
        Sum(Selected(cells, kinds).Where(cell => cell.Coverage == ChangeCoverage.Elided)),
        Sum(cells.Where(cell => cell.Coverage == ChangeCoverage.Excluded)),
        cells.Where(IsBinary).Sum(cell => (long)cell.Files),
        NotCounted(cells, kinds),
        cells.Any(cell => cell.Coverage == ChangeCoverage.Elided),
        cells.Count > 0 && cells.All(IsBinary));

    /// <summary>The lines a delta scope adds up to, in the same terms as the whole one — what the fixers changed (D10).</summary>
    public static long DeltaOf(IReadOnlyList<SizeCell> cells, IReadOnlyList<string> kinds) => Sum(Selected(cells, kinds));

    private static bool IsBinary(SizeCell cell) => cell.Coverage == ChangeCoverage.Binary;

    private static bool WasRead(SizeCell cell) => cell.Coverage is ChangeCoverage.Shown or ChangeCoverage.Elided;

    private static IEnumerable<SizeCell> Selected(IReadOnlyList<SizeCell> cells, IReadOnlyList<string> kinds) =>
        cells.Where(cell => WasRead(cell) && kinds.Contains(cell.Kind));

    /// <summary>A binary's lines are null — never 0 — and a binary is no read cell, so nothing here ever adds a null.</summary>
    private static long Sum(IEnumerable<SizeCell> cells) => cells.Sum(cell => (long)(cell.Added ?? 0) + (cell.Removed ?? 0));

    private static List<NamedCount> NotCounted(IReadOnlyList<SizeCell> cells, IReadOnlyList<string> kinds) =>
    [
        .. cells.Where(cell => WasRead(cell) && !kinds.Contains(cell.Kind))
            .GroupBy(cell => cell.Kind)
            .Select(group => new NamedCount(group.Key, Sum(group)))
            .OrderBy(count => count.Name, StringComparer.Ordinal),
    ];
}

/// <summary>The reviewer set of a round — the DISTINCT (provider, model, effort) of the reviewers that answered (D6, D7).</summary>
internal static class ReviewerSet
{
    public const string NotRecorded = "not recorded";
    public const string None = "none";

    public static string Of(IReadOnlyList<ReviewerFact> reviewers) =>
        string.Join(
            " + ",
            reviewers.Where(reviewer => reviewer.Status == "done")
                .Select(reviewer => (reviewer.Provider, reviewer.Model, Effort: EffortLabel(reviewer)))
                .Distinct()
                .Order()
                .Select(set => $"{set.Provider}/{(set.Model.Length == 0 ? "unknown" : set.Model)}@{set.Effort}"));

    /// <summary>
    /// D7: <c>not recorded</c> is the column default for every row before step 22 and the whole answer for a file older than it; empty is a
    /// launch that applied none (<c>none</c>). The two are never merged.
    /// </summary>
    internal static string EffortLabel(ReviewerFact reviewer) =>
        !reviewer.EffortRecorded || reviewer.Effort == NotRecorded ? NotRecorded : reviewer.Effort.Length == 0 ? None : reviewer.Effort;
}
