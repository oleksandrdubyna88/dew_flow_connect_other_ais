using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The live record against the backfill, on real data (E4.S5, brief 4). A round is attributed live right after it completes, from a
/// transcript that ends there; a backfill reads the whole file. Placement, ownership, association and state must be the same — and the one
/// stated limitation, the tokens of a message still being written at the completion, is pinned beside it: D8 takes the maximum over EVERY
/// line of an id, D6 places it by its FIRST line.
/// </summary>
/// <remarks>
/// <c>c01</c> and <c>c14</c> have activity after the start of their only round. To show the NEXT window on real data the test adds a round
/// 2 in memory — its start one synthetic second after the case's last instant, its completion one second after that. No fixture file
/// changes. Each materialisation is its own temporary folder, so two are compared with that root named <c>&lt;root&gt;</c>.
/// </remarks>
public sealed class AttributionOfARecordedRoundIsStableUnderReplayTests
{
    private static Func<IReadOnlyList<CoaiMcp.Server.RoundRecord>, IReadOnlyList<CoaiMcp.Server.RoundRecord>> WithExtension(DateTime last) =>
        rounds => [.. rounds, AttributionFixtureLoader.Extension(rounds, last)];

    private static DateTime LastInstantOf(string dir)
    {
        using var recorded = RecordedSession.Open(dir);

        return recorded.LastInstant;
    }

    private static DateTime CompletionOfRoundOne(string dir)
    {
        using var recorded = RecordedSession.Open(dir);

        return recorded.Timelines[0].Rounds[0].CompletedUtc!.Value.UtcDateTime;
    }

    private static DateTime StartOfRoundOne(string dir)
    {
        using var recorded = RecordedSession.Open(dir);

        return recorded.Timelines[0].Rounds[0].StartedUtc!.Value.UtcDateTime;
    }

    [Theory]
    [InlineData("c01")]
    [InlineData("c14")]
    public void TheLiveRecord_EqualsTheBackfill(string dir)
    {
        var completion = CompletionOfRoundOne(dir);
        using var live = RecordedSession.Open(dir, truncateAfter: _ => completion);
        using var full = RecordedSession.Open(dir);
        var gate = full.Timelines[0].GateSessionId;

        live.Text(live.Assign(gate, 1)).Should().Be(full.Text(full.Assign(gate, 1)), $"{dir}: placements, owners, states and tokens");
        live.Text(live.Authors(gate, 1)).Should().Be(full.Text(full.Authors(gate, 1)), $"{dir}: rows and totals");
    }

    /// <summary>
    /// Cut at the START of round 1, the same message ids are placed with the same owners — but the message whose lines straddle the start has
    /// LOWER tokens, because its later lines are gone. This is D8 beside D6, the one stated limitation of the evidence horizon.
    /// </summary>
    [Fact]
    public void CutAtTheStart_PlacesTheSameMessages_ButTheStraddlersTokensAreLower()
    {
        var start = StartOfRoundOne("c01");
        using var cut = RecordedSession.Open("c01", truncateAfter: _ => start);
        using var full = RecordedSession.Open("c01");
        var gate = full.Timelines[0].GateSessionId;

        var shortAssignment = cut.Assign(gate, 1);
        var longAssignment = full.Assign(gate, 1);

        shortAssignment.Messages.Select(p => (p.MessageId, p.Association, p.OwnerGate)).Should().Equal(longAssignment.Messages.Select(p => (p.MessageId, p.Association, p.OwnerGate)), "placement and ownership never change");
        var pairs = shortAssignment.Messages.Zip(longAssignment.Messages).ToList();
        pairs.Should().OnlyContain(pair => pair.First.Tokens.Output <= pair.Second.Tokens.Output && pair.First.Tokens.Input <= pair.Second.Tokens.Input);
        pairs.Should().Contain(pair => pair.First.Tokens != pair.Second.Tokens, "a message with lines on both sides of the start is the whole point of the case");
    }

    /// <summary>The negative control: cut BEFORE the straddler's first line and a message is no longer placed — the placement fact has teeth.</summary>
    [Fact]
    public void CutBeforeTheStraddlersFirstLine_LosesThePlacement()
    {
        using var cut = RecordedSession.Open("c01", truncateAfter: fixture => fixture.StampOf(TranscriptFile.Main, 1));
        using var full = RecordedSession.Open("c01");
        var gate = full.Timelines[0].GateSessionId;

        cut.Assign(gate, 1).Messages.Length.Should().BeLessThan(full.Assign(gate, 1).Messages.Length);
    }

    [Fact]
    public void C01WithTheExtension_HoldsTheAgentsTwoLaterMessagesAndNoLaunch()
    {
        var last = LastInstantOf("c01");
        using var recorded = RecordedSession.Open("c01", WithExtension(last));
        var gate = recorded.Timelines[0].GateSessionId;

        var second = recorded.Assign(gate, 2);
        var first = recorded.Assign(gate, 1);

        second.Messages.Should().HaveCount(2).And.OnlyContain(placement => !placement.Agent.IsMain && placement.Association == Association.Ambiguous);
        second.Launches.Should().BeEmpty();
        first.Launches.Should().ContainSingle();
        recorded.Rounds().Select(round => recorded.Assign(round.Gate, round.Number)).SelectMany(assignment => assignment.Messages).Select(p => p.MessageId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void C14WithTheExtension_OneLaunchOverBothWindows_AndTheAssociationFollowsTheWindow()
    {
        var last = LastInstantOf("c14");
        using var recorded = RecordedSession.Open("c14", WithExtension(last));
        var gate = recorded.Timelines[0].GateSessionId;

        var first = recorded.Assign(gate, 1);
        var second = recorded.Assign(gate, 2);

        (first.Launches.Length + second.Launches.Length).Should().Be(1);
        first.Messages.Where(p => !p.Agent.IsMain).Select(p => p.Association).Should().OnlyContain(association => association == Association.WroteHere);
        second.Messages.Should().NotBeEmpty().And.OnlyContain(placement => placement.Association == Association.Ambiguous, "no write falls in the second window");
    }

    [Fact]
    public void C13_AttributedTwice_IsEqualByValueAndText()
    {
        using var recorded = RecordedSession.Open("c13", shape: rounds => [rounds[0]]);
        var gate = recorded.Timelines[0].GateSessionId;

        recorded.Text(recorded.Assign(gate, 1)).Should().Be(recorded.Text(recorded.Assign(gate, 1)));
        recorded.Assign(gate, 1).OwnedMessages().Should().HaveCount(1);
    }
}
