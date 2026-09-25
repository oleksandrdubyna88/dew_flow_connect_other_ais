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
