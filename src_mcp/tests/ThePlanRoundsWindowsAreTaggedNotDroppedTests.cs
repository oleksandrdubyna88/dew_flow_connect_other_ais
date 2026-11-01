using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>A plan round's window is tagged, not dropped: whether a plan author is a code author is E6's cohort to choose.</summary>
public sealed class ThePlanRoundsWindowsAreTaggedNotDroppedTests
{
    [Fact]
    public void APlanStageAuthorIsPresentWithItsStage()
    {
        var journal = new[] { Observed("PlanReview", 1, "c0", Row("planner", stage: "PlanReview")), Observed("CodeReview", 1, "c0", Row("coder")) };

        var planner = Contributors.Of(journal, withBeforeOpen: false).Items.Single(item => item.Agent.Id == "planner");

        planner.ActiveIn.Should().Equal(new RoundRef("PlanReview", 1, WindowKind.Round));
    }
}
