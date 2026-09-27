using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// A round-1 finding and what the caller decided about it — carried into a feature review's second
/// round as it was resolved, never re-bought (D23, S3.4).
/// </summary>
/// <remarks>
/// <para>A retry after a partial failure asks only the reviewers that failed, so the reviewers that
/// answered in round 1 are not asked again — and their findings, already decided at <c>resolve</c>, must
/// not vanish from the verdict the caller reads at the end. They ride on the second round's answer under
/// <c>carried</c>, with the decision that was made, and they are not to be decided twice.</para>
/// <para>The wire shape — <c>accept</c> / <c>reject</c> and a reason — rather than the core's
/// <see cref="Decision"/> union, because this is what a caller reads back and what the session file keeps
/// between the two calls.</para>
/// </remarks>
public sealed record CarriedDecision(Finding Finding, string Action, string Reason = "")
{
    public static CarriedDecision From(Decision decision) => decision switch
    {
        Decision.Accepted accepted => new(accepted.Finding, "accept"),
        Decision.Rejected rejected => new(rejected.Finding, "reject", rejected.Reason),
        _ => throw new InvalidOperationException("the union is closed"),
    };
}
