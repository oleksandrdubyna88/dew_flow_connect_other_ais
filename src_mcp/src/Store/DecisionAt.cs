using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Store;

/// <summary>
/// A decision, and the finding NUMBER the caller made it by.
/// </summary>
/// <remarks>
/// <para>The number is the index <c>resolve</c> names in its entries — <c>{"finding": 2, …}</c> —
/// which is the same number the projection stored as that finding's <c>ordinal</c>, because
/// <c>Pending</c> and the list handed to <c>RecordRound</c> are one list in one order.</para>
/// <para>It travels WITH the decision because the projection used to re-derive it from the
/// decision's position in the list, which is the same number only for a caller that resolves top to
/// bottom. Nothing requires one to: a set that arrives out of order, or covers only some of a
/// round's findings, is accepted by <see cref="RoundMachine.Resolve"/> — it checks that rejections
/// carry reasons and counts nothing — and every mark then landed on the wrong finding. Nothing
/// visibly broke, because the session file is the state machine's own record and was never wrong;
/// only the database read by the log page and the export was.</para>
/// <para><b>It lives here rather than beside <c>Decision</c> in the core.</b> The state machine
/// operates on decisions and has no use at all for the numbers they were made by — <c>Finish</c>
/// strips them before calling it. A projection index in <c>Core.Rounds</c> would be inherited by
/// every future consumer of a namespace that actively discards it. (Code round, gemini.)</para>
/// <para><b>There is no public constructor, and that is the point.</b> A freely built
/// <c>(int, Decision)</c> pair can disagree with itself — <c>new(0, Accepted(findings[2]))</c> — and
/// would then mark a finding nobody decided while leaving the decided one untouched, which is the
/// defect this type exists to end wearing a different hat. The factories never take a finding: they
/// take the round's pending list and a number, and read the finding OUT of it, so the two cannot
/// disagree. A number that names nothing is <c>null</c> rather than an exception, because a bad
/// index is an expected answer from a caller, not a fault of ours
/// (<c>.agents/conventions/csharp/doctrine.md</c> §3). (Code round, codex, two reviewers.)</para>
/// </remarks>
public sealed record DecisionAt
{
    private DecisionAt(int ordinal, Decision decision)
    {
        Ordinal = ordinal;
        Decision = decision;
    }

    /// <summary>The number <c>resolve</c> called this finding by, and the projection stored.</summary>
    public int Ordinal { get; }

    public Decision Decision { get; }

    /// <summary>The caller took this finding — or nothing, when the number names none.</summary>
    public static DecisionAt? Accept(IReadOnlyList<Finding> pending, int ordinal) =>
        Names(pending, ordinal) ? new DecisionAt(ordinal, new Decision.Accepted(pending[ordinal])) : null;

    /// <summary>The caller argued with this finding — or nothing, when the number names none.</summary>
    public static DecisionAt? Reject(IReadOnlyList<Finding> pending, int ordinal, string reason) =>
        Names(pending, ordinal) ? new DecisionAt(ordinal, new Decision.Rejected(pending[ordinal], reason)) : null;

    private static bool Names(IReadOnlyList<Finding> pending, int ordinal) =>
        ordinal >= 0 && ordinal < pending.Count;
}
