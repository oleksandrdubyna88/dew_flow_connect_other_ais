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
}

/// <summary>The ladder, in the only order it fires. The arbiter moves last — changing the author
/// of the plan is the most expensive step available.</summary>
public enum EscalationStep
{
    ReviewerEffortUp,
    ReviewerModelUp,
    ArbiterModelUp,
}

public sealed record PanelConfig(int MaxRounds = 3, int Threshold = 2, StagePolicy OnExhausted = StagePolicy.Human);

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

    public ImmutableArray<PriorRejection> Rejections { get; init; } = [];
}

/// <summary>The canonical identity of a session: same checkout + branch → same session, always.</summary>
public static class SessionKey
{
    public static string For(string repoPath, string branch) =>
        $"{repoPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant()}#{branch.Trim()}";
}
