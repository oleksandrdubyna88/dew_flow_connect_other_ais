using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// A recorded round with no start (a row older than the field) is BLIND: the position of its edge is unknown, so its own window and
/// the next recorded round's are <c>unobserved</c> and place nothing — a window with one unknown edge would over-credit its neighbour
/// with no symptom, the one error this epic exists to avoid (decision 1, §S2.2b G8). <c>OpenedUtc</c> null does the same to the first
/// round.
/// </summary>
public sealed class ABlindRoundIsUnobservedNotGuessedTests
{
    [Fact]
    public void ARoundWithNoStart_IsUnobservedNoRoundStart()
    {
        var assignment = AttributionCases.BlindRounds().Assign("A", "CodeReview", 1);

        assignment.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Unobserved);
        assignment.States.SelectMany(state => state.Reasons).Distinct().Should().Equal(AttributionReasons.NoRoundStart);
    }

    [Fact]
    public void TheRoundAfterIt_IsUnobservedToo()
    {
        var assignment = AttributionCases.BlindRounds().Assign("A", "CodeReview", 2);

        assignment.RoundState().Kind.Should().Be(ObservationKinds.Unobserved);
        assignment.RoundState().Reasons.Should().Equal(AttributionReasons.NoRoundStart);
    }

    [Fact]
    public void TheOneAfterThat_IsTiledNormally()
    {
        var assignment = AttributionCases.BlindRounds().Assign("A", "CodeReview", 3);

        assignment.RoundWindow().Bounds().Should().Be(Span(120, 180));
        assignment.RoundState().Kind.Should().Be(ObservationKinds.Observed);
        assignment.OwnedMessages().Should().Equal("main@130", "main@150");
    }

    [Fact]
    public void NoOpenTime_IsUnobservedNoOpenTime()
    {
        var assignment = AttributionScenario.For().Gate("A", AttributionCases.X).NeverOpened().Code(1, 60, 70).Main(10).Assign("A", "CodeReview", 1);

        assignment.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Unobserved);
        assignment.States.SelectMany(state => state.Reasons).Distinct().Should().Equal(AttributionReasons.NoOpenTime);
    }

    [Fact]
    public void ABlindWindowPlacesNothing()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Round("CodeReview", 1, null, 55).Main(10).Write("main", 10, AttributionCases.X + "/a.cs").Agent("a1", launchedAt: 5).Message("a1", 12);

        var assignment = scenario.Assign("A", "CodeReview", 1);

        assignment.Messages.Should().BeEmpty();
        assignment.Launches.Should().BeEmpty();
        assignment.Writes.Should().BeEmpty();
        assignment.Windows.Should().OnlyContain(window => window.Blind && window.From == null && window.To == null);
    }

    [Fact]
    public void ASkippedRoundBetween_ChangesNothingAboutBlindness()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0)
            .Round("CodeReview", 1, null, 55).Round("CodeReview", 2, 90, 95, verdict: "skipped").Code(3, 120, 130).Code(4, 180, 190);

        scenario.Assign("A", "CodeReview", 3).RoundState().Kind.Should().Be(ObservationKinds.Unobserved, "the next RECORDED round after a blind one is blind; the skip opens none");
        scenario.Assign("A", "CodeReview", 4).RoundState().Kind.Should().Be(ObservationKinds.Observed);
    }
}
