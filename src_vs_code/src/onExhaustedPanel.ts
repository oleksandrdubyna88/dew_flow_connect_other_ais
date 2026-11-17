import { FEATURES } from './binaryFeatures';

/**
 * What to do when the rounds run out (the `coai.onExhausted` setting, `COAI_ON_EXHAUSTED` on the wire).
 *
 * <p>Defined here, with the list of choices and not in `settingsShape.ts` where the rest of the settings' types live,
 * because the settings shape imports the choices (to read a stored word) and the wire imports the type: a leaf both can
 * reach is what keeps those two out of an import cycle. `settingsShape.ts` re-exports it, so no caller moves.</p>
 */
export type OnExhausted = 'auto' | 'human' | 'continue' | 'good_enough' | 'escalate';

/**
 * What the gate section says about When the rounds run out — the choices, and the two sentences it adds under them.
 *
 * <p>Pure and `vscode`-free: the host reads the configuration and hands over plain values, so each sentence is a value a
 * test can hold. Every page, sentence and test that names a choice takes its label from {@link ON_EXHAUSTED_CHOICES}.</p>
 */

/** One choice of the select: its value, the text the option shows, and — when that is a sentence — the word that names it. */
export interface OnExhaustedChoice {
  readonly value: OnExhausted;
  readonly label: string;
  /** What a sentence calls the choice when the option text is longer than a name. */
  readonly short?: string;
}

/** The choices in the select's order — Auto first. */
export const ON_EXHAUSTED_CHOICES: readonly OnExhaustedChoice[] = [
  { value: 'auto', label: 'Auto — the gate raises no new hold and never waits for a person; it logs and moves on', short: 'Auto' },
  { value: 'human', label: 'Ask a human' },
  { value: 'continue', label: 'Continue, and say so' },
  { value: 'good_enough', label: 'Good enough — take what’s true and move on' },
  { value: 'escalate', label: 'Climb the ladder' },
];

/** The name of a choice as a sentence says it — `Auto`, not the option's whole sentence. */
export function labelOf(value: OnExhausted): string {
  const choice = ON_EXHAUSTED_CHOICES.find((one) => one.value === value);

  return choice === undefined ? value : (choice.short ?? choice.label);
}

/** The choice this value names, when it is one of the choices other than Auto — the only ones a workspace can override with. */
function knownOverride(value: unknown): OnExhausted | undefined {
  const choice = ON_EXHAUSTED_CHOICES.find((one) => one.value === value);

  return choice === undefined || choice.value === 'auto' ? undefined : choice.value;
}

/**
 * The policy a workspace's `.vscode/settings.json` sets, which wins over the choice the page shows: the folder value
 * when there is one, else the workspace value — and only when it is a known choice other than Auto, and this side's own
 * settings overlay does not hold the key (the overlay wins over a workspace value, `sideSettings.ts`).
 */
export function workspacePolicyOf(
  inspected: { readonly workspaceValue?: unknown; readonly workspaceFolderValue?: unknown },
  overlayHolds: boolean,
): OnExhausted | undefined {
  if (overlayHolds) {
    return undefined;
  }

  return knownOverride(inspected.workspaceFolderValue ?? inspected.workspaceValue);
}

const PREDATES =
  'This coai-mcp predates Auto: it runs Good enough and still asks a person when no reviewer answers — update it in Setup › MCP server.';

/** Whether the person is on Auto while the binary has SAID it does not list it — not while it has not said anything yet. */
function predatesAuto(value: OnExhausted, serverFeatures: readonly string[] | undefined): boolean {
  return value === 'auto' && serverFeatures !== undefined && !serverFeatures.includes(FEATURES.onExhaustedAuto);
}

/**
 * The plain-text sentences under the select, at most two, in this order: the binary that does not know Auto (only for a
 * person on Auto, and only once the binary has SAID it), then a workspace value that wins over the choice.
 */
export function gateNotes(
  value: OnExhausted,
  serverFeatures: readonly string[] | undefined,
  workspace: OnExhausted | undefined,
): readonly string[] {
  return [
    ...(predatesAuto(value, serverFeatures) ? [PREDATES] : []),
    ...(workspace === undefined ? [] : [workspaceSentence(workspace)]),
  ];
}

function workspaceSentence(workspace: OnExhausted): string {
  return `This workspace sets When the rounds run out to ${labelOf(workspace)} in its .vscode/settings.json, which wins over the choice above. Remove it there to use your own.`;
}
