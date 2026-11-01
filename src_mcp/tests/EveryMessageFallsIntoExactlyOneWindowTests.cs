using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Identity 1 — local completeness (E4 §S2.5): for each non-blind window the messages, launches and writes the input holds are
/// exactly the ones placed, each once, each inside its window. A window is half-open, so a message at exactly a round's start is in
/// the NEXT one.
/// </summary>
public sealed class EveryMessageFallsIntoExactlyOneWindowTests
{
    public static TheoryData<string> Cases => AttributionCases.Names;

    [Theory]
    [MemberData(nameof(Cases))]
    public void LocalCompleteness_HoldsForEveryWindow(string name)
    {
        var scenario = AttributionCases.Named(name);
        var every = scenario.AssignEvery();

        every.Should().NotBeEmpty("a scenario with nothing to attribute proves nothing");
        foreach (var one in every)
        {
            one.Assignment.LocalCompleteness(scenario.Build(one.Gate, one.Stage, one.Number))
                .Should().BeEmpty("{0}: {1} {2} of gate {3}", name, one.Stage, one.Number, one.Gate);
        }
    }

    /// <summary>The check has teeth: an assignment that dropped a placement, or placed one twice, is named.</summary>
    [Fact]
    public void ADroppedOrDuplicatedPlacement_IsNamed()
    {
        var scenario = AttributionCases.Case1();
        var input = scenario.Build("A", "CodeReview", 2);
        var whole = RoundAttribution.Assign(input);

        whole.LocalCompleteness(input).Should().BeEmpty();
        (whole with { Messages = whole.Messages.RemoveAt(0) }).LocalCompleteness(input).Should().NotBeEmpty("a message of the window is missing");
        (whole with { Messages = whole.Messages.Add(whole.Messages[0]) }).LocalCompleteness(input).Should().NotBeEmpty("a message is placed twice");
        (whole with { Launches = [] }).LocalCompleteness(input).Should().NotBeEmpty("a launch of the window is missing");
    }

    [Fact]
    public void AMessageAtExactlyARoundsStart_IsInTheNextWindow()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Code(2, 120, 130).Main(60).Write("main", 60, AttributionCases.X + "/a.cs");

        scenario.Assign("A", "CodeReview", 1).Messages.Should().BeEmpty("[0,60) does not hold +60");
        scenario.Assign("A", "CodeReview", 2).Msg("main", 60).Association.Should().Be(Association.WroteHere);
    }

    [Fact]
    public void AMessageAfterThisRoundsStart_IsNotPlaced()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Main(10).Main(65).Main(90);

        scenario.Assign("A", "CodeReview", 1).Messages.Select(placement => Minutes(placement.At)).Should().Equal(10d);
    }
}
