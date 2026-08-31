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
    public static GateResult Evaluate(
        ImmutableArray<Finding> merged,
        ImmutableArray<PriorRejection> priorRejections,
        int threshold)
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

        var count = gating.Count;
        return new GateResult(count, count <= threshold, gating.ToImmutable(), discounted.ToImmutable());
    }

    /// <summary>Rejected before, and raised again with the SAME argument — the discount case.</summary>
    private static bool IsStandingRejection(Finding finding, ImmutableArray<PriorRejection> rejections) =>
        rejections.Any(r =>
            FindingDedup.SameDefect(r.Finding, finding) &&
            TextSimilarity.SameRemark(r.Finding.Why, finding.Why));
}
