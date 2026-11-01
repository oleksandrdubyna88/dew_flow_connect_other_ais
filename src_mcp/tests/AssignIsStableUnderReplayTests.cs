using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Case 6 — replay. A round attributed live, right after it completes, and the same round attributed later from a store that grew
/// since must say the same thing (E4 decision 7, §S2.2b G3): lines at or after the completion, a longer journal, a later peer round
/// and a new peer gate change nothing about which items are placed, who owns them or how they are associated. A write is evidence for
/// a round only when its RESULT was written before that round completed — for this gate's own ladder, and for a claimant's ladder.
/// </summary>
/// <remarks>
/// Equality is by value, judged on the assignment's text: a record holding an <c>ImmutableArray</c> compares and prints the array by
/// reference, so <c>Equals</c> and <c>ToString()</c> on the record cannot say "the same". The one thing a new peer gate may change is
/// <c>PeerGates</c>, which exists so a backfill can see that the peer set grew.
/// </remarks>
public sealed class AssignIsStableUnderReplayTests
{
    private const string X = AttributionCases.X;
    private const string Y = AttributionCases.Y;

    private static WindowAssignment C2(AttributionScenario scenario) => scenario.Assign("A", "CodeReview", 2);

    [Fact]
    public void TwiceIsEqualByValueAndText()
    {
        var scenario = AttributionCases.Case1();

        var first = C2(scenario);
        var second = C2(scenario);

        second.AsText().Should().Be(first.AsText());
        second.Should().BeEquivalentTo(first);
        first.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public void AnExtendedJournalChangesNothing()
    {
        var extended = AttributionCases.Case1().Gate("A", X).Code(4, 240, 250);

        C2(extended).AsText().Should().Be(C2(AttributionCases.Case1()).AsText());
    }

    [Fact]
    public void ALaterPeerRoundChangesNothing()
    {
        var extended = AttributionCases.Case1().Gate("B", Y).Round("CodeReview", 2, 150, 160);

        C2(extended).AsText().Should().Be(C2(AttributionCases.Case1()).AsText(), "B C2 completes at +160, after A C2's +130, so it claims nothing");
    }

    [Fact]
    public void ANewPeerGateChangesNothing()
    {
        var extended = AttributionCases.Case1().Gate("D", "D:/z").Opened(65).Round("CodeReview", 1, 200, 210);

        C2(extended).AsTextWithoutPeerGates().Should().Be(C2(AttributionCases.Case1()).AsTextWithoutPeerGates());
        C2(extended).PeerGates.Should().Equal("B", "D");
        C2(AttributionCases.Case1()).PeerGates.Should().Equal("B");
    }

    [Fact]
    public void LinesAtOrAfterCompletionChangeNothing()
    {
        var live = AttributionCases.Case1();
        var later = AttributionCases.Case1().Message("cx", 132).Message("cx", 135).Message("cy", 140).Message("s2", 150)
            .Write("cx", 135, Y + "/b.cs", answeredAt: 136).Write("cy", 140, X + "/a.cs");

        C2(later).AsText().Should().Be(C2(live).AsText());
    }

    /// <summary>
    /// Lines in <c>[+120, +130)</c> are inert too, unless a peer's window ends there: B C1 moved to <c>125→128</c> ranks before A C2
    /// (+130) and its window <c>[+30, +125)</c> now holds +122. A cx write into y at +122 changes B's ladder for cx to two
    /// repositories — and the owner of cx +75 from A to B, whose ambiguous totals now carry it. The peer's evidence is honoured as the
    /// peer saw it (decision 7), and <c>AssignEvery</c> still owns +75 once.
    /// </summary>
    [Fact]
    public void LinesBetweenStartAndCompletionMatterOnlyThroughAPeerThatEndsThere()
    {
        var before = AttributionCases.Case1(peerStart: 125, peerStamp: 128);
        var after = AttributionCases.Case1(peerStart: 125, peerStamp: 128).Message("cx", 122).Write("cx", 122, Y + "/b.cs", answeredAt: 122);

        C2(before).Msg("cx", 75).Association.Should().Be(Association.WroteHere, "B's ladder for cx is elsewhere: only x was written");
        C2(after).Msg("cx", 75).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "B");

        var owners = after.AssignEvery().SelectMany(one => one.Assignment.Messages.Where(p => p.Agent.Id == "cx" && Minutes(p.At) == 75 && Association.IsOwning(p.Association)).Select(p => one.Gate));
        owners.Should().Equal("B");
    }

    /// <summary>(b) and (c): the same +122 write answered at +129, after B C1's completion at +128, is not evidence for B; answered at +127 it is.</summary>
    [Theory]
    [InlineData(129, Association.WroteHere)]
    [InlineData(127, Association.ClaimedByPeer)]
    public void APeersLadderSeesOnlyWritesAnsweredBeforeThePeerCompleted(double answeredAt, string expected)
    {
        var scenario = AttributionCases.Case1(peerStart: 125, peerStamp: 128).Message("cx", 122).Write("cx", 122, Y + "/b.cs", answeredAt: answeredAt);

        C2(scenario).Msg("cx", 75).Association.Should().Be(expected);
    }

    /// <summary>
    /// (a): cx's write into x at +75 answered at +131 — after A C2's completion at +130 — is not evidence: it reads the same on the
    /// "live" input (the result line not yet written, so no write) as on the "backfill" input (present), and has no write placement.
    /// Without the write, cx +75 has no evidence of its own: its window's only destination is y (cy's write), so it is ambiguous.
    /// </summary>
    [Fact]
    public void AWriteAnsweredAfterTheCompletion_IsNotEvidence_LiveOrBackfilled()
    {
        var live = AttributionCases.Case1(cx75Answered: null);
        var backfill = AttributionCases.Case1(cx75Answered: 131);

        C2(backfill).AsText().Should().Be(C2(live).AsText(), "live and backfilled agree exactly");
        C2(backfill).Msg("cx", 75).Association.Should().NotBe(Association.WroteHere);
        C2(backfill).Msg("cx", 75).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence);
        C2(backfill).Writes.Should().NotContain(write => Minutes(write.At) == 75, "a write whose result arrived after the completion is not placed");
    }

    [Fact]
    public void AWriteAnsweredJustBeforeTheCompletion_IsEvidence()
    {
        var scenario = AttributionCases.Case1(cx75Answered: 129);

        C2(scenario).Msg("cx", 75).Association.Should().Be(Association.WroteHere);
        C2(scenario).Writes.Should().Contain(write => Minutes(write.At) == 75);
    }

    [Fact]
    public void APermutedInputGivesTheSameOutput()
    {
        var input = AttributionCases.Case1().Build("A", "CodeReview", 2);
        var permuted = input with
        {
            Caller = input.Caller with
            {
                Messages = [.. input.Caller.Messages.Reverse()],
                Agents = [.. input.Caller.Agents.Reverse()],
                Writes = [.. input.Caller.Writes.Reverse()],
                Unobserved = [.. input.Caller.Unobserved.Reverse()],
            },
            Peers = [.. input.Peers.Reverse()],
            Repositories = [.. input.Repositories.Reverse()],
        };

        RoundAttribution.Assign(permuted).AsText().Should().Be(RoundAttribution.Assign(input).AsText());
    }

    [Fact]
    public void EveryArrayIsInAFixedOrder()
    {
        var assignment = AttributionCases.Case1().Assign("A", "CodeReview", 1);

        assignment.Messages.Select(p => (p.Window.Kind, p.At, p.Agent.Id)).Should().BeInAscendingOrder();
        assignment.Agents.Select(a => (a.Window.Kind, a.Agent.Id)).Should().BeInAscendingOrder();
    }
}
