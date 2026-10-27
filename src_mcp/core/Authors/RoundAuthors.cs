using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// One row of the authors of a round: ONE agent, in ONE window, on ONE answering model, one request, one effort and one association
/// (E4.S3). Per AGENT, not per family, so contributors deduplicate on it and "3 Sonnet coders" counts distinct agents.
/// FINAL: E5 persists it as it is (E4 §S2.2a); a member change is an E4 document edit announced to E5 first.
/// </summary>
/// <param name="Window"><c>before-open</c> | <c>round</c>.</param>
/// <param name="Who"><c>main</c> | <c>subagent</c>.</param>
/// <param name="Association"><c>wrote here</c> | <c>child of</c> | <c>same-session</c> — ambiguous and elsewhere activity get no row.</param>
public sealed record AuthorRow(
    string Window,
    string Stage,
    int Number,
    string Who,
    AgentKey Agent,
    string AgentType,
    string Requested,
    string Model,
    string Effort,
    string Association,
    int Launches,
    int Turns,
    TokenTerms Tokens,
    int WriteCalls);

/// <summary>
/// What a set of items adds up to. <see cref="Unknown"/> — every member −1 — is a count nothing measured, never a zero.
/// FINAL, like <see cref="AuthorRow"/>.
/// </summary>
public sealed record ActivityTotals(int Agents, int Launches, int Turns, TokenTerms Tokens)
{
    public static ActivityTotals Zero { get; } = new(0, 0, 0, new TokenTerms(0, 0, 0, 0));

    public static ActivityTotals Unknown { get; } = new(-1, -1, -1, new TokenTerms(-1, -1, -1, -1));

    /// <summary>A computed property is not a stored copy of the rule: it is <c>[JsonIgnore]</c>, as <see cref="AgentKey.IsMain"/> is, so the totals a session file keeps have exactly their four members (E5 §8 item 3).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUnknown => Agents < 0;
}

/// <summary>
/// One window of the round with its OWN state and totals (§S2.2b G9) — empty windows included — so a report that leaves
/// <c>before-open</c> out can still say what the windows it keeps were. FINAL (E4 §11, L1).
/// </summary>
public sealed record WindowAuthors(
    string Window,
    string State,
    ImmutableArray<string> Reasons,
    ActivityTotals Ambiguous,
    ActivityTotals Elsewhere,
    ActivityTotals ClaimedByPeer,
    int UnobservedLaunches);

/// <summary>Writes into a checkout other than the gate's own (a submodule, a nested worktree): in memory only — E5 stores counts, never the root.</summary>
public sealed record CheckoutWrites(string Root, int Calls);

/// <summary>
/// What one round's authors are: the rows, the totals of what is never guessed into a row, the state and, per window, the same.
/// The round-level members are the roll-up: the WORST window state, reasons distinct in window order, totals summed — and
/// <see cref="ActivityTotals.Unknown"/> (with <paramref name="UnobservedLaunches"/> = −1) when any window is unobserved. An unobserved
/// launch with no instant marks EVERY window's count, but the roll-up counts it once: one missing transcript is one item.
/// </summary>
public sealed record RoundAuthors(
    string Stage,
    int Number,
    string CallerSessionId,
    string State,
    ImmutableArray<string> Reasons,
    ImmutableArray<AuthorRow> Rows,
    ActivityTotals Ambiguous,
    ActivityTotals Elsewhere,
    ActivityTotals ClaimedByPeer,
    int UnobservedLaunches,
    ImmutableArray<WindowAuthors> Windows,
    ImmutableArray<string> PeerGates,
    ImmutableArray<CheckoutWrites> WritesOutsideTheGateCheckout);
