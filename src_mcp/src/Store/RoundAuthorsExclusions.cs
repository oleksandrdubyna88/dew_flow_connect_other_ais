namespace CoaiMcp.Store;

/// <summary>
/// The exclusions of a population as counts per (cohort, period, reason), EVERY reason listed with its count — zeros included, because an absent row
/// and a zero are different claims (D12). Additive like the cells, so <c>--add</c> sums them by key.
/// </summary>
public static class RoundAuthorsExclusions
{
    private static readonly string[] CohortOrder = [Cohort.First, Cohort.Corrective];

    private static readonly string[] PeriodOrder = [Period.All, Period.Before, Period.After];

    /// <summary>The counts for the cohorts and periods the selection asks about (plus any a round was excluded under, such as the cohort of a round that did not run).</summary>
    public static IReadOnlyList<ExclusionCount> Of(IReadOnlyList<ExcludedRound> excluded, RoundAuthorsSelection selection) =>
    [
        .. from cohort in Cohorts(selection, excluded)
           from period in Periods(selection, excluded)
           from reason in Reasons(selection.Stage)
           select CountOf(excluded, cohort, period, reason),
    ];

    /// <summary>The exclusions in the one order every reader and every merge uses: cohort, period, then the enum's own order.</summary>
    public static IReadOnlyList<ExclusionCount> Ordered(IEnumerable<ExclusionCount> counts) =>
    [
        .. counts.OrderBy(count => Rank(CohortOrder, count.Cohort)).ThenBy(count => count.Cohort, StringComparer.Ordinal)
            .ThenBy(count => Rank(PeriodOrder, count.Period)).ThenBy(count => count.Period, StringComparer.Ordinal)
            .ThenBy(count => ReasonRank(count.Reason)),
    ];

    /// <summary>Two counts of ONE (cohort, period, reason) added: the rounds summed and the per-word split summed per word.</summary>
    public static ExclusionCount Add(ExclusionCount left, ExclusionCount right) =>
        left with { Rounds = left.Rounds + right.Rounds, Because = RoundAuthorsCells.SumByName(left.Because, right.Because) };

    private static IEnumerable<string> Cohorts(RoundAuthorsSelection selection, IReadOnlyList<ExcludedRound> excluded) =>
        Asked(selection.Cohort).Concat(excluded.Select(round => round.Cohort)).Distinct().OrderBy(cohort => Rank(CohortOrder, cohort)).ThenBy(cohort => cohort, StringComparer.Ordinal);

    private static IEnumerable<string> Asked(string cohort) =>
        cohort == Cohort.All ? CohortOrder : [cohort];

    private static IEnumerable<string> Periods(RoundAuthorsSelection selection, IReadOnlyList<ExcludedRound> excluded) =>
        (selection.SplitUtc.Length == 0 ? [Period.All] : new[] { Period.Before, Period.After }).Concat(excluded.Select(round => round.Period)).Distinct()
            .OrderBy(period => Rank(PeriodOrder, period)).ThenBy(period => period, StringComparer.Ordinal);

    /// <summary>A plan round has no diff, so the reasons that need one are not listed for the plan stage — a row there would claim a check that never ran.</summary>
    private static IEnumerable<ExclusionReason> Reasons(string stage) =>
        Enum.GetValues<ExclusionReason>().Where(reason => stage != nameof(Core.Rounds.Stage.PlanReview) || !ExclusionWords.NeedsADiff(reason));

    private static ExclusionCount CountOf(IReadOnlyList<ExcludedRound> excluded, string cohort, string period, ExclusionReason reason)
    {
        var matching = excluded.Where(round => round.Cohort == cohort && round.Period == period && round.Reason == reason).ToList();

        return new ExclusionCount(cohort, period, ExclusionWords.Of(reason), matching.Count, Because(matching, reason));
    }

    private static IReadOnlyList<NamedCount> Because(List<ExcludedRound> matching, ExclusionReason reason) =>
        reason == ExclusionReason.SizeUnavailable
            ? [.. matching.GroupBy(round => round.Detail, StringComparer.Ordinal).Select(group => new NamedCount(group.Key, group.Count())).OrderBy(count => count.Name, StringComparer.Ordinal)]
            : [];

    private static int Rank(string[] order, string word) =>
        Array.IndexOf(order, word) is var at and >= 0 ? at : order.Length;

    private static int ReasonRank(string word) =>
        Array.FindIndex(Enum.GetValues<ExclusionReason>(), reason => ExclusionWords.Of(reason) == word);
}
