// The extension's entry: `activate` REGISTERS and does nothing else. No file, process, network, memento
// write, configuration read or timer lives here; each command, view and panel loads its module when it is
// first used. `src/test/entryPurity.test.ts` runs this file against a recording `vscode` and fails on any
// call that is not a `register*` or a `subscriptions.push`; `scripts/entry-firewall.mjs` bounds what the bundle
// may contain (shell/entry.ts, lazy.ts, ids.ts, measure.ts) and its size (16 KiB).
// `./measure` FIRST: its first statement is the module-evaluation mark of the cold-start measurement.
import { mark, registerMeasure } from './measure';
import * as vscode from 'vscode';
import { COMMANDS, MODULE_CHAT, MODULE_INSTALL, MODULE_LEGACY, MODULE_SIDEBAR, moduleOf, PANEL_CHAT, VIEW_PANEL } from './ids';
import { type ChatModule, type InstallModule, lazy, loadedModule, type ShellModule, type SidebarModule } from './lazy';

/** What `activate` returns: doors for the test host, which starts the legacy module, the chat module and the install work on demand. */
export interface ShellExports {
  readonly legacy: () => Promise<unknown>;
  readonly chat: () => Promise<unknown>;
  readonly install: () => Promise<unknown>;
}

/** Every command the shell registers: a thunk that loads the module that owns it, and — after the FIRST command has answered — asks for the install work, once. */
function registerCommands(
  context: vscode.ExtensionContext,
  loaders: { readonly legacy: () => Promise<ShellModule>; readonly chat: () => Promise<ChatModule>; readonly install: () => Promise<InstallModule> },
  startInstall: () => Promise<unknown>,
): vscode.Disposable[] {
  const moduleFor = (id: string) => {
    const owner = moduleOf(id);

    return owner === MODULE_CHAT ? loaders.chat() : owner === MODULE_INSTALL ? loaders.install() : loaders.legacy();
  };
  let installAsked = false;
  const afterFirstCommand = (): void => {
    if (!installAsked) {
      installAsked = true;
      startInstall().catch(() => undefined);
    }
  };

  return COMMANDS.map((id) => vscode.commands.registerCommand(id, (...args: unknown[]) => {
    const done = moduleFor(id).then((module) => module.command(context, id, args));
    void done.then(afterFirstCommand, afterFirstCommand);

    return done;
  }));
}

export function activate(context: vscode.ExtensionContext): ShellExports {
  // M1 starts HERE, the first statement: the host's delay between evaluating this file and calling `activate` is `hostDelay`, not the entry's time.
  mark('activateStart');
  const legacy = () => lazy(MODULE_LEGACY);
  // Chat runs from its own module (v2 E4): its commands and its serializer never load the legacy one.
  const chat = () => lazy<ChatModule>(MODULE_CHAT);
  // The install work (v2 E4, s5-install-module) is never started here: the sidebar asks for it after its first `menuReady`, and the first command asks for it once
  // that command has answered. `start` is once per activation inside the module, so asking twice is one run.
  const install = () => lazy<InstallModule>(MODULE_INSTALL);
  const startInstall = () => install().then((module) => module.start(context));
  context.subscriptions.push(
    ...registerCommands(context, { legacy, chat, install }, startInstall),
    vscode.window.registerWebviewViewProvider(VIEW_PANEL, {
      resolveWebviewView: (view, resolveContext, token) => {
        mark('viewResolve');
        // The sidebar module (v2 E4) draws the sidebar's real menu NOW and, after the page says the menu is ready, loads the legacy module and hands it
        // this same view, and starts chat. If it cannot be loaded or throws, the legacy module resolves the view alone, as it did before it existed.
        return lazy<SidebarModule>(MODULE_SIDEBAR)
          .then((sidebar) => sidebar.resolveSidebar(context, view, resolveContext, token, legacy, () => chat().then((module) => module.start(context)), startInstall))
          .catch(() => legacy().then((module) => module.resolveView(context, view, resolveContext, token)));
      },
    }),
    vscode.window.registerWebviewPanelSerializer(PANEL_CHAT, {
      deserializeWebviewPanel: (panel, state) => chat().then((module) => module.restorePanel(context, panel, state)),
    }),
    vscode.window.registerUriHandler({
      handleUri: (uri) => legacy().then((module) => module.handleUri(context, uri)),
    }),
    // Nothing at all unless COAI_MEASURE=1 (shell/measure.ts); then the command that answers the cold-start marks.
    ...registerMeasure(vscode, process.env),
  );
  mark('activateReturn');

  return { legacy: () => legacy().then((module) => module.start(context)), chat: () => chat().then((module) => module.start(context)), install: startInstall };
}

/** Every module that was loaded is told; one that was never loaded is not loaded to be told. Both answers are awaited, the chat drain included. */
export function deactivate(): Thenable<void> | undefined {
  const pending = [loadedModule<ShellModule>(MODULE_LEGACY), loadedModule<ChatModule>(MODULE_CHAT), loadedModule<InstallModule>(MODULE_INSTALL)].map((module) => module?.deactivate()).filter((one) => one !== undefined);

  return pending.length === 0 ? undefined : Promise.all(pending).then(() => undefined);
}
