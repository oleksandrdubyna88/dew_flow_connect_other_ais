import * as vscode from 'vscode';
import { installFailureHint, SingleFlight } from './coaiInstall';
import { claudeSnippet, copiedMessage } from './claudeSnippet';
import { pastedSnippetStatus } from './snippetInWorkspace';
import { clientTargetsLine, CLIENT_TARGETS, installedMessage, mcpServerBlock } from './mcpBlock';
import { installLatest, latestServerVersion, serverExists, serverOnThisSide, serverPath } from './installer';
import { EscalationWatcher } from './escalationWatcher';
import { PanelProvider } from './panelProvider';
import { showHelp } from './helpPanel';
import { parseSession, SessionFile } from './rounds';
import { rowsFrom } from './roundsLog';
import { RoundsLogPanel } from './roundsLogPanel';
import { envBlock, settingsFrom } from './settingsShape';
import { ServerSettingsSync } from './serverSettingsSync';
import { vendorsFrom } from './vendors';

/**
 * ConnectOtherAIs — the human surface. Five commands, one directory watcher, and no port: the
 * review itself lives in `coai-mcp`, which an MCP client owns and starts.
 *
 * <p>The restraint is deliberate and inherited from CredsForDevs: the config block is OFFERED on
 * the clipboard, never written into another program's file; the binary goes into extension
 * storage, never onto the `PATH`. The one thing the server needs to reach us for — a question for
 * a person — arrives as a FILE in the data directory this extension already reads, which is why
 * there is still nothing listening on a socket.</p>
 */
export function activate(context: vscode.ExtensionContext): void {
  const watcher = new EscalationWatcher(dataDir());
  // Declared before the panel so the hooks can reach it; assigned right after.
  let roundsLog: RoundsLogPanel;
  let panelRef: PanelProvider;
  roundsLog = new RoundsLogPanel({
    onAnswer: async (id) => {
      const question = watcher.openQuestions.find((q) => q.id === id);
      if (question !== undefined) {
        await watcher.answerCommand(question);
      }
    },
    // The spending tab's two commands go to the sidebar's provider, which owns the window choice,
    // the price cache and the "forget" marks — one owner, whichever surface shows the numbers.
    onUsageWindow: async (window) => {
      panelRef.setUsageWindow(window);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
    onForget: async (provider) => {
      await panelRef.forgetUsage(provider);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
  });
  const panel = panelRef = new PanelProvider(context, watcher, dataDir(), async (id) => {
    const question = watcher.openQuestions.find((q) => q.id === id);
    if (question !== undefined) {
      await watcher.answerCommand(question);
    }
  });
  // The panel repaints whenever the watcher's state moves, so a question answered in the modal
  // disappears from the sidebar without anyone asking it to.
  watcher.onChanged = () => {
    // What the title bar switches on: green while somebody is waiting on an answer.
    void vscode.commands.executeCommand('setContext', 'coai.hasQuestions', watcher.openQuestions.length > 0);
    void panel.render();
    // And the rounds log, if it is open: a round advances on its own, so the page it is shown on
    // has to as well. Nothing is read for it when nobody is looking, and nothing is pushed when
    // nothing changed.
    void refreshRoundsLog(roundsLog, watcher, panel);
  };
  watcher.start();

  // The settings the server reads, mirrored from activation — never from the panel.
  //
  // This was the defect a colleague hit on macOS: the write lived in `PanelProvider.render()`
  // behind its view guard, and the configuration listener was registered inside
  // `resolveWebviewView`. VS Code resolves a webview view LAZILY, so in a window where nobody had
  // opened the ConnectOtherAIs panel, nothing watched `coai.*` and nothing wrote the file. They
  // set `onExhausted` to `good_enough`, restarted, and the server went on answering `call_human`
  // from an `env` block pasted months earlier — ten third rounds in a row.
  const settingsSync = new ServerSettingsSync(readCoaiConfiguration, (json) => writeSettingsFile(json));
  void settingsSync.sync();

  context.subscriptions.push(
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('coai')) {
        void settingsSync.sync();
        void panel.render();
      }
    }),
    watcher,
    vscode.window.registerWebviewViewProvider(PanelProvider.viewType, panel),
    vscode.commands.registerCommand('coai.help', showHelp),
    // Both doors repaint. The panel's own button used to be the only path that did — it awaits
    // the command and then renders — so an update started from THIS menu left the Server section
    // showing the version it had replaced, which is the very symptom the button was fixed for.
    vscode.commands.registerCommand('coai.installServer', async () => {
      await installServer(context);
      await panel.render();
    }),
    vscode.commands.registerCommand('coai.copyConfigBlock', () => copyConfigBlock(context)),
    vscode.commands.registerCommand('coai.copyClaudeSnippet', copyClaudeSnippet),
    vscode.commands.registerCommand('coai.showRounds', () => showRoundsLog(roundsLog, watcher, panel)),
    vscode.commands.registerCommand('coai.answerQuestion', () => answerQuestion(watcher)),
    // The same action under a second id, so the title bar can show a green icon while a question
    // is waiting — a menu icon cannot be recoloured by state, but which command is shown can.
    vscode.commands.registerCommand('coai.answerQuestionWaiting', () => answerQuestion(watcher)),
  );

  void offerUpdate(context);
}

export function deactivate(): void {
  // The watcher is a subscription; VS Code disposes it. No server, no port, nothing else to stop.
}

/** The `coai.*` settings as the sync wants them: one read, both halves, no VS Code type leaving. */
function readCoaiConfiguration() {
  const config = vscode.workspace.getConfiguration('coai');
  return {
    settings: settingsFrom((section) => config.get(section)),
    vendors: vendorsFrom(config.get('vendors')),
  };
}

/**
 * Writes the settings file the server reads out of its own data directory.
 *
 * <p>The directory is created first: on a machine where no review has ever run it does not exist
 * yet, and the settings are the one thing that has to be there BEFORE the first round rather than
 * after it.</p>
 */
async function writeSettingsFile(json: string): Promise<void> {
  await vscode.workspace.fs.createDirectory(dataDir());
  await vscode.workspace.fs.writeFile(
    vscode.Uri.joinPath(dataDir(), 'settings.json'),
    new TextEncoder().encode(json),
  );
}

/** Where `coai-mcp` keeps its sessions and escalations — its default, or `COAI_DATA_DIR`. */
function dataDir(): vscode.Uri {
  const configured = process.env['COAI_DATA_DIR'];
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;
  return vscode.Uri.file(configured ?? `${localAppData}/coai-mcp`);
}

/** Answer an open question by hand — the same path the modal's button takes. */
async function answerQuestion(watcher: EscalationWatcher): Promise<void> {
  await watcher.refresh();
  const open = watcher.openQuestions;
  if (open.length === 0) {
    void vscode.window.showInformationMessage('No ConnectOtherAIs review is waiting on an answer.');
    return;
  }

  const picked =
    open.length === 1
      ? open[0]
      : await vscode.window
          .showQuickPick(
            open.map((e) => ({ label: e.branch, detail: e.question, escalation: e })),
            { title: 'Which question?' },
          )
          .then((choice) => choice?.escalation);

  if (picked !== undefined) {
    await watcher.answerCommand(picked);
  }
}

function settings(): ReturnType<typeof settingsFrom> {
  const config = vscode.workspace.getConfiguration('coai');
  return settingsFrom((section) => config.get(section));
}

/** One install at a time: the panel button and the ⋯ menu are two doors to the same work. */
const installing = new SingleFlight<void>();

async function installServer(context: vscode.ExtensionContext): Promise<void> {
  await installing.run(() => install(context));
}

async function install(context: vscode.ExtensionContext): Promise<void> {
  try {
    const target = await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: 'Installing coai-mcp…' },
      () => installLatest(context.globalStorageUri, context.globalState),
    );
    await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath, envBlock(settings())));
    const targets = clientTargetsLine(CLIENT_TARGETS);
    void vscode.window.showInformationMessage(`${installedMessage(target.fsPath)} Paste it into: ${targets}`);
  } catch (error) {
    const raw = message(error);
    const hint = installFailureHint(raw, codeOf(error));
    void vscode.window.showErrorMessage(
      hint.length > 0 ? `coai-mcp was not updated: ${hint}` : `coai-mcp was not installed: ${raw}`,
    );
  }
}

async function copyConfigBlock(context: vscode.ExtensionContext): Promise<void> {
  const path = serverPath(context.globalStorageUri);
  if (path === undefined) {
    void vscode.window.showErrorMessage('There is no published coai-mcp build for this platform yet.');
    return;
  }
  // Whether it is there is a question about THIS side's disk. It used to be a question about a
  // remembered version shared by every window of the profile, so a WSL window handed out a path
  // that did not exist and called it installed. `stat` answers it; asking for the full status would
  // launch a `--version` process to learn something the stat already knew.
  const installed = await serverExists(context.globalStorageUri);
  await vscode.env.clipboard.writeText(mcpServerBlock(path.fsPath, envBlock(settings())));
  void vscode.window.showInformationMessage(
    installed
      ? 'The MCP config block is on your clipboard — paste it into your client and restart it.'
      : 'The block is on your clipboard, but coai-mcp is not installed yet — run "Install the MCP Server…" first.',
  );
}

async function copyClaudeSnippet(): Promise<void> {
  // The snippet names no repository: it is pasted into whichever one you are adopting it for, and
  // the AI reading it is already in a checkout it can name for itself.
  await vscode.env.clipboard.writeText(claudeSnippet());
  // What was taken, and what this repository already has. The version is in the menu item too, but
  // a menu is read BEFORE the click; this is the sentence that says whether the click mattered.
  void vscode.window.showInformationMessage(copiedMessage(await pastedSnippetStatus()));
}

/**
 * The rounds log, opened — or brought forward, if it is already open.
 *
 * <p>It was a markdown FILE, `rounds.md` under the data directory, written and then opened as a
 * text document and rewritten every five seconds while its tab was open. Fifty-three lines of
 * tables nobody could sort, filter or search, and a rewrite that reloaded the editor on every tick.
 * The page keeps the same command and the same data; only the surface changed.</p>
 */
async function showRoundsLog(log: RoundsLogPanel, watcher: EscalationWatcher, panel: PanelProvider): Promise<void> {
  await watcher.refresh();
  log.show(rowsFrom(await readSessions()), watcher.openQuestions, await panel.usageTab());
}

/** Keeps an OPEN log current while a round runs. Nothing is read when nobody is looking. */
async function refreshRoundsLog(log: RoundsLogPanel, watcher: EscalationWatcher, panel: PanelProvider, force = false): Promise<void> {
  if (!log.isOpen) {
    return;
  }
  log.update(rowsFrom(await readSessions()), watcher.openQuestions, await panel.usageTab(), force);
}

/** The server's own session files: its data dir, or `COAI_DATA_DIR` when the person set one. */
async function readSessions(): Promise<SessionFile[]> {
  const configured = process.env['COAI_DATA_DIR'];
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;
  const dir = vscode.Uri.file(configured ?? `${localAppData}/coai-mcp`);
  const sessionsDir = vscode.Uri.joinPath(dir, 'sessions');
  const sessions: SessionFile[] = [];
  try {
    for (const [name, kind] of await vscode.workspace.fs.readDirectory(sessionsDir)) {
      if (kind !== vscode.FileType.File || !name.endsWith('.json')) {
        continue;
      }
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(sessionsDir, name));
      const session = parseSession(new TextDecoder().decode(bytes));
      if (session !== undefined) {
        sessions.push(session);
      }
    }
  } catch {
    // No data dir yet — an empty view says so honestly.
  }
  return sessions;
}

async function offerUpdate(context: vscode.ExtensionContext): Promise<void> {
  const server = await serverOnThisSide(
    context.globalStorageUri,
    context.globalState,
    (await latestServerVersion()) ?? '',
  );
  // `absent` never offers: an install is not an update, and this runs at activation — a machine
  // with nothing on this side would otherwise be told to update something it does not have.
  if (!server.updateOffered) {
    return;
  }
  const answer = await vscode.window.showInformationMessage(
    'A newer coai-mcp is published.',
    'Install it',
  );
  if (answer === 'Install it') {
    await installServer(context);
  }
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Node puts an errno here; `vscode.FileSystemError` puts its own name. Absent is empty, never a guess. */
function codeOf(error: unknown): string {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' ? code : '';
}
