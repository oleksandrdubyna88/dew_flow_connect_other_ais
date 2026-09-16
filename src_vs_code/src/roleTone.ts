/**
 * One colour per review role, for every view that draws one.
 *
 * <p>The sidebar has given each role its own tone since the settings panel was written; the page a
 * person EDITS those roles on gave every one of them the same blue. Two views of the same seven
 * roles, coloured by two different rules — issue #293's third picture, and the reason this module
 * exists rather than a second copy of the map in `rolesPage.ts`.</p>
 *
 * <p>Both halves travel together on purpose. A consumer needs the class to write AND the stylesheet
 * that gives that class a colour; handing out only the map is how a view ends up emitting
 * `class="role-arch"` with nothing anywhere defining `--tone-arch`, which draws no edge at all and
 * says nothing about itself.</p>
 */

/**
 * Which tone wraps each role. The colour is never the only signal — the name is always written.
 *
 * <p>Five entries for the five roles this product ships. A role a person added is not in it, and
 * takes its stage's tone below — which means it shares a colour with a shipped role. That is
 * deliberate: matching the sidebar means matching it where it is arbitrary too, and the alternative
 * is two views disagreeing about the same row.</p>
 */
const ROLE_TONE: Readonly<Record<string, string>> = {
  PlanCritique: 'plan',
  Conventions: 'conv',
  Architecture: 'arch',
  SecurityReliability: 'sec',
  UxDxPerformance: 'uxdx',
};

/**
 * The tone a role is drawn in — the shipped colour, or the one its stage implies.
 *
 * <p>`Object.hasOwn` rather than an index and a `??`: the id comes from a settings file a person
 * writes, so `constructor` and `__proto__` are ids somebody can put in it, and an inherited value
 * would become a class name.</p>
 */
export function roleTone(roleId: string, stage: string): string {
  if (Object.hasOwn(ROLE_TONE, roleId)) {
    return ROLE_TONE[roleId];
  }

  return stage === 'plan' ? 'plan' : 'arch';
}

/**
 * The palette itself, and the rules that spend it.
 *
 * <p>Verbatim from `panelView.ts`, where it lived alone: the panel's rendering must not change by
 * one byte when a second view starts reading the same block.</p>
 */
export const ROLE_TONE_CSS = `  /* The role palette, taken from the sibling product's own token set
     (creds/src_vs_code/src/entityFormStyles.ts): a charts token with the hex it falls back to, so a
     theme that defines them wins and one that does not still gets the intended colour. */
  :root {
    --tone-plan: var(--vscode-charts-purple, #c586c0);
    /* Conventions takes yellow: it reads as "check this first", and the four code roles
       then span the palette instead of crowding blue-orange-green. */
    --tone-conv: var(--vscode-charts-yellow, #d7ba7d);
    --tone-arch: var(--vscode-charts-blue, #569cd6);
    --tone-sec: var(--vscode-charts-orange, #ce9178);
    --tone-uxdx: var(--vscode-charts-green, #b5cea8);
    --tone-limits: var(--vscode-charts-yellow, #d7ba7d);
    --tone-keys: var(--vscode-charts-red, #f14c4c);
    --tone-code: var(--vscode-widget-border, #454545);
  }
  .role-plan { border-left-color: var(--tone-plan); }
  .role-arch { border-left-color: var(--tone-arch); }
  .role-sec { border-left-color: var(--tone-sec); }
  .role-uxdx { border-left-color: var(--tone-uxdx); }
  .role-conv { border-left-color: var(--tone-conv); }
  .role-code { border-left-color: var(--tone-code); }`;
