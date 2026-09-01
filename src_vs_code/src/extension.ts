import * as vscode from 'vscode';
import { ridFor } from './coaiInstall';
import { claudeSnippet } from './claudeSnippet';
import { CLIENT_TARGETS, installedMessage, mcpServerBlock } from './mcpBlock';
import { binaryPath, installLatest, installedVersion, updateIsAvailable } from './installer';
import { EscalationWatcher } from './escalationWatcher';
import { PanelProvider } from './panelProvider';
import { renderEscalations } from './escalations';
import { showHelp } from './helpPanel';
import { parseSession, renderRounds, roundsViewIsOpen, SessionFile } from './rounds';
import { envBlock, settingsFrom } from './settingsShape';

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
  const panel = new PanelProvider(context, watcher, dataDir(), async (id) => {
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
    // And the rounds view, if it is open: a round advances on its own, so the page it is shown on
    // has to as well. Nothing is written when nobody is looking.
    void refreshRoundsFile(watcher);
  };
  watcher.start();

  context.subscriptions.push(
    watcher,
    vscode.window.registerWebviewViewProvider(PanelProvider.viewType, panel),
    vscode.commands.registerCommand('coai.help', showHelp),
    vscode.commands.registerCommand('coai.installServer', () => installServer(context)),
    vscode.commands.registerCommand('coai.copyConfigBlock', () => copyConfigBlock(context)),
    vscode.commands.registerCommand('coai.copyClaudeSnippet', copyClaudeSnippet),
    vscode.commands.registerCommand('coai.showRounds', () => showRounds(watcher)),
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

async function installServer(context: vscode.ExtensionContext): Promise<void> {
  try {
    const target = await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: 'Installing coai-mcp…' },
      () => installLatest(context.globalStorageUri, context.globalState),
    );
    await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath, envBlock(settings())));
    const targets = CLIENT_TARGETS.map((t) => `${t.label} (${t.path})`).join(', ');
    void vscode.window.showInformationMessage(`${installedMessage(target.fsPath)} Paste it into: ${targets}`);
  } catch (error) {
    void vscode.window.showErrorMessage(`coai-mcp was not installed: ${message(error)}`);
  }
}

async function copyConfigBlock(context: vscode.ExtensionContext): Promise<void> {
  const rid = ridFor(process.platform, process.arch);
  if (rid === undefined) {
    void vscode.window.showErrorMessage('There is no published coai-mcp build for this platform yet.');
    return;
  }
  const path = binaryPath(context.globalStorageUri, rid);
  const installed = installedVersion(context.globalState) !== undefined;
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
  void vscode.window.showInformationMessage(
    "The CLAUDE.md snippet is on your clipboard — paste it into the CLAUDE.md of the repository you want reviewed.",
  );
}

/** Where the rounds view lives on disk. One file, rewritten — never a new tab per look. */
function roundsFile(): vscode.Uri {
  return vscode.Uri.joinPath(dataDir(), 'rounds.md');
}

/**
 * The rounds view, as a REAL file that is written and then opened.
 *
 * <p>It used to be an untitled document built from a string, which VS Code treats as unsaved
 * work: every close asked whether to save it, and there was nothing to save — the content is
 * derived from the server's session files and regenerated on demand. A real path under the data
 * directory closes without a word, reopens at the same tab, and can be kept open while a round
 * runs because {@link refreshRoundsFile} rewrites it in place.</p>
 */
async function showRounds(watcher: EscalationWatcher): Promise<void> {
  await watcher.refresh();
  const file = await writeRoundsFile(watcher);
  if (file === undefined) {
    void vscode.window.showErrorMessage(
      `The rounds view could not be written to ${roundsFile().fsPath}. Check that the folder is writable.`,
    );
    return;
  }

  const document = await vscode.workspace.openTextDocument(file);
  await vscode.window.showTextDocument(document, { preview: false });
}

/** Renders the view to its file. Returns the path, or undefined when the disk refused. */
async function writeRoundsFile(watcher: EscalationWatcher): Promise<vscode.Uri | undefined> {
  const sessions = await readSessions();
  // Open questions first: a blocked round is more urgent than the history of finished ones.
  const markdown = renderEscalations(watcher.openQuestions) + renderRounds(sessions);
  const file = roundsFile();
  try {
    await vscode.workspace.fs.createDirectory(dataDir());
    await vscode.workspace.fs.writeFile(file, new TextEncoder().encode(markdown));
    return file;
  } catch {
    return undefined;
  }
}

/**
 * Keeps an OPEN rounds view current while a round runs.
 *
 * <p>Only when it is already open: writing a file nobody is looking at every few seconds is churn,
 * and a document VS Code has open is reloaded from disk by the editor itself, so the numbers
 * advance without anybody pressing anything.</p>
 *
 * <p><b>"Open" means a TAB, not a loaded document.</b> `workspace.textDocuments` is the editor's
 * own cache and VS Code is free to drop an entry for a file the person is not currently looking
 * at — so a rounds view left open behind another tab stopped being refreshed, and the file went
 * stale with nothing to see but a number that would not move. A tab is what the person sees, so a
 * tab is what this asks about. A document that IS loaded and has unsaved edits still wins: an
 * automatic rewrite must never discard something somebody typed.</p>
 */
async function refreshRoundsFile(watcher: EscalationWatcher): Promise<void> {
  const path = roundsFile().fsPath;
  const dirty = vscode.workspace.textDocuments.filter((d) => d.isDirty).map((d) => d.uri.fsPath);
  if (roundsViewIsOpen(dirty, path)) {
    return;
  }

  const tabs = vscode.window.tabGroups.all
    .flatMap((group) => group.tabs)
    .map((tab) => (tab.input instanceof vscode.TabInputText ? tab.input.uri.fsPath : ''))
    .filter((p) => p.length > 0);
  const loaded = vscode.workspace.textDocuments.map((d) => d.uri.fsPath);
  if (roundsViewIsOpen([...tabs, ...loaded], path)) {
    await writeRoundsFile(watcher);
  }
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
  if (installedVersion(context.globalState) === undefined || !(await updateIsAvailable(context.globalState))) {
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
