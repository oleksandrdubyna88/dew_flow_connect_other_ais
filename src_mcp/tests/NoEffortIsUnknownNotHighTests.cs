using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>An unknown effort is <c>unknown</c>, never <c>high</c> (NoEffortIsUnknownNotHighTests of E4.S3).</summary>
public sealed class NoEffortIsUnknownNotHighTests
{
    [Fact]
    public void AnAbsentEffort_IsUnknownOnTheRow()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Main(10, effort: string.Empty).Write("main", 10, AttributionCases.X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Effort.Should().Be(Effort.Unknown);
    }
}
