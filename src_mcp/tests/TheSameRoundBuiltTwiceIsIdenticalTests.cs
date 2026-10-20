using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Equal inputs give equal outputs, byte for byte (E4.S3): building twice, or from an input whose message order was permuted, says the
/// same. Equality is judged on the serialised text — a record holding an <c>ImmutableArray</c> compares and prints the array by reference.
/// </summary>
public sealed class TheSameRoundBuiltTwiceIsIdenticalTests
{
    private static string Text(RoundAuthors authors) => System.Text.Json.JsonSerializer.Serialize(authors);

    [Fact]
    public void BuildTwiceIsEqualByValueAndText()
    {
        var scenario = AttributionCases.Case1();

        Text(scenario.AuthorsOf("A", "CodeReview", 1)).Should().Be(Text(scenario.AuthorsOf("A", "CodeReview", 1)));
        scenario.AuthorsOf("A", "CodeReview", 1).Should().BeEquivalentTo(scenario.AuthorsOf("A", "CodeReview", 1));
    }

    [Fact]
    public void ThePermutedInputGivesTheSameOutput()
    {
        var input = AttributionCases.Case1().Build("A", "CodeReview", 1);
        var permuted = input with
        {
            Caller = input.Caller with { Messages = [.. input.Caller.Messages.Reverse()], Agents = [.. input.Caller.Agents.Reverse()], Writes = [.. input.Caller.Writes.Reverse()] },
            Peers = [.. input.Peers.Reverse()],
        };

        Text(RoundAuthorsBuilder.Build(permuted, RoundAttribution.Assign(permuted))).Should().Be(Text(RoundAuthorsBuilder.Build(input, RoundAttribution.Assign(input))));
    }
}
