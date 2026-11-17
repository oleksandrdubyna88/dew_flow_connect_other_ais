import { ON_EXHAUSTED_CHOICES, labelOf } from './onExhaustedPanel';

/**
 * What a person is told when *When the rounds run out* is reset to Auto once, and which sentence a window is shown
 * (todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.3). Pure and `vscode`-free: the host hands over what it read,
 * and each sentence is a value a test can hold. The words are the parent plan's § 4.6, and only the coordinator changes them.
 */

/** The notification in a window that runs `auto`. */
export const NOW_AUTO = 'When the rounds run out is now Auto — the gate raises no new hold and never waits for you; '
  + 'it logs and moves on. Your AI may still ask you a question its consultants could not settle, or when no '
  + 'consultant can be had. Change it in Reviews › The gate.';

/** The warning when the settings file refused the change. */
export const NOT_RESET = 'When the rounds run out could not be reset to Auto in your settings: the settings file '
  + 'refused the change. Your earlier choice there is still in force. ConnectOtherAIs tries again at the next start.';

/** Where the value a window runs is set. */
export type SetWhere = 'side' | 'workspace' | 'user';

/** The parent's three places: "this side's own settings", "this workspace's .vscode/settings.json", "your user settings". */
export const WHERE: Readonly<Record<SetWhere, string>> = {
  side: 'this side\'s own settings',
  workspace: 'this workspace\'s .vscode/settings.json',
  user: 'your user settings',
};

/** The notification in a window where another value stays in force. */
export function staysInForce(label: string, where: string): string {
  return `When the rounds run out now defaults to Auto, but ${label} stays in force in this window: ${where} sets it. `
    + 'Change it in Reviews › The gate.';
}

/** The stored value a window reads, and the location that holds it; no `where` when no location holds one. */
export interface InForce {
  readonly value: unknown;
  readonly where?: SetWhere;
}

/** What `WorkspaceConfiguration.inspect` answers, narrowed to the layers a window's reader holds. */
export interface HeldLayers {
  readonly globalValue?: unknown;
  readonly workspaceValue?: unknown;
  readonly workspaceFolderValue?: unknown;
}

/**
 * The first location that holds a value, in the reader's order: this side's overlay — only while the per-side switch is
 * on, and when the record HAS the property, as `overlaidReader` asks — then the folder, the workspace, the user layer
 * (the first that is neither `undefined` nor `null`, as `heldValue`'s `??` does). A folder and a workspace are both "this
 * workspace's .vscode/settings.json".
 */
export function inForceOf(layers: HeldLayers | undefined, perSide: boolean, overlay: Readonly<Record<string, unknown>>): InForce {
  if (perSide && Object.prototype.hasOwnProperty.call(overlay, 'onExhausted')) {
    return { value: overlay['onExhausted'], where: 'side' };
  }

  return inLayers(layers ?? {});
}

/** A layer holds a value when it is neither `undefined` nor `null` — what `heldValue`'s `??` skips. */
function isHeld(value: unknown): boolean {
  return value !== undefined && value !== null;
}

function inLayers(layers: HeldLayers): InForce {
  const workspace = layers.workspaceFolderValue ?? layers.workspaceValue;
  if (isHeld(workspace)) {
    return { value: workspace, where: 'workspace' };
  }

  return isHeld(layers.globalValue) ? { value: layers.globalValue, where: 'user' } : { value: undefined };
}

/**
 * `NOW_AUTO` when the window runs `auto` — no value, `auto`, or a word that is no choice (the reader answers `auto` for
 * it) — else the second sentence, naming the value and where it is set.
 */
export function sentenceFor(inForce: InForce): string {
  const stays = ON_EXHAUSTED_CHOICES.find((choice) => choice.value === inForce.value && choice.value !== 'auto');
  if (stays === undefined || inForce.where === undefined) {
    return NOW_AUTO;
  }

  return staysInForce(labelOf(stays.value), WHERE[inForce.where]);
}
