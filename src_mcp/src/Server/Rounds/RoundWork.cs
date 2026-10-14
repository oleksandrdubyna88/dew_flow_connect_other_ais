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
    public bool SecurityActive { get; init; }
    public bool OrdinaryDue { get; init; }

    /// <summary>
    /// What the round did NOT look at and must say so, appended to its reviewer line — today the
    /// uncommitted tail of a checkout whose committed part was reviewed. Empty for almost every round.
    /// </summary>
    public string Unreviewed { get; init; } = string.Empty;

    /// <summary>
    /// The round's own scratch folder — its sanitised snapshot and its Vibe homes (<see cref="RoundScratch"/>) — which the
    /// engine disposes when the round ends, however it ends. <see cref="RoundScratch.None"/> for a round that has none.
    /// </summary>
    public RoundScratch Scratch { get; init; } = RoundScratch.None;

    public RoundWork(IReadOnlyList<ReviewerWork> reviewers, IReadOnlyList<SkippedRole> notAsked)
        : this(reviewers, notAsked, [])
    {
    }

    /// <summary>
    /// This work with every launch stamped as <paramref name="key"/>'s round — each reviewer's first launch and its
    /// repair, the security lane's included (story 4.1 of todo/PLAN_every_round_is_counted.md). A retry launches the
    /// same invocation, a follow-up is derived from it, and a conversation's later turn is stamped by the turn loop, so
    /// this ONE stamp reaches every launch the round makes.
    /// </summary>
    internal RoundWork InRound(string key) => this with
    {
        Reviewers = [.. Reviewers.Select(w => w with
        {
            Invocation = w.Invocation with { Round = key },
            Repair = w.Repair is null ? null : w.Repair with { Round = key },
        })],
    };
}
