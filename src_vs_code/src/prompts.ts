/**
 * The prompt catalog — a derivation of `shared/builtin-roles.json`, the seed both halves read.
 *
 * <p>Generated rather than fetched: the panel is drawn before any server has been started, and a
 * settings page that cannot list its own choices until a subprocess answers is a settings page
 * that shows an empty box on first open. So coai-mcp embeds the seed and the panel gets a
 * generated copy of it in `builtinRoles.generated.ts`; `builtinRoleCatalog.test.ts` reads the seed
 * and asserts the copy still matches.</p>
 *
 * <p>Hand-maintained until 2026-09-12, against a test that held it level with the server by parsing
 * C# source with a regular expression. Two lists of twenty-five prompts stay level that way for a
 * while and then quietly do not.</p>
 *
 * <p>Six choices per section, and the last twelve of them were MEASURED before they were shipped:
 * three drafts each, two runs each, seventy-two runs — `research/RESULTS_focused_prompts.md`. The
 * `purpose` strings are what a person picks from, so they say what the lens DOES rather than what
 * it is about.</p>
 */

import { BUILTIN_ROLES } from './builtinRoles.generated';

/**
 * One role as the seed spells it — the generated file's shape, and the CRUD tab's later.
 *
 * <p><b>`stage` is the SEED's word, not the panel's.</b> The seed says `result` where the panel has
 * always said `code`, because a later plan gives the same stage a document kind and "result" is
 * what both are: what the work produced. {@link ROLES} maps it, so nothing that already reads
 * `stage: 'code'` has to change.</p>
 */
export interface RoleDefinition {
  readonly id: string;
  readonly name: string;
  readonly stage: string;
  readonly programmingTask: boolean;
  readonly prompts: readonly { readonly id: string; readonly label: string; readonly purpose: string }[];
}

export interface PromptChoice {
  readonly id: string;
  readonly role: string;
  readonly label: string;
  readonly purpose: string;
  readonly universal: boolean;
}

/**
 * The roles the panel draws, in the seed's order.
 *
 * <p>`stage` is mapped from the seed's word to the panel's: a role the seed calls `result` is a
 * code role here, which is what every caller and every test already reads.</p>
 */
export const ROLES: readonly { readonly id: string; readonly label: string; readonly stage: string }[] =
  BUILTIN_ROLES.map((r) => ({ id: r.id, label: r.name, stage: r.stage === 'plan' ? 'plan' : 'code' }));

/**
 * Every prompt of every role, role by role.
 *
 * <p>A role's FIRST prompt is its universal one — the seed says so by position rather than by a
 * flag, which is the same rule `RoleDefinition.General` applies on the server side. One list rather
 * than a nested one because that is the shape `promptsFor` and the panel have always read.</p>
 */
export const PROMPTS: readonly PromptChoice[] = BUILTIN_ROLES.flatMap((r) =>
  r.prompts.map((p, index) => ({
    id: p.id,
    role: r.id,
    label: p.label,
    purpose: p.purpose,
    universal: index === 0,
  })),
);

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
