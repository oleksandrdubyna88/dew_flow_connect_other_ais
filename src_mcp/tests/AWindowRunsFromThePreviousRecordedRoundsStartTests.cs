using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The tiling (E4 §S2.3): a recorded round's window runs from the PREVIOUS recorded round's START — not its completion — to its own
/// start, so the work done while the previous round was being reviewed lands in the next window and in none that is lost. A window
/// opens for every recorded round and for nothing else.
/// </summary>
public sealed class AWindowRunsFromThePreviousRecordedRoundsStartTests
{
    private static AttributionScenario Gate() => AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0);

    [Fact]
    public void TheFirstRoundsWindow_StartsAtOpen()
    {
        var assignment = Gate().Code(1, 60, 70).Assign("A", "CodeReview", 1);

        assignment.RoundWindow().Bounds().Should().Be(Span(0, 60));
        assignment.BeforeOpenWindow().Bounds().Should().Be(Span(null, 0), "the work before the session opened is the first round's, and unbounded below");
    }

    /// <summary>Case 2: the old rule (the previous round's COMPLETION) would have started C2 at +75 and lost the work of +65 and +70.</summary>
    [Fact]
    public void RoundN_StartsAtRoundNMinusOnesStart_NotItsCompletion()
    {
        var assignment = AttributionCases.Case2().Assign("A", "CodeReview", 2);

        assignment.RoundWindow().Bounds().Should().Be(Span(60, 120));
        assignment.Windows.Should().ContainSingle("only the first round owns a before-open window");
    }

    [Fact]
    public void ASkippedRound_OpensNoWindow()
    {
        var journal = Gate().Code(1, 60, 70).Round("CodeReview", 2, 90, 95, verdict: "skipped").Code(3, 120, 130);

        journal.Assign("A", "CodeReview", 3).RoundWindow().Bounds().Should().Be(Span(60, 120), "the skip's stretch lands in the next recorded round");
    }

    [Fact]
    public void AnInterruptedRound_OpensNoWindow()
    {
        var journal = Gate().Code(1, 60, 70).Round("CodeReview", 2, 90, 95, status: "interrupted").Code(3, 120, 130);

        journal.Assign("A", "CodeReview", 3).RoundWindow().Bounds().Should().Be(Span(60, 120));
    }

    [Theory]
    [InlineData("FeatureReview")]
    [InlineData("DocumentReview")]
    public void AFeatureOrDocumentRound_OpensNoWindow(string stage)
    {
        var journal = Gate().Code(1, 60, 70).Round(stage, 1, 90, 95).Code(2, 120, 130);

        journal.Assign("A", "CodeReview", 2).RoundWindow().Bounds().Should().Be(Span(60, 120));
    }

    [Fact]
    public void PlanAndCodeRounds_TileOneTimeline()
    {
        var journal = Gate().Round("PlanReview", 1, 30, 40).Code(1, 60, 70);

        journal.Assign("A", "CodeReview", 1).RoundWindow().Bounds().Should().Be(Span(30, 60), "the journal is every stage, in the order it ran");
        journal.Assign("A", "PlanReview", 1).RoundWindow().Bounds().Should().Be(Span(0, 30));
    }

    [Fact]
    public void AnOpenAfterTheFirstStart_IsClippedToIt()
    {
        var assignment = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(70).Code(1, 60, 80).Assign("A", "CodeReview", 1);

        assignment.BeforeOpenWindow().Bounds().Should().Be(Span(null, 60), "the before-open window ends where the first round starts");
        assignment.RoundWindow().Bounds().Should().Be(Span(60, 60), "and the round window is empty, never inverted");
    }

    [Fact]
    public void ThePlacementsAreOrderedAndEveryWindowIsKept()
    {
        var assignment = Gate().Code(1, 60, 70).Assign("A", "CodeReview", 1);

        assignment.Windows.Select(window => window.Kind).Should().Equal([WindowKind.BeforeOpen, WindowKind.Round], "ordinal: before-open sorts before round");
    }
}
