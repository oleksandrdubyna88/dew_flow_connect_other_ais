using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Rule 2 of the ladder (E4 decision 5): an agent inherits what its SUBAGENT parent did in the same window — recursively up the
/// chain — and never what <c>main</c> did. <c>toolUseId</c> makes main the parent of every depth-1 agent, so propagating through main
/// would associate every scout of every other repository.
/// </summary>
public sealed class OnlyASubagentParentPropagatesTests
{
    private const string X = AttributionCases.X;

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Code(2, 120, 130);

    [Fact]
    public void AChildOfAWritingSubagent_IsChildOf()
    {
        var scenario = Window().Agent("p", launchedAt: 4).Agent("c", parent: "p", launchedAt: 6).Message("p", 10).Write("p", 10, X + "/a.cs").Message("c", 12);

        scenario.Assign("A", "CodeReview", 1).Msg("c", 12).Should().Match<Placement>(p => p.Association == Association.ChildOf && p.Rung == Rung.Child);
    }

    [Fact]
    public void AGrandchild_IsChildOf_ThroughTheChain()
    {
        var scenario = Window().Agent("p", launchedAt: 4).Agent("c", parent: "p", launchedAt: 6).Agent("g", parent: "c", launchedAt: 8)
            .Message("p", 10).Write("p", 10, X + "/a.cs").Message("c", 12).Message("g", 14);

        scenario.Assign("A", "CodeReview", 1).Msg("g", 14).Association.Should().Be(Association.ChildOf);
    }

    [Fact]
    public void AChildOfMain_IsNotChildOf_EvenWhenMainWroteHere()
    {
        var scenario = Window().Agent("s", launchedAt: 4).Main(10).Write("main", 10, X + "/a.cs").Message("s", 12);

        scenario.Assign("A", "CodeReview", 1).Msg("s", 12).Should().Match<Placement>(
            p => p.Association == Association.SameSession && p.Rung == Rung.Session, "main's association never propagates; the window's only destination does");
    }

    /// <summary>Case 1: the scout of another repository reads <c>ambiguous</c> in A, never <c>child of</c> main.</summary>
    [Fact]
    public void AScoutWhenMainHasTwoDestinations_IsAmbiguous() =>
        AttributionCases.Case1().Assign("A", "CodeReview", 1).Msg("s1", 45).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence);

    [Fact]
    public void TheParentMustBeAssociatedInTheSameWindow()
    {
        var scenario = Window().Agent("p", launchedAt: 4).Agent("c", parent: "p", launchedAt: 6)
            .Message("p", 10).Write("p", 10, X + "/a.cs").Message("c", 90);

        scenario.Assign("A", "CodeReview", 2).Msg("c", 90).Should().Match<Placement>(
            p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence, "the parent wrote in C1; in C2's window it did nothing");
    }

    [Fact]
    public void AChildOfAnAgentThatWroteElsewhere_IsNotChildOf()
    {
        var scenario = Window().Agent("p", launchedAt: 4).Agent("c", parent: "p", launchedAt: 6)
            .Message("p", 10).Write("p", 10, AttributionCases.Y + "/b.cs").Message("c", 12).Repositories(AttributionCases.Y);

        scenario.Assign("A", "CodeReview", 1).Msg("c", 12).Association.Should().Be(Association.Ambiguous);
    }

    [Fact]
    public void ASameSessionParent_DoesNotPropagateEither()
    {
        var scenario = Window().Agent("w", launchedAt: 3).Agent("p", launchedAt: 4).Agent("c", parent: "p", launchedAt: 6)
            .Message("w", 9).Write("w", 9, X + "/a.cs").Message("p", 10).Message("c", 12);

        var assignment = scenario.Assign("A", "CodeReview", 1);

        assignment.Msg("p", 10).Association.Should().Be(Association.SameSession);
        assignment.Msg("c", 12).Association.Should().Be(Association.SameSession, "its own session rung, not its parent's association");
        assignment.Msg("c", 12).Rung.Should().Be(Rung.Session);
    }
}
