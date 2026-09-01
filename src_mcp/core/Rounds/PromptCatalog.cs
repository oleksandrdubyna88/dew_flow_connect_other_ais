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
/// <para>So each role has a UNIVERSAL prompt and two narrow ones, and a round can be told which to
/// use. <see cref="Rotating"/> spends the rounds on different lenses instead of asking the same
/// question louder: round 1 universal, round 2 the first narrow lens, round 3 the second.</para>
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

        new(ConventionsId, ArchitectureRole, "Conventions", ConventionsPurpose, false),
        new("architecture", ArchitectureRole, "Universal", "Boundaries, abstractions, consistency, and the plan-to-code gap.", true),
        new("arch-boundaries", ArchitectureRole, "Boundaries & duplication", "Dependency direction, layers reaching around each other, capabilities implemented twice.", false),
        new("arch-evolution", ArchitectureRole, "Cost of the next change", "What this change makes harder, and what is hard-coded that will have to vary.", false),

        new(ConventionsId, SecurityRole, "Conventions", ConventionsPurpose, false),
        new("security-reliability", SecurityRole, "Universal", "Secrets, input, failure behaviour, state, trust boundaries.", true),
        new("sec-memory-leaks", SecurityRole, "What it holds and leaves", "Secrets that outlive their use, resources leaked on the error path, what a kill -9 leaves behind.", false),
        new("sec-attack", SecurityRole, "Attack surface", "What is trusted that was never checked, injection, privilege, and checks that fail open.", false),

        new(ConventionsId, UxDxRole, "Conventions", ConventionsPurpose, false),
        new("uxdx-performance", UxDxRole, "Universal", "Performance, UI state as code, and the ergonomics of a new API.", true),
        new("perf-scale", UxDxRole, "Cost at scale", "Which input grows, and what this code does when it does.", false),
        new("dx-ergonomics", UxDxRole, "Ergonomics & waiting", "Names that mislead, errors that name no cure, and work a person waits on.", false),
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
        bool rotating,
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

        if (!rotating)
        {
            return UniversalFor(role);
        }

        // Universal first, then each narrow lens in turn, then round-robin. The broad question
        // earns the first round because it is the one most likely to find the obvious thing.
        // The conventions pass is not one of the lenses: rotation exists to vary the QUESTION, and
        // this one is a different job that round 1 already owns.
        var order = For(role).Where(p => p.Id != ConventionsId).OrderByDescending(p => p.Universal).ToList();
        return order[index % order.Count];
    }
}
