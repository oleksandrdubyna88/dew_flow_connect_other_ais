using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// A row's <c>Model</c> is the model that ANSWERED (RESULTS Q2): a session can fall back (Opus 5.5 to Opus 4.8) and a meta often names no
/// request at all. The request stays beside it as <c>Requested</c> — from the messages, never from the agent's meta alias — and is part
/// of the grain, so two requests answered by one model are two rows.
/// </summary>
public sealed class TheModelOnARowIsTheOneThatAnsweredTests
{
    private const string X = AttributionCases.X;

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70);

    [Fact]
    public void ASessionAnsweredByAFallbackModel_CountsTheAnsweringModel()
    {
        var scenario = Window().Main(10, model: "claude-opus-4-8", requested: "claude-opus-5-5[1m]").Write("main", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Should().Match<AuthorRow>(row => row.Model == "claude-opus-4-8" && row.Requested == "claude-opus-5-5[1m]");
    }

    /// <summary>The agent's meta alias is display only: even a non-empty one is never the row's model or request.</summary>
    [Fact]
    public void ASubagentsMetaAlias_IsNeverTheRowsModelOrRequest()
    {
        var scenario = Window().Agent("a1", launchedAt: 4, requested: "opus").Message("a1", 10, model: "claude-sonnet-5-5", requested: "claude-sonnet-5-5").Write("a1", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Should().Match<AuthorRow>(row => row.Model == "claude-sonnet-5-5" && row.Requested == "claude-sonnet-5-5");
    }

    [Fact]
    public void AMetaWithNoAlias_NeverBlanksTheModel()
    {
        var scenario = Window().Agent("a1", launchedAt: 4, requested: string.Empty).Message("a1", 10, model: "claude-sonnet-5-5", requested: "claude-sonnet-5-5").Write("a1", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Should().ContainSingle().Which.Should().Match<AuthorRow>(row => row.Model == "claude-sonnet-5-5" && row.Requested == "claude-sonnet-5-5");
    }

    [Fact]
    public void ASwitchMidWindowAfterAFallback_IsTwoRows()
    {
        var scenario = Window().Main(10, model: "claude-opus-4-8", requested: "claude-opus-5-5[1m]").Main(20, model: "claude-opus-5-5", requested: "claude-opus-5-5[1m]").Write("main", 10, X + "/a.cs");

        scenario.AuthorsOf("A", "CodeReview", 1).Rows.Select(row => row.Model).Should().Equal("claude-opus-4-8", "claude-opus-5-5");
    }
}
