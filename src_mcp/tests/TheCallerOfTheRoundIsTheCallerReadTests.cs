using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Several callers (E4 decision 2): round N reads its OWN caller's transcript over the whole window. When the previous recorded round
/// had another caller, what that caller did after round N-1 started is in a transcript this round did not read, so the window is a
/// floor and says so; an observation of a caller other than the round's is a contract violation, not a quiet misread.
/// </summary>
public sealed class TheCallerOfTheRoundIsTheCallerReadTests
{
    [Fact]
    public void ACallerChange_IsPartialCallerChanged()
    {
        var assignment = AttributionCases.CallerChanged().Assign("A", "CodeReview", 2);

        assignment.RoundWindow().Bounds().Should().Be(Span(60, 120));
        assignment.RoundState().Should().Match<WindowState>(s => s.Kind == ObservationKinds.Partial && s.Reasons.SequenceEqual(new[] { AttributionReasons.CallerChanged }));
        assignment.Msg("main", 80).Association.Should().Be(Association.WroteHere, "placements come from the round's own caller");
    }

    [Fact]
    public void AnEmptyPriorCaller_IsPartialPriorCallerUnknown() =>
        AttributionCases.CallerChanged(priorCaller: string.Empty).Assign("A", "CodeReview", 2).RoundState().Reasons.Should().Equal(AttributionReasons.PriorCallerUnknown);

    [Fact]
    public void TheSameCallerAsThePreviousRound_IsObserved() =>
        AttributionCases.CallerChanged(caller: "c0", priorCaller: "c0").Assign("A", "CodeReview", 2).RoundState().Kind.Should().Be(ObservationKinds.Observed);

    [Fact]
    public void TheFirstRoundHasNoPreviousCaller() =>
        AttributionCases.CallerChanged(caller: "c0", priorCaller: "c0").Assign("A", "CodeReview", 1).States.Should().OnlyContain(state => state.Reasons.IsEmpty);

    [Fact]
    public void AnObservationOfAnotherCaller_IsAContractViolation()
    {
        var input = AttributionCases.CallerChanged().Build("A", "CodeReview", 2);

        var assign = () => RoundAttribution.Assign(input with { Caller = input.Caller with { CallerSessionId = "c9" } });

        assign.Should().Throw<ArgumentException>().WithMessage("*caller*");
    }

    [Fact]
    public void AnInputThatBreaksTheContract_IsRefusedNamingTheFirstViolation()
    {
        var input = AttributionCases.Case1().BuildUnchecked("A", "CodeReview", 7);

        var assign = () => RoundAttribution.Assign(input);

        assign.Should().Throw<ArgumentException>().WithMessage("*not in the gate's journal*");
    }
}
