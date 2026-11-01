using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>RoundAttribution.ReadFrom</c> is the lower bound E5 hands the reader BEFORE it reads (parent D7): the earliest instant any line
/// can matter — the first of this round's windows and of their claimants'. A line before it is in no window and no claimant, so not
/// reading it changes nothing; null when any of those windows is unbounded.
/// </summary>
public sealed class ReadFromIsTheEarliestLineAssignNeedsTests
{
    private static DateTimeOffset? ReadFrom(AttributionScenario scenario, string gate, string stage, int number) =>
        RoundAttribution.ReadFrom(scenario.Build(gate, stage, number));

    [Fact]
    public void ANonFirstRound_ReadsFromThePreviousRecordedStart() =>
        ReadFrom(AttributionCases.Case2(), "A", "CodeReview", 2).Should().Be(AttributionScenario.At(60));

    [Fact]
    public void AClaimantStartingEarlier_LowersIt()
    {
        var scenario = AttributionCases.Case1();

        ReadFrom(scenario, "A", "CodeReview", 2).Should().Be(AttributionScenario.At(30), "B C1 starts its window at +30, and its ladder reads every write in it");
        scenario.Assign("A", "CodeReview", 2).ReadFrom.Should().Be(AttributionScenario.At(30));
    }

    [Fact]
    public void AFirstRoundWithNoPriorEnd_ReadsFromTheFirstLine() =>
        ReadFrom(AttributionCases.Case2(), "A", "CodeReview", 1).Should().BeNull();

    [Fact]
    public void ABlindRound_ReadsFromNothingItCanName() =>
        ReadFrom(AttributionCases.BlindRounds(), "A", "CodeReview", 2).Should().BeNull("a blind window has no edge to read from");

    /// <summary>Dropping every line before the bound — what the reader does not read — changes nothing.</summary>
    [Fact]
    public void DroppingEveryLineBeforeReadFrom_ChangesNothing()
    {
        var scenario = AttributionCases.Case1();
        var input = scenario.Build("A", "CodeReview", 2);
        var from = RoundAttribution.ReadFrom(input)!.Value;
        var trimmed = input with
        {
            Caller = input.Caller with
            {
                Messages = [.. input.Caller.Messages.Where(message => message.At >= from)],
                Writes = [.. input.Caller.Writes.Where(write => write.At >= from)],
            },
        };

        trimmed.Caller.Messages.Length.Should().BeLessThan(input.Caller.Messages.Length, "something was dropped, or this proves nothing");
        RoundAttribution.Assign(trimmed).AsText().Should().Be(RoundAttribution.Assign(input).AsText());
    }

    [Fact]
    public void TheBoundIsComputableWithoutAnObservation()
    {
        var input = AttributionCases.Case1().Build("A", "CodeReview", 2);
        var empty = input with { Caller = input.Caller with { Messages = ImmutableArray<ObservedMessage>.Empty, Writes = [], Agents = [], Unobserved = [] } };

        RoundAttribution.ReadFrom(empty).Should().Be(RoundAttribution.ReadFrom(input));
    }
}
