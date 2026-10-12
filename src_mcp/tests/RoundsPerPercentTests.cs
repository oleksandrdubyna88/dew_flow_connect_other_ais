using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Rounds per 1 % of a vendor's window (story 6.2 of todo/PLAN_every_round_is_counted.md) — the formula E6's plan round
/// fixed: per window (limit identity + duration), between two consecutive readings of the SAME window instance (same
/// <c>resetsAt</c>), the rounds counted between them over the rise; an interval is unusable — never 0 or infinity — when
/// the percentage did not rise, the window reset, or a reading is stale or missing; the figure is the rounds-weighted mean
/// of the last usable intervals, and a known consumer that is not counted hides it.
/// </summary>
public sealed class RoundsPerPercentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    private const string Resets = "2026-10-15T07:00:00.0000000Z";
    private const string NextWeek = "2026-10-22T07:00:00.0000000Z";

    private static GaugeOnLine Reading(DateTimeOffset observed, double? used, string resets, string limit, long? mins) =>
        new(observed.UtcDateTime.ToString("O"), "plus", limit, mins, used, resets, null, null, string.Empty);

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
    public void TheFigure_IsTheRoundsWeightedMeanOfTheUsableIntervals()
    {
        var ratio = Only(RoundsPerPercent.Derive([
            Job(0, "r0", 10),
            // 3 rounds over 1 % — ratio 3.
            Job(10, "r1", 11), Job(10, "r2", 11), Job(10, "r3", 11),
            // 1 round over 2 % — ratio 0.5.
            Job(20, "r4", 13),
        ]));

        // Weighted by rounds: (3 × 3 + 1 × 0.5) / 4. A pooled 4 / 3 would be a different formula.
        ratio.RoundsPerPercent.Should().BeApproximately(2.375, 1e-9);
        ratio.Intervals.Should().Be(2);
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
            var gauge = new GaugeOnLine(observed.UtcDateTime.ToString("O"), "plus", "acme", 300, fiveHour, "2026-10-12T12:00:00.0000000Z", 10080, weekly, Resets);

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
