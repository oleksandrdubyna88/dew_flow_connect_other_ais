using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>before-open</c> window (E4 cases 7, 8c and 8d): the work before a gate session opened belongs to its first recorded round,
/// from the end of the SETTLED PREFIX of the other gate sessions of the same repository and caller (the prior end), which is a READ
/// bound — the owned set is the same without it — and which stops, never skips, at a round that is not settled (§S2.2b G2).
/// </summary>
public sealed class BeforeOpenWorkBelongsToTheFirstRoundOnlyTests
{
    [Fact]
    public void WorkBeforeOpen_IsTheBeforeOpenWindowOfRoundOne()
    {
        var assignment = AttributionCases.Case7().Assign("A", "CodeReview", 1);

        assignment.BeforeOpenWindow().Bounds().Should().Be(Span(null, 30));
        assignment.RoundWindow().Bounds().Should().Be(Span(30, 60));
        assignment.Messages.Where(p => p.Window.Kind == WindowKind.BeforeOpen).Select(Key).Should().Equal("main@-5", "main@10");
        assignment.Messages.Where(p => p.Window.Kind == WindowKind.Round).Select(Key).Should().Equal("main@40", "main@45");
        assignment.OwnedMessages().Should().HaveCount(4);
        assignment.Messages.Should().OnlyContain(p => p.Association == Association.WroteHere);
    }

    [Fact]
    public void RoundTwo_HasNoBeforeOpenWindow() =>
        AttributionCases.Case1().Assign("A", "CodeReview", 2).Windows.Should().OnlyContain(window => window.Kind == WindowKind.Round);

    [Fact]
    public void ASecondGateOnTheSameRepo_StartsAtThePriorEnd()
    {
        var scenario = AttributionCases.Case7();
        var assignment = scenario.Assign("G", "CodeReview", 1);

        assignment.BeforeOpenWindow().Bounds().Should().Be(Span(60, 200), "the end of A's settled prefix is A C1's start");
        assignment.RoundWindow().Bounds().Should().Be(Span(200, 250));
        assignment.OwnedMessages().Should().Equal("main@100", "main@220");
        assignment.ReadFrom.Should().Be(AttributionScenario.At(60));
    }

    /// <summary>
    /// Without a prior end G's before-open window is <c>[∅, +200)</c>: A's four messages are inside it, A's windows rank first and own
    /// them, so G labels them <c>claimed by peer</c> — and the OWNED set of G is identical. The bound only saves reading.
    /// </summary>
    [Fact]
    public void ThePriorEndIsAReadBoundNotAnOwnershipRule()
    {
        var withBound = AttributionCases.Case7().Assign("G", "CodeReview", 1);
        var without = AttributionCases.Case7().WithoutPriorEnd().Assign("G", "CodeReview", 1);

        without.BeforeOpenWindow().Bounds().Should().Be(Span(null, 200));
        without.OwnedMessages().Should().Equal(withBound.OwnedMessages());
        without.Labelled(Association.ClaimedByPeer).Should().Equal("main@-5", "main@10", "main@40", "main@45");
        without.ReadFrom.Should().BeNull("the bound is what made it a number");
    }

    [Fact]
    public void PeersUnknown_IsPartialPriorWindowsUnknown()
    {
        var assignment = AttributionCases.Case7().PeersUnknown().Assign("G", "CodeReview", 1);

        assignment.States.Should().OnlyContain(state => state.Kind == ObservationKinds.Partial && state.Reasons.SequenceEqual(new[] { AttributionReasons.PriorWindowsUnknown }));
        assignment.BeforeOpenWindow().Bounds().Should().Be(Span(null, 200));
        assignment.PeerGates.Should().BeEmpty();
    }

    /// <summary>
    /// Case 8c: A's only round is still running, so A's settled prefix is empty and there is no prior end. G's before-open window
    /// is <c>[∅, +100)</c> and owns +30 — under the old rule (the maximum start over every recorded round, +60) it would be
    /// <c>[+60, +100)</c> and +30 would be owned by nobody with the bound and by G without it.
    /// </summary>
    [Fact]
    public void APriorEndStopsAtTheFirstUnsettledPeerRound()
    {
        var withBound = AttributionCases.Case8c().Assign("G", "CodeReview", 1);
        var without = AttributionCases.Case8c().WithoutPriorEnd().Assign("G", "CodeReview", 1);

        withBound.BeforeOpenWindow().Bounds().Should().Be(Span(null, 100));
        withBound.ReadFrom.Should().BeNull();
        withBound.OwnedMessages().Should().Equal("main@30");
        without.OwnedMessages().Should().Equal(withBound.OwnedMessages(), "the equality of case 7 holds BECAUSE the walk stopped");
        withBound.StateOf(withBound.BeforeOpenWindow()).Should().Match<WindowState>(s => s.Kind == ObservationKinds.Partial && s.Reasons.SequenceEqual(new[] { AttributionReasons.PeerRunning }));
    }

    [Fact]
    public void WhenThatPeerRoundIsLaterInterrupted_OnlyTheStateChanges()
    {
        var running = AttributionCases.Case8c().Assign("G", "CodeReview", 1);
        var interrupted = AttributionCases.Case8c(aStatus: "interrupted").Assign("G", "CodeReview", 1);

        interrupted.OwnedMessages().Should().Equal(running.OwnedMessages());
        interrupted.StateOf(interrupted.BeforeOpenWindow()).Kind.Should().Be(ObservationKinds.Observed);
    }

    /// <summary>
    /// A done at +150 ranks AFTER G's +140, so G keeps +30 — and a prior end that counted A's round would take +30 away from G and
    /// leave it with A, which completed later: the one difference that would make the bound an ownership rule. A's own assignment
    /// labels +30 <c>claimed by peer (G)</c>.
    /// </summary>
    [Fact]
    public void WhenThatPeerRoundSettlesAfterG_GKeepsTheMessageAndAClaimsNothing()
    {
        var scenario = AttributionCases.Case8c(aStatus: "done", aStamp: 150);

        var g = scenario.Assign("G", "CodeReview", 1);
        var a = scenario.Assign("A", "CodeReview", 1);

        g.BeforeOpenWindow().Bounds().Should().Be(Span(null, 100), "a round that completes after this one never bounds it");
        g.OwnedMessages().Should().Equal("main@30");
        g.StateOf(g.BeforeOpenWindow()).Kind.Should().Be(ObservationKinds.Observed);
        a.Msg("main", 30).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "G");
    }

    /// <summary>
    /// Case 8d: the walk takes A C1 (start +60) and stops at the running A C2. A message at +70 is in A C2's window, which is not
    /// settled, so G owns it and says so; a message at +50 is in A C1's window and G never reads it — yet without the bound it labels
    /// it <c>claimed by peer (A)</c>, and the owned set is equal either way.
    /// </summary>
    [Fact]
    public void ASettledPrefixEndsBeforeTheRunningRound()
    {
        var withBound = AttributionCases.Case8d().Assign("G", "CodeReview", 1);
        var without = AttributionCases.Case8d().WithoutPriorEnd().Assign("G", "CodeReview", 1);

        withBound.BeforeOpenWindow().Bounds().Should().Be(Span(60, 100));
        withBound.ReadFrom.Should().Be(AttributionScenario.At(60));
        withBound.OwnedMessages().Should().Equal("main@70");
        withBound.StateOf(withBound.BeforeOpenWindow()).Reasons.Should().Equal(AttributionReasons.PeerRunning);
        without.OwnedMessages().Should().Equal(withBound.OwnedMessages());
        without.Msg("main", 50).Should().Match<Placement>(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "A");
    }
}
