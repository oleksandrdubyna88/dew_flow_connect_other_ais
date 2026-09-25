using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>What a round will run, and any role it decided not to ask for.</summary>
/// <remarks>
/// The two travel together because the decision is made where the roles are chosen and the sentence
/// is written where the round ends, and nothing carried the fact across the gap before — which is
/// how a dropped role reached the server's log and never the caller.
/// </remarks>
/// <param name="Excluded">
/// Vendors this round could not give a particular role to. It exists because the exclusion is
/// discovered while the work is BUILT rather than before it: a Team server accepts the roles it was
/// compiled with, so a role a person defined is refused per (vendor, role) and not per vendor.
/// </param>
internal sealed record RoundWork(
    IReadOnlyList<ReviewerWork> Reviewers,
    IReadOnlyList<SkippedRole> NotAsked,
    IReadOnlyList<ExcludedRole> Excluded)
{
    /// <summary>What the diff in this work was RESOLVED against, for the record of the round.</summary>
    /// <remarks>
    /// The stage that assembles the diff is the only place that knows this — the caller names a ref,
    /// and what the diff is actually taken against may be the merge base of that ref instead. It
    /// rides back out on the work because the round that records it runs after the assembling is
    /// done and has no other way to reach the local it lived in. Empty for a plan round, which
    /// compares nothing.
    /// </remarks>
    public string BaseRef { get; init; } = string.Empty;

    /// <summary>
    /// What the round did NOT look at and must say so, appended to its reviewer line — today the
    /// uncommitted tail of a checkout whose committed part was reviewed. Empty for almost every round.
    /// </summary>
    public string Unreviewed { get; init; } = string.Empty;

    public RoundWork(IReadOnlyList<ReviewerWork> reviewers, IReadOnlyList<SkippedRole> notAsked)
        : this(reviewers, notAsked, [])
    {
    }
}
