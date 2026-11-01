using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>Two requests answered by one model are two rows: the request is never one placement's value chosen for all (risk consultation, 2026-10-16).</summary>
public sealed class TwoRequestsAnsweredByOneModelAreTwoRowsTests
{
    [Fact]
    public void ADifferentRequestIsADifferentRow()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Agent("a1", launchedAt: 4)
            .Message("a1", 10, model: "claude-sonnet-5-5", requested: "sonnet").Message("a1", 20, model: "claude-sonnet-5-5", requested: "claude-sonnet-5-5")
            .Write("a1", 10, AttributionCases.X + "/a.cs");

        var rows = scenario.AuthorsOf("A", "CodeReview", 1).Rows;

        rows.Select(row => row.Requested).Should().BeEquivalentTo("sonnet", "claude-sonnet-5-5");
        rows.Should().OnlyContain(row => row.Model == "claude-sonnet-5-5" && row.Turns == 1);
        rows.Sum(row => row.Launches).Should().Be(1, "the launch is on ONE of the rows");
        rows.Sum(row => row.WriteCalls).Should().Be(1, "so is the write call");
    }
}
