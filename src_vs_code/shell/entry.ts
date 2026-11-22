// The extension's entry: `activate` REGISTERS and does nothing else. No file, process, network, memento
// write, configuration read or timer lives here; each command, view and panel loads its module when it is
// first used. `src/test/entryPurity.test.ts` runs this file against a recording `vscode` and fails on any
// call that is not a `register*` or a `subscriptions.push`; `scripts/entry-firewall.mjs` bounds what the bundle
// may contain (shell/entry.ts, lazy.ts, ids.ts, measure.ts) and its size (16 KiB).
// `./measure` FIRST: its first statement is the module-evaluation mark of the cold-start measurement.
import { mark, registerMeasure } from './measure';
import * as vscode from 'vscode';
import { COMMANDS, MODULE_LEGACY, PANEL_CHAT, VIEW_PANEL } from './ids';
import { lazy, loadedModule } from './lazy';

/** What `activate` returns: a door for the test host, which starts the legacy module on demand. */
export interface ShellExports {
  readonly legacy: () => Promise<unknown>;
}

export function activate(context: vscode.ExtensionContext): ShellExports {
  // M1 starts HERE, the first statement: the host's delay between evaluating this file and calling `activate` is `hostDelay`, not the entry's time.
  mark('activateStart');
  const legacy = () => lazy(MODULE_LEGACY);
  context.subscriptions.push(
    ...COMMANDS.map((id) => vscode.commands.registerCommand(id, (...args: unknown[]) => legacy().then((module) => module.command(context, id, args)))),
    vscode.window.registerWebviewViewProvider(VIEW_PANEL, {
      resolveWebviewView: (view, resolveContext, token) => {
        mark('viewResolve');

        return legacy().then((module) => module.resolveView(context, view, resolveContext, token));
      },
    }),
    vscode.window.registerWebviewPanelSerializer(PANEL_CHAT, {
      deserializeWebviewPanel: (panel, state) => legacy().then((module) => module.restorePanel(context, panel, state)),
    }),
    vscode.window.registerUriHandler({
      handleUri: (uri) => legacy().then((module) => module.handleUri(context, uri)),
    }),
    // Nothing at all unless COAI_MEASURE=1 (shell/measure.ts); then the command that answers the cold-start marks.
    ...registerMeasure(vscode, process.env),
  );
  mark('activateReturn');

  return { legacy: () => legacy().then((module) => module.start(context)) };
}

export function deactivate(): Thenable<void> | undefined {
  return loadedModule(MODULE_LEGACY)?.deactivate();
}
