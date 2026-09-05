using System.Collections.Immutable;
using CoaiMcp.Core.Findings;

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
public readonly record struct RoundContext(
    string PlanText = "",
    string HeadSha = "",
    string Caller = "",
    ImmutableArray<Finding> ReRaised = default)
{
    /// <summary>Whether this finding is one the caller had already rejected.</summary>
    /// <remarks>
    /// Matched by identity of what it SAYS rather than by reference: the discounted list holds the
    /// same finding objects the merged list holds today, and a comparison that quietly depends on
    /// that would break the day one of them is rebuilt from JSON.
    /// </remarks>
    public bool WasReRaised(Finding finding) =>
        !ReRaised.IsDefaultOrEmpty
        && ReRaised.Any(other =>
            string.Equals(other.Title, finding.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(other.File, finding.File, StringComparison.OrdinalIgnoreCase));

}
