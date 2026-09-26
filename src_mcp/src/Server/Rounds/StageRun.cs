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
    /// (<c>research/PLAN_consult_on_a_cadence.md</c>). Off for every stage that does not set it.
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

    /// <summary>
    /// Which PLAN's feature session this run is about: empty for everything that is not a feature
    /// review, the plan's identity for one — the fourth segment of the session key.
    /// </summary>
    public string Feature { get; init; } = string.Empty;

    /// <summary>
    /// The ref the round's commit is resolved from, when it is not the session's branch: a feature
    /// session lives under a branch no git ref can spell, so its head is named here. Empty resolves
    /// the session's own branch, as every other stage does.
    /// </summary>
    public string Head { get; init; } = string.Empty;

    /// <summary>What the engine does when the roster comes back empty. Refuse, for every stage but one.</summary>
    public NobodyPolicy WhenNobody { get; init; } = NobodyPolicy.Refuse;

    /// <summary>Whether a session that is not there yet is created under the engine's own claim, and how.</summary>
    public SessionRule Session { get; init; } = new SessionRule.MustExist();

    /// <summary>
    /// A reason this round is SKIPPED that the stage decided from its own inputs — empty when it is not.
    /// </summary>
    /// <remarks>
    /// The feature stage's D17: a plan of fewer epics than <c>COAI_FEATURE_MIN_EPICS</c> is covered by
    /// <c>review_code</c>, and a feature round for it is recorded as <c>skipped</c> and does not block.
    /// The engine records it under the claim, AFTER <see cref="Begin"/> — a standing call_human is a
    /// person's decision and no skip dissolves it — and before any work is built.
    /// </remarks>
    public string SkipBecause { get; init; } = string.Empty;

    /// <summary>
    /// The resolved base a feature review is held to from this round on — empty for every other stage.
    /// Saved with the round only (never by a skip, a refusal or a build that failed — the session is
    /// created with none), so <c>again</c> against a new base moves it the moment that review actually
    /// runs, and starts that review over (§4.3, <c>RoundMachine.FreshFeatureReview</c>).
    /// </summary>
    public string FeatureBase { get; init; } = string.Empty;
}

/// <summary>What a round with nobody to ask becomes.</summary>
internal enum NobodyPolicy
{
    /// <summary>A refusal: a round with no reviewer would pass having reviewed nothing.</summary>
    Refuse,

    /// <summary>
    /// A round recorded as <c>skipped</c> with its reason, leaving the session untouched — the
    /// feature stage's answer (D1): optional per vendor, so nobody ticked is the ordinary state.
    /// </summary>
    RecordSkip,
}

/// <summary>
/// Whether the engine may CREATE the session it runs on.
/// </summary>
/// <remarks>
/// A closed union rather than a nullable factory, which is how every other two-state thing here is
/// spelled. <see cref="MustExist"/> is every stage that predates the feature review: <c>open</c> made
/// the session, and a run without one is refused. <see cref="CreateIfAbsent"/> is the feature
/// stage's: it needs no <c>open</c>, and the session is made UNDER THE CLAIM — the document stage
/// creates outside it and narrows the race by re-reading, so two concurrent first calls on one plan
/// would be two creators (§4.3).
/// </remarks>
internal abstract record SessionRule
{
    public sealed record MustExist : SessionRule;

    public sealed record CreateIfAbsent(Func<PersistedSession> Make) : SessionRule;

    private SessionRule() { }
}
