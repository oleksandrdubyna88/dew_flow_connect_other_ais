using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFacts;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// E6.S3: one pure unit turns a <see cref="Population"/> into ADDITIVE cell sums and, at render time only, into figures — ratios of sums, Unknown
/// never 0, marks from the sums, the header. Populations are built through S2 from in-memory facts, so every cell is asserted from rounds a
/// reader can see.
/// </summary>
public sealed class RoundAuthorsCellsTests
{
    private static RoundAuthorsSums Counted(RoundAuthorsSelection selection, params RoundFacts[] rounds) =>
        RoundAuthorsCells.Count(RoundAuthorsPopulation.Of(rounds, selection), selection);

    private static CellSums Only(RoundAuthorsSums sums) => sums.Cells.Should().ContainSingle().Subject;

    private static FindingFacts Found(int all, int severe = 0, int gate = 0, int reRaised = 0, int accepted = 0, int unresolved = 0, bool resolved = false) =>
        new(all, severe, gate, reRaised, accepted, unresolved, resolved);

    [Fact]
    public void FiguresAreRatiosOfSums_NotMeansOfRatios()
    {
        var sums = Counted(Sel(), Round(1, session: "a", findings: Found(10, unresolved: 10), size: Code(100)), Round(1, session: "b", findings: Found(0), size: Code(900)));

        var cell = Only(sums);

        RoundAuthorsCells.PerHundredLines(cell.Numerators.All, "CodeReview").Should().Be(RoundFigure.Of(1.00), "10 findings over 1000 lines, not the mean of 10.00 and 0.00");
        RoundAuthorsCells.PerRound(cell.Numerators.All).Should().Be(RoundFigure.Of(5.00));
    }

    [Fact]
    public void ACoverageSpecificRatio_IsOverItsOwnCoveredRoundsAndLines()
    {
        var resolved = Round(1, session: "a", findings: Found(4, accepted: 4, resolved: true));
        var never = Round(1, session: "b", findings: Found(0));

        var cell = Only(Counted(Sel(), resolved, never));

        cell.Numerators.Accepted.Should().Be(new NumeratorSums(4, 1, 100, 0, 0, 0));
        RoundAuthorsCells.PerHundredLines(cell.Numerators.Accepted, "CodeReview").Should().Be(RoundFigure.Of(4.00), "over the one resolved round's 100 lines, not 200");
        RoundAuthorsCells.PerRound(cell.Numerators.Accepted).Should().Be(RoundFigure.Of(4.00));
    }

    [Fact]
    public void UnknownIsNotZero_AndAPlanHasNoPerHundredFigure()
    {
        var cell = Only(Counted(Sel(), Round(1, session: "a", findings: Found(2, unresolved: 2)), Round(1, session: "b", findings: Found(1, unresolved: 1))));

        RoundAuthorsCells.PerRound(cell.Numerators.Accepted).Should().Be(RoundFigure.Unknown, "no round was resolved: unknown, not 0");
        RoundAuthorsCells.PerHundredLines(cell.Numerators.Accepted, "CodeReview").Should().Be(RoundFigure.Unknown);
        RoundAuthorsCells.PerRound(cell.Numerators.Unresolved).State.Should().Be(FigureState.Known, "unresolved is a real state, a known number");
        RoundAuthorsCells.PerHundredLines(cell.Numerators.All, "PlanReview").Should().Be(RoundFigure.NotApplicable, "a plan has no diff");
        RoundAuthorsCells.PerHundredDelta(cell.Numerators.All, "PlanReview").Should().Be(RoundFigure.NotApplicable);
    }

    [Fact]
    public void CohortsAreNeverPooled_OneKeyInFirstAndInCorrectiveIsTwoCells()
    {
        var sums = Counted(Sel(), Round(1), Round(2));

        sums.Cells.Select(cell => cell.Key.Cohort).Should().Equal([Cohort.Corrective, Cohort.First]);
        sums.Cells.Should().AllSatisfy(cell => cell.Key.Cohort.Should().BeOneOf(Cohort.First, Cohort.Corrective));
    }

    [Fact]
    public void ExactCells_TwoMixesTwoReviewerSetsTwoPeriods_EveryFieldAsserted()
    {
        var gemini = new ReviewerFact("gemini", "gemini-3", string.Empty, true, "done");
        var failed = new ReviewerFact("gemini", "gemini-3", string.Empty, true, "failed");
        var sonnet = Observed(MainRow(), Coder("so", "claude-sonnet-5-5"));
        var opus = Observed(MainRow(), Coder("op", "claude-opus-5-5", "opus"));
        var rounds = new[]
        {
            Round(1, session: "a1", hours: 1, branch: "main", authors: sonnet, findings: Found(3, 2, 1, 0, 1, 1, true)),
            Round(1, session: "b1", hours: 2, branch: "main", authors: opus, findings: Found(1, unresolved: 1)),
            Round(1, session: "c1", hours: 3, branch: "main", authors: sonnet, reviewers: [Codex, gemini], findings: Found(2, 1, 1, 1, 0, 2, true)),
            Round(1, session: "d1", hours: 4, branch: "other", authors: opus, reviewers: [Codex, failed], findings: Found(0)),
        };

        var cells = Counted(Sel(split: Stamp(T0.AddHours(2.5))), rounds).Cells;

        string repo = RoundAuthorsCells.RepoId("D:/repo");
        CellKey Key(string period, string mix, string set) => new(Cohort.First, period, mix, "high", "claude-opus-5-5", set);
        NumeratorSums Num(long total, bool covered = true) => covered ? new NumeratorSums(total, 1, 100, 0, 0, 0) : new NumeratorSums(0, 0, 0, 0, 0, 0);
        CellSums Cell(CellKey key, string session, string branch, string family, CellNumerators numerators, int failedRows = 0) => new(
            key, 1, [session], [new NamedCount(repo + "\n" + branch, 1)], 100, 0, 0, 0, [], 0, 0, [], numerators, failedRows, 0, failedRows, 0, 0, 0, 0,
            new AmbiguousSums(1, 0, 0, 0, 0, 0, 0, 0, 0), [new NamedCount(family, 1)], [new NamedCount(family + "\n" + family, 1)]);

        cells.Should().BeEquivalentTo(
            [
                Cell(Key("after", "opus", "codex/gpt-6-luna@high"), "d1", "other", "opus", new CellNumerators(Num(0), Num(0), Num(0), Num(0), Num(0, false), Num(0)), failedRows: 1),
                Cell(Key("after", "sonnet", "codex/gpt-6-luna@high + gemini/gemini-3@none"), "c1", "main", "sonnet", new CellNumerators(Num(2), Num(1), Num(1), Num(1), Num(0), Num(2))),
                Cell(Key("before", "opus", "codex/gpt-6-luna@high"), "b1", "main", "opus", new CellNumerators(Num(1), Num(0), Num(0), Num(0), Num(0, false), Num(1))),
                Cell(Key("before", "sonnet", "codex/gpt-6-luna@high"), "a1", "main", "sonnet", new CellNumerators(Num(3), Num(2), Num(1), Num(0), Num(1), Num(1))),
            ],
            options => options.WithStrictOrdering(),
            "four rounds, two mixes, two reviewer sets, two periods, one failed reviewer: every field of every cell");
    }

    [Fact]
    public void Marks_AreSmallUnderTenRounds_AndDominatedWhenOneBranchHoldsMoreThanHalf()
    {
        IReadOnlyList<string> Marks(int rounds, int onMain) =>
            RoundAuthorsCells.Marks(Only(Counted(Sel(), [.. Enumerable.Range(0, rounds).Select(at => Round(1, session: "m" + at, branch: at < onMain ? "main" : "x" + at))])));

        Marks(9, 1).Should().Equal(["small"]);
        Marks(10, 1).Should().BeEmpty("ten rounds is not small");
        Marks(10, 6).Should().Equal(["dominated"], "6 of 10 is more than half");
        Marks(10, 5).Should().BeEmpty("5 of 10 is not");
    }

    [Fact]
    public void TheHeader_NamesCoincidentChanges_AndSaysAssociationOnly()
    {
        var split = Stamp(T0.AddHours(2.5));
        var gemini = new ReviewerFact("gemini", "gemini-3", string.Empty, true, "done");
        IReadOnlyList<string> Header(RoundAuthorsSelection selection, params RoundFacts[] rounds) =>
            RoundAuthorsHeader.Of(Counted(selection, rounds), selection, effortMarkerKnown: true);

        var both = Header(
            Sel(split: split),
            Round(1, session: "a", hours: 1, authors: Observed(MainRow(), Coder("so", "claude-sonnet-5-5"))),
            Round(1, session: "b", hours: 3, authors: Observed(MainRow(), Coder("op", "claude-opus-5-5", "opus")), reviewers: [gemini]));
        var effortOnly = Header(
            Sel(split: split),
            Round(1, session: "a", hours: 1, authors: Observed(MainRow(), Coder("so", "claude-sonnet-5-5", effort: "high"))),
            Round(1, session: "b", hours: 3, authors: Observed(MainRow(), Coder("so", "claude-sonnet-5-5", effort: "low"))));
        var noSplit = Header(Sel(), Round(1, session: "a"));

        both.Should().Contain(line => line.Contains("coder mix") && line.Contains("reviewer set") && line.StartsWith("Cohort first: changed between the periods"));
        both.Should().Contain(RoundAuthorsHeader.CannotSeparate);
        effortOnly.Should().Contain("Cohort first: changed between the periods: coder effort.").And.NotContain(RoundAuthorsHeader.CannotSeparate);
        noSplit.Should().Contain(RoundAuthorsHeader.NoComparison).And.NotContain(line => line.Contains("changed between the periods"));
        foreach (var header in new[] { both, effortOnly, noSplit })
        {
            header[0].Should().Be(RoundAuthorsHeader.AssociationOnly, "the association-only sentence is the first line");
            header.Should().Contain([RoundAuthorsHeader.AskedModel, RoundAuthorsHeader.Contribution, RoundAuthorsHeader.UnobservedLines, RoundAuthorsHeader.RankAllocation, RoundAuthorsHeader.RollUpLimit, RoundAuthorsHeader.Denominator, RoundAuthorsHeader.DeltaFallback, RoundAuthorsHeader.UnknownKind, RoundAuthorsHeader.Numerators, RoundAuthorsHeader.ReviewerEffort]);
            header.Should().NotContain(RoundAuthorsHeader.Provisional, "no counted round is provisional");
        }

        var provisional = Stated(AuthorsStateKind.Partial, "peer running", 0, MainRow(), Coder("so", "claude-sonnet-5-5"));
        Header(Sel(), Round(1, session: "a", authors: provisional)).Should().Contain(RoundAuthorsHeader.Provisional);
        RoundAuthorsHeader.Of(Counted(Sel(), Round(1)), Sel(), effortMarkerKnown: false).Should().Contain(RoundAuthorsHeader.EffortOlderSource);
    }

    [Fact]
    public void Exclusions_Conserve_AndEveryReasonIsListedWithItsCount()
    {
        var selection = Sel();
        var rounds = new[] { Round(1), Round(2, status: "running"), Round(3, reviewers: []), Round(4, verdict: "skipped"), Round(5, size: SizeFacts.NotRecorded) };

        var sums = Counted(selection, rounds);

        (sums.Cells.Sum(cell => cell.Rounds) + sums.Exclusions.Sum(count => count.Rounds)).Should().Be(rounds.Length);
        foreach (var cohort in new[] { Cohort.First, Cohort.Corrective })
        {
            sums.Exclusions.Where(count => count.Cohort == cohort && count.Period == Period.All).Select(count => count.Reason)
                .Should().Equal(Enum.GetValues<ExclusionReason>().Select(ExclusionWords.Of), "every reason, zeros included, in the enum's order");
        }
    }

    [Fact]
    public void ThePlanStage_ListsOnlyTheReasonsThatCouldApply()
    {
        var sums = Counted(Sel("PlanReview"), Round(1, stage: "PlanReview"));

        sums.Exclusions.Select(count => count.Reason).Should().NotContain(ExclusionWords.Of(ExclusionReason.SizeNotRecorded), "a plan has no diff: a row there would claim a check that never ran");
        sums.Exclusions.Select(count => count.Reason).Should().Contain(ExclusionWords.Of(ExclusionReason.NoReviewerAnswered));
    }

    [Fact]
    public void SizeUnavailable_IsCountedPerScopeWordBesideTheExclusion()
    {
        var gone = Round(1, size: Sized(new RoundSize(ScopeSize.Unavailable(ScopeState.NoMergeBase), ScopeSize.Unavailable(ScopeState.FirstRound))));
        var moved = Round(1, session: "t", size: Sized(new RoundSize(ScopeSize.Unavailable(ScopeState.BaseMoved), ScopeSize.Unavailable(ScopeState.FirstRound))));

        var count = Counted(Sel(), gone, moved).Exclusions.Single(row => row.Reason == "size unavailable" && row.Cohort == Cohort.First);

        count.Rounds.Should().Be(2);
        count.Because.Should().Equal([new NamedCount("base moved", 1), new NamedCount("no merge base", 1)]);
    }

    [Fact]
    public void TheSameCounting_IsTheSameBytes_InAnyInputOrder()
    {
        RoundFacts[] rounds = [Round(1, session: "a"), Round(1, session: "b", authors: Observed(MainRow(), Coder("o", "claude-opus-5-5", "opus"))), Round(2, session: "a"), Round(1, session: "c", status: "running")];

        var first = Counted(Sel(), rounds);
        var again = Counted(Sel(), rounds);
        var shuffled = Counted(Sel(), [.. rounds.Reverse()]);

        again.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
        shuffled.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ADeltaFigure_IsOverTheRoundsWhoseDeltaIsRecorded_AndUnknownWhenNoneIs()
    {
        var whole = new[] { Cell("shown", "code", 100) };
        SizeFacts Recorded() => Sized(WithDelta(whole, Cell("shown", "code", 20)));
        SizeFacts Unavailable(string state) => Sized(new RoundSize(new ScopeSize(ScopeState.Recorded, "b", whole), ScopeSize.Unavailable(state)));

        var mixed = Counted(
            Sel(),
            Round(1, session: "a"), Round(2, session: "a", size: Recorded(), findings: Found(3, unresolved: 3)),
            Round(1, session: "b"), Round(2, session: "b", size: Unavailable(ScopeState.NoMergeBase), findings: Found(1, unresolved: 1)));
        var none = Counted(Sel(), Round(1, session: "c"), Round(2, session: "c", size: Unavailable(ScopeState.BaseMoved)));

        var corrective = mixed.Cells.Single(cell => cell.Key.Cohort == Cohort.Corrective);
        RoundAuthorsCells.PerHundredLines(corrective.Numerators.All, "CodeReview").Should().Be(RoundFigure.Of(2.00), "4 findings over 200 whole lines");
        RoundAuthorsCells.PerHundredDelta(corrective.Numerators.All, "CodeReview").Should().Be(RoundFigure.Of(15.00), "3 findings over the 20 delta lines of the one round whose delta is recorded");
        corrective.Numerators.All.DeltaCoveredRounds.Should().Be(1);
        corrective.DeltaUnavailable.Should().Equal([new NamedCount("no merge base", 1)]);
        RoundAuthorsCells.PerHundredDelta(none.Cells.Single(cell => cell.Key.Cohort == Cohort.Corrective).Numerators.All, "CodeReview").Should().Be(RoundFigure.Unknown, "every delta is unavailable: never a figure over whole lines");
    }

    [Fact]
    public void AnAmbiguousTotal_WithAnUnknownRound_IsAFloor()
    {
        var known = Observed(MainRow(), Coder("a", "claude-sonnet-5-5")) with { Ambiguous = new AmbiguousFacts(true, 1, 4, 5, 6, 7, 8, 9) };
        var unknown = known with { Ambiguous = AmbiguousFacts.Unknown };

        var cell = Only(Counted(Sel(), Round(1, session: "a", authors: known), Round(1, session: "b", authors: unknown)));

        cell.Ambiguous.Should().Be(new AmbiguousSums(1, 1, 1, 4, 5, 6, 7, 8, 9), "an unknown total is counted, never summed as a minus one");
        RoundAuthorsCells.Ambiguous(cell.Ambiguous.Launches, cell.Ambiguous).Should().Be(RoundFigure.AtLeast(4));
        RoundAuthorsCells.Ambiguous(4, new AmbiguousSums(2, 0, 0, 0, 0, 0, 0, 0, 0)).Should().Be(RoundFigure.Of(4));
    }

    [Fact]
    public void TheJsonOfASumsNeverCarriesAFigure_EveryNumberIsAnInteger()
    {
        var cell = Only(Counted(Sel(), Round(1)));

        cell.Numerators.All.Total.Should().Be(1);
        typeof(CellSums).GetProperties().Select(property => property.PropertyType).Should().NotContain([typeof(double), typeof(float), typeof(decimal), typeof(RoundFigure)], "a figure is a return value of a figure function, never a member of a sum");
    }
}
