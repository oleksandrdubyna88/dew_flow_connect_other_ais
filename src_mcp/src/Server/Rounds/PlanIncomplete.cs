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
/// <para><b>Any</b> finding titled with THIS round's token folds the reviewer, whatever else it answered. The token is a
/// statement that the plan did not arrive whole; a reviewer that makes it did not review the plan, and "exactly one
/// finding" let two identical tokens — or the token beside others — stay an answer and carry a round past the plan
/// threshold with no review at all (the cadence consultation). Its other findings are set aside, and the reason says how
/// many. A token for ANOTHER round's id is an ordinary finding. <see cref="ReviewerOutcome.Unparseable"/> is reused: it
/// already means "an answer the gate cannot use", and it keeps what the reviewer cost — the usage AND everything the
/// outcome inherited from earlier launches, turns and the launch control, which the round's totals and the ledger read.</para>
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

    private static ReviewerOutcome Folded(ReviewerOutcome outcome, string title)
    {
        if (outcome is not ReviewerOutcome.Ok ok)
        {
            return outcome;
        }

        var tokens = ok.Review.Findings.Count(f => IsToken(f.Title, title));

        return tokens == 0 ? outcome : Failed(ok, ok.Review.Findings.Count() - tokens);
    }

    private static bool IsToken(string findingTitle, string title) =>
        string.Equals(findingTitle.Trim(), title, StringComparison.OrdinalIgnoreCase);

    /// <summary>The failure, carrying EVERYTHING the answer's accounting carried — a billed retry stays billed.</summary>
    private static ReviewerOutcome Failed(ReviewerOutcome.Ok ok, int setAside) =>
        new ReviewerOutcome.Unparseable(Reason + Aside(setAside), ok.Usage)
        {
            EarlierTurns = ok.EarlierTurns,
            EarlierLaunches = ok.EarlierLaunches,
            Launches = ok.Launches,
            Warning = ok.Warning,
            CliVersion = ok.CliVersion,
        };

    private static string Aside(int count) =>
        count == 0 ? string.Empty : $"; {count} other finding{(count == 1 ? string.Empty : "s")} set aside";
}
