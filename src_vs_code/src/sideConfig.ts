import * as vscode from 'vscode';
import { ConfigReader } from './settingsShape';
import { sideConfigReader } from './sideSettings';
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
