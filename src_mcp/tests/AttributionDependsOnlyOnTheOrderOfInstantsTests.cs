using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Instants are an ORDER, not a time (E4 §2): every rule compares them and none subtracts them — no tolerance, no duration. E1's fixtures
/// depend on this, because its sanitiser redraws every instant as a rank, one second apart from <c>2000-01-01</c>, ties kept. This is the
/// proof that such fixtures are valid inputs: every worked example, with each instant replaced by its rank on that clock, gives the same
/// placements, owners, states and rows — apart from the instants themselves.
/// </summary>
public sealed class AttributionDependsOnlyOnTheOrderOfInstantsTests
{
    private static readonly DateTimeOffset RankBase = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Cases => AttributionCases.Names;

    [Theory]
    [MemberData(nameof(Cases))]
    public void ARankClockGivesTheSameAttribution(string name)
    {
        var scenario = AttributionCases.Named(name);
        var every = scenario.AssignEvery();

        every.Should().NotBeEmpty(name);
        foreach (var one in every)
        {
            var input = scenario.Build(one.Gate, one.Stage, one.Number);
            Signature(OnARankClock(input)).Should().Equal(Signature(input), $"{name}: {one.Gate} {one.Stage} {one.Number}");
        }
    }

    [Fact]
    public void TheRankClockReallyMovesEveryInstant()
    {
        var input = AttributionCases.Case1().Build("A", "CodeReview", 2);
        var ranked = OnARankClock(input);

        ranked.Caller.Messages.Select(message => message.At).Should().OnlyContain(at => at.Year == 2000);
        ranked.Gate.Rounds.Select(round => round.StartedUtc!.Value).Should().OnlyContain(at => at.Year == 2000);
        ranked.Caller.Messages.Select(message => message.At).Distinct().Count().Should().Be(input.Caller.Messages.Select(message => message.At).Distinct().Count(), "ties are kept and nothing merges");
    }

    /// <summary>What an attribution says apart from its instants: every label, owner, state and row, in order.</summary>
    private static IReadOnlyList<string> Signature(AttributionInput input)
    {
        var assignment = RoundAttribution.Assign(input);
        var authors = RoundAuthorsBuilder.Build(input, assignment);

        return
        [
            .. assignment.Windows.Select(window => $"window {window.Kind} {window.Stage} {window.Number} blind={window.Blind} unbounded={window.From is null}"),
            .. assignment.Messages.Select(placement => $"msg {placement.MessageId} {placement.Agent.Id} {placement.Window.Kind} {placement.Association} {placement.Rung} {placement.OwnerGate} {placement.Model} {placement.Tokens}"),
            .. assignment.Launches.Select(launch => $"launch {launch.Agent.Id} {launch.Window.Kind} {launch.Association} {launch.Rung} {launch.OwnerGate}"),
            .. assignment.Writes.Select(write => $"write {write.Agent.Id} {write.Window.Kind} {write.Root} {write.Where}"),
            .. assignment.Unobserved.Select(item => $"unobserved {item.Parent.Id} {item.Window.Kind} {item.Why}"),
            .. assignment.Agents.Select(agent => $"agent {agent.Agent.Id} {agent.Window.Kind} {agent.Association} {agent.Rung} {string.Join('|', agent.ElsewhereRoots)}"),
            .. assignment.States.Select(state => $"state {state.Window.Kind} {state.Kind} {string.Join('|', state.Reasons)}"),
            $"readFrom null={assignment.ReadFrom is null} peers={string.Join('|', assignment.PeerGates)}",
            .. authors.Rows.Select(row => $"row {row.Window}|{row.Who}|{row.Model}|{row.Effort}|{row.Association}|L{row.Launches}|T{row.Turns}|W{row.WriteCalls}|{row.Tokens}"),
            $"totals {authors.Ambiguous}|{authors.Elsewhere}|{authors.ClaimedByPeer}|{authors.UnobservedLaunches}|{authors.State}|{string.Join('|', authors.Reasons)}",
        ];
    }

    /// <summary>Every instant of the input replaced by its rank among the input's distinct instants: strictly increasing, ties kept.</summary>
    private static AttributionInput OnARankClock(AttributionInput input)
    {
        var instants = InstantsOf(input).Distinct().Order().ToList();
        DateTimeOffset Rank(DateTimeOffset at) => RankBase.AddSeconds(instants.IndexOf(at));
        DateTimeOffset? RankOf(DateTimeOffset? at) => at is { } instant ? Rank(instant) : null;
        GateTimeline Timeline(GateTimeline timeline) => timeline with
        {
            OpenedUtc = RankOf(timeline.OpenedUtc),
            Rounds = [.. timeline.Rounds.Select(round => round with { StartedUtc = RankOf(round.StartedUtc), CompletedUtc = RankOf(round.CompletedUtc) })],
        };

        return input with
        {
            Caller = input.Caller with
            {
                State = input.Caller.State with { Reasons = [.. input.Caller.State.Reasons.Select(reason => reason with { At = RankOf(reason.At) })] },
                Messages = [.. input.Caller.Messages.Select(message => message with { At = Rank(message.At) })],
                Agents = [.. input.Caller.Agents.Select(agent => agent with { FirstLineAt = RankOf(agent.FirstLineAt) })],
                Writes = [.. input.Caller.Writes.Select(write => write with { At = Rank(write.At), AnsweredAt = Rank(write.AnsweredAt) })],
                Unobserved = [.. input.Caller.Unobserved.Select(item => item with { At = RankOf(item.At) })],
            },
            Gate = Timeline(input.Gate),
            Peers = [.. input.Peers.Select(Timeline)],
        };
    }

    private static IEnumerable<DateTimeOffset> InstantsOf(AttributionInput input)
    {
        var timelines = new[] { input.Gate }.Concat(input.Peers).ToList();

        return input.Caller.Messages.Select(message => message.At)
            .Concat(input.Caller.Writes.SelectMany(write => new[] { write.At, write.AnsweredAt }))
            .Concat(input.Caller.Agents.Select(agent => agent.FirstLineAt).OfType<DateTimeOffset>())
            .Concat(input.Caller.Unobserved.Select(item => item.At).OfType<DateTimeOffset>())
            .Concat(input.Caller.State.Reasons.Select(reason => reason.At).OfType<DateTimeOffset>())
            .Concat(timelines.Select(timeline => timeline.OpenedUtc).OfType<DateTimeOffset>())
            .Concat(timelines.SelectMany(timeline => timeline.Rounds).SelectMany(round => new[] { round.StartedUtc, round.CompletedUtc }).OfType<DateTimeOffset>());
    }
}
