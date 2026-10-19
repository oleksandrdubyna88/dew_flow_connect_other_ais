using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using Authors = CoaiMcp.Core.Authors;

namespace CoaiMcp.Store;

/// <summary>
/// THE one mapping from what the reader saw (E3's <see cref="ObservedSession"/>) and what the gate session recorded
/// (<see cref="RoundRecord"/>) to the pure core's <see cref="AttributionInput"/> records (E4.S5, decision B1). It decides NOTHING: every
/// member is a copy, a closed-vocabulary word or an instant through <see cref="UtcInstant"/>. E5's call site and E7's backfill call it;
/// neither writes a mapping of its own, and there is no second reader.
/// </summary>
/// <remarks>
/// It lives beside <c>ObservedSession.cs</c> because this is the only place that sees both E3's <c>src/Store</c> records and E4's core
/// records: E3 cannot build it (E4's types do not exist when E3 ships) and E5 is built after E4.
/// </remarks>
internal static class AttributionMapping
{
    /// <summary>
    /// The observation as attribution reads it. <c>main</c> is never listed as an agent (E3 gives it no launch and every rule reads it as
    /// implicit). <c>FirstLineAt</c> is the agent file's first line, else its launching call — null only for an agent with no transcript
    /// (inert, §S2.2b G4). Every E3 reason becomes a <see cref="StateReason"/> with no instant (G5) EXCEPT the two transcript-missing
    /// codes, whose instants ride on the unobserved launches (L8) — so a missing child marks only ITS window.
    /// </summary>
    public static CallerObservation Observation(ObservedSession session) => new(
        session.Session.Id,
        new Authors.ObservationState(KindOf(session.State.Kind), [.. session.State.Reasons.Where(IsStateReason).Select(reason => new StateReason(reason.Code, null))]),
        [.. session.Messages.Select(Message)],
        [.. session.Agents.Where(agent => agent.Key != Authors.AgentKey.MainId).Select(Agent)],
        [.. session.Writes.Select(Write)],
        [.. session.UnobservedLaunches.Select(Launch)]);

    /// <summary>
    /// The journal of one gate session, rounds in the order given (the order they ran). <c>StartedUtc</c> and <c>OpenedUtc</c> through
    /// <see cref="UtcInstant"/> (<c>default</c> is not recorded); the completion ONLY when the round is <c>done</c> — a running row
    /// carries a stamp too, its last progress tick, which ranks nothing (G1). <paramref name="callerOf"/> is the caller's rule: which
    /// session served a given round.
    /// </summary>
    public static GateTimeline Timeline(string gateSessionId, string repoPath, DateTime openedUtc, IReadOnlyList<RoundRecord> rounds, Func<RoundRecord, string> callerOf) =>
        new(gateSessionId, repoPath, UtcInstant.From(openedUtc), [.. rounds.Select(round => Round(round, callerOf))]);

    /// <summary>
    /// The universe repository membership is decided against (decision 6): the gate's and every peer's <c>repo_path</c> and every
    /// checkout root the reader resolved for a write, each in the spelling it arrives in — no Windows/WSL translation — distinct under
    /// normalisation and case, in ordinal order.
    /// </summary>
    public static ImmutableArray<string> Universe(GateTimeline gate, ImmutableArray<GateTimeline> peers, CallerObservation observation) =>
    [
        .. new[] { gate.RepoPath }.Concat(peers.Select(peer => peer.RepoPath)).Concat(observation.Writes.Where(write => write.Kind == WriteKind.Checkout).Select(write => write.Where))
            .Where(root => root.Length > 0).Order(StringComparer.Ordinal).DistinctBy(Repositories.Normalise, StringComparer.OrdinalIgnoreCase),
    ];

    private static string KindOf(ObservationKind kind) => kind switch
    {
        ObservationKind.Observed => ObservationKinds.Observed,
        ObservationKind.Partial => ObservationKinds.Partial,
        _ => ObservationKinds.Unobserved,
    };

    private static bool IsStateReason(ObservationReason reason) =>
        reason.Code != ObservationReasons.AgentCallWithoutTranscript && reason.Code != ObservationReasons.MetaWithoutTranscript;

    private static Authors.ObservedMessage Message(ObservedMessage message) => new(
        new Authors.AgentKey(message.AgentKey),
        message.MessageId,
        new DateTimeOffset(DateTime.SpecifyKind(message.Utc, DateTimeKind.Utc)),
        message.Model,
        message.Requested,
        message.Effort,
        new TokenTerms(message.Tokens.Input, message.Tokens.Output, message.Tokens.CacheCreation, message.Tokens.CacheRead));

    private static Authors.ObservedAgent Agent(ObservedAgent agent) => new(
        new Authors.AgentKey(agent.Key),
        new Authors.AgentKey(agent.ParentKey),
        agent.AgentType,
        agent.RequestedAlias,
        UtcInstant.From(agent.LaunchedUtc ?? agent.CalledUtc),
        agent.TranscriptPresent);

    private static Authors.ObservedWrite Write(ObservedWrite write) => new(
        new Authors.AgentKey(write.AgentKey),
        new DateTimeOffset(DateTime.SpecifyKind(write.Utc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(write.AnsweredUtc, DateTimeKind.Utc)),
        write.Target.Kind switch
        {
            WriteTargetKind.Checkout => WriteKind.Checkout,
            WriteTargetKind.OutsideAnyCheckout => WriteKind.OutsideAnyCheckout,
            _ => WriteKind.Unresolved,
        },
        write.Target.Where);

    private static Authors.UnobservedLaunch Launch(UnobservedLaunch launch) =>
        new(new Authors.AgentKey(launch.ParentKey), launch.ToolUseId, UtcInstant.From(launch.Utc), launch.Reason);

    private static GateRound Round(RoundRecord round, Func<RoundRecord, string> callerOf) => new(
        round.Stage,
        round.Number,
        UtcInstant.From(round.StartedUtc),
        round.Status,
        round.Verdict,
        callerOf(round),
        round.Status == RoundRecord.Done ? UtcInstant.From(round.CompletedUtc) : null);
}
