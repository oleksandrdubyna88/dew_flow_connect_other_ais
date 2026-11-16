import * as vscode from 'vscode';

import { notify, notifyAndAsk } from './notify';
import {
  ADD,
  askTitle,
  latchedCheck,
  toldTitle,
  type SkipShellMemory,
  type SkipShellOutcome,
  type SkipShellSeams,
} from './skipShell';

/**
 * The host half of the skip-shell check (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, E3.S3): it only supplies the `vscode`
 * seams to `skipShell.ts`, which decides everything. The one write of the whole feature is `update` below, and it targets the
 * USER settings — the global target — and nothing else. `skipShellWiring.test.ts` holds that.
 */

const SECTION = 'terminal.integrated';
const SETTING = 'commandsToSkipShell';

/** The `globalState` key of the answers: at most one `added`/`dismissed` row and one `told` row per layer for each id, under 1 KiB. */
const MEMORY_KEY = 'coai.skipShellAnswers';

/** The layers of the setting, as VS Code reports them. */
const inspectSetting = (): ReturnType<SkipShellSeams['inspect']> =>
  vscode.workspace.getConfiguration(SECTION).inspect<unknown>(SETTING);

/** The offer: one notification with one button, and the wait for the person. */
const askToAdd = (ids: readonly string[]): Promise<string | undefined> => notifyAndAsk({
  as: 'information',
  class: 'offer',
  source: 'skipShell',
  code: 'skip-list-hides-terminal-keys',
  title: askTitle(ids),
  action: ADD,
});

const tellAboutLayer: SkipShellSeams['tell'] = (layer, ids) => notify({
  as: 'information',
  class: 'outcome',
  source: 'skipShell',
  code: 'skip-list-hidden-by-workspace',
  subject: layer,
  title: toldTitle(layer, ids),
});

const sayFailed: SkipShellSeams['failed'] = (sentence) => notify({
  as: 'warning',
  class: 'failure',
  source: 'skipShell',
  code: 'skip-list-not-written',
  title: sentence,
});

/** The USER settings, and never the workspace or a folder. */
const writeUserList: SkipShellSeams['update'] = (next) =>
  vscode.workspace.getConfiguration(SECTION).update(SETTING, [...next], vscode.ConfigurationTarget.Global);

const memoryIn = (state: vscode.Memento): SkipShellMemory => ({
  read: () => state.get<unknown>(MEMORY_KEY),
  write: (next) => state.update(MEMORY_KEY, next),
});

/**
 * What a host scenario may reach of the BOUND check. The extension hands it out only when it runs in test mode
 * (`ExtensionMode.Test`): in production the field is absent, so no other extension can replace the offer or write the memory.
 */
export interface SkipShellTestDoor {
  /** Replace the offer; `undefined` puts the real notification back. */
  readonly replaceAsk: (ask: SkipShellSeams['ask'] | undefined) => void;
  /** The bound check, behind its latch. */
  readonly check: () => Promise<SkipShellOutcome>;
  /** The bound memory, so a scenario can save it and put back exactly what it found (`undefined` removes the key). */
  readonly memory: { readonly read: () => unknown; readonly write: (value: unknown) => PromiseLike<void> };
}

/**
 * Check once at activation and again whenever the setting changes — both through ONE latch, so a second trigger while a
 * notification is open does nothing. Nothing here waits for the person: the first check runs detached.
 */
export function bindSkipShell(context: vscode.ExtensionContext): SkipShellTestDoor {
  let ask: SkipShellSeams['ask'] = askToAdd;
  const memory = memoryIn(context.globalState);
  const check = latchedCheck({
    inspect: inspectSetting,
    ask: (ids) => ask(ids),
    tell: tellAboutLayer,
    update: writeUserList,
    memory,
    failed: sayFailed,
    now: () => new Date(),
  });
  const run = (): void => {
    check().catch((reason: unknown) => {
      console.warn('ConnectOtherAIs: the skip-list check of the terminal keys could not run', reason);
    });
  };
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration((change) => {
    if (change.affectsConfiguration(`${SECTION}.${SETTING}`)) {
      run();
    }
  }));
  run();

  const keep = { read: memory.read, write: (value: unknown) => context.globalState.update(MEMORY_KEY, value) };

  return { replaceAsk: (next) => { ask = next ?? askToAdd; }, check, memory: keep };
}
