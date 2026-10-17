using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// A reviewer that said the plan did not reach it whole did not review the plan — it failed, and is never an answer.
/// </summary>
/// <remarks>
/// <para>Parent D12, settled ONCE: the scheduler applies <see cref="Settle"/> to each reviewer's outcome where it finishes
/// (<c>BoundedScheduler.RunAllAsync</c>'s <c>settle</c>), so the spending ledger, the live view, the audit line, the local
/// stand-down count and the round's <c>results</c> — the summary, the merge (which keeps only <see cref="ReviewerOutcome.Ok"/>),
/// the decision, the notice and the spent lenses — all read the same outcome. It used to run on <c>results</c> after the
/// fan-out, which left the first four saying <c>ok</c>: the "accepted mismatch" D12 recorded, superseded by
/// <c>todo/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md</c> (D8). The tokens the reviewer spent are kept (D9).</para>
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

    /// <summary>The title THIS round's token carries — computed once per round, from the plan the round was handed.</summary>
    internal static string TitleOf(string planText) => PlanSection.IncompleteTitle(DocumentRules.ArtifactIdOf(planText));

    /// <summary>
    /// The rule a round hands the scheduler: <see cref="Settle"/> with this round's title worked out ONCE — and not at all for a
    /// stage that has no plan to be cut, whose <c>planText</c> can be a whole diff.
    /// </summary>
    internal static Func<ReviewerOutcome, ReviewerOutcome> Settler(Stage stage, string planText)
    {
        var title = stage == Stage.PlanReview ? TitleOf(planText) : string.Empty;

        return outcome => Settle(stage, title, outcome);
    }

    /// <summary>
    /// ONE reviewer's outcome, settled: a plan-stage answer carrying this round's token is the failure it is, everything else is
    /// returned as it came.
    /// </summary>
    internal static ReviewerOutcome Settle(Stage stage, string title, ReviewerOutcome outcome) =>
        stage == Stage.PlanReview ? Folded(outcome, title) : outcome;

    private static ReviewerOutcome Folded(ReviewerOutcome outcome, string title)
    {
        if (outcome is not ReviewerOutcome.Ok ok)
        {
            return outcome;
        }

        var tokens = ok.Review.Findings.Count(f => IsToken(f.Title, title));

        return tokens == 0 ? outcome : Failed(ok, ok.Review.Findings.Count() - tokens);
    }

    /// <summary>
    /// What a model copies from the instruction — which shows the title in backticks — is still the title: backticks, quotes
    /// (straight or curly), emphasis marks and spaces are stripped from BOTH ends, repeatedly, before the exact compare.
    /// The compare itself stays exact, so a token for ANOTHER round's id is never this round's.
    /// </summary>
    private static readonly char[] Wrapping = ['`', '\'', '"', '\u2018', '\u2019', '\u201C', '\u201D', '*', '_', ' ', '\t', '\r', '\n'];

    private static bool IsToken(string findingTitle, string title) =>
        string.Equals(findingTitle.Trim(Wrapping), title, StringComparison.OrdinalIgnoreCase);

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
