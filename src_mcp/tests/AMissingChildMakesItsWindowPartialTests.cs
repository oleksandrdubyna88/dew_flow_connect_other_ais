using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Case 5 (E4 decision 9, §S2.2b G4 and G5): a missing child transcript is ONE unobserved launch and ONE reason in the window its
/// instant falls in — never a zero row — while every other window stays <c>observed</c>. An E3 reason carries no instant and marks
/// every window.
/// </summary>
public sealed class AMissingChildMakesItsWindowPartialTests
{
    private static WindowAssignment C1() => AttributionCases.Case5().Assign("A", "CodeReview", 1);

    private static WindowAssignment C2() => AttributionCases.Case5().Assign("A", "CodeReview", 2);

    [Fact]
    public void TheWindowHoldingTheCallIsPartialNamingEach() =>
        C1().RoundState().Should().Match<WindowState>(state => state.Kind == ObservationKinds.Partial
            && state.Reasons.SequenceEqual(new[] { "agent call without transcript", "meta without transcript" }));

    [Fact]
    public void TheOtherWindowStaysObserved()
    {
        C2().RoundState().Kind.Should().Be(ObservationKinds.Observed);
        C1().StateOf(C1().BeforeOpenWindow()).Kind.Should().Be(ObservationKinds.Observed);
    }

    [Fact]
    public void TheChildHasNoRowNoLaunchAndNoTokens()
    {
        var assignment = C1();

        assignment.Launches.Should().BeEmpty("an inert agent launches nothing");
        assignment.Messages.Should().NotContain(placement => placement.Agent.Id == "m2");
        assignment.Agents.Should().NotContain(association => association.Agent.Id == "m2");
        assignment.Unobserved.Should().HaveCount(2);
    }

    [Fact]
    public void TheParentsOwnAssignmentIsUnchanged()
    {
        C1().Msg("main", 20).Association.Should().Be(Association.WroteHere);
        C2().Msg("main", 100).Association.Should().Be(Association.WroteHere);
    }

    /// <summary>E3 emits BOTH an <c>UnobservedLaunch</c> and an inert agent for one meta without a file: one loss, counted once.</summary>
    [Fact]
    public void AMetaWithoutATranscript_IsOneUnobservedLaunchNotTwo()
    {
        var assignment = C1();

        assignment.Unobserved.Select(item => (Minutes(item.At), item.Why)).Should().Equal((30d, "agent call without transcript"), (40d, "meta without transcript"));
        assignment.RoundState().Reasons.Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }

    [Fact]
    public void AnE3ReasonWithoutAnInstant_MarksEveryWindow()
    {
        var scenario = AttributionCases.Case5().Reason("read budget");

        var first = scenario.Assign("A", "CodeReview", 1);
        var second = scenario.Assign("A", "CodeReview", 2);

        first.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Partial && state.Reasons.Contains("read budget"));
        second.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Partial && state.Reasons.SequenceEqual(new[] { "read budget" }));
    }

    [Fact]
    public void AnUnobservedCaller_MakesEveryWindowUnobserved()
    {
        var assignment = AttributionCases.Case5().CallerUnobserved("transcript not found").Assign("A", "CodeReview", 1);

        assignment.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Unobserved && state.Reasons.SequenceEqual(new[] { "transcript not found" }));
        assignment.Messages.Should().BeEmpty("an unobserved caller has nothing to place");
    }

    [Fact]
    public void AnUnobservedLaunchWithNoInstant_MarksEveryWindow()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Code(2, 120, 130).MissingChild("main", null, "agent call without transcript");

        scenario.Assign("A", "CodeReview", 2).RoundState().Reasons.Should().Equal("agent call without transcript");
        scenario.Assign("A", "CodeReview", 2).Unobserved.Should().BeEmpty("with no instant there is nothing to place");
    }
}
