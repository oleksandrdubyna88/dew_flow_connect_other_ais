using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Case 3: a model switch inside one window. Attribution keeps each placement's own model (the one that ANSWERED) and its request
/// beside it; grouping them into rows is S3's job.
/// </summary>
public sealed class AModelSwitchKeepsEachMessagesModelTests
{
    [Fact]
    public void ThreePlacementsCarryTheirOwnModels()
    {
        var assignment = AttributionCases.Case3().Assign("A", "CodeReview", 1);

        assignment.Messages.Should().HaveCount(3).And.OnlyContain(p => p.Association == Association.WroteHere && p.Agent.IsMain);
        assignment.Messages.Select(p => p.Model).Should().Equal("claude-opus-5-5", "claude-opus-5-5", "claude-sonnet-5");
    }

    [Fact]
    public void TheRequestStaysBesideTheAnsweringModel()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70)
            .Main(10, model: "claude-opus-4-8", requested: "claude-opus-5-5[1m]").Write("main", 10, AttributionCases.X + "/a.cs");

        scenario.Assign("A", "CodeReview", 1).Msg("main", 10).Should().Match<Placement>(p => p.Model == "claude-opus-4-8" && p.Requested == "claude-opus-5-5[1m]");
    }

    [Fact]
    public void AnEffortThatWasNeverWritten_IsUnknownNotHigh()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Main(10, effort: string.Empty).Write("main", 10, AttributionCases.X + "/a.cs");

        scenario.Assign("A", "CodeReview", 1).Msg("main", 10).Effort.Should().Be(Effort.Unknown);
    }
}
