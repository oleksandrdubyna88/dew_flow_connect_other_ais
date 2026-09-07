import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { installFailureHint, SingleFlight } from './coaiInstall';
import { claudeSnippet, copiedMessage } from './claudeSnippet';
import { pastedSnippetStatus } from './snippetInWorkspace';
import { clientTargetsLine, CLIENT_TARGETS, installedMessage, mcpServerBlock } from './mcpBlock';
import { installLatest, latestServerVersion, serverExists, serverOnThisSide, serverPath } from './installer';
import { EscalationWatcher } from './escalationWatcher';
import { PanelProvider } from './panelProvider';
import { showHelp } from './helpPanel';
import { parseSession, SessionFile } from './rounds';
import { blindSpotsHtml, rowsFrom } from './roundsLog';
import { RoundsLogPanel } from './roundsLogPanel';
import { ExistingFile, ServerSettingsSync } from './serverSettingsSync';
import { lockIsStale } from './settingsLock';
import { settingsFrom } from './settingsShape';
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
  //
  // And it stamps what it writes, because this file has more than one writer: every open window
  // has its own extension host, they all hear `onDidChangeConfiguration`, and a host loaded before
  // the last update is still running the build it started with. One of those reverted a
  // Team-server reviewer on 2026-09-07 by rewriting a runtime it did not know. An older build now
  // stands down and says so, once.
  const settingsSync = new ServerSettingsSync(
    readCoaiConfiguration,
    (json) => writeSettingsFile(json),
    extensionVersion(context),
    readSettingsFile,
    reportStandDown,
    underSettingsLock,
  );
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
  // Written beside it and renamed over it, the way an answered escalation already is. `writeFile`
  // truncates before it fills, so a host killed between the two leaves every other window — and the
  // server — reading a truncated file. That is worse than the revert this epic is about, and it
  // cannot be recovered from, because the original is gone. Raised on story 2.1's plan round.
  const target = vscode.Uri.joinPath(dataDir(), 'settings.json');
  const temp = vscode.Uri.joinPath(dataDir(), `settings.json.${process.pid}.tmp`);
  await vscode.workspace.fs.writeFile(temp, new TextEncoder().encode(json));
  await vscode.workspace.fs.rename(temp, target, { overwrite: true });
}

/**
 * Runs the settings read, the version comparison and the write with nobody else in the file.
 *
 * <p>Every window on this machine writes this one path, so the guard that reads a stamp and then
 * overwrites it is a time-of-check-to-time-of-use race: two guard-aware hosts can both read a stamp
 * they are allowed to overwrite, and the second to finish wins with a payload decided before the
 * first one's write existed.</p>
 *
 * <p><b>The lock is a rename, because the API has no exclusive create.</b> `writeFile` overwrites and
 * `createDirectory` is `mkdir -p`; `rename` with `overwrite: false` is the one operation here that
 * FAILS when the destination exists, which is exactly what taking a lock means. The temp file
 * carries this process id so two windows never fight over one temp path on the way in.</p>
 *
 * <p>A lock that cannot be taken is not an error and nothing waits: whoever holds it is writing the
 * same settings from the same configuration, and the only cost of standing aside is a write that
 * happens on the next configuration change. A lock left behind by a killed window is broken once it
 * is stale — see `settingsLock.ts` for why that rule is a tested function rather than a line here.</p>
 */
async function underSettingsLock(work: () => Promise<void>): Promise<void> {
  const lock = vscode.Uri.joinPath(dataDir(), 'settings.lock');
  await vscode.workspace.fs.createDirectory(dataDir());
  if (!(await takeLock(lock))) {
    return;
  }

  try {
    await work();
  } finally {
    try {
      await vscode.workspace.fs.delete(lock);
    } catch {
      // Left behind: the next window breaks it once it is stale, which is the case that rule exists
      // for. Throwing here would replace a settings write with an extension error.
    }
  }
}

async function takeLock(lock: vscode.Uri, mayBreakAStaleOne = true): Promise<boolean> {
  const mine = vscode.Uri.joinPath(dataDir(), `settings.lock.${process.pid}.tmp`);
  try {
    await vscode.workspace.fs.writeFile(mine, new TextEncoder().encode(String(process.pid)));
    await vscode.workspace.fs.rename(mine, lock, { overwrite: false });

    return true;
  } catch {
    await vscode.workspace.fs.delete(mine).then(undefined, () => undefined);

    // ONE attempt at breaking a stale lock, never a loop. Without the flag this and `breakIfStale`
    // call each other, and a lock somebody keeps re-taking between our delete and our rename spins
    // the extension host instead of skipping a write nobody would have missed.
    return mayBreakAStaleOne ? breakIfStale(lock) : false;
  }
}

/** A lock older than the window it should ever be held for belonged to a window that died. */
async function breakIfStale(lock: vscode.Uri): Promise<boolean> {
  try {
    const held = await vscode.workspace.fs.stat(lock);
    if (!lockIsStale(held.mtime, Date.now())) {
      return false;
    }
    await vscode.workspace.fs.delete(lock);
  } catch {
    return false;
  }

  // Deleted it; take it the ordinary way rather than assuming the gap is ours. Two windows can
  // reach this line together and only one of their renames can succeed, which is the point.
  return takeLock(lock, false);
}

/**
 * The settings file as it is now, or which of the two ways it could not be read.
 *
 * <p><b>Absent and unreadable are different answers and the difference decides a write.</b> An
 * earlier draft answered `''` for both, so a file that is locked, or on a volume that blinked, was
 * indistinguishable from a file that is not there — and "not there" is permission to overwrite. That
 * is the exact revert this whole epic is about, reached through a different door. Three reviewers
 * found it independently on this story's code round.</p>
 *
 * <p>Only a confirmed `FileNotFound` is absent. Everything else — a permission error, a disconnected
 * volume, a provider that threw — stands the write down and is retried on the next change.</p>
 */
async function readSettingsFile(): Promise<ExistingFile> {
  try {
    return {
      kind: 'contents',
      text: new TextDecoder().decode(
        await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dataDir(), 'settings.json')),
      ),
    };
  } catch (e) {
    return e instanceof vscode.FileSystemError && e.code === 'FileNotFound'
      ? { kind: 'absent' }
      : { kind: 'unreadable' };
  }
}

/**
 * This build's own version, from the manifest VS Code loaded it from.
 *
 * <p>`packageJSON` is `any`, so it is narrowed here rather than asserted: a manifest without a
 * version string yields an empty one, and an empty version makes the sync behave exactly as it did
 * before the stamp existed. Being unable to name yourself is a reason to stand aside from the
 * comparison, never a reason to guess at it.</p>
 */
function extensionVersion(context: vscode.ExtensionContext): string {
  const manifest: unknown = context.extension.packageJSON;
  if (typeof manifest !== 'object' || manifest === null) {
    return '';
  }
  const version = (manifest as Record<string, unknown>)['version'];

  return typeof version === 'string' ? version : '';
}

/**
 * Says that a newer build's settings were left alone, and offers the one action that fixes it.
 *
 * <p>Once per session, from the sync, which cannot import this module. A window running an older
 * build reverts settings SILENTLY today — that silence is the whole defect, seen from the other
 * side — and the cure is not a setting or a reinstall: it is reloading this window, which is what
 * makes VS Code pick up the extension it has already downloaded.</p>
 */
function reportStandDown(theirVersion: string): void {
  void vscode.window
    .showWarningMessage(
      `ConnectOtherAIs left the server settings alone: they were written by version ${theirVersion}, `
        + 'which is newer than the build this window is running. Reload the window to catch up.',
      'Reload Window',
    )
    .then((choice) => {
      if (choice === 'Reload Window') {
        void vscode.commands.executeCommand('workbench.action.reloadWindow');
      }
    });
}

/** The one answer, as the Uri the rest of this file wants. It lives in `dataDir.ts`. */
function dataDir(): vscode.Uri {
  return vscode.Uri.file(coaiDataDir());
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
    await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath));
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
  await vscode.env.clipboard.writeText(mcpServerBlock(path.fsPath));
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

  // The page FIRST, from the session files alone — everything it has ever shown comes from those, so
  // it is complete without the database. Reading the database spawns the server, and a person who
  // opened a log should not wait on a process to see it: the findings arrive in the next push, a
  // moment later. Raised by the gate as blocking the panel on an unannounced read.
  log.show(
    rowsFrom(await readSessions(), Date.now(), await panel.modelPrice(), await panel.usageLines()),
    watcher.openQuestions,
    await panel.usageTab());
  await refreshRoundsLog(log, watcher, panel, true);
}

/** Keeps an OPEN log current while a round runs. Nothing is read when nobody is looking. */
async function refreshRoundsLog(log: RoundsLogPanel, watcher: EscalationWatcher, panel: PanelProvider, force = false): Promise<void> {
  if (!log.isOpen) {
    return;
  }
  const fresh = await panel.roundsLog();
  log.update(
    rowsFrom(await readSessions(), Date.now(), await panel.modelPrice(), await panel.usageLines(), fresh),
    watcher.openQuestions,
    await panel.usageTab(),
    force,
    blindSpotsHtml(fresh));
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
