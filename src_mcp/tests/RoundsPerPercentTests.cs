using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Rounds per 1 % of a vendor's window (story 6.2 of research/PLAN_every_round_is_counted.md) — the formula E6's plan round
/// fixed: per window (limit identity + duration), between a baseline reading and the first later reading of the SAME window instance (same
/// <c>resetsAt</c>) whose percentage rose, the rounds counted between them over the rise; an interval is unusable — never 0 or infinity — when
/// the window reset, or a reading is stale or missing; a plateau (no rise) keeps its rounds until the percentage rises (story
/// 6.2b); the figure is pooled (sum of rounds over sum of rise) over the last usable intervals, and a known consumer that is not counted hides it.
/// </summary>
public sealed class RoundsPerPercentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    private const string Resets = "2026-10-15T07:00:00.0000000Z";
    private const string NextWeek = "2026-10-22T07:00:00.0000000Z";

    private static GaugeOnLine Reading(DateTimeOffset observed, double? used, string resets, string limit, long? mins) =>
        new(observed.UtcDateTime.ToString("O"), "plus", [new GaugeWindowOnLine(limit, "primary", mins, used, resets)]);

    /// <summary>A review launch on an account, its line written a second after the reading taken right after it.</summary>
    private static GaugeLine Job(
        int minute, string round, double? used, string resets = Resets, string limit = "acme", string account = "a",
        bool review = true, long? mins = 10080, string vendor = "acme")
    {
        var observed = T0.AddMinutes(minute);

        return new GaugeLine(observed.AddSeconds(1), vendor, account, round, review, Reading(observed, used, resets, limit, mins));
    }

    /// <summary>A review launch after which nothing was read.</summary>
    private static GaugeLine Bare(int minute, string round, string account = "a") =>
        new(T0.AddMinutes(minute).AddSeconds(1), "acme", account, round, true, GaugeOnLine.None);

    private static GaugeRatio Only(GaugeAnswer answer) => answer.Ratios.Should().ContainSingle().Subject;

    [Fact]
    public void TwoReadingsOfOneWindow_GiveTheRoundsBetweenThemOverTheRise()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            // Three rounds whose lines carry the second reading: everything spent since the first one.
            Job(10, "r1", 11), Job(10, "r2", 11), Job(10, "r3", 11),
        ]));

        ratio.Should().Be(new GaugeRatio("acme", "acme", 10080, 3.0, 1, string.Empty, string.Empty));
    }

    [Fact]
    public void AResetBetweenTheReadings_MakesTheIntervalUnusable()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 90),
            // The window reset in between: 2 % of a NEW week is not 88 % less of the old one.
            Job(10, "r1", 2, resets: NextWeek),
        ]));

        ratio.RoundsPerPercent.Should().BeNull("two instances of a window are not one interval");
        ratio.Unavailable.Should().Be(GaugeReasons.WindowReset);
    }

    [Fact]
    public void ALaunchWithNoReadingBetweenThem_MakesTheIntervalUnusable()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            Bare(5, "r1"),
            Job(10, "r2", 12),
        ]));

        ratio.RoundsPerPercent.Should().BeNull("the reading after r1 is missing, so the two readings are not consecutive");
        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading);
    }

    [Fact]
    public void AReadingThatDoesNotReportTheWindow_IsAMissingReading()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            // A reading arrived, but without this window's percentage.
            Job(5, "r1", null),
            Job(10, "r2", 12),
        ]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading);
    }

    [Fact]
    public void AnUnchangedReading_IsUnavailable_NeverZeroOrInfinity()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, "r1", 10)]));

        ratio.RoundsPerPercent.Should().BeNull("one round over a rise of 0 % is infinity, and infinity is not a measurement");
        ratio.Unavailable.Should().Be(GaugeReasons.PercentDidNotRise);
        ratio.Intervals.Should().Be(0);
    }

    [Fact]
    public void AFallingReading_IsUnavailable_NeverNegative()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 12), Job(10, "r1", 11)]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.PercentDidNotRise);
    }

    [Fact]
    public void AStaleReading_MakesTheIntervalUnusable()
    {
        var stale = Job(10, "r1", 12) with { AtUtc = T0.AddMinutes(30) };

        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), stale]));

        ratio.RoundsPerPercent.Should().BeNull("a reading twenty minutes older than its line is not the state after that launch");
        ratio.Unavailable.Should().Be(GaugeReasons.StaleReading);
    }

    [Fact]
    public void AReviewWithoutARound_MakesTheIntervalUnusable()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, string.Empty, 12)]));

        ratio.RoundsPerPercent.Should().BeNull("a launch from a client that sent no round cannot be counted as a round or left out");
        ratio.Unavailable.Should().Be(GaugeReasons.RunWithoutRound);
    }

    [Fact]
    public void OneReading_IsTooFewReadings()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10)]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.TooFewReadings);
    }

    [Fact]
    public void ReadingsOfTwoAccounts_AreNeverPaired()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10, account: "a"), Job(10, "r1", 30, account: "b")]));

        ratio.Unavailable.Should().Be(GaugeReasons.TooFewReadings, "account b's 30 % says nothing about account a's 10 %");
    }

    [Fact]
    public void TheFigure_IsPooled_RoundsOverRise_NotAMeanOfRatios()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            // 3 rounds over 1 % — ratio 3.
            Job(10, "r1", 11), Job(10, "r2", 11), Job(10, "r3", 11),
            // 1 round over 2 % — ratio 0.5.
            Job(20, "r4", 13),
        ]));

        // Owner decision 2026-10-16 (story 6.2b): pooled, Σ rounds ÷ Σ rise = 4 / 3. This test used to expect the
        // rounds-weighted mean of per-interval ratios, (3 × 3 + 1 × 0.5) / 4 = 2.375 — that expectation is now wrong.
        ratio.RoundsPerPercent.Should().BeApproximately(4.0 / 3.0, 1e-9);
        ratio.Intervals.Should().Be(2);
    }

    [Fact]
    public void APlateau_KeepsItsRounds_TheIntervalClosesWhenThePercentageRises()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "r1", 10), Job(20, "r2", 10), Job(30, "r3", 11),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(3.0, 1e-9, "three rounds were spent between the 10 % and the 11 %, whatever the readings in between said");
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void TwoPlateausInARow_AreTwoIntervals_AndNoRoundIsCountedTwice()
    {
        // 10,10,11 then 11,11,12: the 11 closing the first interval opens the second.
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "r1", 10), Job(20, "r2", 11), Job(30, "r3", 11), Job(40, "r4", 11), Job(50, "r5", 12),
        ]));

        ratio.Intervals.Should().Be(2, "intervals of 2 rounds and 3 rounds");
        ratio.RoundsPerPercent.Should().BeApproximately(2.5, 1e-9, "(2 + 3) rounds over (1 + 1) %");
    }

    [Fact]
    public void AStaleReadingInsideAPlateau_VoidsTheSpan_TheNextGoodReadingIsTheNewBaseline()
    {
        var stale = Job(10, "r1", 10) with { AtUtc = T0.AddMinutes(40) };

        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), stale, Job(20, "r2", 10), Job(30, "r3", 11),
        ]));

        // The span 10 → 11 would be 3 rounds; the stale reading voids it, and the new baseline is the reading at minute 20.
        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "only the interval after the new baseline counts: one round over 1 %");
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void AStaleReadingInsideAPlateau_WithNothingGoodAfterIt_IsUnavailableAsStale()
    {
        var stale = Job(10, "r1", 10) with { AtUtc = T0.AddMinutes(40) };

        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), stale, Job(20, "r2", 11)]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.StaleReading);
    }

    [Fact]
    public void AMissingReadingInsideAPlateau_VoidsTheSpan_TheNextGoodReadingIsTheNewBaseline()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "r1", null), Job(20, "r2", 10), Job(30, "r3", 11),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "the reading that did not report the window voids the span before it");
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void ALaunchWithNoReadingInsideAPlateau_VoidsTheSpan()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Bare(5, "r1"), Job(10, "r2", 10), Job(20, "r3", 10), Job(30, "r4", 11),
        ]));

        // The reading at minute 10 follows a reading-less launch, so the span 0 → 10 is void; the new baseline is that
        // reading, and the interval 10 → 30 holds two rounds over 1 %.
        ratio.RoundsPerPercent.Should().BeApproximately(2.0, 1e-9);
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void AResetInsideAPlateau_VoidsTheSpan_TheResettingReadingOpensTheNextOne()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 90), Job(10, "r1", 90), Job(20, "r2", 2, resets: NextWeek), Job(30, "r3", 2, resets: NextWeek), Job(40, "r4", 3, resets: NextWeek),
        ]));

        // The old week's span is void. The new week starts at the reading of minute 20: two rounds over 1 %.
        ratio.RoundsPerPercent.Should().BeApproximately(2.0, 1e-9);
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void AResetInsideAPlateau_WithNoRiseAfterIt_IsUnavailableAsAReset()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 90), Job(10, "r1", 90), Job(20, "r2", 2, resets: NextWeek)]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.WindowReset);
    }

    [Fact]
    public void AReviewWithoutARoundOnTheClosingReading_VoidsTheSpan()
    {
        // The closing reading of the span carries a launch with no round id: its rounds cannot be counted or left out.
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "r1", 10), Job(20, string.Empty, 11),
        ]));

        ratio.RoundsPerPercent.Should().BeNull("a launch with no round id inside the span means the span's rounds cannot be counted");
        ratio.Unavailable.Should().Be(GaugeReasons.RunWithoutRound);
    }

    [Fact]
    public void AReviewWithoutARoundInsideAPlateau_TheReadingAfterIt_OpensTheNextSpan()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, string.Empty, 10), Job(20, "r2", 10), Job(30, "r3", 11),
        ]));

        // The round-less launch's reading is itself a true state of the account, so it opens the next span:
        // the rounds r2 and r3 over the rise to 11 %. The void span's round is not in it.
        ratio.RoundsPerPercent.Should().BeApproximately(2.0, 1e-9);
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void ARoundSplitAcrossLaunchesInsideASpan_AddsUpToTheWholeRound()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            // Round R ran two launches, one on the plateau reading and one on the closing reading: half a round each.
            Job(0, "r0", 10), Job(10, "R", 10), Job(20, "R", 11),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "the two halves are one round over 1 %");
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AReadingOutsideZeroToOneHundred_IsAMissingReading_ForTheDerivation(double bad)
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, "r1", bad), Job(20, "r2", 11)]));

        ratio.RoundsPerPercent.Should().BeNull($"{bad} % is not a percentage; it cannot close an interval");
        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading);
    }

    [Fact]
    public void OnlyTheLastUsableIntervals_AreInTheFigure()
    {
        // Eleven usable intervals: the first one (10 rounds over 1 %) is the eleventh from the end and must not count.
        List<GaugeLine> lines = [Job(0, "r0", 10), .. Enumerable.Range(0, 10).Select(i => Job(1, $"x{i}", 11))];
        lines.AddRange(Enumerable.Range(1, RoundsPerPercent.LastIntervals).Select(i => Job(1 + i, $"y{i}", 11 + i)));

        var ratio = Only(RoundsPerPercent.Derive(lines));

        ratio.Intervals.Should().Be(RoundsPerPercent.LastIntervals);
        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "each of the last ten intervals is one round over 1 %");
    }

    [Fact]
    public void ARoundSpreadOverTwoIntervals_IsSharedBetweenThem()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            // Round R ran two launches on the account, each followed by its own reading: half a round each.
            Job(10, "R", 11),
            Job(20, "R", 12),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(0.5, 1e-9, "one round over 2 % is half a round per 1 %, not two");
    }

    [Fact]
    public void ANonReviewLaunch_SpendsButCountsNoRound()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, "r1", 12), Job(10, string.Empty, 12, review: false)]));

        ratio.RoundsPerPercent.Should().BeApproximately(0.5, 1e-9, "a consultation's spend is in the rise, and it is not a round");
    }

    // Review round 2026-10-18 of story 6.2b — the cases the first round did not name.

    [Fact]
    public void AFallWithoutAReset_VoidsTheSpan_TheLowerReadingIsTheNewBaseline()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 50), Job(10, "r1", 20), Job(20, "r2", 30), Job(30, "r3", 51),
        ]));

        // 50 → 20 is a fall inside one window instance, not a rise of 1 %: the span is void and 20 is the new baseline.
        // Intervals 20 → 30 (1 round over 10 %) and 30 → 51 (1 round over 21 %): 2 rounds over 31 %.
        ratio.RoundsPerPercent.Should().BeApproximately(2.0 / 31.0, 1e-9);
        ratio.Intervals.Should().Be(2);
    }

    [Fact]
    public void AFractionalFall_RebasesAtTheLowerReading()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 12.0), Job(10, "r1", 11.8), Job(20, "r2", 12.05)]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0 / 0.25, 1e-9, "one round over the 0.25 % from the new baseline 11.8");
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void TwoReadingsWithNoResetsAt_ReportMissingReading_NotTooFewReadings()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10, resets: string.Empty), Job(10, "r1", 11, resets: string.Empty)]));

        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading, "no reading names when the window resets");
    }

    [Fact]
    public void ThreeStaleReadings_ReportStaleReading_NotTooFewReadings()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10) with { AtUtc = T0.AddMinutes(40) },
            Job(10, "r1", 11) with { AtUtc = T0.AddMinutes(50) },
            Job(20, "r2", 12) with { AtUtc = T0.AddMinutes(60) },
        ]));

        ratio.Unavailable.Should().Be(GaugeReasons.StaleReading, "every reading is older than its line");
    }

    [Fact]
    public void TwoReadingsOutsideZeroToOneHundred_ReportMissingReading_NotTooFewReadings()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 150), Job(10, "r1", 160)]));

        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading, "150 % and 160 % are no readings");
    }

    [Fact]
    public void AnInfiniteReadingThenAGoodOne_ReportsMissingReading_NotTooFewReadings()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", double.PositiveInfinity), Job(10, "r1", 11)]));

        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading, "the infinite reading is no reading, and the good one alone is no interval");
    }

    [Fact]
    public void OneReadingThatCannotOpenASpan_IsStillTooFewReadings()
    {
        Only(RoundsPerPercent.Derive([Job(0, "r0", 10) with { AtUtc = T0.AddMinutes(40) }]))
            .Unavailable.Should().Be(GaugeReasons.TooFewReadings);
    }

    [Fact]
    public void TwoAccountsInterleavedInTime_ArePooledTogether_AndTheLastTenAreTakenAcrossBoth()
    {
        // Six intervals per account, ending alternately a1, b1, a2, b2 … The first two to end hold 5 rounds over 1 %;
        // every other holds 1 round over 1 %. Twelve usable intervals: the last ten are all 1 over 1.
        List<GaugeLine> lines = [Job(0, "a0", 10, account: "a"), Job(1, "b0", 10, account: "b")];
        foreach (var i in Enumerable.Range(1, 6))
        {
            var rounds = i == 1 ? 5 : 1;
            lines.AddRange(Enumerable.Range(0, rounds).Select(n => Job(2 * i, $"a{i}-{n}", 10 + i, account: "a")));
            lines.AddRange(Enumerable.Range(0, rounds).Select(n => Job((2 * i) + 1, $"b{i}-{n}", 10 + i, account: "b")));
        }

        var ratio = Only(RoundsPerPercent.Derive(lines));

        ratio.Intervals.Should().Be(RoundsPerPercent.LastIntervals);
        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "the two older, heavier intervals fell out of the last ten");
    }

    [Fact]
    public void TwoAccountsEachWithAnInterval_ArePooled()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "a0", 10, account: "a"), Job(5, "b0", 20, account: "b"),
            Job(10, "a1", 11, account: "a"), Job(15, "b1", 22, account: "b"),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(2.0 / 3.0, 1e-9, "(1 + 1) rounds over (1 + 2) %");
        ratio.Intervals.Should().Be(2);
    }

    [Fact]
    public void ABareLaunchAfterTheLastReading_IsAMissingReading_NotAPlateauWaitingForARise()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, "r1", 10), Bare(20, "r2")]));

        ratio.RoundsPerPercent.Should().BeNull();
        ratio.Unavailable.Should().Be(GaugeReasons.MissingReading, "a launch after the last reading spent something nobody read");
    }

    [Fact]
    public void ABareLaunchAfterTheLastReading_DoesNotChangeAFigureThatHasUsableIntervals()
    {
        var ratio = Only(RoundsPerPercent.Derive([Job(0, "r0", 10), Job(10, "r1", 11), Job(20, "r2", 11), Bare(30, "r3")]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "the usable interval 10 → 11 stands; the trailing launch only ends an unfinished span");
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void ABareLaunchOnAnotherAccount_DoesNotVoidThisAccountsSpan()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "a0", 10, account: "a"), Bare(5, "rb", account: "b"), Job(10, "a1", 11, account: "a"),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9);
        ratio.Intervals.Should().Be(1);
    }

    [Fact]
    public void ABarrierRightAfterARise_KeepsTheRiseAndOpensTheNextSpanAtTheResetReading()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "r1", 11), Job(20, "r2", 1, resets: NextWeek), Job(30, "r3", 2, resets: NextWeek),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "r1 over 10 → 11 and r3 over 1 → 2");
        ratio.Intervals.Should().Be(2);
    }

    [Fact]
    public void ARoundOfThreeLaunchesInOneSpan_AddsUpToOneWholeRound_ThirdsIncluded()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10), Job(10, "R", 10), Job(20, "R", 10), Job(30, "R", 11),
        ]));

        ratio.RoundsPerPercent.Should().BeApproximately(1.0, 1e-9, "three thirds are one round over 1 %");
    }

    [Fact]
    public void EveryLimitOfACodexVendor_IsHidden_NotOnlyTheLimitCalledCodex()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10, limit: "codex_other", vendor: "codex"),
            Job(10, "r1", 11, limit: "codex_other", vendor: "codex"),
        ]));

        ratio.RoundsPerPercent.Should().BeNull("direct codex use spends every limit of the account, whatever its id");
        ratio.Unavailable.Should().Be(GaugeReasons.ConsumerNotCounted);
        ratio.MissingConsumer.Should().Be(GaugeConsumers.DirectCodexUse);
        ratio.LimitId.Should().Be("codex_other");
    }

    [Fact]
    public void AVendorRowNotCalledCodex_ThatReportsTheCodexLimit_IsStillHidden()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10, limit: "codex", vendor: "codex-work"),
            Job(10, "r1", 11, limit: "codex", vendor: "codex-work"),
        ]));

        ratio.MissingConsumer.Should().Be(GaugeConsumers.DirectCodexUse, "the codex limit id is codex's own, whatever the row is called");
    }

    [Fact]
    public void AMissingConsumer_HidesTheFigure_AndSaysWhichConsumer()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10, limit: "codex", vendor: "codex"),
            Job(10, "r1", 11, limit: "codex", vendor: "codex"),
        ]));

        ratio.RoundsPerPercent.Should().BeNull("direct codex use spends the same limit and writes no line coai can count");
        ratio.Unavailable.Should().Be(GaugeReasons.ConsumerNotCounted);
        ratio.MissingConsumer.Should().Be(GaugeConsumers.DirectCodexUse);
        ratio.LimitId.Should().Be("codex", "the window is still named, so the gauge beside it is still shown");
    }

    [Fact]
    public void WindowsAreIdentifiedByLimitAndDuration_NotByWhereTheVendorFiledThem()
    {
        // The same limit's five-hour and weekly windows, reported on one reading, are two figures.
        static GaugeLine Both(int minute, string round, double fiveHour, double weekly)
        {
            var observed = T0.AddMinutes(minute);
            var gauge = new GaugeOnLine(observed.UtcDateTime.ToString("O"), "plus",
            [
                new GaugeWindowOnLine("acme", "primary", 300, fiveHour, "2026-10-12T12:00:00.0000000Z"),
                new GaugeWindowOnLine("acme", "secondary", 10080, weekly, Resets),
            ]);

            return new GaugeLine(observed.AddSeconds(1), "acme", "a", round, true, gauge);
        }

        var answer = RoundsPerPercent.Derive([Both(0, "r0", 10, 40), Both(10, "r1", 11, 42)]);

        answer.Ratios.Select(r => (r.WindowMins, r.RoundsPerPercent)).Should().BeEquivalentTo([((long)300, (double?)1.0), ((long)10080, (double?)0.5)]);
    }

    [Fact]
    public void NoReadingAtAll_IsNoFigure_NotAZero()
    {
        var answer = RoundsPerPercent.Derive([Bare(0, "r0"), Bare(10, "r1")]);

        answer.Ratios.Should().BeEmpty("a window nobody read is absent, never 0 rounds per 1 %");
    }

    /// <summary>Epic 6's code round: a public list defaults to empty — an answer read without its list is no ratios, never null.</summary>
    [Fact]
    public void AnAnswerWithoutItsRatios_ReadsEmpty_NeverNull()
    {
        var constructed = new GaugeAnswer(null!, GaugeReasons.LocalCaptureDeferred);
        var read = System.Text.Json.JsonSerializer.Deserialize(
            """{"nowUtc":"2026-10-12T12:00:00Z","windows":[],"modelsKnownSince":null,"gauge":{"unavailable":"local-capture-deferred"}}""",
            CoaiMcp.Server.ServerJsonContext.Default.RoundCountsAnswer)!;

        constructed.Ratios.Should().BeEmpty("a null handed to the constructor is no ratios");
        read.Gauge.Ratios.Should().BeEmpty("an answer whose gauge carries no list is read as no ratios, never null");
    }

    [Fact]
    public void ThisMachine_SaysLocalCaptureIsDeferred()
    {
        RoundsPerPercent.NotReadHere.Should().Be(new GaugeAnswer([], GaugeReasons.LocalCaptureDeferred));
    }
}
