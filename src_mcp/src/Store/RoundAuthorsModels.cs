using CoaiMcp.Core.Context;

namespace CoaiMcp.Store;

// The records of the round-authors report (E6 of todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md). Records only: no
// behaviour, no string?, no nullable collection — an absent value is NotRecorded, false or []. This file holds S1's reading shapes; the
// counting shapes of S2-S4 sit in RoundAuthorsCountModels.cs, so each file stays one job and under the family's ceiling.

/// <summary>
/// What a round's authors state says, in D3's worst-first order — <see cref="NotRecorded"/> is the worst, <see cref="Observed"/> the best.
/// The order IS the rule: the state of a round's union is the smallest value over its contributing rounds.
/// </summary>
public enum AuthorsStateKind
{
    NotRecorded,
    Unobserved,
    Partial,
    Observed,
}

/// <summary>One round of a gate session, as the report reads it: no text column ever crosses (E6.S1).</summary>
/// <param name="InWindow">Whether the round is of the asked stage and started inside <c>[since, until)</c>; the others are context for D1 and D2 only.</param>
public sealed record RoundFacts(
    long RoundId,
    string SessionId,
    string RepoPath,
    string Branch,
    string Stage,
    int Number,
    string Status,
    string Verdict,
    string StartedUtc,
    bool InWindow,
    AuthorsFacts Authors,
    SizeFacts Size,
    IReadOnlyList<ReviewerFact> Reviewers,
    FindingFacts Findings);

/// <summary>
/// The authors of one round as the projection holds them. <paramref name="CallerSessionId"/> is <c>rounds.authors_session</c> — the caller
/// D18 hands E4.S4, whose contributor identity is (caller session, agent). <paramref name="UncoveredWrites"/> is kept as stored: <c>-1</c> is
/// unknown, never clamped to <c>0</c>; <c>0</c> when the state is not recorded.
/// </summary>
public sealed record AuthorsFacts(
    AuthorsStateKind State,
    IReadOnlyList<string> Reasons,
    string CallerSessionId,
    IReadOnlyList<StoredAuthorRow> Rows,
    AmbiguousFacts Ambiguous,
    int UncoveredWrites)
{
    /// <summary>A round that recorded no authors: no rows, nothing ambiguous known, no write count.</summary>
    public static AuthorsFacts NotRecorded { get; } = new(AuthorsStateKind.NotRecorded, [], string.Empty, [], AmbiguousFacts.Unknown, 0);
}

/// <summary>
/// The ambiguous totals of a round. <paramref name="Known"/> is false when E5 wrote <c>-1</c> (an unobserved round, D17); every member then
/// reads <c>0</c> and means nothing — a figure over them is a floor, never a sum with a minus one in it.
/// </summary>
public sealed record AmbiguousFacts(
    bool Known,
    int Agents,
    int Launches,
    int Turns,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens)
{
    /// <summary>No total was measured.</summary>
    public static AmbiguousFacts Unknown { get; } = new(false, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// One <c>round_authors</c> row as stored — a store-side name, so it never collides with E4.S3's <c>AuthorRow</c>, into which D18 maps it.
/// <paramref name="Agent"/> is the COLUMN: empty for main.
/// </summary>
public sealed record StoredAuthorRow(
    int Ordinal,
    string Who,
    string Agent,
    string Requested,
    string Model,
    string Effort,
    string AgentType,
    string Association,
    string Window,
    int Launches,
    int Turns,
    int WriteCalls);

/// <summary>A round's size as <see cref="RoundChangesLog.Read"/> returns it; <paramref name="Recorded"/> is false when the round is absent from its map.</summary>
public sealed record SizeFacts(bool Recorded, RoundSize Size)
{
    /// <summary>A round with no size row: not recorded, and an empty size that must not be read.</summary>
    public static SizeFacts NotRecorded { get; } = new(false, new RoundSize(ScopeSize.Unavailable(string.Empty), ScopeSize.Unavailable(string.Empty)));
}

/// <summary>
/// One reviewer row. <paramref name="Effort"/> is the column as stored — <c>not recorded</c> (the column default), or empty for none — and
/// <paramref name="EffortRecorded"/> is false for a file older than step 22, which has no column at all (D7).
/// </summary>
public sealed record ReviewerFact(string Provider, string Model, string Effort, bool EffortRecorded, string Status);

/// <summary>
/// A round's finding counts. <c>Severe</c> is <c>findings.is_gating</c> (blocking or major, BEFORE the gate's discount); <c>GateCount</c> is
/// <c>rounds.gating</c> (after it) — never both called "gating" (D8). <paramref name="Resolved"/> is <c>rounds.accepted &lt;&gt; -1</c>.
/// </summary>
public sealed record FindingFacts(int All, int Severe, int GateCount, int ReRaised, int Accepted, int Unresolved, bool Resolved)
{
    /// <summary>A round with no finding row: every count is zero, and no key is missing.</summary>
    public static FindingFacts None(int gateCount, bool resolved) => new(0, 0, gateCount, 0, 0, 0, resolved);
}
