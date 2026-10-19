using System.Collections.Immutable;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using Authors = CoaiMcp.Core.Authors;
using CallerObservation = CoaiMcp.Core.Authors.CallerObservation;
using GateTimeline = CoaiMcp.Core.Authors.GateTimeline;
using ObservationKinds = CoaiMcp.Core.Authors.ObservationKinds;
using StateReason = CoaiMcp.Core.Authors.StateReason;
using TokenTerms = CoaiMcp.Core.Authors.TokenTerms;
using WriteKind = CoaiMcp.Core.Authors.WriteKind;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>AttributionMapping</c> is the ONE mapping from the reader's <see cref="ObservedSession"/> and the journal's <see cref="RoundRecord"/>
/// to the core's input records (E4.S5, decision B1), and it decides nothing: each rule here is a copy, a closed word or an instant
/// through <see cref="UtcInstant"/>. E5's call site and E7's backfill call it and write no mapping of their own, so a field spelling E3
/// changes fails here by name.
/// </summary>
public sealed class TheMappingDecidesNothingTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static ObservedAgent MainAgent() => new("main", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true);

    private static ObservedSession Session(ObservationState state, IReadOnlyList<ObservedAgent>? agents = null, IReadOnlyList<ObservedWrite>? writes = null, IReadOnlyList<UnobservedLaunch>? unobserved = null) =>
        new(new CallerSessionId("sess", "stored"), state, Start, agents ?? [MainAgent()], [], writes ?? [], unobserved ?? [], ReadDiagnostics.None);

    private static RoundRecord Record(string status, int number = 1) =>
        new("CodeReview", number, "proceed", 0, "all 1 reviewers answered", Start.AddMinutes(10)) { Status = status, StartedUtc = Start };

    private static GateTimeline Timeline(params RoundRecord[] rounds) => AttributionMapping.Timeline("g", "D:/x", Start.AddMinutes(-5), rounds, _ => "c0");

    [Fact]
    public void ARunningRoundsStamp_IsNotItsCompletion()
    {
        var timeline = Timeline(Record(RoundRecord.Running, 1), Record(RoundRecord.Done, 2));

        timeline.Rounds[0].CompletedUtc.Should().BeNull("the running row's stamp is its last progress tick, which ranks nothing");
        timeline.Rounds[1].CompletedUtc.Should().Be(new DateTimeOffset(Start.AddMinutes(10)));
    }

    [Fact]
    public void TheTwoTranscriptMissingCodes_AreNeverStateReasons()
    {
        var state = ObservationState.Partial([
            new ObservationReason(ObservationReasons.AgentCallWithoutTranscript, 1),
            new ObservationReason(ObservationReasons.MetaWithoutTranscript, 2),
            new ObservationReason(ObservationReasons.ReadBudget, 1),
        ]);

        var observation = AttributionMapping.Observation(Session(state));

        observation.State.Reasons.Should().Equal(new StateReason("read budget", null));
        observation.State.Kind.Should().Be(ObservationKinds.Partial);
    }

    [Fact]
    public void EveryOtherReasonArrivesWithoutAnInstant()
    {
        var codes = new[] { ObservationReasons.TornLine, ObservationReasons.TornMeta, ObservationReasons.LineTooLong, ObservationReasons.ReadFailed, ObservationReasons.InconsistentTranscript, ObservationReasons.AgentWithoutMeta };
        var state = ObservationState.Partial([.. codes.Select(code => new ObservationReason(code, 1))]);

        var reasons = AttributionMapping.Observation(Session(state)).State.Reasons;

        reasons.Select(reason => reason.Why).Should().Equal(codes);
        reasons.Should().OnlyContain(reason => reason.At == null);
    }

    [Fact]
    public void AnUnobservedSessionKeepsItsWordAndItsReason()
    {
        var observation = AttributionMapping.Observation(Session(ObservationState.Unobserved(ObservationReasons.TranscriptNotFound)));

        observation.State.Kind.Should().Be(ObservationKinds.Unobserved);
        observation.State.Reasons.Should().Equal(new StateReason("transcript not found", null));
    }

    [Fact]
    public void AMetaWithoutAFileAndNoCall_HasNoFirstLine()
    {
        var agents = new[] { MainAgent(), new ObservedAgent("gone", "main", "toolu_1", "general-purpose", string.Empty, null, null, false) };

        AttributionMapping.Observation(Session(ObservationState.Observed, agents)).Agents.Single().FirstLineAt.Should().BeNull();
    }

    [Fact]
    public void AMetaWithoutAFileButAParsedCall_HasTheCallsInstant()
    {
        var agents = new[] { MainAgent(), new ObservedAgent("gone", "main", "toolu_1", "general-purpose", string.Empty, null, Start.AddMinutes(3), false) };

        var agent = AttributionMapping.Observation(Session(ObservationState.Observed, agents)).Agents.Single();

        agent.FirstLineAt.Should().Be(new DateTimeOffset(Start.AddMinutes(3)));
        agent.TranscriptPresent.Should().BeFalse();
    }

    [Fact]
    public void APresentAgent_HasItsFirstLineAndNotTheCall()
    {
        var agents = new[] { MainAgent(), new ObservedAgent("a1", "main", "toolu_1", "Explore", "sonnet", Start.AddMinutes(2), Start.AddMinutes(1), true) };

        var agent = AttributionMapping.Observation(Session(ObservationState.Observed, agents)).Agents.Single();

        agent.Should().Match<Authors.ObservedAgent>(a => a.FirstLineAt == new DateTimeOffset(Start.AddMinutes(2)) && a.TranscriptPresent && a.AgentType == "Explore" && a.Requested == "sonnet" && a.Parent.IsMain);
    }

    [Fact]
    public void MainIsNeverListedAsAnAgent() =>
        AttributionMapping.Observation(Session(ObservationState.Observed)).Agents.Should().BeEmpty("every rule reads main as implicit; E3 gives it no launch");

    [Fact]
    public void AnsweredAtIsTheResultLinesInstant()
    {
        var write = new ObservedWrite("main", Start.AddMinutes(1), Start.AddMinutes(4), "Edit", new WriteTarget(WriteTargetKind.Checkout, "d:/x"));

        var mapped = AttributionMapping.Observation(Session(ObservationState.Observed, writes: [write])).Writes.Single();

        mapped.Should().Match<Authors.ObservedWrite>(w => w.At == new DateTimeOffset(Start.AddMinutes(1)) && w.AnsweredAt == new DateTimeOffset(Start.AddMinutes(4)) && w.Kind == WriteKind.Checkout && w.Where == "d:/x");
    }

    [Theory]
    [InlineData(WriteTargetKind.Checkout, WriteKind.Checkout)]
    [InlineData(WriteTargetKind.OutsideAnyCheckout, WriteKind.OutsideAnyCheckout)]
    [InlineData(WriteTargetKind.Unresolved, WriteKind.Unresolved)]
    public void EveryWriteKindMapsOneToOne(WriteTargetKind from, WriteKind to)
    {
        var write = new ObservedWrite("main", Start, Start, "Edit", new WriteTarget(from, string.Empty));

        AttributionMapping.Observation(Session(ObservationState.Observed, writes: [write])).Writes.Single().Kind.Should().Be(to);
    }

    [Fact]
    public void TokenTermsAreMappedTermByTerm()
    {
        var session = Session(ObservationState.Observed) with
        {
            Messages = [new ObservedMessage("main", "m1", Start, "opus", "claude-opus-5-5", "high", new TokenUsage(Input: 1, CacheCreation: 2, CacheRead: 3, Output: 4), true)],
        };

        AttributionMapping.Observation(session).Messages.Single().Tokens.Should().Be(new TokenTerms(Input: 1, Output: 4, CacheCreation: 2, CacheRead: 3));
    }

    [Fact]
    public void UnobservedLaunchesArriveOneForOne()
    {
        var launches = new[] { new UnobservedLaunch("main", "toolu_1", Start, "agent call without transcript"), new UnobservedLaunch("a1", "toolu_2", null, "meta without transcript") };

        var mapped = AttributionMapping.Observation(Session(ObservationState.Observed, unobserved: launches)).Unobserved;

        mapped.Select(item => (item.Parent.Id, item.ToolUseId, item.At.HasValue, item.Why)).Should().Equal(("main", "toolu_1", true, "agent call without transcript"), ("a1", "toolu_2", false, "meta without transcript"));
    }

    [Fact]
    public void EveryInstantIsUtcWithZeroOffset()
    {
        var unspecified = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);
        var timeline = AttributionMapping.Timeline("g", "D:/x", unspecified, [Record(RoundRecord.Done) with { CompletedUtc = unspecified.AddMinutes(10) }], _ => "c0");

        timeline.OpenedUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        timeline.Rounds[0].StartedUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        timeline.OpenedUtc.Should().Be(new DateTimeOffset(Start));
    }

    [Fact]
    public void AMessageAndAWriteOfLocalKind_AreReadAsTheInstantTheyName()
    {
        var local = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Local);
        var write = new ObservedWrite("main", local, local.AddMinutes(1), "Edit", new WriteTarget(WriteTargetKind.Checkout, "d:/x"));
        var session = Session(ObservationState.Observed, writes: [write]) with
        {
            Messages = [new ObservedMessage("main", "m1", local, "opus", "claude-opus-5-5", "high", new TokenUsage(Input: 1, CacheCreation: 0, CacheRead: 0, Output: 1), true)],
        };

        var observation = AttributionMapping.Observation(session);

        observation.Messages.Single().At.Should().Be(new DateTimeOffset(local.ToUniversalTime()), "a local-kind instant is converted, never relabelled as UTC");
        observation.Writes.Single().At.Should().Be(new DateTimeOffset(local.ToUniversalTime()));
        observation.Writes.Single().AnsweredAt.Should().Be(new DateTimeOffset(local.AddMinutes(1).ToUniversalTime()));
    }

    [Fact]
    public void ARoundWithNoRecordedStart_HasNone()
    {
        var round = Record(RoundRecord.Done) with { StartedUtc = default };

        Timeline(round).Rounds[0].StartedUtc.Should().BeNull("default means not recorded and never an instant");
    }

    [Fact]
    public void TheJournalOrderIsKept() =>
        Timeline(Record("done", 3), Record("done", 1), Record("done", 2)).Rounds.Select(round => round.Number).Should().Equal(3, 1, 2);

    [Fact]
    public void TheCallersRuleSaysWhoServedEachRound()
    {
        var timeline = AttributionMapping.Timeline("g", "D:/x", Start, [Record("done", 1), Record("done", 2)], round => "c" + round.Number);

        timeline.Rounds.Select(round => round.CallerSessionId).Should().Equal("c1", "c2");
        timeline.Rounds.Select(round => (round.Stage, round.Status, round.Verdict)).Should().OnlyContain(row => row.Stage == "CodeReview" && row.Status == "done" && row.Verdict == "proceed");
    }

    /// <summary>L6: each spelling arrives as it is — no Windows/WSL translation — and no root is dropped, but one root in two spellings is one.</summary>
    [Fact]
    public void TheUniverseKeepsEachSpellingAndDropsNoRoot()
    {
        var gate = new GateTimeline("g", "D:/x", null, []);
        var peers = new[] { new GateTimeline("p", "/mnt/d/y", null, []) }.ToImmutableArray();
        var writes = new[]
        {
            new Authors.ObservedWrite(Authors.AgentKey.Main, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WriteKind.Checkout, "d:/x"),
            new Authors.ObservedWrite(Authors.AgentKey.Main, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WriteKind.Checkout, "/mnt/d/y/sub"),
            new Authors.ObservedWrite(Authors.AgentKey.Main, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WriteKind.OutsideAnyCheckout, string.Empty),
            new Authors.ObservedWrite(Authors.AgentKey.Main, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WriteKind.Unresolved, "d:/gone"),
        };
        var observation = new CallerObservation("c0", new Authors.ObservationState(ObservationKinds.Observed, []), [], [], [.. writes], []);

        AttributionMapping.Universe(gate, peers, observation).Should().Equal("/mnt/d/y", "/mnt/d/y/sub", "D:/x");
    }
}
