using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// E4.S5: the guarantees of S2–S4 on the sanitised transcripts E1 cut from real sessions, through E3's real reader and the production
/// mapping — so a disagreement between what E3 emits and what E4 assumes (a field spelling, an instant, the <c>toolUseId</c> join, a path
/// spelling) fails HERE, by case name, and not in the report. One theory row per round of a recorded case; every cell is a hand-read
/// (<see cref="RecordedCell"/>).
/// </summary>
/// <remarks>
/// The positive half runs first (testing.md, A fixture the code REJECTS proves nothing): the reader returned the state the case is named
/// for, a non-zero message count (but for c10), and every answered write under a checkout resolved to a <c>Checkout</c> on THIS host.
/// CI runs this on Linux, macOS and Windows: a host that reads a re-pointed path differently must fail by case name, never as a wrong cell.
/// </remarks>
public sealed class AttributionHoldsOnTheRecordedSessionsTests
{
    public static TheoryData<string> Cells => RecordedCell.Keys;

    [Theory]
    [MemberData(nameof(Cells))]
    public void TheRecordedCell_IsWhatTheSessionSays(string key)
    {
        var cell = RecordedCell.Find(key);
        using var recorded = RecordedSession.Open(cell.Case);
        var gate = recorded.Timelines[cell.Gate].GateSessionId;

        // the positive half
        recorded.Session.State.Render().Should().Be(cell.Reader, key);
        (recorded.Session.Messages.Count > 0).Should().Be(cell.Case != "c10", $"{key}: the reader found messages");
        recorded.Session.Writes.Count(write => write.Target.Kind == WriteTargetKind.Checkout).Should().Be(cell.CheckoutWrites, $"{key}: answered writes under a checkout resolve to one on this host");
        recorded.Input(gate, cell.Number).Validate().Should().BeEmpty(key);

        var assignment = recorded.Assign(gate, cell.Number);
        var authors = recorded.Authors(gate, cell.Number);

        authors.State.Should().Be(cell.State, key);
        string.Join(", ", authors.Reasons).Should().Be(cell.Reasons, key);
        authors.Rows.Select(Row).Should().Equal(cell.Rows, key);
        (authors.Ambiguous.Agents, authors.Ambiguous.Launches, authors.Ambiguous.Turns).Should().Be(cell.Ambiguous, key);
        authors.Elsewhere.Turns.Should().Be(cell.ElsewhereTurns, key);
        authors.ClaimedByPeer.Turns.Should().Be(cell.ClaimedTurns, key);
        authors.UnobservedLaunches.Should().Be(cell.Unobserved, key);
        assignment.Messages.Select(placement => $"{placement.Window.Kind}:{placement.Association}/{placement.Rung}").Should().Equal(cell.Labels, key);
        authors.WritesOutsideTheGateCheckout.Should().HaveCount(cell.NestedCheckoutWrites, key);
    }

    private static string Row(AuthorRow row) =>
        $"{row.Window}|{row.Who}|{row.Model}|{row.Effort}|{row.Association}|L{row.Launches}|T{row.Turns}|W{row.WriteCalls}";

    /// <summary>c19: the submodule write is <c>wrote here</c> and listed apart; the scratchpad write is outside and in no row.</summary>
    [Fact]
    public void ASubmoduleWriteIsHereAndListed_TheScratchpadWriteIsOutside()
    {
        using var recorded = RecordedSession.Open("c19");
        var gate = recorded.Timelines[0].GateSessionId;

        var authors = recorded.Authors(gate, 1);
        var assignment = recorded.Assign(gate, 1);

        authors.WritesOutsideTheGateCheckout.Should().ContainSingle().Which.Root.Should().EndWith("p1000");
        assignment.Writes.Select(write => write.Where).Should().BeEquivalentTo([WriteWhere.Here, WriteWhere.Outside]);
    }

    /// <summary>c10: no transcript — nothing placed, no rows, unknown totals, no contributors, and the round is named as not seen.</summary>
    [Fact]
    public void APersistenceOffSession_IsNeverAZero()
    {
        using var recorded = RecordedSession.Open("c10");
        var gate = recorded.Timelines[0].GateSessionId;

        var authors = recorded.Authors(gate, 1);
        var contributors = Contributors.Of([authors], withBeforeOpen: true);

        recorded.Assign(gate, 1).Messages.Should().BeEmpty();
        authors.Ambiguous.Should().Be(ActivityTotals.Unknown);
        contributors.Items.Should().BeEmpty();
        contributors.NotSeen.Should().Equal(new NotSeen("CodeReview", 1, "unobserved: transcript not found"));
    }

    /// <summary>c18: work before open is the before-open window's — left out of the contributors unless asked.</summary>
    [Fact]
    public void WorkBeforeOpen_IsLeftOutOfTheContributorsUnlessAsked()
    {
        using var recorded = RecordedSession.Open("c18");
        var authors = recorded.Authors(recorded.Timelines[0].GateSessionId, 1);

        Contributors.Of([authors], withBeforeOpen: false).Items.Should().BeEmpty();
        Contributors.Of([authors], withBeforeOpen: true).Items.Should().ContainSingle().Which.Agent.IsMain.Should().BeTrue();
    }

    /// <summary>c14: the agent that wrote here and main (same-session) are the two contributors of round one; the agent's launch is on its row.</summary>
    [Fact]
    public void ARecordedCoderAndMain_AreTheContributors()
    {
        using var recorded = RecordedSession.Open("c14");
        var authors = recorded.Authors(recorded.Timelines[0].GateSessionId, 1);

        var set = Contributors.Of([authors], withBeforeOpen: false);

        set.Items.Select(item => (item.Who, item.Models.Single(), item.WriteCalls)).Should().Equal(("main", "claude-opus-5-5", 0), ("subagent", "claude-sonnet-5-5", 1));
    }

    /// <summary>c13: the journal lists round 1 twice — the contract names it, and a journal with ONE copy attributes.</summary>
    [Fact]
    public void ARoundListedTwice_IsNamedByTheContract_AndOneCopyAttributes()
    {
        using var twice = RecordedSession.Open("c13");
        using var once = RecordedSession.Open("c13", shape: rounds => [rounds[0]]);
        var gate = twice.Timelines[0].GateSessionId;

        twice.Input(gate, 1).Validate().Should().ContainSingle(violation => violation.Contains("listed twice", StringComparison.Ordinal));
        once.Input(gate, 1).Validate().Should().BeEmpty();
        once.Assign(gate, 1).OwnedMessages().Should().HaveCount(1, "main's one message");
    }

    /// <summary>The window of c16's third round begins at the FIRST round's start: the gap in the numbering opens no window.</summary>
    [Fact]
    public void ANumberingGap_OpensNoWindow()
    {
        using var recorded = RecordedSession.Open("c16");
        var gate = recorded.Timelines[0].GateSessionId;

        var window = recorded.Assign(gate, 3).RoundWindow();

        window.From.Should().Be(recorded.Timelines[0].Rounds.First(round => round.Number == 1).StartedUtc);
        window.To.Should().Be(recorded.Timelines[0].Rounds.First(round => round.Number == 3).StartedUtc);
    }

    /// <summary>c15: the second gate session's before-open window starts at the first one's round start, and the owned set is the same without that read bound.</summary>
    [Fact]
    public void ASecondGateOnTheSameCaller_StartsAtThePriorEnd_AndItIsOnlyAReadBound()
    {
        using var recorded = RecordedSession.Open("c15");
        var first = recorded.Timelines[0];
        var second = recorded.Timelines[1];

        var withBound = recorded.Assign(second.GateSessionId, 1);
        var without = RoundAttribution.Assign(recorded.Input(second.GateSessionId, 1), usePriorEnd: false);

        withBound.BeforeOpenWindow().From.Should().Be(first.Rounds[0].StartedUtc);
        withBound.Messages.Should().ContainSingle(placement => placement.Window.Kind == WindowKind.Round);
        without.OwnedMessages().Should().Equal(withBound.OwnedMessages());
        recorded.Assign(first.GateSessionId, 1).OwnedMessages().Should().BeEmpty("gate one owns nothing");
    }
}
