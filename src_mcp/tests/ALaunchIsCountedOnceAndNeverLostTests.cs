using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// A launch lands on the row holding the agent's first message in the window of its first line, never twice and never lost: an agent
/// launched in a window where it sent no assistant message gets a zero-token row so its launch is not dropped (E4.S3, Grain).
/// </summary>
public sealed class ALaunchIsCountedOnceAndNeverLostTests
{
    private const string X = AttributionCases.X;

    [Fact]
    public void ALaunchLandsOnItsFirstMessageRow()
    {
        var authors = AttributionCases.Case4().AuthorsOf("A", "CodeReview", 1);

        authors.Rows.Select(row => row.Show()).Should().Equal("round|r1|claude-sonnet-5-5|high|wrote here|L1|T2|W1");
    }

    [Fact]
    public void AStragglersLaterWindowShowsNoLaunch()
    {
        var authors = AttributionCases.Case4().AuthorsOf("A", "CodeReview", 3);

        authors.Rows.Where(row => row.Agent.Id == "r1").Should().ContainSingle().Which.Should().Match<AuthorRow>(row => row.Launches == 0 && row.Turns == 2 && row.Association == Association.SameSession);
    }

    /// <summary>An agent with two rows in its launch window (an effort switch) carries the launch on ONE of them.</summary>
    [Fact]
    public void AnAgentWithTwoRowsInItsLaunchWindow_CarriesTheLaunchOnTheFirst()
    {
        var scenario = AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Agent("a1", launchedAt: 4)
            .Message("a1", 10, effort: "high").Message("a1", 20, effort: "max").Write("a1", 10, X + "/a.cs");

        var rows = scenario.AuthorsOf("A", "CodeReview", 1).Rows;

        rows.Select(row => (row.Effort, row.Launches)).Should().Equal(("high", 1), ("max", 0));
        rows.Sum(row => row.Launches).Should().Be(1);
    }

    [Fact]
    public void AnAgentLaunchedWithNoMessage_GetsAZeroTokenRowCarryingTheLaunch()
    {
        var scenario = AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Main(10).Write("main", 10, X + "/a.cs").Agent("quiet", launchedAt: 4);

        var row = scenario.AuthorsOf("A", "CodeReview", 1).Rows.Single(candidate => candidate.Agent.Id == "quiet");

        row.Should().Match<AuthorRow>(r => r.Launches == 1 && r.Turns == 0 && r.Model == "unknown" && r.Requested == "unknown" && r.Effort == "unknown" && r.Association == Association.SameSession);
        row.Tokens.Should().Be(new TokenTerms(0, 0, 0, 0));
    }

    /// <summary>Σ launches over every window of a session = the number of distinct launched agents (rows plus the ambiguous totals that own them).</summary>
    [Theory]
    [InlineData("case2-background-work-across-rounds", 1)]
    [InlineData("case4-a-resumed-agent", 1)]
    [InlineData("case1-two-repositories-in-one-session", 5)]
    public void EveryLaunchIsCountedOnceOverTheSession(string name, int agents)
    {
        var scenario = AttributionCases.Named(name);
        var launches = scenario.AssignEvery().Sum(one =>
        {
            var authors = RoundAuthorsBuilder.Build(scenario.Build(one.Gate, one.Stage, one.Number), one.Assignment);

            return authors.Rows.Sum(row => row.Launches) + authors.Windows.Sum(window => window.Ambiguous.Launches);
        });

        launches.Should().Be(agents);
    }
}
