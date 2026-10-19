using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// An agent that keeps working across rounds (E4 cases 2 and 4): its messages are split by INSTANT, its launch lands once — in the
/// window of its first line — and a resumed agent launches nothing again; its association is decided per window.
/// </summary>
public sealed class ABackgroundAgentStraddlesARoundTests
{
    [Fact]
    public void ItsMessagesSplitByInstant()
    {
        var scenario = AttributionCases.Case2();

        scenario.Assign("A", "CodeReview", 1).Messages.Where(p => p.Agent.Id == "bg").Select(p => Minutes(p.At)).Should().Equal(55d);
        scenario.Assign("A", "CodeReview", 2).Messages.Where(p => p.Agent.Id == "bg").Select(p => Minutes(p.At)).Should().Equal(65d, 70d, 110d);
    }

    /// <summary>+65 and +70 ran while C1 was being reviewed (60→75): C2's window starts at C1's START, so they are C2's — in none lost.</summary>
    [Fact]
    public void TheReviewTimeWorkIsInRoundNsWindow_NotInNone()
    {
        var assignment = AttributionCases.Case2().Assign("A", "CodeReview", 2);

        assignment.Msg("bg", 65).Should().Match<Placement>(p => p.Association == Association.SameSession && p.Window.Kind == WindowKind.Round);
        assignment.Msg("bg", 70).Association.Should().Be(Association.SameSession);
        assignment.Msg("bg", 65).Tokens.Output.Should().Be(100);
    }

    [Fact]
    public void ItLaunchesOnce_InTheWindowOfItsFirstLine()
    {
        var scenario = AttributionCases.Case2();

        scenario.Assign("A", "CodeReview", 1).Launches.Should().ContainSingle(launch => launch.Agent.Id == "bg" && Minutes(launch.At) == 50);
        scenario.Assign("A", "CodeReview", 2).Launches.Should().BeEmpty();
        scenario.AssignEvery().SelectMany(one => one.Assignment.OwnedLaunches()).Should().Equal("bg");
    }

    [Fact]
    public void TheResumedAgentLaunchesNothingAgain()
    {
        var scenario = AttributionCases.Case4();

        scenario.Assign("A", "CodeReview", 1).Launches.Should().ContainSingle(launch => launch.Agent.Id == "r1");
        scenario.Assign("A", "CodeReview", 2).Launches.Should().BeEmpty();
        scenario.Assign("A", "CodeReview", 3).Launches.Should().BeEmpty();
    }

    [Fact]
    public void AResumedAgentIsAssociatedPerWindow()
    {
        var scenario = AttributionCases.Case4();

        scenario.Assign("A", "CodeReview", 1).Labelled(Association.WroteHere).Should().Equal("r1@15", "r1@20");
        scenario.Assign("A", "CodeReview", 2).Messages.Should().BeEmpty();
        scenario.Assign("A", "CodeReview", 3).Labelled(Association.SameSession).Should().Equal("r1@130", "r1@140");
        scenario.Assign("A", "CodeReview", 3).Msg("r1", 130).Rung.Should().Be(Rung.Session, "its parent is main, which never propagates, and it wrote nothing in this window");
    }
}
