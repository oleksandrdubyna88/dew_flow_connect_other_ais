using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Both identities of E4 §S2.5 over every recorded case through the real reader: local completeness (each window holds exactly what the
/// input holds in it) and global uniqueness (no message and no launch is owned twice over the settled rounds of the caller, tokens counted
/// once, and a claim always points at an owner). <c>c09</c>, <c>c15</c> and <c>c17</c> are the cases with more than one gate session.
/// </summary>
public sealed class TheRecordedSessionsKeepBothIdentitiesTests
{
    public static TheoryData<string> Dirs => [.. RecordedCell.All.Select(cell => cell.Case).Append("c13").Distinct(StringComparer.Ordinal)];

    /// <summary>c13 lists round 1 twice; the identities run over ONE copy (the journal the contract accepts).</summary>
    private static RecordedSession Open(string dir) => dir == "c13" ? RecordedSession.Open(dir, shape: rounds => [rounds[0]]) : RecordedSession.Open(dir);

    private static IEnumerable<(string Gate, int Number, AttributionInput Input, WindowAssignment Assignment)> Every(RecordedSession recorded) =>
        recorded.Rounds().Select(round => (round.Gate, round.Number, Input: recorded.Input(round.Gate, round.Number)))
            .Where(round => round.Input.Validate().IsEmpty)
            .Select(round => (round.Gate, round.Number, round.Input, RoundAttribution.Assign(round.Input)));

    [Theory]
    [MemberData(nameof(Dirs))]
    public void LocalCompleteness_HoldsForEveryWindowOfEveryRound(string dir)
    {
        using var recorded = Open(dir);
        var every = Every(recorded).ToList();

        every.Should().NotBeEmpty(dir);
        foreach (var round in every)
        {
            round.Assignment.LocalCompleteness(round.Input).Should().BeEmpty($"{dir}: round {round.Number} of {round.Gate}");
        }
    }

    [Theory]
    [MemberData(nameof(Dirs))]
    public void GlobalUniqueness_HoldsOverTheSettledRounds(string dir)
    {
        using var recorded = Open(dir);
        var every = Every(recorded).ToList();
        var placements = every.SelectMany(round => round.Assignment.Messages).ToList();
        var owned = placements.Where(placement => Association.IsOwning(placement.Association)).ToList();

        owned.Select(placement => placement.MessageId).Should().OnlyHaveUniqueItems($"{dir}: no message is owned twice");
        every.SelectMany(round => round.Assignment.Launches).Where(launch => Association.IsOwning(launch.Association)).Select(launch => launch.Agent.Id)
            .Should().OnlyHaveUniqueItems($"{dir}: no agent is launched twice");
        foreach (var claim in placements.Where(placement => placement.Association == Association.ClaimedByPeer))
        {
            owned.Should().Contain(placement => placement.MessageId == claim.MessageId && placement.OwnerGate == claim.OwnerGate, $"{dir}: {claim.MessageId} is claimed by its owner");
        }

        var ownedIds = owned.Select(placement => placement.MessageId).ToHashSet(StringComparer.Ordinal);
        placements.Where(placement => !ownedIds.Contains(placement.MessageId) && placement.Association != Association.WroteElsewhere).Should().BeEmpty($"{dir}: a message nobody owns is labelled wrote elsewhere wherever it is placed");
        owned.Sum(placement => placement.Tokens.Output).Should().Be(recorded.Observation.Messages.Where(message => ownedIds.Contains(message.MessageId)).Sum(message => message.Tokens.Output), $"{dir}: tokens once");
    }

    /// <summary>The cases the identities are about: two gate sessions of one caller (c09, c15, c17) own each message once between them.</summary>
    [Theory]
    [InlineData("c09")]
    [InlineData("c15")]
    [InlineData("c17")]
    public void TheCasesWithTwoGateSessions_DoHaveTwo(string dir)
    {
        using var recorded = Open(dir);

        recorded.Timelines.Should().HaveCount(2);
        Every(recorded).Count().Should().Be(2);
    }
}
