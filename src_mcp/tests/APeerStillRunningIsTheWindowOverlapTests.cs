using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// E7's two confirmations (E7 §10 items 8 and 9), shown by code: (9) "a round of a named peer still runs" is E4 §S2.3's WINDOW OVERLAP —
/// a settled-or-not peer window of THIS caller, not blind, unsettled, that overlaps one of this round's windows — and it is the same
/// test that sets <c>partial: peer running</c>, so <see cref="RoundAttribution.PeerStillRuns"/> and the state cannot disagree; (8)
/// <c>PeersKnown</c> on the input is "the lookup completed": false means the peers were not named, none is read, every window says
/// <c>prior windows unknown</c> and no peer is recorded in <c>PeerGates</c>.
/// </summary>
public sealed class APeerStillRunningIsTheWindowOverlapTests
{
    private static bool Runs(AttributionScenario scenario, string gate, int number) => RoundAttribution.PeerStillRuns(scenario.Build(gate, "CodeReview", number));

    [Fact]
    public void ARunningPeerThatOverlapsTheRoundsWindow_StillRuns()
    {
        var scenario = AttributionCases.Case1(peerStatus: "running", peerStamp: 105);

        Runs(scenario, "A", 2).Should().BeTrue("B's window [+30,+100) overlaps A C2's [+60,+120)");
        Runs(scenario, "A", 1).Should().BeTrue("and A C1's");
    }

    [Fact]
    public void ARunningPeerThatDoesNotOverlap_DoesNotRun() =>
        Runs(AttributionCases.Case1(peerStatus: "running", peerStamp: 105), "A", 3).Should().BeFalse("A C3 starts at +120; B's window ends at +100");

    [Fact]
    public void ASettledPeer_NeverStillRuns() => Runs(AttributionCases.Case1(), "A", 2).Should().BeFalse();

    [Fact]
    public void AnInterruptedPeer_IsFinalAndNeverStillRuns() =>
        Runs(AttributionCases.Case1(peerStatus: "interrupted", peerStamp: 105), "A", 2).Should().BeFalse("an interrupted round opens no window");

    [Fact]
    public void ARunningPeerOfAnotherCaller_NeverStillRuns() =>
        Runs(AttributionCases.Case1(peerCaller: "c9", peerStatus: "running", peerStamp: 105), "A", 2).Should().BeFalse("its window says nothing about this caller's transcript");

    [Theory]
    [MemberData(nameof(Cases))]
    public void ThePredicateAndTheStateAreTheSameTest(string name)
    {
        var scenario = AttributionCases.Named(name);

        foreach (var one in scenario.AssignEvery())
        {
            var says = one.Assignment.States.Any(state => state.Reasons.Contains(AttributionReasons.PeerRunning));

            Runs(scenario, one.Gate, one.Number).Should().Be(says, $"{name}: {one.Gate} round {one.Number}");
        }
    }

    public static TheoryData<string> Cases => AttributionCases.Names;

    [Fact]
    public void WhenThePeersAreNotKnown_NoneIsReadWhateverTheInputHolds()
    {
        var known = AttributionCases.Case1().Build("A", "CodeReview", 2);
        var unknown = known with { PeersKnown = false };

        var assignment = RoundAttribution.Assign(unknown);

        assignment.PeerGates.Should().BeEmpty();
        assignment.Messages.Should().OnlyContain(placement => placement.Association != Association.ClaimedByPeer);
        assignment.States.Should().OnlyContain(state => state.Reasons.SequenceEqual(new[] { AttributionReasons.PriorWindowsUnknown }));
        RoundAttribution.PeerStillRuns(unknown).Should().BeFalse();
    }
}
