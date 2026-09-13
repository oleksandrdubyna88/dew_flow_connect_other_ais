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
/// <param name="CalledBy">
/// WHICH AI that caller is, and which model it declared — issue #174.
/// </param>
/// <remarks>
/// <para><paramref name="Caller"/> is the calling agent's own session id, which is what keys its
/// split order; <paramref name="CalledBy"/> is who that agent is. Null here rather than a default
/// instance because <c>default(RoundContext)</c> runs no field initialiser at all — the same trap
/// the string fields already carry a comment about, and the reason
/// <see cref="Server.CallerDeclaration"/> is coalesced where it is written rather than here.</para>
/// </remarks>
public readonly record struct RoundContext(
    string PlanText = "",
    string HeadSha = "",
    string Caller = "",
    ImmutableArray<Finding> ReRaised = default,
    string AgentLog = "",
    Server.CallerDeclaration? CalledBy = null)
{
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
