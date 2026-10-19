using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// The agent a message, a launch or a write belongs to: <c>main</c>, or the agent id taken from the transcript's file name.
/// E5 maps <see cref="MainId"/> to the parent's empty <c>agent</c> column in ONE place (E4 §S2.2a, E5 §8.2).
/// </summary>
public readonly record struct AgentKey(string Id)
{
    public const string MainId = "main";

    public static AgentKey Main => new(MainId);

    /// <summary>A computed property is not a stored copy of the rule: a serialised key has exactly one member, <c>id</c>.</summary>
    [JsonIgnore]
    public bool IsMain => Id == MainId;
}

/// <summary>The four token terms of one message, as the reader's per-term maximum over the id's lines.</summary>
public readonly record struct TokenTerms(long Input, long Output, long CacheCreation, long CacheRead);

/// <summary>The one closed spelling of an effort nobody wrote down. Never <c>high</c>, never the session's.</summary>
public static class Effort
{
    public const string Unknown = "unknown";

    /// <summary>The effort as the reader gave it; an empty one is not known.</summary>
    public static string Of(string raw) => raw.Length == 0 ? Unknown : raw;
}

/// <summary>The three states of an observation, and of a window.</summary>
public static class ObservationKinds
{
    public const string Observed = "observed";
    public const string Partial = "partial";
    public const string Unobserved = "unobserved";
}

/// <summary>One message id with ONE instant — its earliest line — and the model that ANSWERED it.</summary>
public sealed record ObservedMessage(
    AgentKey Agent,
    string MessageId,
    DateTimeOffset At,
    string Model,
    string Requested,
    string Effort,
    TokenTerms Tokens);

/// <summary>
/// A subagent (or <c>main</c>, which no rule reads from here). <paramref name="FirstLineAt"/> is null ONLY for a
/// <paramref name="TranscriptPresent"/> = false agent whose launching call E3 did not parse; such an agent is inert (§S2.2b G4).
/// </summary>
public sealed record ObservedAgent(
    AgentKey Key,
    AgentKey Parent,
    string AgentType,
    string Requested,
    DateTimeOffset? FirstLineAt,
    bool TranscriptPresent);

/// <summary>Where a write landed; 1:1 with E3's write-target kinds.</summary>
public enum WriteKind
{
    Checkout,
    OutsideAnyCheckout,
    Unresolved,
}

/// <summary>
/// <paramref name="At"/> is its MESSAGE's instant; <paramref name="AnsweredAt"/> is the instant of the <c>tool_result</c> line that
/// answered it — the horizon of §S2.4. <paramref name="Where"/> is a normalised root KEY, never a file path.
/// </summary>
public sealed record ObservedWrite(AgentKey Agent, DateTimeOffset At, DateTimeOffset AnsweredAt, WriteKind Kind, string Where);

/// <summary>E3's unobserved launches, one for one: the ONLY source of unobserved-launch items and their reasons (§S2.2b G4).</summary>
public sealed record UnobservedLaunch(AgentKey Parent, string ToolUseId, DateTimeOffset? At, string Why);

/// <summary>A reason the observation lost something. <paramref name="At"/> null = the whole session.</summary>
public sealed record StateReason(string Why, DateTimeOffset? At);

/// <summary><c>observed</c> | <c>partial</c> | <c>unobserved</c>, with the reasons E3 gave.</summary>
public sealed record ObservationState(string Kind, ImmutableArray<StateReason> Reasons);

/// <summary>What ONE caller session did, in the shape attribution reads — filled by the one mapping, <c>AttributionMapping</c>.</summary>
public sealed record CallerObservation(
    string CallerSessionId,
    ObservationState State,
    ImmutableArray<ObservedMessage> Messages,
    ImmutableArray<ObservedAgent> Agents,
    ImmutableArray<ObservedWrite> Writes,
    ImmutableArray<UnobservedLaunch> Unobserved);

/// <summary>
/// One row of the gate session's journal. <paramref name="CompletedUtc"/> is READ ONLY WHEN <paramref name="Status"/> is
/// <c>done</c> (§S2.2b G1): a running row carries a stamp too, the time of its last progress tick.
/// </summary>
public sealed record GateRound(
    string Stage,
    int Number,
    DateTimeOffset? StartedUtc,
    string Status,
    string Verdict,
    string CallerSessionId,
    DateTimeOffset? CompletedUtc);

/// <summary>A gate session: its identity, its repository, when it opened, and its journal in the order the rounds ran.</summary>
public sealed record GateTimeline(string GateSessionId, string RepoPath, DateTimeOffset? OpenedUtc, ImmutableArray<GateRound> Rounds);

/// <summary>Everything one call of <c>RoundAttribution.Assign</c> reads.</summary>
public sealed record AttributionInput(
    CallerObservation Caller,
    GateTimeline Gate,
    ImmutableArray<GateTimeline> Peers,
    ImmutableArray<string> Repositories,
    string Stage,
    int Number,
    bool PeersKnown)
{
    /// <summary>The contract violations of this input, in a fixed order; empty when it is well formed.</summary>
    public ImmutableArray<string> Validate() => AttributionContract.Violations(this);
}
