using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Server;

namespace CoaiMcp.Store;

/// <summary>
/// What one counted round adds to its cell (D9, D10): its lines, its delta, the six numerators with their coverage, the reviewers beside the
/// set, and the flags a header needs. Pure.
/// </summary>
internal static class RoundAuthorsSumsOf
{
    private const string Failed = ReviewerState.Failed;

    private sealed record Delta(bool Covered, long Lines, string Why);

    public static RoundSums Of(
        RoundFacts round,
        RoundLines.Counted lines,
        string cohort,
        AuthorsStateKind worst,
        IReadOnlyList<RoundFacts> contributing,
        RoundAuthorsCoders.Coders coders,
        RoundAuthorsSelection selection)
    {
        var delta = DeltaOf(round, cohort, selection);
        var relaxed = selection.WithUncovered && selection.Stage != nameof(Core.Rounds.Stage.PlanReview);
        var uncovered = RoundAuthorsPopulation.UncoveredOf(contributing);

        return new RoundSums(
            lines.Selected, lines.Elided, lines.Excluded, lines.BinaryFiles, lines.NotCounted,
            delta.Lines, delta.Covered, delta.Why,
            NumeratorsOf(round.Findings),
            Count(round, Failed), Count(round, ReviewerState.StoodDown), Count(round, Failed) > 0,
            worst == AuthorsStateKind.Partial,
            contributing.Any(other => other.Authors.Reasons.Contains(AttributionReasons.PeerRunning)),
            relaxed && uncovered > 0,
            relaxed && uncovered < 0,
            round.Authors.Ambiguous,
            coders.Launches,
            coders.Pairs);
    }

    /// <summary>
    /// The delta of a CORRECTIVE round of the code stage (D10): covered when recorded (an observed zero included), else counted per scope word.
    /// A first round has no delta, and a plan round has no diff — neither is "unavailable".
    /// </summary>
    private static Delta DeltaOf(RoundFacts round, string cohort, RoundAuthorsSelection selection) =>
        cohort != Cohort.Corrective || selection.Stage == nameof(Core.Rounds.Stage.PlanReview)
            ? new Delta(false, 0, string.Empty)
            : IsRecorded(round) ? new Delta(true, RoundLines.DeltaOf(round.Size.Size.Delta.Cells, selection.Kinds), string.Empty) : new Delta(false, 0, WhyNot(round));

    private static bool IsRecorded(RoundFacts round) => round.Size.Recorded && round.Size.Size.Delta.State == ScopeState.Recorded;

    private static string WhyNot(RoundFacts round) => ScopeWord.Of(round.Size.Recorded ? round.Size.Size.Delta.State : string.Empty);

    private static int Count(RoundFacts round, string status) => round.Reviewers.Count(reviewer => reviewer.Status == status);

    private static RoundNumerators NumeratorsOf(FindingFacts found) => new(
        new Numerator(found.All, true),
        new Numerator(found.Severe, true),
        new Numerator(found.GateCount, true),
        new Numerator(found.ReRaised, true),
        new Numerator(found.Accepted, found.Resolved),
        new Numerator(found.Unresolved, true));
}
