using System.Collections.Immutable;
using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Rounds;

/// <summary>What happens when max rounds are exhausted and findings still gate.</summary>
public enum StagePolicy
{
    /// <summary>Proceed as-is, saying so.</summary>
    Continue,

    /// <summary>Call a human.</summary>
    Human,

    /// <summary>Climb the escalation ladder, then a fresh set of rounds.</summary>
    Escalate,

    /// <summary>Good enough: read the findings, apply what is true and useful, and move on.</summary>
    /// <remarks>
    /// The gap between the other three, and the ordinary case. The reviewers found real things, they
    /// are not worth another round each, and the right move is to WORK them rather than to stop or
    /// to ignore them. <see cref="Continue"/> is not this: it proceeds and leaves every finding
    /// untouched, which is how a gate becomes decoration.
    /// </remarks>
    GoodEnough,
}

/// <summary>The ladder, in the only order it fires. The arbiter moves last — changing the author
/// of the plan is the most expensive step available.</summary>
public enum EscalationStep
{
    ReviewerEffortUp,
    ReviewerModelUp,
    ArbiterModelUp,
}

/// <summary>One stage's budget: how many attempts it gets, and how much may still be open.</summary>
public sealed record StageGate(int MaxRounds, int Threshold);

/// <summary>
/// The gate, per stage — because a plan and a diff are not the same object.
/// </summary>
/// <remarks>
/// <para>One threshold for both was wrong in a way only use revealed. A plan is a document: two
/// findings still open is a lot of doubt about a page of text. A diff is hundreds of lines across a
/// dozen files, and three open findings there is an ordinary Tuesday — so the number that makes the
/// plan gate strict makes the code gate a permanent <c>call_human</c>. Measured on this product's
/// own rounds: the plan stage passed at two and the code stage never passed at all.</para>
/// <para><see cref="For"/> is the only way to read them, so no call site picks a stage by hand.</para>
/// </remarks>
public sealed record PanelConfig(
    StageGate? Plan = null,
    StageGate? Code = null,
    StagePolicy OnExhausted = StagePolicy.Human)
{
    /// <summary>Three attempts, at most two findings still open. A page of text can be got right.</summary>
    public static readonly StageGate PlanDefault = new(3, 2);

    /// <summary>Three attempts, at most three. A diff of any size carries more than a plan does.</summary>
    public static readonly StageGate CodeDefault = new(3, 3);

    public StageGate Plan { get; init; } = Plan ?? PlanDefault;

    public StageGate Code { get; init; } = Code ?? CodeDefault;

    public StageGate For(Stage stage) => stage == Stage.CodeReview ? Code : Plan;

    /// <summary>
    /// The same gate for both stages — what the legacy single-value settings mean, and what a test
    /// that does not care about the split is asking for.
    /// </summary>
    public static PanelConfig Uniform(int maxRounds, int threshold, StagePolicy onExhausted = StagePolicy.Human) =>
        new(new StageGate(maxRounds, threshold), new StageGate(maxRounds, threshold), onExhausted);
}

public enum Stage
{
    PlanReview,
    CodeReview,
    Done,
}

/// <summary>How many reviewers were asked and how many answered — partial rounds are honest.</summary>
public sealed record ReviewerSummary(int Asked, int Answered, ImmutableArray<string> Failures)
{
    public static ReviewerSummary AllAnswered(int asked) => new(asked, asked, []);

    public string Sentence => Answered == Asked
        ? $"all {Asked} reviewers answered"
        : $"{Answered} of {Asked} reviewers answered; failed: {string.Join(", ", Failures)}";
}

/// <summary>
/// One repo+branch under review. Immutable; every transition returns a new state or a refusal —
/// the ordering contract lives here, not in anyone's good behaviour.
/// </summary>
public sealed record SessionState(
    string SessionId,
    string RepoPath,
    string Branch,
    PanelConfig Config)
{
    public Stage Stage { get; init; } = Stage.PlanReview;

    public int RoundsRunThisStage { get; init; }

    public int EscalationsUsed { get; init; }

    /// <summary>A round's verdict is out and decisions have not been recorded yet.</summary>
    public bool AwaitingResolve { get; init; }

    /// <summary>Set by resolve when the last plan verdict allowed proceeding.</summary>
    public bool PlanProceeded { get; init; }

    /// <summary>Pending stage advance, decided at round completion, applied by resolve.</summary>
    public bool AdvanceOnResolve { get; init; }

    /// <summary>
    /// The last verdict was <c>call_human</c> — the ONLY state in which a human "proceed" override
    /// is honoured. Its own flag rather than an inference from round counts, because the first
    /// code-gate run proved the inference wrong: an exhausted Escalate stage also has no rounds
    /// left, and the override could skip the configured ladder.
    /// </summary>
    public bool HumanGate { get; init; }

    public ImmutableArray<PriorRejection> Rejections { get; init; } = [];
}

/// <summary>The canonical identity of a session: same checkout + branch → same session, always.</summary>
public static class SessionKey
{
    public static string For(string repoPath, string branch) =>
        $"{repoPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant()}#{branch.Trim()}";
}
