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
/// <param name="Enabled">
/// <para>Whether this role takes part at all. Asked for on 2026-09-08 as a checkbox on each of the
/// four code-review boxes: unticking one means the role does not take part in the round — not a
/// reviewer that runs and is then ignored.</para>
/// <para><b>Defaulted to true, positionally, on purpose.</b> Every construction site that predates
/// the switch keeps compiling and keeps meaning what it meant, and "absent means on" becomes true by
/// construction rather than by a rule somebody has to remember. That matters at three boundaries at
/// once: an older panel driving a newer server, a stored settings record written before the key
/// existed, and a role nobody has ever touched.</para>
/// </param>
public sealed record RoleGate(int MaxRounds, int Threshold, bool Enabled = true);

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

    /// <summary>
    /// The SHIPPED role names, in the order a round runs them — read from the seed, not retyped.
    /// </summary>
    /// <remarks>
    /// These two were the hard-coded list of roles this product had. They are now a projection of
    /// <see cref="RoleCatalog.Builtin"/>, which is what every caller of them still means: a default
    /// that predates custom roles, or a sentence naming the boxes a person sees out of the box. The
    /// roles a ROUND runs come from <see cref="Catalog"/>, which may carry a person's own.
    /// </remarks>
    public static readonly string[] AllRoles = [.. RoleCatalog.Builtin.Roles.Select(r => r.Id)];

    public static readonly string[] CodeRoleNames = [.. RoleCatalog.Builtin.RolesOf(RoleStages.Result)];

    /// <summary>
    /// Which roles exist for this session — the shipped five, plus whatever a person configured.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Roles"/> on purpose: that one is how much each role may SPEND, this
    /// one is what a role IS. A config built before custom roles existed, or by a test that cares
    /// about budgets only, gets the shipped catalog and behaves exactly as it always did.
    /// </remarks>
    public RoleCatalog Catalog { get; init; } = RoleCatalog.Builtin;

    public IReadOnlyDictionary<string, RoleGate> Roles { get; init; } = Roles ?? Defaults();

    private static Dictionary<string, RoleGate> Defaults() =>
        AllRoles.ToDictionary(r => r, r => r == RoleCatalog.PlanRole ? PlanDefault : CodeDefault);

    /// <summary>This role's numbers, falling back to its stage's default for an unknown name.</summary>
    /// <remarks>
    /// The fallback is what makes a role a person just created work with no settings at all: nobody
    /// has written it a budget yet, so it takes its stage's shipped one.
    /// </remarks>
    public RoleGate For(string role) =>
        Roles.TryGetValue(role, out var gate)
            ? gate
            : Catalog.ById(role)?.Stage == RoleStages.Plan ? PlanDefault : CodeDefault;

    /// <summary>
    /// The roles of this stage that are switched ON, in the order a round runs them.
    /// </summary>
    /// <remarks>
    /// <para><b>Empty is a legitimate answer</b>, and it is the whole reason this is a named member
    /// rather than a `Where` inside two callers: it means the operator has unticked every reviewer
    /// this stage has, and a caller must refuse the round rather than start one nobody is in. A
    /// round with no reviewers is not an empty round — the session state counts it as unresolved,
    /// and it would sit there for ever.</para>
    /// <para>The plan stage is never affected: its one role carries no switch, by the operator's
    /// ruling that this is code review only.</para>
    /// </remarks>
    public IReadOnlyList<string> EnabledRolesOf(Stage stage) =>
        [.. RolesOf(stage).Where(r => For(r).Enabled)];

    /// <summary>The stage's budget: its widest ENABLED role, because the stage counts rounds once.</summary>
    /// <remarks>
    /// A role that is switched off lends the stage neither its rounds nor its threshold. Leaving
    /// either in would keep the stage running rounds nobody reviews, or hold the gate open against a
    /// number no reviewer can bring down. With every role off the answer is <c>(0, 0)</c> — a stage
    /// that may run no round — rather than an exception from <c>Max</c> over an empty sequence.
    /// </remarks>
    public StageGate For(Stage stage)
    {
        var roles = EnabledRolesOf(stage);
        return roles.Count == 0
            ? NoEnabledRoles
            : new StageGate(roles.Max(r => For(r).MaxRounds), roles.Max(r => For(r).Threshold));
    }

    /// <summary>What a stage whose every role is switched off is worth: no round, nothing open.</summary>
    public static readonly StageGate NoEnabledRoles = new(0, 0);

    /// <summary>
    /// Which roles take part in a given round of a stage — those switched on whose budget reaches it.
    /// </summary>
    /// <remarks>
    /// Two different reasons a role is absent, and they are not interchangeable: its budget is spent
    /// (it reviewed and has no round left) or the operator switched it off (it never reviews). The
    /// first is a fact about this round; the second is a fact about the stage.
    /// </remarks>
    public IReadOnlyList<string> RolesForRound(Stage stage, int round) =>
        [.. EnabledRolesOf(stage).Where(r => For(r).MaxRounds >= Math.Max(round, 1))];

    /// <summary>
    /// The roles of a stage, from this session's catalog — switched on, and of this stage's kind.
    /// </summary>
    /// <remarks>
    /// A hard-coded pair of arrays until the catalog became data, which is why a role a person
    /// created could not take part in a round however carefully it was configured.
    /// </remarks>
    private IReadOnlyList<string> RolesOf(Stage stage) =>
        Catalog.RolesOf(stage == Stage.CodeReview ? RoleStages.Result : RoleStages.Plan);

    /// <summary>
    /// The same gate for every role — what the legacy single-value settings mean, and what a test
    /// that does not care about the split is asking for.
    /// </summary>
    public static PanelConfig Uniform(int maxRounds, int threshold, StagePolicy onExhausted = StagePolicy.Human) =>
        new(AllRoles.ToDictionary(r => r, _ => new RoleGate(maxRounds, threshold)), onExhausted);

    /// <summary>The same config over a catalog that carries a person's own roles as well.</summary>
    public PanelConfig With(RoleCatalog catalog) => this with { Catalog = catalog };
}

public enum Stage
{
    PlanReview,
    CodeReview,
    Done,
}

/// <summary>A role the round decided not to ask for, and why.</summary>
/// <remarks>
/// <para>Structured rather than a finished sentence, which the plan round asked for: a caller parsing
/// a summary gets a role it can name, the punctuation of the clause is decided in one place beside
/// the clauses it sits next to, and a second reason one day is a second VALUE rather than a second
/// way of writing a sentence.</para>
/// <para>NOT a failure, and the wording must never let it read as one. A reviewer that could not run
/// is a problem — something was enabled and the round could not deliver it. A role that was not asked
/// is a DECISION this gate made, with a reason; reporting the two in the same words would teach a
/// caller to treat a correct round as a degraded one.</para>
/// </remarks>
public sealed record SkippedRole(string Role, string Reason);

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
    ImmutableArray<string> Excluded = default,
    ImmutableArray<SkippedRole> NotAsked = default)
{
    public static ReviewerSummary AllAnswered(int asked) => new(asked, asked, []);

    /// <summary>
    /// Set to the round's deadline ONLY when reaching it is what ended the round.
    /// </summary>
    /// <remarks>
    /// <para>Null for every round that finished in time, which is nearly all of them — and the
    /// sentence says nothing about a deadline that was not reached, because a change to every
    /// round's wording in service of the rare one is a change nobody asked for.</para>
    /// <para><b>Passed in, never inferred.</b> Cancelling the outstanding reviewers makes each of
    /// them abandoned, and counting abandoned reviewers would call it a deadline the moment a PERSON
    /// cancels a round. Raised on the plan's own code round, before any of it was built.</para>
    /// </remarks>
    public TimeSpan? EndedByDeadline { get; init; }

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
            // The deadline clause comes FIRST of the two additions, because it explains the
            // failures the sentence has just listed: they were cut off, not incompetent.
            var withDeadline = EndedByDeadline is { } limit
                ? $"{answered}; the round reached its {limit.TotalMinutes:0} minute limit "
                  + "and the reviewers still running were cancelled"
                : answered;
            // No early return here any more: a round can skip a role while excluding nobody, and the
            // clause below has to be reachable in that case — which is the ordinary one.
            var left = Excluded.IsDefaultOrEmpty ? [] : Excluded;
            var plural = left.Length == 1 ? string.Empty : "s";
            var withExcluded = left.Length == 0
                ? withDeadline
                : $"{withDeadline}; {left.Length} enabled reviewer{plural} "
                  + $"could not run: {string.Join("; ", left)}";

            // LAST of the three additions, and the order carries meaning: a deadline explains the
            // failures, the failures explain the count, and what was never asked for is last because
            // it is the only one of the three that is not a problem. Its own verb, too — "not asked"
            // and never "could not run", so a correct round cannot read as a degraded one.
            var skipped = NotAsked.IsDefaultOrEmpty ? [] : NotAsked;

            return skipped.Length == 0
                ? withExcluded
                : $"{withExcluded}; "
                  + string.Join("; ", skipped.Select(s => $"{s.Role} was not asked: {s.Reason}"));
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
