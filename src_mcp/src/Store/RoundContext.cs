using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;

namespace CoaiMcp.Store;

/// <summary>
/// What a round was ABOUT, recorded beside what it found.
/// </summary>
/// <remarks>
/// <para>The counts and the verdict say a round happened. They cannot answer the question the
/// operator actually asked of this data on 2026-09-05: <i>what does the AI writing this code
/// habitually miss?</i> For that, a finding has to be readable against the thing it was about — the
/// scope the caller stated, the commit the reviewers actually read, and which caller it was.</para>
/// <para><b>And which findings are not new.</b> The gate discounts a finding that repeats one the
/// caller already rejected with a reason. A first disagreement is one thing; the same objection
/// raised again by a different reviewer, over a rejection that still stands, is a different and far
/// more interesting thing — so it is recorded rather than merely discounted.</para>
/// <para>A struct with defaults, so a caller that has none of this still records the round.</para>
/// </remarks>
/// <param name="PlanText">The scope the caller sent — what this change was supposed to achieve.</param>
/// <param name="HeadSha">The commit the reviewers read, pinned for the round.</param>
/// <param name="Caller">Which agent session drove the gate, as the server knows it.</param>
/// <param name="ReRaised">Findings the gate discounted as repeats of a standing rejection.</param>
/// <param name="AgentLog">
/// What the caller was doing in the stretch this round closes, from its own transcript — JSON, or
/// empty. See <see cref="Store.AgentLog"/> for the shape and the trimming.
/// </param>
/// <remarks>
/// <para><paramref name="Caller"/> is the calling agent's own session id, which is what keys its
/// split order. WHICH AI that agent is — its vendor, client and declared model (issue #174) — is
/// NOT here: it lives on <see cref="Server.RoundRecord.Caller"/>, which <c>RecordRound</c> already
/// receives. A copy here as well would be a second source of truth for one fact, and a caller that
/// filled one and not the other would write an unknown vendor over a perfectly good declaration.
/// Raised by gemini on the second code round of that change.</para>
/// </remarks>
public readonly record struct RoundContext(
    string PlanText = "",
    string HeadSha = "",
    string Caller = "",
    ImmutableArray<Finding> ReRaised = default,
    string AgentLog = "")
{
    /// <summary>What the diff the reviewers read was actually compared against.</summary>
    /// <remarks>
    /// <para><see cref="HeadSha"/> names one end of a range. Without the other end a code round's
    /// diff cannot be rebuilt from this store at all — and the base is unrecoverable the moment the
    /// round is over, because nothing else on the machine remembers what it was.</para>
    /// <para>The RESOLVED base, not the ref the caller asked for. The two differ whenever the merge
    /// base is used instead of the ref itself — a case the round already warns its reviewers about
    /// in as many words — and it is the resolved one that makes the diff reconstructible.</para>
    /// <para>An init property rather than a constructor parameter, for the same reason
    /// <see cref="Finding.Role"/> is one: every existing call site keeps working, and a round
    /// recorded by an older build simply has none.</para>
    /// </remarks>
    public string BaseRef { get; init; } = string.Empty;

    /// <summary>Whether this finding is one the caller had already rejected.</summary>
    /// <remarks>
    /// Through the product's OWN rule for "the same defect" — same category, same file, lines within
    /// five, and as much wording overlap as those coordinates leave necessary. The first version
    /// compared titles and file names by hand, which was a second matching rule for a question this
    /// codebase had already answered once; the gate named it and it was right.
    /// </remarks>
    public bool WasReRaised(Finding finding) =>
        !ReRaised.IsDefaultOrEmpty && ReRaised.Any(other => FindingDedup.SameDefect(other, finding));

}
