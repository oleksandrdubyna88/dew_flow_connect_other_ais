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

/// <summary>One role's budget: how many attempts it gets, and how much may still be open.</summary>
public sealed record RoleGate(int MaxRounds, int Threshold);

/// <summary>Kept as the name a stage-wide budget goes by; a stage's gate is its widest role.</summary>
public sealed record StageGate(int MaxRounds, int Threshold);

/// <summary>
/// The gate, per ROLE — because the reviewers do different jobs.
/// </summary>
/// <remarks>
/// <para>It was per stage, and before that one number for both. Each step was the same discovery:
/// a budget shared by things that are not alike forces the cheapest of them to pay for the most
/// expensive. A plan is a document and a diff is not; architecture may be worth two passes with
/// different lenses while performance is worth one.</para>
/// <para><see cref="For(string)"/> is how a role's numbers are read, and the only way — so no call
/// site picks a budget by hand. A stage's round budget is the WIDEST of its roles: the stage counts
/// rounds once, and a role simply stops taking part when its own budget is spent.</para>
/// </remarks>
public sealed record PanelConfig(
    IReadOnlyDictionary<string, RoleGate>? Roles = null,
    StagePolicy OnExhausted = StagePolicy.Human)
{
    /// <summary>One attempt, at most six findings open.</summary>
    /// <remarks>
    /// <para>One round because the second and third re-raise what the first found rather than
    /// finding more; six because the old bound sat where a real change could not pass, and a gate
    /// that blocks everything is a gate people route around.</para>
    /// <para><b>These numbers are also the panel's, and that is a requirement rather than a
    /// coincidence.</b> The panel writes a `COAI_ROUNDS_*` key only when the value DIFFERS from its
    /// own default, so a pristine configuration sends nothing and this fallback is what runs. While
    /// the two disagreed — they did, for a day — the panel displayed one round and the server ran
    /// three. `panelServerDefaultsAgreement.test.ts` reads these two lines and fails when they
    /// drift.</para>
    /// </remarks>
    public static readonly RoleGate PlanDefault = new(1, 6);

    /// <summary>One attempt, at most five. Mirrored by the panel — see <see cref="PlanDefault"/>.</summary>
    public static readonly RoleGate CodeDefault = new(1, 5);

    /// <summary>The role names this config knows, in the order a round runs them.</summary>
    public static readonly string[] AllRoles =
        ["PlanCritique", "Conventions", "Architecture", "SecurityReliability", "UxDxPerformance"];

    public static readonly string[] CodeRoleNames =
        ["Conventions", "Architecture", "SecurityReliability", "UxDxPerformance"];

    public IReadOnlyDictionary<string, RoleGate> Roles { get; init; } = Roles ?? Defaults();

    private static Dictionary<string, RoleGate> Defaults() =>
        AllRoles.ToDictionary(r => r, r => r == "PlanCritique" ? PlanDefault : CodeDefault);

    /// <summary>This role's numbers, falling back to its stage's default for an unknown name.</summary>
    public RoleGate For(string role) =>
        Roles.TryGetValue(role, out var gate)
            ? gate
            : role == "PlanCritique" ? PlanDefault : CodeDefault;

    /// <summary>The stage's budget: its widest role, because the stage counts rounds once.</summary>
    public StageGate For(Stage stage)
    {
        var roles = RolesOf(stage);
        return new StageGate(roles.Max(r => For(r).MaxRounds), roles.Max(r => For(r).Threshold));
    }

    /// <summary>
    /// Which roles take part in a given round of a stage — those whose own budget reaches it.
    /// </summary>
    public IReadOnlyList<string> RolesForRound(Stage stage, int round) =>
        [.. RolesOf(stage).Where(r => For(r).MaxRounds >= Math.Max(round, 1))];

    private static string[] RolesOf(Stage stage) =>
        stage == Stage.CodeReview ? CodeRoleNames : ["PlanCritique"];

    /// <summary>
    /// The same gate for every role — what the legacy single-value settings mean, and what a test
    /// that does not care about the split is asking for.
    /// </summary>
    public static PanelConfig Uniform(int maxRounds, int threshold, StagePolicy onExhausted = StagePolicy.Human) =>
        new(AllRoles.ToDictionary(r => r, _ => new RoleGate(maxRounds, threshold)), onExhausted);
}

public enum Stage
{
    PlanReview,
    CodeReview,
    Done,
}

/// <summary>
/// How many reviewers were asked, how many answered — and who was never asked at all.
/// </summary>
/// <param name="Excluded">
/// Reviewers the operator ENABLED for this stage that the round could not run, each as
/// <c>name: reason</c>. Empty on almost every round, and the reason it exists is the one where it is
/// not: a reviewer that is asked and fails has always been reported honestly, and one that never
/// entered the roster was reported by nothing at all. On 2026-09-07 that made a Team-server reviewer
/// invisible for a day — the log said "4 enabled" and "3 reviewer(s)" eleven seconds apart, and the
/// verdict said "all 3 reviewers answered", which was true about what it asked.
/// </param>
public sealed record ReviewerSummary(
    int Asked,
    int Answered,
    ImmutableArray<string> Failures,
    ImmutableArray<string> Excluded = default)
{
    public static ReviewerSummary AllAnswered(int asked) => new(asked, asked, []);

    /// <remarks>
    /// The excluded clause is APPENDED rather than folded in, so a round with nothing to add reads
    /// exactly as it always did. A change to every round's sentence in service of the rare one is a
    /// change nobody asked for.
    /// </remarks>
    public string Sentence
    {
        get
        {
            var answered = Answered == Asked
                ? $"all {Asked} reviewers answered"
                : $"{Answered} of {Asked} reviewers answered; failed: {string.Join(", ", Failures)}";
            var left = Excluded.IsDefaultOrEmpty ? [] : Excluded;
            if (left.Length == 0)
            {
                return answered;
            }

            var plural = left.Length == 1 ? string.Empty : "s";

            return $"{answered}; {left.Length} enabled reviewer{plural} "
                + $"could not run: {string.Join("; ", left)}";
        }
    }
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
