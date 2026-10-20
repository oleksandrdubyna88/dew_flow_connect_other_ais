using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Identity 2 — global uniqueness (E4 §S2.5) over every worked example, through the test-side <c>AssignEvery</c>: over the settled
/// rounds of the observation's caller, no message id and no launch is owned twice, and tokens are never added twice without any
/// further rule (§S2.2b G7: a message is owned once, or owned by nobody and <c>wrote elsewhere</c> wherever it is placed).
/// </summary>
public sealed class NoMessageIsOwnedTwiceTests
{
    public static TheoryData<string> Cases => AttributionCases.Names;

    private static (IReadOnlyList<AttributionScenario.Assigned> Every, AttributionScenario Scenario) Run(string name)
    {
        var scenario = AttributionCases.Named(name);

        return (scenario.AssignEvery(), scenario);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GlobalUniqueness_HoldsOverEveryGateAndPeer(string name)
    {
        var (every, _) = Run(name);
        var owned = every.SelectMany(one => one.Assignment.Messages).Where(placement => Association.IsOwning(placement.Association)).Select(placement => placement.MessageId);
        var launches = every.SelectMany(one => one.Assignment.Launches).Where(placement => Association.IsOwning(placement.Association)).Select(placement => placement.Agent.Id);

        every.Should().NotBeEmpty();
        owned.Should().OnlyHaveUniqueItems($"{name}: no message id is owned twice");
        launches.Should().OnlyHaveUniqueItems($"{name}: no agent is launched twice");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TokensAreTheSumOfOwnedMessagesOnce(string name)
    {
        var (every, scenario) = Run(name);
        var placements = every.SelectMany(one => one.Assignment.Messages).ToList();
        var ownedIds = placements.Where(placement => Association.IsOwning(placement.Association)).Select(placement => placement.MessageId).ToHashSet(StringComparer.Ordinal);

        foreach (var id in placements.Select(placement => placement.MessageId).Distinct(StringComparer.Ordinal).Where(id => !ownedIds.Contains(id)))
        {
            placements.Where(placement => placement.MessageId == id).Should().OnlyContain(
                placement => placement.Association == Association.WroteElsewhere, $"{name}: a message nobody owns is labelled wrote elsewhere wherever it is placed ({id})");
        }

        var summed = placements.Where(placement => Association.IsOwning(placement.Association)).Sum(placement => placement.Tokens.Output);
        var once = scenario.Observation().Messages.Where(message => ownedIds.Contains(message.MessageId)).Sum(message => message.Tokens.Output);

        summed.Should().Be(once, $"{name}: owned tokens are the tokens of the ids owned somewhere, each once");
    }

    /// <summary>A <c>claimed by peer</c> label always points at an assignment that owns the id (the claimant's ladder was owning).</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryClaimPointsAtAnOwner(string name)
    {
        var (every, _) = Run(name);

        foreach (var claim in every.SelectMany(one => one.Assignment.Messages).Where(placement => placement.Association == Association.ClaimedByPeer))
        {
            every.Where(one => one.Gate == claim.OwnerGate).SelectMany(one => one.Assignment.Messages)
                .Should().Contain(placement => placement.MessageId == claim.MessageId && Association.IsOwning(placement.Association), $"{name}: {claim.MessageId} is claimed by {claim.OwnerGate}");
        }
    }

    /// <summary>Case 1: cx +10 is owned by A and labelled <c>wrote elsewhere</c> by B — counted once, which a subtraction of every elsewhere would get wrong.</summary>
    [Fact]
    public void ACxMessageOwnedInOneGateAndElsewhereInTheOther_IsCountedOnce()
    {
        var (every, _) = Run("case1-two-repositories-in-one-session");
        var cx10 = every.SelectMany(one => one.Assignment.Messages).Where(placement => placement.Agent.Id == "cx" && Minutes(placement.At) == 10).ToList();

        cx10.Select(placement => placement.Association).Should().BeEquivalentTo([Association.WroteHere, Association.WroteElsewhere]);
    }

    [Fact]
    public void AssignEverySkipsRunningRoundsAndOtherCallers()
    {
        var running = AttributionCases.Case1(peerStatus: "running", peerStamp: 105).AssignEvery();
        var other = AttributionCases.Case1(peerCaller: "c9").AssignEvery();

        running.Select(one => one.Gate).Distinct().Should().Equal("A");
        other.Select(one => one.Gate).Distinct().Should().Equal("A");
        running.Should().HaveCount(3, "A's three rounds; B's is not settled");
    }

    /// <summary>
    /// The one scenario outside the identity by design (G6): when the store could not name the peers, the same activity is owned by
    /// both gates — and the second is <c>partial: prior windows unknown</c>, so the duplication is named, never silent.
    /// </summary>
    [Fact]
    public void PeersUnknown_DuplicatesAndSaysSo()
    {
        var scenario = AttributionCases.Case7().PeersUnknown();

        var first = scenario.Assign("A", "CodeReview", 1);
        var second = scenario.Assign("G", "CodeReview", 1);

        second.OwnedMessages().Intersect(first.OwnedMessages()).Should().NotBeEmpty("without the peers nothing can tell the second gate those were the first's");
        second.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Partial && state.Reasons.Contains(AttributionReasons.PriorWindowsUnknown));
    }
}
