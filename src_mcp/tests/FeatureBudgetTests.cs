using CoaiMcp.Core.Feature;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every feature-review budget still covers the measurement it was calibrated from (plan §6, S0.2).
/// </summary>
/// <remarks>
/// The numbers below are the S0.2 rows, restated. A budget lowered under its own measurement would
/// cut what the measurement says a typical feature needs — so lowering one means re-measuring, and
/// this test is where that shows.
/// </remarks>
public sealed class FeatureBudgetTests
{
    [Fact]
    public void ThePlanBudget_HoldsTheLargestMeasuredPlan() =>
        FeatureBudget.PlanBytes.Should().BeGreaterThanOrEqualTo((int)(52.7 * 1024), "the consultant's plan measured 52.7 KB");

    [Fact]
    public void TheOutlineBudget_HoldsTheMedianMeasuredFeatureWhole() =>
        FeatureBudget.OutlineBytes.Should().BeGreaterThanOrEqualTo((int)(166.1 * 1024), "the consultant's outline measured 166.1 KB");

    [Fact]
    public void TheOmissionsReserve_HoldsTheLongestMeasuredList() =>
        FeatureBudget.OmissionsReserveBytes.Should().BeGreaterThanOrEqualTo((int)(4.2 * 1024), "the notices' not-outlined list measured 4.2 KB");

    /// <summary>
    /// The feature-pack trial (2026-09-26) measured one pack's omissions — the not-outlined list plus the
    /// member hunks cut for the budget, which the hybrid added — over 8 KB (its cs1 feature), so the
    /// reserve that section may never be cut from had to grow.
    /// </summary>
    [Fact]
    public void TheOmissionsReserve_HoldsTheTrialsLongestHybridOmissions() =>
        FeatureBudget.OmissionsReserveBytes.Should().BeGreaterThan(8 * 1024, "the trial's cs1 omissions exceeded 8 KB once cut member hunks were named");

    [Fact]
    public void TheRenderedInputBudgets_AreThePlansFigures()
    {
        FeatureBudget.EpicsBytes.Should().Be(16 * 1024, "plan §4.6");
        FeatureBudget.LessonsBytes.Should().Be(16 * 1024, "plan §4.6");
        FeatureBudget.HistoryBytes.Should().Be(24 * 1024, "plan §4.6");
    }

    /// <summary>The hunk reserve is carved OUT of the outline budget: a third of it, the total unchanged.</summary>
    [Fact]
    public void TheHunkReserve_IsAThirdOfTheOutlineBudget_AndScalesWithASmallerOne()
    {
        FeatureBudget.HunkReserveBytes.Should().Be(56 * 1024, "the coordinator's S2.2b decision");
        FeatureBudget.OutlineBytes.Should().Be(168 * 1024, "carved out of the outline budget, which stays the same");
        FeatureBudget.HunkReserveFor(FeatureBudget.OutlineBytes).Should().Be(FeatureBudget.HunkReserveBytes);
        FeatureBudget.HunkReserveFor(FeatureBudget.OutlineBytes / 2).Should().Be(FeatureBudget.HunkReserveBytes / 2, "the same third of a smaller section");
        FeatureBudget.HunkReserveFor(4 * FeatureBudget.OutlineBytes).Should().Be(FeatureBudget.HunkReserveBytes, "never more than the reserve");
        FeatureBudget.HunkReserveFor(-1).Should().Be(0);
    }

    [Fact]
    public void TheFileCap_HoldsTheWidestMeasuredRange() =>
        FeatureBudget.MaxOutlinedFiles.Should().BeGreaterThanOrEqualTo(258, "the notices' range had 258 files at head");

    [Fact]
    public void CollapsingStartsAboveTheMeasuredNinetiethPercentile()
    {
        FeatureBudget.CollapseAboveBytes.Should().BeGreaterThanOrEqualTo(3666, "p90 of per-file outline size measured 3.1–3.7 KB");
        FeatureBudget.CollapseAboveBytes.Should().BeLessThan(11417, "and the largest files (11.4–12.7 KB) must be collapsible");
    }
}
