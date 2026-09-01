using System.Collections.Immutable;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Gate;

/// <summary>A finding the main AI rejected in an earlier round, with the reason it gave.</summary>
public sealed record PriorRejection(Finding Finding, string Reason);

/// <summary>What the gate saw: the count that gates, and where every discount went.</summary>
public sealed record GateResult(
    int GatingCount,
    bool Passed,
    ImmutableArray<Finding> Gating,
    ImmutableArray<Finding> Discounted)
{
    public static GateResult Empty { get; } = new(0, true, [], []);

    /// <summary>
    /// The roles whose own findings are over their own threshold — the ones with work left.
    /// </summary>
    /// <remarks>
    /// This is what makes a per-role budget mean anything: a round revises only for the roles that
    /// are actually over, and a stage is finished when this is empty. Unattributed findings answer to
    /// the empty role, which is how a plan round still gates.
    /// </remarks>
    public ImmutableArray<string> OverThreshold { get; init; } = [];
}

/// <summary>
/// The counting rule — what makes "fewer than N remarks" mean something.
/// </summary>
/// <remarks>
/// <para>1. Only <c>blocking</c> and <c>major</c> gate; minors and nits are reported and never
/// counted. 2. Counting happens AFTER <see cref="FindingDedup"/>. 3. A finding the main AI
/// rejected with a reason, and which no reviewer re-raises with a new argument, does not count in
/// later rounds — otherwise one disputed opinion blocks forever. Re-raised with a genuinely new
/// <c>why</c>, it counts again in full.</para>
/// </remarks>
public static class GateRule
{
    /// <summary>The same rule with one threshold for every role — what the plan stage wants.</summary>
    public static GateResult Evaluate(
        ImmutableArray<Finding> merged,
        ImmutableArray<PriorRejection> priorRejections,
        int threshold) => Evaluate(merged, priorRejections, _ => threshold);

    /// <param name="thresholdFor">
    /// This role's threshold. A finding is counted against the role that RAISED it, because the three
    /// code reviewers do different jobs and one number for all of them forces the cheapest role to
    /// pay for the most expensive one.
    /// </param>
    public static GateResult Evaluate(
        ImmutableArray<Finding> merged,
        ImmutableArray<PriorRejection> priorRejections,
        Func<string, int> thresholdFor)
    {
        var gating = ImmutableArray.CreateBuilder<Finding>();
        var discounted = ImmutableArray.CreateBuilder<Finding>();
        foreach (var finding in merged.Where(f => f.IsGating))
        {
            if (IsStandingRejection(finding, priorRejections))
            {
                discounted.Add(finding);
            }
            else
            {
                gating.Add(finding);
            }
        }

        // Per ROLE, because that is whose budget it is. A role at exactly its threshold passes:
        // "passes at or under" is what the panel says and what a person set.
        var over = gating.ToImmutable()
            .GroupBy(f => f.Role)
            .Where(g => g.Count() > thresholdFor(g.Key))
            .Select(g => g.Key)
            .ToImmutableArray();

        // Passing is EVERY role being at or under its own threshold, not a single total being small
        // enough. A stage where architecture is fine and security has one unacceptable finding has
        // not passed, whatever the sum says.
        return new GateResult(gating.Count, over.IsEmpty, gating.ToImmutable(), discounted.ToImmutable())
        {
            OverThreshold = over,
        };
    }

    /// <summary>Rejected before, and raised again with the SAME argument — the discount case.</summary>
    private static bool IsStandingRejection(Finding finding, ImmutableArray<PriorRejection> rejections) =>
        rejections.Any(r =>
            FindingDedup.SameDefect(r.Finding, finding) &&
            TextSimilarity.SameRemark(r.Finding.Why, finding.Why));
}
