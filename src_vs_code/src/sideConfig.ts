import * as vscode from 'vscode';
import { ConfigReader, OVERLAID_SETTINGS } from './settingsShape';
import { sideConfigReader, writeOverlay } from './sideSettings';
import { thisSide } from './installer';

/**
 * The ONE construction of this side's settings reader.
 *
 * <p><b>Why a module of four lines.</b> `sideSettings.ts` holds the decision and is deliberately free
 * of `vscode` so it can be tested at all; but WHICH side is running can only be answered with
 * `vscode.env.remoteName`, so the side and the store have to be assembled somewhere that imports the
 * host. That assembly was written out twice — once in the chat and once beside the server settings
 * file — and a reviewer named the obvious consequence: two copies of a four-argument call drift, and
 * the argument that drifts is the one nobody can see is wrong.</p>
 *
 * <p><b>It is also what makes "this side" an invariant rather than a habit.</b> `sideConfigReader`
 * takes any `Side` at all, so a caller holding a cached or unrelated one would read another side's
 * `vendors` — the wrong `executablePath`, the wrong CLI, and no type error anywhere. Nobody outside
 * this function passes a `Side` now: the only door takes the CONTEXT and derives the side from it.
 * (codex and gemini, the code round.)</p>
 */
export function readerFor(
  context: vscode.ExtensionContext,
  config: vscode.WorkspaceConfiguration,
): ConfigReader {
  return sideConfigReader(
    (section) => config.get(section),
    config.get<boolean>('perSideSettings') === true,
    context.globalState,
    thisSide(context.globalStorageUri),
  );
}

/**
 * The ONE write of a `coai.*` setting, and the mirror of {@link readerFor}.
 *
 * <p>Here for the same reason the reader is: a write that decides for itself which layer it belongs
 * in is a setting that silently goes to the wrong one. A side that keeps its own settings never
 * writes to `settings.json` — that file is the CLIENT's, and VS Code hands it to every extension
 * host, which is the whole reason the per-side switch exists.</p>
 *
 * <p>The roles page is why this is a function rather than a private method of the panel. It wrote
 * `roles` — a setting that IS in `OVERLAID_SETTINGS` — straight to the global target, so a role
 * added on one side appeared on every side and ran in reviews it was never meant for, while the page
 * itself said "saved for this side of the machine". Two copies of a rule is one copy of the rule.</p>
 */
export async function saveSetting(
  context: vscode.ExtensionContext,
  config: vscode.WorkspaceConfiguration,
  key: string,
  value: unknown,
): Promise<void> {
  if (config.get<boolean>('perSideSettings') === true && OVERLAID_SETTINGS.includes(key)) {
    await writeOverlay(context.globalState, thisSide(context.globalStorageUri), key, value);

    return;
  }

  try {
    await config.update(key, value, vscode.ConfigurationTarget.Global);
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    void vscode.window.showErrorMessage(`ConnectOtherAIs could not save "coai.${key}": ${detail}`);
  }
}
