using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;

namespace CoaiMcp.Store;

/// <summary>
/// WHICH rounds are in the report, which cohort each is in, and why each excluded one is out (E6.S2) — decided once, apart from any arithmetic,
/// before anything is counted. Pure: no clock, no disk, no database.
/// </summary>
/// <remarks>
/// <para><b>One predicate for cohorts, contributors and the authors that exist.</b> A round is <i>reviewed</i> — and a contributor of later
/// rounds — when E4 calls it <i>settled</i> (<see cref="Windows.IsSettled"/>: a plan or code round, not skipped, not interrupted, <c>done</c>).
/// The cohort is decided over ALL such rounds of the gate session's stage, BEFORE the window and before any exclusion: <c>first</c> is the lowest
/// number, every later one is <c>corrective</c>, and an excluded first round promotes nobody (D1).</para>
/// <para><b>The union of authors is E4.S4's</b> (<see cref="Contributors.Of"/>) over the stored rows mapped by <see cref="StoredAuthors"/> — the
/// settled rounds of the session that started at or before this one, both stages (D2). The authors state of the round is the WORST state over
/// them (D3). The state read is each round's roll-up over its two windows — per-window states are in the session file only and step 24 does not
/// project them — so with <c>before-open</c> left out a round whose before-open window alone is partial or unobserved still reads partial or
/// excluded. The error goes one way: toward exclusion, never toward a claim.</para>
/// <para><b>Exclusion is the FIRST failing check, once</b> (D12): the checks are declared in the enum's order and a round is out under the first it
/// fails, so no round is counted under two reasons. A plan round has no diff, so none of the size checks apply to it.</para>
/// </remarks>
public static class RoundAuthorsPopulation
{
    private sealed record Verdict(ExclusionReason Reason, string Detail);

    private sealed record View(RoundFacts Fact, RoundAuthorsSelection Selection, AuthorsStateKind Worst, int Uncovered, RoundLines.Counted Lines);

    private sealed record Judged(string Cohort, IReadOnlyList<CountedRound> Counted, IReadOnlyList<ExcludedRound> Excluded);

    public static Population Of(IReadOnlyList<RoundFacts> facts, RoundAuthorsSelection selection)
    {
        var cohorts = Cohorts(facts, selection.Stage);
        var sessions = facts.ToLookup(round => round.SessionId);
        var judged = facts.Where(round => round.InWindow && round.Stage == selection.Stage)
            .Select(round => Judge(round, [.. sessions[round.SessionId]], cohorts.GetValueOrDefault(round.RoundId, ExclusionWords.NoCohort), selection))
            .Where(round => Asked(round.Cohort, selection.Cohort))
            .ToList();

        return new Population([.. judged.SelectMany(round => round.Counted)], [.. judged.SelectMany(round => round.Excluded)]);
    }

    /// <summary>The ONE place the settled predicate is applied to a stored round: E4's, over the round's own stage, status and verdict.</summary>
    internal static bool IsSettled(RoundFacts round) =>
        Windows.IsSettled(new GateRound(round.Stage, round.Number, null, round.Status, round.Verdict, string.Empty, null));

    /// <summary>The cohort of every settled round of the stage: the lowest-numbered of its gate session is <c>first</c>, the rest <c>corrective</c> (D1).</summary>
    private static Dictionary<long, string> Cohorts(IReadOnlyList<RoundFacts> facts, string stage) =>
        facts.Where(round => round.Stage == stage && IsSettled(round))
            .GroupBy(round => round.SessionId)
            .SelectMany(session => session.OrderBy(round => round.Number).Select((round, at) => (round.RoundId, Cohort: at == 0 ? Cohort.First : Cohort.Corrective)))
            .ToDictionary(pair => pair.RoundId, pair => pair.Cohort);

    /// <summary>A round dropped by <c>--cohort</c> was not asked for, and is no exclusion; a round with no cohort is always reported.</summary>
    private static bool Asked(string cohort, string wanted) =>
        cohort == ExclusionWords.NoCohort || wanted == Cohort.All || wanted == cohort;

    private static Judged Judge(RoundFacts round, List<RoundFacts> session, string cohort, RoundAuthorsSelection selection)
    {
        var contributing = Contributing(round, session);
        var view = new View(round, selection, WorstState(contributing), UncoveredOf(contributing), RoundLines.Of(round.Size.Size.Whole.Cells, selection.Kinds));
        var period = PeriodOf(round.StartedUtc, selection.SplitUtc);
        var failure = Failures(view).Take(1).ToList();

        return failure.Count > 0
            ? new Judged(cohort, [], [new ExcludedRound(round.RoundId, cohort, period, failure[0].Reason, failure[0].Detail)])
            : new Judged(cohort, [Counted(view, contributing, cohort, period)], []);
    }

    /// <summary>The settled rounds of the gate session that started at or before this one — the plan rounds' windows included (D2).</summary>
    private static List<RoundFacts> Contributing(RoundFacts round, List<RoundFacts> session) =>
        [.. session.Where(other => IsSettled(other) && string.CompareOrdinal(other.StartedUtc, round.StartedUtc) <= 0)];

    /// <summary>D3: the worst state over the contributing rounds — not recorded &lt; unobserved &lt; partial &lt; observed.</summary>
    private static AuthorsStateKind WorstState(List<RoundFacts> contributing) =>
        contributing.Select(round => round.Authors.State).DefaultIfEmpty(AuthorsStateKind.NotRecorded).Min();

    /// <summary>
    /// D12 over the union, as D3 is: the round's whole change carries what every contributing round's windows wrote, so their writes outside the
    /// covered checkout are the round's too. A known write elsewhere (the sum, &gt; 0) outranks an unknown count (<c>-1</c>); otherwise 0 is covered.
    /// </summary>
    internal static int UncoveredOf(IReadOnlyList<RoundFacts> contributing)
    {
        var elsewhere = contributing.Where(round => round.Authors.UncoveredWrites > 0).Sum(round => round.Authors.UncoveredWrites);

        return elsewhere > 0 ? elsewhere : contributing.Any(round => round.Authors.UncoveredWrites < 0) ? -1 : 0;
    }

    /// <summary>D13: no split is one period <c>all</c>; else a round AT the split is <c>after</c>. Compared textually over the column's own UTC stamps.</summary>
    internal static string PeriodOf(string startedUtc, string splitUtc) =>
        splitUtc.Length == 0 ? Period.All : string.CompareOrdinal(startedUtc, splitUtc) < 0 ? Period.Before : Period.After;

    private static IEnumerable<Verdict> Failures(View view) =>
        Always(view).Concat(view.Selection.Stage == nameof(Core.Rounds.Stage.PlanReview) ? [] : NeedingADiff(view));

    private static IEnumerable<Verdict> Always(View view) =>
        When(view.Fact.Status != Windows.Done, ExclusionReason.NotDone)
            .Concat(When(view.Fact.Verdict == Windows.Skipped, ExclusionReason.Skipped))
            .Concat(When(!view.Fact.Reviewers.Any(reviewer => reviewer.Status == "done"), ExclusionReason.NoReviewerAnswered))
            .Concat(When(view.Worst == AuthorsStateKind.NotRecorded, ExclusionReason.AuthorsNotRecorded))
            .Concat(When(view.Worst == AuthorsStateKind.Unobserved, ExclusionReason.AuthorsUnobserved));

    private static IEnumerable<Verdict> NeedingADiff(View view) =>
        When(!view.Fact.Size.Recorded, ExclusionReason.SizeNotRecorded)
            .Concat(When(Unavailable(view), ExclusionReason.SizeUnavailable, ScopeWord.Of(view.Fact.Size.Size.Whole.State)))
            .Concat(When(view.Lines.BinaryOnly, ExclusionReason.BinaryOnly))
            .Concat(When(CoverageRequired(view) && view.Uncovered > 0, ExclusionReason.SizeNotCovered))
            .Concat(When(CoverageRequired(view) && view.Uncovered < 0, ExclusionReason.SizeCoverageUnknown))
            .Concat(When(view.Lines.Selected == 0, ExclusionReason.NoSelectedLines))
            .Concat(When(view.Selection.WithoutElided && view.Lines.AnyElided, ExclusionReason.Elided));

    private static bool Unavailable(View view) =>
        view.Fact.Size.Recorded && view.Fact.Size.Size.Whole.State != ScopeState.Recorded;

    /// <summary>Whether the write counts are to be believed: <c>--with-uncovered</c> relaxes the coverage requirement as a whole (D12).</summary>
    private static bool CoverageRequired(View view) => !view.Selection.WithUncovered;

    private static IEnumerable<Verdict> When(bool failed, ExclusionReason reason, string detail = "") =>
        failed ? [new Verdict(reason, detail)] : [];

    private static CountedRound Counted(View view, List<RoundFacts> contributing, string cohort, string period)
    {
        var authors = contributing.Select(StoredAuthors.Of).ToList();
        var coders = RoundAuthorsCoders.Of(authors, view.Selection.WithBeforeOpen);
        var key = new CellKey(cohort, period, coders.Mix, coders.Effort, coders.MainModel, ReviewerSet.Of(view.Fact.Reviewers));

        return new CountedRound(view.Fact, cohort, period, key, RoundAuthorsSumsOf.Of(view.Fact, view.Lines, cohort, view.Worst, contributing, coders, view.Selection));
    }
}

/// <summary>The three cohort words and the cohort filter's <c>all</c>. A counted round is <c>first</c> or <c>corrective</c>; there is no third value.</summary>
public static class Cohort
{
    public const string First = "first";
    public const string Corrective = "corrective";
    public const string All = "all";
}

/// <summary>The period words: one <c>all</c>, or <c>before</c> and <c>after</c> a split.</summary>
public static class Period
{
    public const string All = "all";
    public const string Before = "before";
    public const string After = "after";
}

/// <summary>A scope state's own word without its <c>unavailable: </c> prefix — what the report counts per reason.</summary>
internal static class ScopeWord
{
    private const string Prefix = "unavailable: ";

    public static string Of(string state) =>
        state.Length == 0 ? "not recorded" : state.StartsWith(Prefix, StringComparison.Ordinal) ? state[Prefix.Length..] : state;
}
