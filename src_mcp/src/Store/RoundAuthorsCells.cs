using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Store;

/// <summary>
/// HOW a <see cref="Population"/> is counted (E6.S3): additive SUMS per cell, and — at render time only — figures, marks and the header
/// (the benchmark's <c>GateReport</c>/<c>Figure</c> conventions carried over: a figure with no population is Unknown, never 0).
/// </summary>
/// <remarks>
/// <para><b>One arithmetic for counting and for merging.</b> A round becomes a one-round cell (<see cref="One"/>) and cells add (<see cref="Add"/>):
/// <c>Count</c> folds the rounds of a key with it and <c>--add</c> (S4) folds two stores' cells with the very same function, so two halves
/// equal the union by construction. Integers are summed, session ids are UNIONED, and every map is summed per key.</para>
/// <para><b>Ratios of sums, never means of ratios</b> (D16): a figure is <c>total / covered</c>, each over its OWN covered rounds and lines (D9),
/// rounded to two places half-to-even — computed in the figure functions below and never stored back.</para>
/// <para><b>The branch map is keyed by an opaque repository id</b> beside the branch name, never by <c>repo_path</c>: the JSON answer carries no
/// absolute path (it would expose a user name and a workspace layout), and dominance needs only counts per distinct (repository, branch).</para>
/// </remarks>
public static class RoundAuthorsCells
{
    private const int SmallBelow = 10;

    /// <summary>The cells of a population, ordered by key, and the exclusions with every reason listed — an absent row and a zero are different claims.</summary>
    public static RoundAuthorsSums Count(Population population, RoundAuthorsSelection selection) =>
        new(
            Ordered([.. population.In.GroupBy(round => round.Key).Select(group => group.Select(One).Aggregate(Add))]),
            RoundAuthorsExclusions.Of(population.Out, selection));

    /// <summary>The cells in the one order every reader and every merge uses: the key's words, ordinally, field by field.</summary>
    public static IReadOnlyList<CellSums> Ordered(IEnumerable<CellSums> cells) =>
    [
        .. cells.OrderBy(cell => cell.Key.Cohort, StringComparer.Ordinal)
            .ThenBy(cell => cell.Key.Period, StringComparer.Ordinal)
            .ThenBy(cell => cell.Key.CoderMix, StringComparer.Ordinal)
            .ThenBy(cell => cell.Key.CoderEffort, StringComparer.Ordinal)
            .ThenBy(cell => cell.Key.MainModel, StringComparer.Ordinal)
            .ThenBy(cell => cell.Key.ReviewerSet, StringComparer.Ordinal),
    ];

    /// <summary>The opaque id of a repository path: the first eight hex digits of its SHA-256 — enough to tell two repositories apart, no path in it.</summary>
    public static string RepoId(string repoPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repoPath)))[..8].ToLowerInvariant();

    /// <summary>One counted round as a cell of one round.</summary>
    internal static CellSums One(CountedRound round)
    {
        var sums = round.Sums;
        var numerators = sums.Numerators;

        return new CellSums(
            round.Key, 1, [round.Facts.SessionId], [new NamedCount(RepoId(round.Facts.RepoPath) + "\n" + round.Facts.Branch, 1)],
            sums.SelectedLines, sums.ElidedLines, sums.ExcludedLines, sums.BinaryFiles, sums.NotCounted,
            sums.DeltaLines, sums.DeltaCovered ? 1 : 0, sums.DeltaUnavailable.Length == 0 ? [] : [new NamedCount(sums.DeltaUnavailable, 1)],
            new CellNumerators(
                Sums(numerators.All, sums), Sums(numerators.Severe, sums), Sums(numerators.GateCount, sums),
                Sums(numerators.ReRaised, sums), Sums(numerators.Accepted, sums), Sums(numerators.Unresolved, sums)),
            sums.FailedReviewers, sums.StoodDownReviewers, Flag(sums.PartialSet), Flag(sums.AuthorsPartial), Flag(sums.Provisional),
            Flag(sums.SizeNotCovered), Flag(sums.SizeCoverageUnknown), AmbiguousOf(sums.Ambiguous), sums.CoderLaunches, sums.Requested);
    }

    private static int Flag(bool value) => value ? 1 : 0;

    /// <summary>One numerator of one round: counted only where its value is KNOWN, and in the delta twins only where the round's delta is recorded too.</summary>
    private static NumeratorSums Sums(Numerator numerator, RoundSums round)
    {
        var known = numerator.Covered;
        var delta = known && round.DeltaCovered;

        return new NumeratorSums(
            known ? numerator.Value : 0, Flag(known), known ? round.SelectedLines : 0,
            Flag(delta), delta ? round.DeltaLines : 0, delta ? numerator.Value : 0);
    }

    /// <summary>A round's ambiguous totals: a known total joins the sums, an unknown one is only COUNTED — never a minus one in a sum (D17).</summary>
    private static AmbiguousSums AmbiguousOf(AmbiguousFacts ambiguous) =>
        ambiguous.Known
            ? new AmbiguousSums(1, 0, ambiguous.Agents, ambiguous.Launches, ambiguous.Turns, ambiguous.InputTokens, ambiguous.OutputTokens, ambiguous.CacheCreationTokens, ambiguous.CacheReadTokens)
            : new AmbiguousSums(0, 1, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Two cells of ONE key added: integers summed, session ids unioned, maps summed per key. The key of <paramref name="left"/> stands.</summary>
    public static CellSums Add(CellSums left, CellSums right) => new(
        left.Key, left.Rounds + right.Rounds, Union(left.Sessions, right.Sessions), SumByName(left.Branches, right.Branches),
        left.SelectedLines + right.SelectedLines, left.ElidedLines + right.ElidedLines, left.ExcludedLines + right.ExcludedLines,
        left.BinaryFiles + right.BinaryFiles, SumByName(left.NotCounted, right.NotCounted),
        left.DeltaLines + right.DeltaLines, left.DeltaCoveredRounds + right.DeltaCoveredRounds, SumByName(left.DeltaUnavailable, right.DeltaUnavailable),
        AddNumerators(left.Numerators, right.Numerators),
        left.FailedReviewers + right.FailedReviewers, left.StoodDownReviewers + right.StoodDownReviewers, left.PartialSetRounds + right.PartialSetRounds,
        left.AuthorsPartialRounds + right.AuthorsPartialRounds, left.ProvisionalRounds + right.ProvisionalRounds,
        left.SizeNotCoveredRounds + right.SizeNotCoveredRounds, left.SizeCoverageUnknownRounds + right.SizeCoverageUnknownRounds,
        AddAmbiguous(left.Ambiguous, right.Ambiguous), SumByName(left.CoderLaunches, right.CoderLaunches), SumByName(left.Requested, right.Requested));

    private static CellNumerators AddNumerators(CellNumerators left, CellNumerators right) => new(
        AddSums(left.All, right.All), AddSums(left.Severe, right.Severe), AddSums(left.GateCount, right.GateCount),
        AddSums(left.ReRaised, right.ReRaised), AddSums(left.Accepted, right.Accepted), AddSums(left.Unresolved, right.Unresolved));

    private static NumeratorSums AddSums(NumeratorSums left, NumeratorSums right) => new(
        left.Total + right.Total, left.CoveredRounds + right.CoveredRounds, left.CoveredLines + right.CoveredLines,
        left.DeltaCoveredRounds + right.DeltaCoveredRounds, left.DeltaCoveredLines + right.DeltaCoveredLines, left.DeltaTotal + right.DeltaTotal);

    private static AmbiguousSums AddAmbiguous(AmbiguousSums left, AmbiguousSums right) => new(
        left.KnownRounds + right.KnownRounds, left.UnknownRounds + right.UnknownRounds, left.Agents + right.Agents, left.Launches + right.Launches,
        left.Turns + right.Turns, left.InputTokens + right.InputTokens, left.OutputTokens + right.OutputTokens,
        left.CacheCreationTokens + right.CacheCreationTokens, left.CacheReadTokens + right.CacheReadTokens);

    /// <summary>A set of ids: the union, sorted ordinally — one session counted once however many halves saw it.</summary>
    internal static IReadOnlyList<string> Union(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        [.. left.Concat(right).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Counts by name, summed per name and sorted ordinally — the one shape every small map of the report merges in.</summary>
    internal static IReadOnlyList<NamedCount> SumByName(IReadOnlyList<NamedCount> left, IReadOnlyList<NamedCount> right) =>
    [
        .. left.Concat(right).GroupBy(count => count.Name, StringComparer.Ordinal)
            .Select(group => new NamedCount(group.Key, group.Sum(count => count.Count)))
            .OrderBy(count => count.Name, StringComparer.Ordinal),
    ];

    /// <summary>D14: <c>small</c> under ten rounds; <c>dominated</c> when the largest (repository, branch) holds MORE than half of them. Computed from sums, so a merge recomputes it.</summary>
    public static IReadOnlyList<string> Marks(CellSums cell) =>
    [
        .. cell.Rounds < SmallBelow ? new[] { "small" } : [],
        .. IsDominated(cell) ? new[] { "dominated" } : [],
    ];

    private static bool IsDominated(CellSums cell) =>
        cell.Branches.Count > 0 && cell.Branches.Max(branch => branch.Count) * 2 > cell.Rounds;

    /// <summary>Per round: <c>total / covered rounds</c> — Unknown when no round covered it.</summary>
    public static RoundFigure PerRound(NumeratorSums sums) =>
        sums.CoveredRounds == 0 ? RoundFigure.Unknown : RoundFigure.Of(Rounded((double)sums.Total / sums.CoveredRounds));

    /// <summary>Per 100 lines the reviewers read: <c>100 · total / covered lines</c> — Unknown with no covered line, not applicable to a plan (no diff).</summary>
    public static RoundFigure PerHundredLines(NumeratorSums sums, string stage) =>
        IsPlan(stage) ? RoundFigure.NotApplicable
        : sums.CoveredLines == 0 ? RoundFigure.Unknown
        : RoundFigure.Of(Rounded(100.0 * sums.Total / sums.CoveredLines));

    /// <summary>
    /// Per 100 lines the FIXERS changed: over the rounds whose delta is recorded, and never with whole lines as a fallback (D10) — that would
    /// credit the original authors' lines to the fixers, the case the delta exists to prevent.
    /// </summary>
    public static RoundFigure PerHundredDelta(NumeratorSums sums, string stage) =>
        IsPlan(stage) ? RoundFigure.NotApplicable
        : sums.DeltaCoveredLines == 0 ? RoundFigure.Unknown
        : RoundFigure.Of(Rounded(100.0 * sums.DeltaTotal / sums.DeltaCoveredLines));

    /// <summary>An ambiguous total: a floor (<c>≥ n</c>) when any round of the cell contributed no total, exact otherwise (D17).</summary>
    public static RoundFigure Ambiguous(long total, AmbiguousSums sums) =>
        sums.UnknownRounds > 0 ? RoundFigure.AtLeast(total) : RoundFigure.Of(total);

    /// <summary>The percentage of the cell's rounds on its largest branch, to one place — a figure of the marks' own sums.</summary>
    public static RoundFigure TopBranchPercent(CellSums cell) =>
        cell.Rounds == 0 || cell.Branches.Count == 0
            ? RoundFigure.Unknown
            : RoundFigure.Of(Math.Round(100.0 * cell.Branches.Max(branch => branch.Count) / cell.Rounds, 0, MidpointRounding.ToEven));

    private static bool IsPlan(string stage) => stage == nameof(Core.Rounds.Stage.PlanReview);

    private static double Rounded(double value) => Math.Round(value, 2, MidpointRounding.ToEven);
}
