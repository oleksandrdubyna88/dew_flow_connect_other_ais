using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFacts;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// E6.S2: WHO is in the report, which cohort each round is in, and why each excluded round is out — decided apart from any arithmetic, over
/// facts built in memory (records, no database). One failing check excludes a round once, in D12's order.
/// </summary>
public sealed class RoundAuthorsPopulationTests
{
    private static CountedRound CountedAt(Population population, int number) =>
        population.In.Should().ContainSingle(round => round.Facts.Number == number, $"round {number} is counted").Subject;

    private static ExcludedRound OutAt(Population population, int number) =>
        population.Out.Should().ContainSingle(round => round.RoundId % 100 == number, $"round {number} is excluded").Subject;

    [Fact]
    public void Cohorts_AreTheLowestReviewedRoundFirst_TheRestCorrective_AnInterruptedOneHasNone()
    {
        var population = RoundAuthorsPopulation.Of([Round(1), Round(2), Round(3, status: "interrupted")], Sel());

        CountedAt(population, 1).Cohort.Should().Be("first");
        CountedAt(population, 2).Cohort.Should().Be("corrective");
        OutAt(population, 3).Should().Match<ExcludedRound>(round => round.Reason == ExclusionReason.NotDone && round.Cohort == ExclusionWords.NoCohort);
    }

    [Fact]
    public void AnInterruptedFirstRound_LeavesTheNextOneFirst()
    {
        var population = RoundAuthorsPopulation.Of([Round(1, status: "interrupted"), Round(2)], Sel());

        CountedAt(population, 2).Cohort.Should().Be("first");
    }

    [Fact]
    public void AnExcludedFirstRound_DoesNotPromoteTheNext_AndAnUnobservedOneHoldsTheNextOutToo()
    {
        var population = RoundAuthorsPopulation.Of([Round(1, size: SizeFacts.NotRecorded), Round(2)], Sel());

        OutAt(population, 1).Should().Match<ExcludedRound>(round => round.Reason == ExclusionReason.SizeNotRecorded && round.Cohort == "first");
        CountedAt(population, 2).Cohort.Should().Be("corrective", "round 1's exclusion does not make round 2 the first");
        RoundAuthorsPopulation.Of([Round(1, size: SizeFacts.NotRecorded), Round(2)], Sel(cohort: "first")).In.Should().BeEmpty("the filter drops round 2 and counts nothing");

        var cascade = RoundAuthorsPopulation.Of([Round(1, authors: Stated(AuthorsStateKind.Unobserved, "transcript empty")), Round(2)], Sel());
        OutAt(cascade, 2).Reason.Should().Be(ExclusionReason.AuthorsUnobserved, "round 2's union holds round 1's window, and a union with a hole is not an observed union (D3)");
    }

    [Fact]
    public void AFirstRoundBeforeSince_LeavesTheNextOneCorrective()
    {
        var population = RoundAuthorsPopulation.Of([Round(1, inWindow: false), Round(2)], Sel());

        CountedAt(population, 2).Cohort.Should().Be("corrective");
        population.In.Concat<object>(population.Out).Should().HaveCount(1, "only in-window rounds are counted or excluded; the rest is context");
    }

    [Fact]
    public void TheCheapModelFixingRoundThree_IsNotSeenByRoundOne()
    {
        var opus = Observed(MainRow(), Coder("op", "claude-opus-5-5", "opus"));
        var population = RoundAuthorsPopulation.Of(
            [Round(1, authors: opus), Round(2, authors: Observed(MainRow())), Round(3, authors: Observed(MainRow(), Coder("so", "claude-sonnet-5-5")))], Sel());

        CountedAt(population, 1).Key.CoderMix.Should().Be("opus", "round 1 does not see a fixer that wrote after it started");
        CountedAt(population, 3).Key.CoderMix.Should().Be("mixed", "round 3's union holds the Opus author of round 1 and the Sonnet fixer");
    }

    [Fact]
    public void ThePlanRoundsWindowsCount_ASonnetCoderInThePlanWindowIsACoderOfCodeRoundOne()
    {
        var plan = Round(1, hours: 1, stage: "PlanReview", inWindow: false, authors: Observed(MainRow(), Coder("so", "claude-sonnet-5-5")));
        var code = Round(1, hours: 2, authors: Observed(MainRow()));

        CountedAt(RoundAuthorsPopulation.Of([plan, code], Sel()), 1).Key.CoderMix.Should().Be("sonnet");
    }

    [Fact]
    public void BeforeOpenCoders_AreLeftOutUnlessAsked()
    {
        var authors = Observed(MainRow(Association.WroteHere), Coder("so", "claude-sonnet-5-5", window: "before-open"));

        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: authors)], Sel()), 1).Key.CoderMix.Should().Be("main only", "the only coder is in before-open and main wrote here");
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: authors)], Sel(beforeOpen: true)), 1).Key.CoderMix.Should().Be("sonnet");
    }

    [Fact]
    public void Families_AreOneFunctionForTheObservedIdAndTheRequestedAlias()
    {
        ModelFamily.OfModel("claude-sonnet-5-5").Should().Be("sonnet");
        ModelFamily.OfModel(string.Empty).Should().Be("unknown");
        ModelFamily.OfModel("unknown").Should().Be("unknown");
        ModelFamily.OfModel("gpt-6-luna").Should().Be("other");
        ModelFamily.OfRequested("sonnet").Should().Be("sonnet", "an alias equal to a family word is that family");
        ModelFamily.OfRequested(string.Empty).Should().Be("unknown");
        ModelFamily.OfRequested("claude-opus-5-5").Should().Be("opus", "any other value goes through the id rule");
        ModelFamily.OfRequested("gpt-6-luna").Should().Be("other");
        ModelFamily.OfRequested("Sonnet").Should().Be("other", "an alias is matched exactly: the card's rule never folds case either");
    }

    [Fact]
    public void ARequestedFamilyThatWasAnsweredByAnother_ShowsAsAnOffDiagonalPair_AndNoCoderMeansNoWrite()
    {
        var authors = Observed(MainRow(), Coder("a", "claude-opus-5-5", requested: "sonnet"), Coder("b", "claude-opus-5-5", requested: string.Empty));
        var counted = CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: authors)], Sel()), 1);

        counted.Sums.Requested.Should().BeEquivalentTo([new NamedCount("sonnet\nopus", 1), new NamedCount("unknown\nopus", 1)]);
        counted.Sums.CoderLaunches.Should().Equal([new NamedCount("opus", 2)]);
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: Observed(MainRow()))], Sel()), 1).Key.CoderMix.Should().Be("no write observed");
    }

    [Fact]
    public void Unknown_IsNotAFamily_ItIsASuffixAndNeverMakesAMixMixed()
    {
        var twoRepositories = Coder("b2", "unknown", "unknown", "unknown", writes: 2);
        string Mix(params StoredAuthorRow[] rows) => CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: Observed([MainRow(), .. rows]))], Sel()), 1).Key.CoderMix;

        Mix(Coder("a1", "claude-sonnet-5-5"), twoRepositories).Should().Be("sonnet+unknown");
        Mix(Coder("a1", "claude-sonnet-5-5"), Coder("a2", "claude-opus-5-5", "opus"), Coder("a3", "unknown", "unknown", "unknown", launches: 1, writes: 0)).Should().Be("mixed+unknown");
        Mix(twoRepositories).Should().Be("unknown", "write evidence with no model claim is not 'no write observed'");
    }

    [Fact]
    public void ASkipBetweenRounds_IsNotAContributingRound_SoRoundThreeIsCountedNotExcludedByTheSkipsEmptyState()
    {
        var population = RoundAuthorsPopulation.Of([Round(1), Round(2, verdict: "skipped", authors: AuthorsFacts.NotRecorded), Round(3)], Sel());

        CountedAt(population, 3).Cohort.Should().Be("corrective");
        OutAt(population, 2).Should().Match<ExcludedRound>(round => round.Reason == ExclusionReason.Skipped && round.Cohort == ExclusionWords.NoCohort);
    }

    [Fact]
    public void APeerRunningRound_IsCountedAsPartialAndProvisional()
    {
        var provisional = Stated(AuthorsStateKind.Partial, "peer running", 0, MainRow(), Coder("a1", "claude-sonnet-5-5"));

        var counted = CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: provisional)], Sel()), 1);

        counted.Sums.AuthorsPartial.Should().BeTrue();
        counted.Sums.Provisional.Should().BeTrue("ownership was decided while a peer round still ran; E7's backfill finalises it");
        CountedAt(RoundAuthorsPopulation.Of([Round(1)], Sel()), 1).Sums.AuthorsPartial.Should().BeFalse();
    }

    [Fact]
    public void CoderEffort_KeepsUnknownVisible_AndNoCodersIsADash()
    {
        var authors = Observed(MainRow(), Coder("a1", "claude-sonnet-5-5", effort: "high"), Coder("a2", "claude-sonnet-5-5", effort: string.Empty));

        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: authors)], Sel()), 1).Key.CoderEffort.Should().Be("high+unknown");
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: Observed(MainRow()))], Sel()), 1).Key.CoderEffort.Should().Be("—");
    }

    [Fact]
    public void TheMainModel_IsTheDistinctObservedModelsOfMain_OrUnknown()
    {
        var authors = Observed(MainRow(model: "claude-opus-5-5"), MainRow(model: "claude-fable-5-0"));

        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: authors)], Sel()), 1).Key.MainModel.Should().Be("claude-fable-5-0+claude-opus-5-5");
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: Observed(Coder("a", "claude-sonnet-5-5")))], Sel()), 1).Key.MainModel.Should().Be("unknown");
    }

    [Fact]
    public void TheReviewerSet_IsTheDistinctAnsweringRows_FailedRowsCountBeside_AndNoAnswerExcludes()
    {
        ReviewerFact[] reviewers = [Codex, Codex with { Model = "gpt-6-luna" }, Codex, new ReviewerFact("gemini", "gemini-3", "", true, "failed"), new ReviewerFact("codex", "gpt-6-luna", "high", true, "stood down")];

        var counted = CountedAt(RoundAuthorsPopulation.Of([Round(1, reviewers: reviewers)], Sel()), 1);

        counted.Key.ReviewerSet.Should().Be("codex/gpt-6-luna@high", "three done rows of one model are one entry; failed and stood-down rows are not in the key");
        counted.Sums.FailedReviewers.Should().Be(1);
        counted.Sums.StoodDownReviewers.Should().Be(1);
        counted.Sums.PartialSet.Should().BeTrue();
        OutAt(RoundAuthorsPopulation.Of([Round(1, reviewers: [new ReviewerFact("codex", "m", "", true, "failed")])], Sel()), 1).Reason.Should().Be(ExclusionReason.NoReviewerAnswered);
    }

    [Fact]
    public void ReviewerEffort_NotRecordedAndNone_AreNeverMerged()
    {
        string Set(ReviewerFact reviewer) => CountedAt(RoundAuthorsPopulation.Of([Round(1, reviewers: [reviewer])], Sel()), 1).Key.ReviewerSet;

        Set(new ReviewerFact("codex", "m", "not recorded", true, "done")).Should().Be("codex/m@not recorded", "the column default of every row before step 22");
        Set(new ReviewerFact("codex", "m", string.Empty, false, "done")).Should().Be("codex/m@not recorded", "a file older than step 22 has no column");
        Set(new ReviewerFact("codex", "m", string.Empty, true, "done")).Should().Be("codex/m@none", "the launch applied none");
    }

    [Fact]
    public void Lines_AreWhatTheReviewersRead_ExcludedAndBinaryAreApart_AndEveryKindIsPrinted()
    {
        var size = Sized(Whole(Cell("shown", "code", 80), Cell("elided", "test", 20), Cell("excluded", "code", 500), Binary(2), Cell("shown", "docs", 40)));
        var counted = CountedAt(RoundAuthorsPopulation.Of([Round(1, size: size)], Sel()), 1);

        counted.Sums.SelectedLines.Should().Be(100);
        counted.Sums.ElidedLines.Should().Be(20);
        counted.Sums.ExcludedLines.Should().Be(500);
        counted.Sums.BinaryFiles.Should().Be(2);
        counted.Sums.NotCounted.Should().Equal([new NamedCount("docs", 40)]);
        OutAt(RoundAuthorsPopulation.Of([Round(1, size: Sized(Whole(Cell("shown", "docs", 40))))], Sel()), 1).Reason.Should().Be(ExclusionReason.NoSelectedLines);
        OutAt(RoundAuthorsPopulation.Of([Round(1, size: Sized(Whole(Binary(1))))], Sel()), 1).Reason.Should().Be(ExclusionReason.BinaryOnly);
        CountedAt(RoundAuthorsPopulation.Of([Round(1, size: Sized(Whole(Cell("shown", "docs", 40))))], Sel(kinds: ["docs"])), 1).Sums.SelectedLines.Should().Be(40);
        OutAt(RoundAuthorsPopulation.Of([Round(1, size: size)], Sel(withoutElided: true)), 1).Reason.Should().Be(ExclusionReason.Elided);
    }

    [Fact]
    public void TheUnknownKind_IsPrintedAsNotCounted_NeverDropped()
    {
        var size = Sized(Whole(Cell("shown", "code", 50), Cell("shown", "unknown", 12)));

        var counted = CountedAt(RoundAuthorsPopulation.Of([Round(1, size: size)], Sel()), 1);

        counted.Sums.SelectedLines.Should().Be(50);
        counted.Sums.NotCounted.Should().Equal([new NamedCount("unknown", 12)]);
    }

    [Fact]
    public void ACorrectiveDelta_StaysInTheWholeFigures_AndLeavesTheDeltaCoverageWhenUnavailable()
    {
        var whole = new[] { Cell("shown", "code", 100) };
        var unavailable = Sized(new RoundSize(new ScopeSize(ScopeState.Recorded, "b", whole), ScopeSize.Unavailable(ScopeState.BaseMoved)));
        var population = RoundAuthorsPopulation.Of(
            [Round(1), Round(2, size: Sized(WithDelta(whole, Cell("shown", "code", 30)))), Round(3, size: unavailable), Round(4, size: Sized(new RoundSize(new ScopeSize(ScopeState.Recorded, "b", whole), ScopeSize.ObservedZero("sha"))))],
            Sel());

        CountedAt(population, 2).Sums.Should().Match<RoundSums>(sums => sums.DeltaCovered && sums.DeltaLines == 30);
        CountedAt(population, 3).Sums.Should().Match<RoundSums>(sums => !sums.DeltaCovered && sums.DeltaLines == 0 && sums.DeltaUnavailable == "base moved" && sums.SelectedLines == 100);
        CountedAt(population, 4).Sums.Should().Match<RoundSums>(sums => sums.DeltaCovered && sums.DeltaLines == 0, "an observed zero is in coverage with no lines");
        CountedAt(population, 1).Sums.DeltaCovered.Should().BeFalse("a first round has no delta");
    }

    [Fact]
    public void ASkippedRound_HasNoCohort_AndTheNextRealReviewIsFirst()
    {
        var population = RoundAuthorsPopulation.Of([Round(1, verdict: "skipped"), Round(2)], Sel());

        OutAt(population, 1).Should().Match<ExcludedRound>(round => round.Reason == ExclusionReason.Skipped && round.Cohort == ExclusionWords.NoCohort);
        CountedAt(population, 2).Cohort.Should().Be("first");
    }

    [Fact]
    public void UnknownCoverage_IsNotCovered_AndNotUncovered_TheTwoNeverMerge()
    {
        var unknown = Stated(AuthorsStateKind.Partial, "read budget", -1, MainRow(), Coder("a", "claude-sonnet-5-5"));
        var uncovered = Stated(AuthorsStateKind.Partial, "read budget", 2, MainRow(), Coder("a", "claude-sonnet-5-5"));

        OutAt(RoundAuthorsPopulation.Of([Round(1, authors: unknown)], Sel()), 1).Reason.Should().Be(ExclusionReason.SizeCoverageUnknown);
        OutAt(RoundAuthorsPopulation.Of([Round(1, authors: uncovered)], Sel()), 1).Reason.Should().Be(ExclusionReason.SizeNotCovered);
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: unknown)], Sel(withUncovered: true)), 1).Sums.SizeCoverageUnknown.Should().BeTrue();
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: uncovered)], Sel(withUncovered: true)), 1).Sums.SizeNotCovered.Should().BeTrue();
    }

    /// <summary>
    /// The coverage of a round's union is the coverage of its CONTRIBUTING rounds (the D3 rule, applied to D12): round 2's whole change carries
    /// what round 1's window wrote, so a write round 1 made into a submodule is a write round 2's size does not cover either.
    /// </summary>
    [Fact]
    public void AContributingRoundsUncoveredWrites_LeaveTheLaterRoundNotCoveredToo()
    {
        var uncovered = Stated(AuthorsStateKind.Partial, "read budget", 2, MainRow(), Coder("a", "claude-sonnet-5-5"));
        var unknown = Stated(AuthorsStateKind.Partial, "read budget", -1, MainRow(), Coder("a", "claude-sonnet-5-5"));

        OutAt(RoundAuthorsPopulation.Of([Round(1, authors: uncovered), Round(2)], Sel()), 2).Reason.Should().Be(ExclusionReason.SizeNotCovered,
            "round 2's contributors include round 1, which wrote twice outside the checkout the size covers");
        OutAt(RoundAuthorsPopulation.Of([Round(1, authors: unknown), Round(2)], Sel()), 2).Reason.Should().Be(ExclusionReason.SizeCoverageUnknown,
            "round 1's coverage was never measured, so round 2's union is not known to be covered");
        OutAt(RoundAuthorsPopulation.Of([Round(1, authors: uncovered), Round(2, authors: unknown)], Sel()), 2).Reason.Should().Be(ExclusionReason.SizeNotCovered,
            "a known write elsewhere outranks an unknown count, as it does for one round");
        CountedAt(RoundAuthorsPopulation.Of([Round(1, authors: uncovered), Round(2)], Sel(withUncovered: true)), 2).Sums.SizeNotCovered.Should().BeTrue(
            "under --with-uncovered the round is counted, and the cell still says its size is not covered");
        CountedAt(RoundAuthorsPopulation.Of([Round(1), Round(2, authors: uncovered)], Sel()), 1).Sums.SizeNotCovered.Should().BeFalse(
            "a round that started later is no contributor of round 1");
    }

    [Fact]
    public void ARoundThatFailsEveryCheck_IsExcludedOnce_UnderTheFirst()
    {
        var everything = Round(1, status: "running", verdict: "skipped", authors: AuthorsFacts.NotRecorded, size: SizeFacts.NotRecorded, reviewers: []);

        var population = RoundAuthorsPopulation.Of([everything], Sel());

        population.Out.Should().ContainSingle().Which.Reason.Should().Be(ExclusionReason.NotDone);
        population.In.Should().BeEmpty();
    }

    [Fact]
    public void EveryReasonOfTheEnum_ExcludesARoundThatFailsExactlyThatCheck()
    {
        foreach (var reason in Enum.GetValues<ExclusionReason>())
        {
            var selection = Sel(withoutElided: reason == ExclusionReason.Elided);

            var population = RoundAuthorsPopulation.Of([FailingOnly(reason)], selection);

            population.Out.Should().ContainSingle($"a round failing only '{reason}' is out").Which.Reason.Should().Be(reason);
        }
    }

    [Fact]
    public void ASizeUnavailableRound_NamesTheScopesOwnWord()
    {
        var population = RoundAuthorsPopulation.Of([FailingOnly(ExclusionReason.SizeUnavailable)], Sel());

        population.Out.Single().Detail.Should().Be("no merge base");
    }

    private static RoundFacts FailingOnly(ExclusionReason reason) => reason switch
    {
        ExclusionReason.NotDone => Round(1, status: "running"),
        ExclusionReason.Skipped => Round(1, verdict: "skipped"),
        ExclusionReason.NoReviewerAnswered => Round(1, reviewers: []),
        ExclusionReason.AuthorsNotRecorded => Round(1, authors: AuthorsFacts.NotRecorded),
        ExclusionReason.AuthorsUnobserved => Round(1, authors: Stated(AuthorsStateKind.Unobserved, "transcript empty")),
        ExclusionReason.SizeNotRecorded => Round(1, size: SizeFacts.NotRecorded),
        ExclusionReason.SizeUnavailable => Round(1, size: Sized(new RoundSize(ScopeSize.Unavailable(ScopeState.NoMergeBase), ScopeSize.Unavailable(ScopeState.FirstRound)))),
        ExclusionReason.BinaryOnly => Round(1, size: Sized(Whole(Binary(1)))),
        ExclusionReason.SizeNotCovered => Round(1, authors: Stated(AuthorsStateKind.Partial, "read budget", 3, MainRow())),
        ExclusionReason.SizeCoverageUnknown => Round(1, authors: Stated(AuthorsStateKind.Partial, "read budget", -1, MainRow())),
        ExclusionReason.NoSelectedLines => Round(1, size: Sized(Whole(Cell("shown", "docs", 5)))),
        ExclusionReason.Elided => Round(1, size: Sized(Whole(Cell("elided", "code", 5)))),
        _ => throw new InvalidOperationException($"a reason was added to the enum without a round that fails exactly it: {reason}"),
    };

    [Fact]
    public void ARoundAtTheSplitInstant_IsAfter()
    {
        var split = Stamp(T0.AddHours(2));

        var population = RoundAuthorsPopulation.Of([Round(1), Round(2), Round(3)], Sel(split: split));

        population.In.Select(round => (round.Facts.Number, round.Period)).Should().Equal([(1, "before"), (2, "after"), (3, "after")]);
    }

    [Fact]
    public void ThePlanStage_CountsARoundWithNoSize_AndAppliesNoSizeCheck()
    {
        var plan = Round(1, stage: "PlanReview", size: SizeFacts.NotRecorded);

        var population = RoundAuthorsPopulation.Of([plan], Sel("PlanReview"));

        CountedAt(population, 1).Sums.SelectedLines.Should().Be(0);
        population.Out.Should().BeEmpty();
    }

    [Fact]
    public void EveryInWindowRound_IsCountedOrExcluded_ExactlyOnce()
    {
        RoundFacts[] rounds = [Round(1), Round(2, status: "running"), Round(3, reviewers: []), Round(4, verdict: "skipped"), Round(5, inWindow: false), Round(6, stage: "PlanReview")];

        var population = RoundAuthorsPopulation.Of(rounds, Sel());

        (population.In.Count + population.Out.Count).Should().Be(rounds.Count(round => round.InWindow && round.Stage == "CodeReview"));
        population.In.Select(round => round.Facts.RoundId).Concat(population.Out.Select(round => round.RoundId)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheFamilyFunction_AnswersEveryVectorOfTheSharedFile_AndKnowsExactlyTheFamiliesItNames()
    {
        using var file = File.OpenRead(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "model-display-vectors.json")));
        using var parsed = JsonDocument.Parse(file);
        var rows = parsed.RootElement.GetProperty("models").EnumerateArray().Select(row => (Model: row.GetProperty("model").GetString()!, Family: row.GetProperty("family").GetString()!)).ToList();

        rows.Should().NotBeEmpty();
        foreach (var (model, family) in rows)
        {
            ModelFamily.OfModel(model).Should().Be(family, $"the vector for '{model}'");
        }

        ModelFamily.KnownWords.Should().BeEquivalentTo(rows.Select(row => row.Family).Where(family => family is not ("other" or "unknown")).Distinct(), "the alias words are the families the vectors name — a new family there is a new word here");
    }
}
