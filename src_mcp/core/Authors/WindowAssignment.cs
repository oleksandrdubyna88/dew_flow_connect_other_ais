using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>The closed vocabulary of an association (E4 §S2.5): what one agent's activity in one window is credited to.</summary>
public static class Association
{
    public const string WroteHere = "wrote here";
    public const string ChildOf = "child of";
    public const string SameSession = "same-session";
    public const string Ambiguous = "ambiguous";
    public const string WroteElsewhere = "wrote elsewhere";
    public const string ClaimedByPeer = "claimed by peer";

    /// <summary>Owning: the activity is this gate's own. <c>wrote elsewhere</c> and <c>claimed by peer</c> are labels, not ownership.</summary>
    public static bool IsOwning(string association) => association is WroteHere or ChildOf or SameSession or Ambiguous;
}

/// <summary>Which rung of the ladder decided an association.</summary>
public static class Rung
{
    public const string Write = "write";
    public const string TwoRepositories = "two repositories";
    public const string Child = "child";
    public const string Session = "session";
    public const string NoEvidence = "no evidence";
}

/// <summary>Where a placed write landed, from this gate's point of view.</summary>
public static class WriteWhere
{
    public const string Here = "here";
    public const string Elsewhere = "elsewhere";
    public const string Outside = "outside";
}

/// <summary>The window kinds: the stretch before the gate session opened, and a round's own stretch.</summary>
public static class WindowKind
{
    public const string BeforeOpen = "before-open";
    public const string Round = "round";
}

/// <summary>
/// The closed set of reasons E4 itself gives (§S2.3 <i>States</i>). Every other reason is E3's constant, copied verbatim and never
/// composed. NO reason contains <c>", "</c>: E5 joins them with exactly that.
/// </summary>
public static class AttributionReasons
{
    public const string NoRoundStart = "no round start";
    public const string NoOpenTime = "no open time";
    public const string PriorWindowsUnknown = "prior windows unknown";
    public const string CallerChanged = "caller changed";
    public const string PriorCallerUnknown = "prior caller unknown";
    public const string PeerRunning = "peer running";
}

/// <summary>
/// One half-open stretch <c>[From, To)</c> of one gate session's journal. <paramref name="Blind"/> windows have no edge and take part
/// in nothing. <paramref name="CompletedUtc"/> is the round's completion only when it is <c>done</c> — null otherwise, so a window
/// that is not settled ranks last whatever stamp its row carries (§S2.2b G1).
/// </summary>
public sealed record Window(
    string Gate,
    string Kind,
    string Stage,
    int Number,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string CallerSessionId,
    DateTimeOffset? CompletedUtc,
    bool Blind)
{
    public bool Contains(DateTimeOffset at) => !Blind && (From is null || From <= at) && at < To;
}

/// <summary>One message placed in a window, with its association, rung and owner (the gate, the peer, or empty when <c>wrote elsewhere</c>).</summary>
public sealed record Placement(
    string MessageId,
    AgentKey Agent,
    DateTimeOffset At,
    Window Window,
    string Association,
    string Rung,
    string OwnerGate,
    string Model,
    string Requested,
    string Effort,
    TokenTerms Tokens);

public sealed record LaunchPlacement(AgentKey Agent, DateTimeOffset At, Window Window, string Association, string Rung, string OwnerGate);

/// <summary><paramref name="Where"/> is <see cref="WriteWhere"/>; <paramref name="Root"/> is the repository root the write resolved to (empty when outside).</summary>
public sealed record WritePlacement(AgentKey Agent, DateTimeOffset At, Window Window, string Root, string Where);

public sealed record UnobservedLaunchPlacement(AgentKey Parent, DateTimeOffset At, Window Window, string Why);

/// <summary>An agent's association in one window — the ladder's verdict, which a two-repositories agent's row is built from.</summary>
public sealed record AgentAssociation(AgentKey Agent, Window Window, string Association, string Rung, ImmutableArray<string> ElsewhereRoots);

/// <summary>A window's state: <c>observed</c> | <c>partial</c> | <c>unobserved</c>, with the reasons, distinct, in first-seen order.</summary>
public sealed record WindowState(Window Window, string Kind, ImmutableArray<string> Reasons);

/// <summary>
/// What one call of <c>RoundAttribution.Assign</c> says about ONE round: every placement of its windows, the agents' associations
/// and the window states. Every array is ordered ordinally, so equal inputs give equal outputs and a permuted input gives the same
/// output (§S2.5). <paramref name="PeerGates"/> are the gate session ids this assignment saw, so a backfill can tell a round whose
/// peer set grew (§S2.2b G3 iii).
/// </summary>
public sealed record WindowAssignment(
    string Gate,
    string Stage,
    int Number,
    string CallerSessionId,
    ImmutableArray<Window> Windows,
    ImmutableArray<Placement> Messages,
    ImmutableArray<LaunchPlacement> Launches,
    ImmutableArray<WritePlacement> Writes,
    ImmutableArray<UnobservedLaunchPlacement> Unobserved,
    ImmutableArray<AgentAssociation> Agents,
    ImmutableArray<WindowState> States,
    DateTimeOffset? ReadFrom,
    ImmutableArray<string> PeerGates)
{
    /// <summary>
    /// Identity 1 — local completeness (§S2.5): per non-blind window the messages, launches and writes the input holds are EXACTLY
    /// the ones placed, each once, each inside its window. Returns the violations; empty when it holds.
    /// </summary>
    public ImmutableArray<string> LocalCompleteness(AttributionInput input) => CompletenessCheck.Violations(this, input);
}
