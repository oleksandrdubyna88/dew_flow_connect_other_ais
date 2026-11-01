using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Ownership of contested activity (E4 decision 3, completion rank): where two gate sessions of one caller both see a message, the
/// SETTLED window whose round completed first owns it, and the other labels it <c>claimed by peer</c>. Case 1 of §S2.6, end to end,
/// and the three ways a peer is no claimant (another caller, still running, running with a stamp on disk — §S2.2b G1, G3).
/// </summary>
public sealed class TheEarlierCompletedWindowClaimsTests
{
    private static WindowAssignment A(int round, AttributionScenario? scenario = null) => (scenario ?? AttributionCases.Case1()).Assign("A", "CodeReview", round);

    private static WindowAssignment B(AttributionScenario? scenario = null) => (scenario ?? AttributionCases.Case1()).Assign("B", "CodeReview", 1);

    [Fact]
    public void CxIsHereInAAndElsewhereInB()
    {
        A(1).Labelled(Association.WroteHere).Should().Equal("cx@10", "cx@40", "cx@50");
        A(2).Msg("cx", 75).Association.Should().Be(Association.WroteHere);
        A(3).Msg("cx", 130).Association.Should().Be(Association.WroteHere);
        B().Labelled(Association.WroteElsewhere).Should().Equal("cx@10", "cx@40", "cx@50", "cx@75");
    }

    [Fact]
    public void CyIsTheMirror()
    {
        A(1).Msg("cy", 35).Association.Should().Be(Association.WroteElsewhere);
        A(2).Msg("cy", 90).Association.Should().Be(Association.WroteElsewhere);
        B().Labelled(Association.WroteHere).Should().Equal("cy@35", "cy@90");
    }

    [Fact]
    public void S1IsAmbiguousInExactlyOneWindow()
    {
        A(1).Msg("s1", 45).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.OwnerGate == "A" && p.Rung == Rung.NoEvidence);
        B().Msg("s1", 45).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "A");
    }

    [Fact]
    public void S2IsClaimedByPeerInAAndOwnedByB()
    {
        A(2).Msg("s2", 80).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "B");
        B().Msg("s2", 80).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.OwnerGate == "B");
    }

    [Fact]
    public void S3IsSameSessionInAAndAbsentFromB()
    {
        A(3).Msg("s3", 140).Association.Should().Be(Association.SameSession);
        B().Messages.Should().NotContain(placement => placement.Agent.Id == "s3");
    }

    /// <summary>The own ladder says <c>wrote elsewhere</c>: elsewhere is this gate's own evidence, and claimants are not consulted.</summary>
    [Fact]
    public void AnElsewhereMessageIsLabelledElsewhereNotClaimed() =>
        A(2).Msg("cy", 90).Should().Match<Placement>(p => p.Association == Association.WroteElsewhere && p.OwnerGate.Length == 0);

    [Fact]
    public void TheBeforeOpenWindowOfBClaimsNothingFromAOnlyTheOtherWayRound()
    {
        B().Labelled(Association.ClaimedByPeer).Should().Equal("main@-5", "main@20", "s1@45");
        A(1).OwnedMessages().Should().Equal("main@-5", "cx@10", "main@20", "cx@40", "s1@45", "cx@50");
    }

    [Fact]
    public void APeerWindowOfAnotherCaller_ClaimsNothing()
    {
        var other = AttributionCases.Case1(peerCaller: "c9");

        A(2, other).Msg("s2", 80).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.OwnerGate == "A");
    }

    [Fact]
    public void APeerStillRunning_ClaimsNothing_AndItsLaterFateChangesOnlyTheState()
    {
        var running = A(2, AttributionCases.Case1(peerStatus: "running", peerStamp: 105));
        var interrupted = A(2, AttributionCases.Case1(peerStatus: "interrupted", peerStamp: 105));
        var done = A(2, AttributionCases.Case1(peerStatus: "done", peerStamp: 110));

        running.Msg("s2", 80).Association.Should().Be(Association.Ambiguous);
        running.OwnedMessages().Should().Equal(interrupted.OwnedMessages(), "interrupting the running peer moves no ownership");
        running.RoundState().Kind.Should().Be(ObservationKinds.Partial);
        interrupted.RoundState().Kind.Should().Be(ObservationKinds.Observed, "once the peer is final the provisional reason is gone");
        done.Msg("s2", 80).Association.Should().Be(Association.ClaimedByPeer, "settling it at +110 is what E7's recomputation of a peer-running row records");
    }

    /// <summary>
    /// G1: the running row on disk CARRIES a stamp (its last progress tick). Rank by the stamp and the running peer would rank before
    /// A C2 (+105 against +130) and claim s2 — a claim from a round that has not completed.
    /// </summary>
    [Fact]
    public void ARunningPeerWithAStamp_RanksLast()
    {
        var assignment = A(2, AttributionCases.Case1(peerStatus: "running", peerStamp: 105));

        assignment.Msg("s2", 80).Association.Should().NotBe(Association.ClaimedByPeer);
        assignment.Msg("s2", 80).OwnerGate.Should().Be("A");
    }

    [Fact]
    public void AWindowOverlappedByARunningPeer_IsPartialPeerRunning()
    {
        var scenario = AttributionCases.Case1(peerStatus: "running", peerStamp: 105);

        A(2, scenario).RoundState().Should().Match<WindowState>(state => state.Kind == ObservationKinds.Partial && state.Reasons.SequenceEqual(new[] { AttributionReasons.PeerRunning }));
        A(3, scenario).RoundState().Kind.Should().Be(ObservationKinds.Observed, "B's window ends at +100; A C3 starts at +120");
    }

    [Fact]
    public void ASettledOrAnotherCallersPeer_IsNotPeerRunning()
    {
        A(2).RoundState().Reasons.Should().BeEmpty();
        A(2, AttributionCases.Case1(peerCaller: "c9")).RoundState().Reasons.Should().BeEmpty("a window of another caller overlaps and says nothing about this caller's transcript");
    }

    [Theory]
    [InlineData("G1", "G2")]
    [InlineData("G2", "G1")]
    public void TiesBreakByGateIdOrdinal(string declaredFirst, string declaredSecond)
    {
        var scenario = AttributionScenario.For().Gate(declaredFirst, AttributionCases.X).Opened(0).Code(1, 60, 70)
            .Gate(declaredSecond, AttributionCases.X).Opened(0).Code(1, 60, 70).Main(10);

        scenario.Assign("G1", "CodeReview", 1).Msg("main", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.OwnerGate == "G1");
        scenario.Assign("G2", "CodeReview", 1).Msg("main", 10).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "G1");
    }

    [Fact]
    public void ALaterCompletingPeerNeverClaimsWhatAnEarlierOneOwns()
    {
        // B completes at +110 and A C1 at +70: B never claims what A owns, whatever the order the gates were declared in.
        A(1).Labelled(Association.ClaimedByPeer).Should().BeEmpty();
    }
}
