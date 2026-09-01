/**
 * The prompt catalog, mirrored from the server's `PromptCatalog`.
 *
 * <p>Mirrored rather than fetched: the panel is drawn before any server has been started, and a
 * settings page that cannot list its own choices until a subprocess answers is a settings page
 * that shows an empty box on first open. A test holds the two lists together.</p>
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
  { id: 'Architecture', label: 'Architecture', stage: 'code' },
  { id: 'SecurityReliability', label: 'Security & reliability', stage: 'code' },
  { id: 'UxDxPerformance', label: 'Performance & UX-DX', stage: 'code' },
];

export const PROMPTS: readonly PromptChoice[] = [
  { id: 'plan-critique', role: 'PlanCritique', label: 'Universal', purpose: 'The whole plan: assumptions, failure paths, order, testability.', universal: true },
  { id: 'plan-assumptions', role: 'PlanCritique', label: 'Assumptions & verification', purpose: 'What the plan takes for granted, and what it promises but never checks.', universal: false },
  { id: 'plan-human-path', role: 'PlanCritique', label: 'The human path', purpose: 'What a person does with it, and what happens when they do it wrong.', universal: false },

  { id: 'conventions', role: 'Architecture', label: 'Conventions', purpose: 'Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. A convention the reviewer believes in but the project never wrote is not a finding.', universal: false },
  { id: 'architecture', role: 'Architecture', label: 'Universal', purpose: 'Boundaries, abstractions, consistency, and the plan-to-code gap.', universal: true },
  { id: 'arch-boundaries', role: 'Architecture', label: 'Boundaries & duplication', purpose: 'Dependency direction, layers reaching around each other, capabilities implemented twice.', universal: false },
  { id: 'arch-evolution', role: 'Architecture', label: 'Cost of the next change', purpose: 'What this change makes harder, and what is hard-coded that will have to vary.', universal: false },

  { id: 'conventions', role: 'SecurityReliability', label: 'Conventions', purpose: 'Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. A convention the reviewer believes in but the project never wrote is not a finding.', universal: false },
  { id: 'security-reliability', role: 'SecurityReliability', label: 'Universal', purpose: 'Secrets, input, failure behaviour, state, trust boundaries.', universal: true },
  { id: 'sec-memory-leaks', role: 'SecurityReliability', label: 'What it holds and leaves', purpose: 'Secrets that outlive their use, resources leaked on the error path, what a kill -9 leaves behind.', universal: false },
  { id: 'sec-attack', role: 'SecurityReliability', label: 'Attack surface', purpose: 'What is trusted that was never checked, injection, privilege, and checks that fail open.', universal: false },

  { id: 'conventions', role: 'UxDxPerformance', label: 'Conventions', purpose: 'Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. A convention the reviewer believes in but the project never wrote is not a finding.', universal: false },
  { id: 'uxdx-performance', role: 'UxDxPerformance', label: 'Universal', purpose: 'Performance, UI state as code, and the ergonomics of a new API.', universal: true },
  { id: 'perf-scale', role: 'UxDxPerformance', label: 'Cost at scale', purpose: 'Which input grows, and what this code does when it does.', universal: false },
  { id: 'dx-ergonomics', role: 'UxDxPerformance', label: 'Ergonomics & waiting', purpose: 'Names that mislead, errors that name no cure, and work a person waits on.', universal: false },
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
 * What the panel shows as selected for one round — the stored choice, or what the server would
 * actually use, so the box never reads as "nothing" when a prompt is in fact chosen.
 *
 * <p><b>This function is a claim about another program.</b> Every branch here has to be the same
 * branch `PromptCatalog.ForRound` takes, and `panelServerPromptAgreement.test.ts` is what holds the
 * two together. There used to be a third branch — rotate through the lenses when a round is unset
 * — fed by the panel's DEAL switch, which the server's rotation never read; the picker named
 * prompts nobody ran. A branch that only one of the two programs has is not a feature.</p>
 */
export function selectedFor(
  role: string,
  round: number,
  stored: Readonly<Record<string, readonly string[]>>,
  hasRules = true,
): string {
  const chosen = stored[role]?.[round - 1];
  if (chosen !== undefined && promptsFor(role).some((p) => p.id === chosen)) {
    return chosen;
  }
  // ROUND ONE of a CODE role is the conventions pass, mirroring PromptCatalog.ForRound on the
  // server. Without this the panel showed `Universal` for a round the server would run
  // `conventions` in — the panel saying one thing while the server does another, which is the
  // defect this product keeps producing. Its twin is ConventionsPassTests in the C# suite.
  //
  // `hasRules` is optimistic here because the panel cannot know: the server decides at round time
  // by looking in the worktree. The section text says so, and a repo with no written rules falls
  // back to the universal prompt on the server side.
  if (hasRules && round === 1 && role !== 'PlanCritique') {
    return CONVENTIONS_ID;
  }

  return universalFor(role).id;
}
