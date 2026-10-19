namespace CoaiMcp.Store;

/// <summary>
/// The header of the report (D15, D7, D8, D10): what the numbers below it are NOT, said before any number. Fixed sentences with values filled in —
/// no text from a store row except dimension values (models, providers, effort words, family names). Pure.
/// </summary>
/// <remarks>
/// The sentences are constants so a test asserts the constant, never a retyped copy. The first line is the association-only sentence: a cell of
/// this report is an association between who wrote the code and what the reviewers found, never a cause.
/// </remarks>
public static class RoundAuthorsHeader
{
    public const string AssociationOnly =
        "Association only: this report counts who wrote the code under review and what the reviewers found. It does not show that one caused the other.";

    public const string AskedModel =
        "The reviewer model is the one ASKED for; the model that answered is not recorded.";

    public const string Contribution =
        "A write is contribution, not authorship of the lines that survive.";

    public const string UnobservedLines =
        "A diff can carry lines nobody observed: a hand edit, a tool's output, a paste.";

    public const string RankAllocation =
        "Activity that two gate sessions of one caller both see is allocated to the gate whose round completed first and labelled `claimed by peer` on the other. This is an accounting convention, never evidence of a repository or a model.";

    public const string RollUpLimit =
        "The authors state of a round is its roll-up over its before-open and round windows; per-window states are not stored. With before-open left out, a round whose before-open window alone is partial or unobserved is still read partial or excluded. The error goes toward exclusion, never toward a claim.";

    public const string Provisional =
        "Some counted rounds are provisional: a same-caller peer round was still running when they were attributed. They are counted as partial until a backfill finalises them.";

    public const string Denominator =
        "Lines are what the reviewers read: the shown and elided files of the counted kinds in the whole change. Excluded and binary files are reported apart and are never in it.";

    public const string DeltaFallback =
        "A corrective cell also shows findings per 100 DELTA lines, what the fixers changed. A round whose delta is unavailable stays in the whole figures, leaves the delta figures, and is counted per reason; its whole lines are never used as a delta denominator.";

    public const string UnknownKind =
        "Kind `unknown` is every extension the outline does not map (.ps1, .sh, .razor, .html, .css among them). Lines of a kind outside the counted kinds are printed as not counted.";

    public const string Numerators =
        "`blocking+major` counts findings of severity blocking or major BEFORE the gate's discount. It is not the gate's own count: `gate_count` is that count, after the discount.";

    public const string ReviewerEffort =
        "Reviewer effort `not recorded` is every round written before schema step 22; `none` means the launch applied none. The two are never merged.";

    public const string EffortOlderSource =
        "At least one source store is older than schema step 22: its reviewer effort reads `not recorded`.";

    public const string NoComparison = "No period comparison is claimed: the report was not split.";

    public const string CannotSeparate = "Coder mix and reviewer set both changed between the periods: this report cannot separate them.";

    private static readonly string[] Dimensions = ["coder mix", "coder effort", "main model", "reviewer set"];

    /// <summary>The header lines for a report: the fixed sentences, and the coincident changes between the periods when the report was split.</summary>
    public static IReadOnlyList<string> Of(RoundAuthorsSums sums, RoundAuthorsSelection selection, bool effortMarkerKnown) =>
    [
        AssociationOnly,
        $"Stage {selection.Stage}; cohort {selection.Cohort}; kinds counted {string.Join(", ", selection.Kinds)}.",
        .. Always(),
        .. effortMarkerKnown ? [] : new[] { EffortOlderSource },
        .. sums.Cells.Any(cell => cell.ProvisionalRounds > 0) ? new[] { Provisional } : [],
        .. Periods(sums, selection),
    ];

    private static string[] Always() =>
        [Denominator, DeltaFallback, UnknownKind, Numerators, ReviewerEffort, AskedModel, Contribution, UnobservedLines, RankAllocation, RollUpLimit];

    private static IEnumerable<string> Periods(RoundAuthorsSums sums, RoundAuthorsSelection selection) =>
        selection.SplitUtc.Length == 0 ? [NoComparison] : sums.Cells.Select(cell => cell.Key.Cohort).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SelectMany(cohort => Changed(sums, cohort));

    /// <summary>For one cohort: the dimensions whose SET of values differs between the periods, and the cannot-separate sentence when coder mix and reviewer set both do.</summary>
    private static IEnumerable<string> Changed(RoundAuthorsSums sums, string cohort)
    {
        var before = InPeriod(sums, cohort, Period.Before);
        var after = InPeriod(sums, cohort, Period.After);

        return before.Count == 0 || after.Count == 0
            ? [$"Cohort {cohort}: only one period has counted rounds, so nothing is compared."]
            : Compared(cohort, Dimensions.Where(name => !ValuesOf(before, name).SetEquals(ValuesOf(after, name))).ToList());
    }

    private static List<CellSums> InPeriod(RoundAuthorsSums sums, string cohort, string period) =>
        [.. sums.Cells.Where(cell => cell.Key.Cohort == cohort && cell.Key.Period == period)];

    private static IEnumerable<string> Compared(string cohort, List<string> changed) =>
    [
        changed.Count == 0 ? $"Cohort {cohort}: no dimension changed between the periods." : $"Cohort {cohort}: changed between the periods: {string.Join(", ", changed)}.",
        .. CannotSeparateWhen(changed),
    ];

    private static string[] CannotSeparateWhen(List<string> changed) =>
        changed.Contains("coder mix") && changed.Contains("reviewer set") ? [CannotSeparate] : [];

    private static HashSet<string> ValuesOf(List<CellSums> cells, string dimension) =>
        [.. cells.Select(cell => dimension switch
        {
            "coder mix" => cell.Key.CoderMix,
            "coder effort" => cell.Key.CoderEffort,
            "main model" => cell.Key.MainModel,
            _ => cell.Key.ReviewerSet,
        })];
}
