using System.Collections.Immutable;

namespace CoaiMcp.Core.Rounds;

/// <summary>One prompt a reviewer can be given: an id, the role it serves, and what it is for.</summary>
/// <param name="Id">The file name without its extension — what settings and the panel name.</param>
/// <param name="Universal">
/// True for the broad prompt of a role. Exactly one per role, and it is what a round uses when
/// nobody has chosen otherwise: a narrow lens is a deliberate act, never a default.
/// </param>
public sealed record PromptChoice(string Id, string Role, string Label, string Purpose, bool Universal);

/// <summary>
/// Which prompts exist, and which one a given round gets.
/// </summary>
/// <remarks>
/// <para>A reviewer's prompt used to be one file per role, forever. That is the right default and
/// the wrong ceiling: asked to look at everything, a model spreads itself thin, and three rounds
/// of the same broad question tend to return the same broad answers. A narrow lens — "what does
/// this hold and leave behind" rather than "review reliability" — asks for something a wide prompt
/// will not reach.</para>
/// <para>So each role has a UNIVERSAL prompt and five narrow ones, and a round can be told which
/// to use. <see cref="Rotating"/> spends the rounds on different lenses instead of asking the same
/// question louder: round 1 universal, round 2 the first narrow lens, round 3 the second.</para>
/// <para><b>The last twelve lenses were measured before they were shipped, and the measurement
/// changed what shipped.</b> Each was drafted three times, and the three drafts turned out to be
/// three SHAPES rather than three wordings — a question list, a task to enact, a rule with
/// exceptions — held constant across all twelve. Seventy-two runs later
/// (<c>research/RESULTS_focused_prompts.md</c>): the shapes find the same AMOUNT (6.6–6.9 findings,
/// 79–82 % gating, flat) and differ in whether they find the same thing TWICE — 42 % against 32 %.
/// So a lens here is written as a task to perform wherever the subject has a sequence to enact
/// ("run it twice, a millisecond apart, and narrate both"), and as a question list only where it
/// does not. Five of the twelve picks were decided by that measurement; seven were inside its noise
/// and took the shape result as a prior. Anyone adding a lens should read that file first.</para>
/// </remarks>
public static class PromptCatalog
{
    public const string PlanRole = "PlanCritique";
    public const string ArchitectureRole = "Architecture";
    public const string SecurityRole = "SecurityReliability";
    public const string UxDxRole = "UxDxPerformance";

    /// <summary>The one prompt that judges nothing but the project's own written rules.</summary>
    public const string ConventionsId = "conventions";

    private const string ConventionsPurpose =
        "Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. " +
        "A convention the reviewer believes in but the project never wrote is not a finding.";

    public static readonly ImmutableArray<PromptChoice> All =
    [
        new("plan-critique", PlanRole, "Universal", "The whole plan: assumptions, failure paths, order, testability.", true),
        new("plan-assumptions", PlanRole, "Assumptions & verification", "What the plan takes for granted, and what it promises but never checks.", false),
        new("plan-human-path", PlanRole, "The human path", "What a person does with it, and what happens when they do it wrong.", false),
        new("plan-data-loss", PlanRole, "Data loss & recovery", "What the plan destroys, overwrites or moves — and whether a failure halfway can be undone.", false),
        new("plan-operability", PlanRole, "Operability", "What it is like to run this at 3 a.m.: what is observable, what is alertable, what is diagnosable.", false),
        new("plan-scope-creep", PlanRole, "Scope & budget", "What this plan quietly takes on beyond its goal, and what it will cost to keep.", false),

        new(ConventionsId, ArchitectureRole, "Conventions", ConventionsPurpose, false),
        new("architecture", ArchitectureRole, "Universal", "Boundaries, abstractions, consistency, and the plan-to-code gap.", true),
        new("arch-boundaries", ArchitectureRole, "Boundaries & duplication", "Dependency direction, layers reaching around each other, capabilities implemented twice.", false),
        new("arch-evolution", ArchitectureRole, "Cost of the next change", "What this change makes harder, and what is hard-coded that will have to vary.", false),
        new("arch-coupling", ArchitectureRole, "Coupling & knowledge", "What this change makes one part know about another, and what breaks when either moves.", false),
        new("arch-naming", ArchitectureRole, "Names & the shape they imply", "Where a name promises a shape the code does not have — the misreading it invites next.", false),
        new("arch-testability", ArchitectureRole, "Testability of the seams", "Which decision here can only be tested by starting a server, a browser or a clock.", false),

        new(ConventionsId, SecurityRole, "Conventions", ConventionsPurpose, false),
        new("security-reliability", SecurityRole, "Universal", "Secrets, input, failure behaviour, state, trust boundaries.", true),
        new("sec-memory-leaks", SecurityRole, "What it holds and leaves", "Secrets that outlive their use, resources leaked on the error path, what a kill -9 leaves behind.", false),
        new("sec-attack", SecurityRole, "Attack surface", "What is trusted that was never checked, injection, privilege, and checks that fail open.", false),
        new("sec-blast-radius", SecurityRole, "Blast radius", "If this one thing is wrong or compromised, how far does it reach before anything stops it.", false),
        new("sec-concurrency", SecurityRole, "Two at once", "The same code running twice, a millisecond apart, over the state they share.", false),
        new("sec-supply-chain", SecurityRole, "What this change trusts", "Every input, dependency and endpoint it believes without checking — and who can change them.", false),

        new(ConventionsId, UxDxRole, "Conventions", ConventionsPurpose, false),
        new("uxdx-performance", UxDxRole, "Universal", "Performance, UI state as code, and the ergonomics of a new API.", true),
        new("perf-scale", UxDxRole, "Cost at scale", "Which input grows, and what this code does when it does.", false),
        new("dx-ergonomics", UxDxRole, "Ergonomics & waiting", "Names that mislead, errors that name no cure, and work a person waits on.", false),
        new("perf-first-run", UxDxRole, "The first run and the empty case", "A brand-new machine, no cache, no config, nothing yet: the first thirty seconds, narrated.", false),
        new("perf-wasted-work", UxDxRole, "Work done twice", "What this recomputes, refetches or re-renders that it already had.", false),
        new("ux-undo", UxDxRole, "What cannot be taken back", "Using it wrongly on purpose: what state that leaves, and how somebody gets back.", false),
    ];

    public static IEnumerable<PromptChoice> For(string role) => All.Where(p => p.Role == role);

    public static PromptChoice UniversalFor(string role) => For(role).First(p => p.Universal);

    public static PromptChoice? ById(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The prompt for one round: the person's explicit choice, the rotation, or the universal one.
    /// </summary>
    /// <param name="round">1-based. Round 0 or less is treated as the first round.</param>
    /// <param name="chosen">
    /// What the panel selected for THIS role, per round — index 0 is round 1. An entry that is
    /// blank or unknown falls through to the rotation or the universal prompt, so a stale setting
    /// can never leave a round with no prompt at all.
    /// </param>
    /// <param name="hasRules">
    /// Whether the repository under review actually wrote any conventions down. Round 1 of a code
    /// role is the conventions pass only when it did.
    /// </param>
    public static PromptChoice ForRound(
        string role,
        int round,
        IReadOnlyList<string> chosen,
        bool hasRules = false)
    {
        var index = Math.Max(round, 1) - 1;
        if (index < chosen.Count && For(role).FirstOrDefault(p => p.Id == chosen[index]) is { } picked)
        {
            return picked;
        }

        // ROUND ONE of the code stage belongs to the written rules, when there are any.
        //
        // Three reviewers already cover architecture, security and performance, each with its own
        // taste; the one thing none of them was doing is holding the change to the standard the
        // project WROTE DOWN — which is the standard its human authors are held to, so the two
        // halves were being judged differently by construction. It takes round 1 because a broken
        // written rule is the cheapest finding to act on and the least arguable: there is a sentence
        // to point at.
        //
        // Only when rules were actually found. A conventions pass with nothing to judge against
        // would invent a standard, which is worse than the review it displaced. And an explicit
        // choice above still wins: this is a default, not a lock.
        if (hasRules && index == 0 && role != PlanRole)
        {
            return For(role).First(p => p.Id == ConventionsId);
        }

        return For(role).First(p => p.Universal);
    }
}
