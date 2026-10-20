namespace CoaiMcp.Store;

// The counting shapes of the round-authors report (E6.S2-S4 of todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md). Records only.
// What is stored and serialised is SUMS, COUNTS and SETS — integers and short ids, never a figure (D16): a ratio is computed at render time.

/// <summary>One named integer — the shape every small map of the report takes, so a map serialises as a sorted list and merges by key.</summary>
public sealed record NamedCount(string Name, long Count);

/// <summary>
/// What the report was asked for. <c>Stage</c> is the STORED name (<c>CodeReview</c>/<c>PlanReview</c>); <c>Kinds</c> arrives sorted and distinct;
/// <c>UntilUtc</c> is <c>""</c> when <c>--until</c> was not given (the resolved instant is the source's <c>ReadUtc</c>), and <c>SplitUtc</c> is
/// <c>""</c> for no split. Two selections are two populations (S4 refuses to add them).
/// </summary>
public sealed record RoundAuthorsSelection(
    string Stage,
    string SinceUtc,
    string UntilUtc,
    string SplitUtc,
    string Cohort,
    IReadOnlyList<string> Kinds,
    bool WithBeforeOpen,
    bool WithoutElided,
    bool WithUncovered);

/// <summary>Why a round is out of the report — DECLARED in D12's order; the first check that fails wins, so a round is excluded once.</summary>
public enum ExclusionReason
{
    NotDone,
    Skipped,
    NoReviewerAnswered,
    AuthorsNotRecorded,
    AuthorsUnobserved,
    SizeNotRecorded,
    SizeUnavailable,
    BinaryOnly,
    SizeNotCovered,
    SizeCoverageUnknown,
    NoSelectedLines,
    Elided,
}

/// <summary>The cell a counted round falls in: every dimension a figure is split by, as words. Cohorts are never pooled.</summary>
public sealed record CellKey(string Cohort, string Period, string CoderMix, string CoderEffort, string MainModel, string ReviewerSet);

/// <summary>One numerator of one round and whether its value is KNOWN (an unresolved round has no <c>accepted</c> — D9).</summary>
public sealed record Numerator(long Value, bool Covered);

/// <summary>The six numerators of D8/D9 for one round. <c>Severe</c> is pre-discount (<c>is_gating</c>), <c>GateCount</c> post-discount (<c>rounds.gating</c>).</summary>
public sealed record RoundNumerators(Numerator All, Numerator Severe, Numerator GateCount, Numerator ReRaised, Numerator Accepted, Numerator Unresolved);

/// <summary>Everything S3 adds up for one counted round (D9, D10).</summary>
public sealed record RoundSums(
    long SelectedLines,
    long ElidedLines,
    long ExcludedLines,
    long BinaryFiles,
    IReadOnlyList<NamedCount> NotCounted,
    long DeltaLines,
    bool DeltaCovered,
    string DeltaUnavailable,
    RoundNumerators Numerators,
    int FailedReviewers,
    int StoodDownReviewers,
    bool PartialSet,
    bool AuthorsPartial,
    bool Provisional,
    bool SizeNotCovered,
    bool SizeCoverageUnknown,
    AmbiguousFacts Ambiguous,
    IReadOnlyList<NamedCount> CoderLaunches,
    IReadOnlyList<NamedCount> Requested);

/// <summary>A round that is in. <c>Facts</c> is the round itself; the rest is decided once by S2.</summary>
public sealed record CountedRound(RoundFacts Facts, string Cohort, string Period, CellKey Key, RoundSums Sums);

/// <summary>A round that is out, and why — <c>Detail</c> is the scope's own word for <c>size unavailable</c> (<c>no merge base</c>), empty otherwise.</summary>
public sealed record ExcludedRound(long RoundId, string Cohort, string Period, ExclusionReason Reason, string Detail);

/// <summary>WHO is in and why each excluded round is out — decided apart from any arithmetic.</summary>
public sealed record Population(IReadOnlyList<CountedRound> In, IReadOnlyList<ExcludedRound> Out);

/// <summary>The words of the exclusion reasons, as the table and the JSON print them — one place, so the two cannot drift.</summary>
public static class ExclusionWords
{
    /// <summary>The cohort of a round that has none (a round that did not run, or a skip).</summary>
    public const string NoCohort = "—";

    public static string Of(ExclusionReason reason) => reason switch
    {
        ExclusionReason.NotDone => "not done",
        ExclusionReason.Skipped => "skipped",
        ExclusionReason.NoReviewerAnswered => "no reviewer answered",
        ExclusionReason.AuthorsNotRecorded => "authors not recorded",
        ExclusionReason.AuthorsUnobserved => "authors unobserved",
        ExclusionReason.SizeNotRecorded => "size not recorded",
        ExclusionReason.SizeUnavailable => "size unavailable",
        ExclusionReason.BinaryOnly => "binary only",
        ExclusionReason.SizeNotCovered => "size not covered",
        ExclusionReason.SizeCoverageUnknown => "size coverage unknown",
        ExclusionReason.NoSelectedLines => "no selected lines",
        _ => "elided",
    };

    /// <summary>The first reason that needs a diff: from here on a plan round is never excluded (it has none).</summary>
    public static bool NeedsADiff(ExclusionReason reason) => reason >= ExclusionReason.SizeNotRecorded;
}

/// <summary>One numerator summed over a cell's rounds whose value is KNOWN (D9), and — for a corrective cell — over those whose delta is also recorded (D10).</summary>
public sealed record NumeratorSums(long Total, int CoveredRounds, long CoveredLines, int DeltaCoveredRounds, long DeltaCoveredLines, long DeltaTotal);

/// <summary>The six numerators of one cell.</summary>
public sealed record CellNumerators(NumeratorSums All, NumeratorSums Severe, NumeratorSums GateCount, NumeratorSums ReRaised, NumeratorSums Accepted, NumeratorSums Unresolved);

/// <summary>Ambiguous activity summed over the cell's KNOWN rounds; <c>UnknownRounds</c> says how many contributed no total, so a figure over it is a floor (D17).</summary>
public sealed record AmbiguousSums(
    int KnownRounds, int UnknownRounds, long Agents, long Launches, long Turns, long InputTokens, long OutputTokens, long CacheCreationTokens, long CacheReadTokens);

/// <summary>
/// One cell, as ADDITIVE sums: integers, sets and counts-by-name only, so two halves add to the union (S4). A figure, a mark or a header line is
/// never in here — those are render-time functions over these sums (D14, D16).
/// </summary>
public sealed record CellSums(
    CellKey Key,
    int Rounds,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<NamedCount> Branches,
    long SelectedLines,
    long ElidedLines,
    long ExcludedLines,
    long BinaryFiles,
    IReadOnlyList<NamedCount> NotCounted,
    long DeltaLines,
    int DeltaCoveredRounds,
    IReadOnlyList<NamedCount> DeltaUnavailable,
    CellNumerators Numerators,
    int FailedReviewers,
    int StoodDownReviewers,
    int PartialSetRounds,
    int AuthorsPartialRounds,
    int ProvisionalRounds,
    int SizeNotCoveredRounds,
    int SizeCoverageUnknownRounds,
    AmbiguousSums Ambiguous,
    IReadOnlyList<NamedCount> CoderLaunches,
    IReadOnlyList<NamedCount> Requested);

/// <summary>How many rounds one reason excluded in one cohort and period; <c>Because</c> splits <c>size unavailable</c> per scope word and is empty for every other reason.</summary>
public sealed record ExclusionCount(string Cohort, string Period, string Reason, int Rounds, IReadOnlyList<NamedCount> Because);

/// <summary>What S3 counts: the cells and the exclusions, both additive.</summary>
public sealed record RoundAuthorsSums(IReadOnlyList<CellSums> Cells, IReadOnlyList<ExclusionCount> Exclusions);

/// <summary>What a figure is: a number, an absent population (<c>Unknown</c>), a thing that does not exist for the stage (<c>NotApplicable</c>), or a floor.</summary>
public enum FigureState
{
    Known,
    Unknown,
    NotApplicable,
    Floor,
}

/// <summary>A figure computed at render time from sums — with no population it is <see cref="FigureState.Unknown"/>, never 0.</summary>
public readonly record struct RoundFigure(FigureState State, double Value)
{
    public static RoundFigure Unknown => new(FigureState.Unknown, 0);

    public static RoundFigure NotApplicable => new(FigureState.NotApplicable, 0);

    public static RoundFigure Of(double value) => new(FigureState.Known, value);

    public static RoundFigure AtLeast(double value) => new(FigureState.Floor, value);
}
