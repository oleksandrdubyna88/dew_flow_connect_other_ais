using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Which stage a run IS: how it begins, whether its reviewers get a checkout, and how its work is
/// built.
/// </summary>
/// <remarks>
/// <para>One argument instead of four, because <c>RunStageAsync</c> had grown to eight parameters and
/// an analyser is right that eight is where a signature stops being readable. These four are not four
/// independent knobs — they are one answer to "which stage is this", and every combination other than
/// the two the callers pass is meaningless.</para>
/// <para><b>Every one of these is TOLD, not derived.</b> This file records what deriving the stage
/// cost twice: <c>planPrompts is { Count: &gt; 0 }</c> is empty on an ordinary plan round, and
/// reading the ROLES works only because no code round happens to carry <c>PlanCritique</c>.</para>
/// <para>And what ONE flag for two questions cost a third time. <c>IsPlanStage</c> answered both
/// "which vendor switch serves this" and "does a reviewer get a checkout", which agreed for as long
/// as there were two stages. A document round wants the plan stage's answer to both and is NOT the
/// plan stage, so passing <c>isPlanStage: true</c> for it was a claim about identity made in order
/// to get two behaviours — and two reviewers said so on its code round, separately.</para>
/// </remarks>
/// <param name="Stage">
/// Which stage this run IS — the question a vendor's switches are asked, and the round's deadline
/// arithmetic with it.
/// <para>It was a bool named for the plan switch, which could say "plan or code" and had no way to
/// say "document": plan 4's document round therefore rode the PLAN tick, and plan 5 found that tick
/// deciding whether a company document leaves the machine. A bool cannot answer a three-way
/// question, and the caller has always known which stage it was running.</para>
/// </param>
/// <param name="ReadsCheckout">
/// Whether this round's reviewers are given the repository to explore.
/// <para>Still its own flag rather than a reading of <paramref name="Stage"/>, because the two
/// genuinely disagree: a CODE round with <c>CodeWorkspace: none</c> reads no checkout either. Plan 4
/// split these apart for exactly that reason and nothing here re-merges them.</para>
/// </param>
internal sealed record StageRun(
    Func<SessionState, Transition> Begin,
    bool NeedsWorktree,
    Stage Stage,
    bool ReadsCheckout,
    Func<PersistedSession, string, string, Task<RoundWork>> MakeWork)
{
    /// <summary>
    /// Which session this run is about: empty for the branch's own, the document's identity for a
    /// document round.
    /// </summary>
    public string Document { get; init; } = string.Empty;

    /// <summary>
    /// A refusal decided from the resolved commit alone, BEFORE any worktree is made or any reviewer
    /// launched — an empty sentence when the round may go on.
    /// </summary>
    /// <remarks>
    /// The code stage asks it whether there is anything to review (S1 of
    /// research/PLAN_a_failed_round_can_be_retried.md): a round over an empty diff used to launch every
    /// reviewer and answer <c>proceed</c>. Here rather than inside <see cref="MakeWork"/> because by
    /// then a tree has already been checked out for nothing, and because nothing about the session
    /// has been written yet — a refusal leaves it exactly as it was.
    /// <para>It is handed the session as read UNDER the claim and before the begin moved it, so a
    /// decision about the last round is never taken from a read that another call could have made
    /// stale (code round, gemini).</para>
    /// </remarks>
    public Func<PersistedSession, string, Task<string>> RefuseBeforeBuilding { get; init; } =
        static (_, _) => Task.FromResult(string.Empty);

    /// <summary>
    /// This call's consultation cadence, filled by <see cref="RefuseBeforeBuilding"/> under the claim and read
    /// by the rest of the round — the orders, the session's plan and epic, the record's note
    /// (<c>todo/PLAN_consult_on_a_cadence.md</c>). Off for every stage that does not set it.
    /// </summary>
    public CadenceTrace Cadence { get; init; } = new();

    /// <summary>
    /// How many reviewers ONE vendor runs in this round — the multiplier the deadline is derived
    /// from.
    /// </summary>
    /// <remarks>
    /// <b>Told, not derived from <see cref="IsPlanStage"/>.</b> It used to be
    /// <c>isPlanStage ? 1 : CodeRoleNames.Length</c>, which is right for the two stages that
    /// existed and silently wrong for a third: a document round reads a document and has no
    /// checkout, so it would have taken the plan stage's ONE — a lower bound, and the wrong
    /// direction for a deadline to be wrong in, where the file's own remark says an upper bound is
    /// the right one.
    /// </remarks>
    public int RolesPerVendor { get; init; } = 1;
}
