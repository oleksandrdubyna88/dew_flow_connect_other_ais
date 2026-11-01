using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The grain of a row (E4.S3): one agent, one window, one request, one answering model, one effort, one association. A model switch
/// or an effort switch inside a window is two rows for one agent; tokens are exact sums of the messages on the row.
/// </summary>
public sealed class ARowIsOneAgentModelAndEffortInOneWindowTests
{
    private const string X = AttributionCases.X;

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70);

    [Fact]
    public void AModelSwitchGivesTwoRowsForMain()
    {
        var authors = AttributionCases.Case3().AuthorsOf("A", "CodeReview", 1);

        authors.Rows.Select(row => row.Show()).Should().Equal("round|main|claude-opus-5-5|high|wrote here|L0|T2|W1", "round|main|claude-sonnet-5|high|wrote here|L0|T1|W0");
    }

    [Fact]
    public void AnEffortSwitchGivesTwoRows()
    {
        var scenario = Window().Main(10, effort: "high").Main(20, effort: "max").Write("main", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Select(row => row.Effort).Should().Equal("high", "max");
    }

    [Fact]
    public void TokensAreExactSumsTermByTerm()
    {
        var scenario = Window().Main(10, tokens: new TokenTerms(1, 2, 3, 4)).Main(20, tokens: new TokenTerms(10, 20, 30, 40)).Write("main", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Tokens.Should().Be(new TokenTerms(11, 22, 33, 44));
    }

    /// <summary>A message E3 folded from several lines reaches here as ONE message: one turn.</summary>
    [Fact]
    public void AMessageIsOneTurn()
    {
        var scenario = Window().Main(10, tokens: new TokenTerms(5, 5, 5, 5)).Write("main", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Turns.Should().Be(1);
    }

    [Fact]
    public void ARowCarriesItsWhoItsTypeAndItsRound()
    {
        var scenario = Window().Agent("a1", launchedAt: 4, type: "feature-dev:code-reviewer").Message("a1", 10).Write("a1", 10, X + "/a.cs").Main(12);

        var rows = scenario.AuthorsOf("A", "CodeReview", 1).Rows;

        rows.Single(row => row.Agent.IsMain).Should().Match<AuthorRow>(row => row.Who == "main" && row.AgentType.Length == 0 && row.Stage == "CodeReview" && row.Number == 1 && row.Window == WindowKind.Round);
        rows.Single(row => !row.Agent.IsMain).Should().Match<AuthorRow>(row => row.Who == "subagent" && row.AgentType == "feature-dev:code-reviewer");
    }

    [Fact]
    public void RowsAreOrderedByWindowWhoAndAgent()
    {
        var scenario = Window().Agent("b1", launchedAt: 4).Agent("a1", launchedAt: 5).Message("b1", 10).Message("a1", 11).Main(12).Write("a1", 11, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Select(row => row.Agent.Id).Should().Equal("main", "a1", "b1");
    }
}
