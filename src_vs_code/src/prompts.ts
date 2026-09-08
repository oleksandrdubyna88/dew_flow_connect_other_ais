/**
 * The prompt catalog, mirrored from the server's `PromptCatalog`.
 *
 * <p>Mirrored rather than fetched: the panel is drawn before any server has been started, and a
 * settings page that cannot list its own choices until a subprocess answers is a settings page
 * that shows an empty box on first open. A test holds the two lists together.</p>
 *
 * <p>Six choices per section, and the last twelve of them were MEASURED before they were shipped:
 * three drafts each, two runs each, seventy-two runs — `research/RESULTS_focused_prompts.md`. The
 * `purpose` strings are what a person picks from, so they say what the lens DOES rather than what
 * it is about.</p>
 */

export interface PromptChoice {
  readonly id: string;
  readonly role: string;
  readonly label: string;
  readonly purpose: string;
  readonly universal: boolean;
}

export const ROLES: readonly { readonly id: string; readonly label: string; readonly stage: string }[] = [
  { id: 'PlanCritique', label: 'Plan review', stage: 'plan' },
  // FIRST among the code roles: a broken written rule is the cheapest finding to act on and the
  // least arguable — there is a sentence to point at. That is why the conventions pass used to be
  // forced into round 1; as a role of its own it keeps the position and gains a budget.
  { id: 'Conventions', label: 'Conventions', stage: 'code' },
  { id: 'Architecture', label: 'Architecture', stage: 'code' },
  { id: 'SecurityReliability', label: 'Security & reliability', stage: 'code' },
  { id: 'UxDxPerformance', label: 'Performance & UX-DX', stage: 'code' },
];

export const PROMPTS: readonly PromptChoice[] = [
  // The one prompt of the Conventions role, and therefore its universal one. It judges the
  // change against what the project WROTE DOWN and nothing else — the standard its human authors
  // are held to, which no other reviewer was applying.
  { id: 'conventions', role: 'Conventions', label: 'Conventions', purpose: 'Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. A convention the reviewer believes in but the project never wrote is not a finding.', universal: true },

  { id: 'plan-critique', role: 'PlanCritique', label: 'Universal', purpose: 'The whole plan: assumptions, failure paths, order, testability.', universal: true },
  { id: 'plan-assumptions', role: 'PlanCritique', label: 'Assumptions & verification', purpose: 'What the plan takes for granted, and what it promises but never checks.', universal: false },
  { id: 'plan-human-path', role: 'PlanCritique', label: 'The human path', purpose: 'What a person does with it, and what happens when they do it wrong.', universal: false },
  { id: 'plan-data-loss', role: 'PlanCritique', label: 'Data loss & recovery', purpose: 'What the plan destroys, overwrites or moves — and whether a failure halfway can be undone.', universal: false },
  { id: 'plan-operability', role: 'PlanCritique', label: 'Operability', purpose: 'What it is like to run this at 3 a.m.: what is observable, what is alertable, what is diagnosable.', universal: false },
  { id: 'plan-scope-creep', role: 'PlanCritique', label: 'Scope & budget', purpose: 'What this plan quietly takes on beyond its goal, and what it will cost to keep.', universal: false },

  { id: 'architecture', role: 'Architecture', label: 'Universal', purpose: 'Boundaries, abstractions, consistency, and the plan-to-code gap.', universal: true },
  { id: 'arch-boundaries', role: 'Architecture', label: 'Boundaries & duplication', purpose: 'Dependency direction, layers reaching around each other, capabilities implemented twice.', universal: false },
  { id: 'arch-evolution', role: 'Architecture', label: 'Cost of the next change', purpose: 'What this change makes harder, and what is hard-coded that will have to vary.', universal: false },
  { id: 'arch-coupling', role: 'Architecture', label: 'Coupling & knowledge', purpose: 'What this change makes one part know about another, and what breaks when either moves.', universal: false },
  { id: 'arch-naming', role: 'Architecture', label: 'Names & the shape they imply', purpose: 'Where a name promises a shape the code does not have — the misreading it invites next.', universal: false },
  { id: 'arch-testability', role: 'Architecture', label: 'Testability of the seams', purpose: 'Which decision here can only be tested by starting a server, a browser or a clock.', universal: false },

  { id: 'security-reliability', role: 'SecurityReliability', label: 'Universal', purpose: 'Secrets, input, failure behaviour, state, trust boundaries.', universal: true },
  { id: 'sec-memory-leaks', role: 'SecurityReliability', label: 'What it holds and leaves', purpose: 'Secrets that outlive their use, resources leaked on the error path, what a kill -9 leaves behind.', universal: false },
  { id: 'sec-attack', role: 'SecurityReliability', label: 'Attack surface', purpose: 'What is trusted that was never checked, injection, privilege, and checks that fail open.', universal: false },
  { id: 'sec-blast-radius', role: 'SecurityReliability', label: 'Blast radius', purpose: 'If this one thing is wrong or compromised, how far does it reach before anything stops it.', universal: false },
  { id: 'sec-concurrency', role: 'SecurityReliability', label: 'Two at once', purpose: 'The same code running twice, a millisecond apart, over the state they share.', universal: false },
  { id: 'sec-supply-chain', role: 'SecurityReliability', label: 'What this change trusts', purpose: 'Every input, dependency and endpoint it believes without checking — and who can change them.', universal: false },

  { id: 'uxdx-performance', role: 'UxDxPerformance', label: 'Universal', purpose: 'Performance, UI state as code, and the ergonomics of a new API.', universal: true },
  { id: 'perf-scale', role: 'UxDxPerformance', label: 'Cost at scale', purpose: 'Which input grows, and what this code does when it does.', universal: false },
  { id: 'dx-ergonomics', role: 'UxDxPerformance', label: 'Ergonomics & waiting', purpose: 'Names that mislead, errors that name no cure, and work a person waits on.', universal: false },
  { id: 'perf-first-run', role: 'UxDxPerformance', label: 'The first run and the empty case', purpose: 'A brand-new machine, no cache, no config, nothing yet: the first thirty seconds, narrated.', universal: false },
  { id: 'perf-wasted-work', role: 'UxDxPerformance', label: 'Work done twice', purpose: 'What this recomputes, refetches or re-renders that it already had.', universal: false },
  { id: 'ux-undo', role: 'UxDxPerformance', label: 'What cannot be taken back', purpose: 'Using it wrongly on purpose: what state that leaves, and how somebody gets back.', universal: false },
];

export function promptsFor(role: string): readonly PromptChoice[] {
  return PROMPTS.filter((p) => p.role === role);
}

export function universalFor(role: string): PromptChoice {
  return promptsFor(role).find((p) => p.universal) ?? PROMPTS[0]!;
}


/** The one prompt that judges nothing but the project's own written rules. */
export const CONVENTIONS_ID = 'conventions';

/**
 * The first coai-mcp that knows `Conventions` is a ROLE.
 *
 * <p>Older servers have four roles, not five, and they DEGRADE rather than break — a distinction
 * this comment got wrong first and three gate reviewers then got wrong after it. The server builds
 * a round by iterating its OWN role list and reading one env key per role it knows, so a key naming
 * `Conventions` is never looked up and no enum ever sees the name. Nothing fails to start: the box
 * is drawn in the panel and the reviewer simply never runs.</p>
 *
 * <p>Which is quieter than a failure and worth saying out loud for exactly that reason. The panel
 * and the server are installed separately — an extension updates itself, a server is a binary
 * somebody presses a button to replace — so the Prompts section says a conventions check is visible
 * and unreachable, while that is true.</p>
 */
export const CONVENTIONS_ROLE_SINCE = '0.18.10';

/**
 * The `coai-mcp` that understands a role being switched OFF.
 *
 * <p>Same shape of skew as {@link CONVENTIONS_ROLE_SINCE}, and the same reason it must be said out
 * loud: an older server reads the per-role env keys it knows and simply never looks for
 * `COAI_ENABLED_*`, so it launches the reviewer anyway. That failure is silent AND backwards — the
 * panel shows a role unticked while it is the one thing still reviewing — which is worse than the
 * conventions skew, where an absent reviewer at least matches an absent result.</p>
 */
export const ROLE_SWITCH_SINCE = '0.18.13';

/**
 * What the panel shows as selected for one round — the stored choice, or what the server would
 * actually use, so the box never reads as "nothing" when a prompt is in fact chosen.
 *
 * <p><b>This function is a claim about another program.</b> Every branch here has to be the same
 * branch `PromptCatalog.ForRound` takes, and `panelServerPromptAgreement.test.ts` is what holds the
 * two together. It has lost two branches to that rule. A rotation fed by the panel's DEAL switch,
 * which the server's rotation never read — the picker named prompts nobody ran. And a round-1
 * conventions default, which existed because the conventions pass had no budget of its own; now
 * that Conventions is a ROLE there is nothing to substitute, and `hasRules` went with it.</p>
 */
export function selectedFor(
  role: string,
  round: number,
  stored: Readonly<Record<string, readonly string[]>>,
): string {
  const chosen = stored[role]?.[round - 1];

  return chosen !== undefined && promptsFor(role).some((p) => p.id === chosen)
    ? chosen
    : universalFor(role).id;
}
