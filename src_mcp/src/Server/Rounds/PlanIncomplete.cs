using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// A reviewer that said the plan did not reach it whole did not review the plan — it failed, and is never an answer.
/// </summary>
/// <remarks>
/// <para>Parent D12. Applied to <c>results</c> right after the fan-out returns, because everything after it reads
/// them: the summary, the merge (which keeps only <see cref="ReviewerOutcome.Ok"/>), the decision, the notice, the
/// live view's finish and the spent lenses. The progress callback has already shown the reviewer as finished — an
/// accepted mismatch the parent records.</para>
/// <para>Only an answer that is EXACTLY the one token finding is folded. The token beside other findings stays an
/// answer, so a substantive review is never discarded over a title collision. <see cref="ReviewerOutcome.Unparseable"/>
/// is reused: it already means "an answer the gate cannot use", and it keeps the usage.</para>
/// </remarks>
internal static class PlanIncomplete
{
    internal const string Reason = "the plan did not reach this reviewer whole (PLAN_INCOMPLETE)";

    internal static IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> Fold(
        Stage stage,
        string planText,
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results)
    {
        if (stage != Stage.PlanReview)
        {
            return results;
        }

        var title = PlanSection.IncompleteTitle(DocumentRules.ArtifactIdOf(planText));

        return [.. results.Select(r => (r.Invocation, Outcome: Folded(r.Outcome, title)))];
    }

    private static ReviewerOutcome Folded(ReviewerOutcome outcome, string title) =>
        outcome is ReviewerOutcome.Ok { Review.Findings: [var only] } ok
        && string.Equals(only.Title.Trim(), title, StringComparison.OrdinalIgnoreCase)
            ? new ReviewerOutcome.Unparseable(Reason, ok.Usage)
            : outcome;
}
