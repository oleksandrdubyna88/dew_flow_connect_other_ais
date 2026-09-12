import { randomUUID } from 'node:crypto';
import * as fsp from 'node:fs/promises';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { openChatPresets, presetsReadDiscoveriesFrom } from './chatPresetsPanel';
import { openRoles } from './rolesPanel';
import { ChatPanels } from './chatPanels';
import {
  chatReadsThisSide,
  chatWithOtherAi,
  noteChatDoor,
  rememberChatsIn,
  restoreConversation,
  takeTheQuestion,
} from './chatCommand';
import { ChatTabMemory } from './chatTabs';
import { openLedger, reconcile } from './chatOrphans';
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
import { blindSpotsHtml, chatRows, LogRow, mergedRows, rowsFrom } from './roundsLog';
import { DbLog } from './roundsDb';
import { flushChatUsage } from './chatUsageFile';
import { RoundsLogPanel } from './roundsLogPanel';
import { ExistingFile, ServerSettingsSync } from './serverSettingsSync';
import { LOCK_STALE_AFTER_MS, lockIsStale } from './settingsLock';
import { ConfigReader, settingsFrom } from './settingsShape';
import { readerFor } from './sideConfig';
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
  // FIRST, before anything is constructed and long before a command can be invoked: the side whose
  // settings the chat reads. Its reader falls back to the shared configuration while unbound, which
  // is the behaviour this branch exists to end — so the window in which that fallback could be
  // reached is closed by ordering rather than argued about. Four reviewers raised it in one round,
  // and `chatWiring.test.ts` now fails if this line ever drifts below a `registerCommand`.
  chatReadsThisSide(context);
  presetsReadDiscoveriesFrom(context);
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
    // A row was opened. The list no longer carries findings — 3.78 MB of a 3.83 MB payload, for
    // rounds nobody had opened — so this is the read that replaces them, for the one round clicked.
    // The round's identity arrives WITH the request rather than being looked up in state the host
    // happens to be holding, which is what three reviewers of the code round asked for.
    onFindings: async (key, round) => {
      try {
        const found = await panelRef.roundFindings(round.sessionId, round.stage, round.number);
        await roundsLog.tell(key, found.state, found.findings);
      } catch (reason: unknown) {
        // The row is already showing "Reading…" and only this call can move it off. A rejection
        // here — the binary gone between the list read and the click — would otherwise leave it
        // spinning for ever, which is the state this whole change exists to end.
        console.error('ConnectOtherAIs: a round\'s findings could not be read', reason);
        await roundsLog.tell(key, 'failed', []);
      }
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
    () => readCoaiConfiguration(context),
    (json) => writeSettingsFile(json),
    extensionVersion(context),
    readSettingsFile,
    reportStandDown,
    underSettingsLock,
  );
  mirrorSettings(settingsSync);

  // One registry per window: a conversation belongs to a Claude Code tab, and tabs are per window.
  const chatPanels = new ChatPanels();
  // Where conversations are kept so a window reload does not empty every chat tab. `workspaceState`
  // rather than `globalState`, which everything else here uses: a setting belongs to the person and
  // is shared by every window of the profile, but a chat tab belongs to THIS workspace, and offering
  // one window's conversations to another is the namesake defect wearing a different hat.
  const chatTabMemory = new ChatTabMemory(context.workspaceState);
  rememberChatsIn(chatTabMemory);
  // A record is kept when a tab CLOSES as well as when it is reloaded, because a disposal cannot tell
  // the two apart — VS Code disposes every panel on reload too, and deleting on disposal would erase
  // the transcript at exactly the moment it is needed. A closed tab is simply never restored: VS Code
  // only deserializes panels that were open. So the store is bounded by this sweep instead.
  chatTabMemory.prune();
  // Bound once, and never asked for again: an extension host has one storage directory for its
  // whole life, and threading it through six functions paired a per-call path with a module-level
  // list of children — two things that must agree, with nothing making them.
  openLedger(context.globalStorageUri.fsPath);
  // What the LAST run left behind. Deactivation ends every chat child, and so does closing a tab —
  // but neither runs when the editor is force-killed, and what survives that is a vendor CLI signed
  // in as the person with nobody to stop it. Every candidate is re-identified before anything is
  // killed; see `chatLedger.ts`, which is where that judgement lives.
  void reconcile(context.globalStorageUri.fsPath)
    .then((ended) => {
      for (const one of ended) {
        // Named rather than counted: a line saying "ended 1 process" is the one thing nobody can act
        // on if it was ever the wrong one.
        console.warn(`[coai] ended a chat process left by a previous session: ${one}`);
      }
    })
    // The outer edge of a detached call ends in a catch that says something — `reliability.md`, and
    // an unhandled rejection during activation is a defect this repository has already paid for.
    .catch((reason: unknown) => {
      console.warn(`[coai] the chat orphan sweep failed: ${reason instanceof Error ? reason.message : reason}`);
    });

  context.subscriptions.push(
    {
      dispose: () => {
        clearTimeout(deferred);
        // And forget it. Clearing the timer without clearing the marker means a reactivation that
        // finds the lock busy schedules nothing, and the configuration then waits for a change that
        // may never come. Accepted finding, this story's code round.
        deferred = undefined;
      },
    },
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('coai')) {
        mirrorSettings(settingsSync);
        void panel.render();
      }
    }),
    watcher,
    vscode.window.registerWebviewViewProvider(PanelProvider.viewType, panel),
    vscode.commands.registerCommand('coai.editChatPresets', () => { openChatPresets(); }),
    // The CONTEXT goes with it: the roles page reads and writes `coai.roles`, which is a per-side
    // setting, and only the context says which side this window is.
    vscode.commands.registerCommand('coai.editRoles', () => { openRoles(context); }),
    vscode.commands.registerCommand('coai.help', showHelp),
    // Chat with another vendor about a passage. Two doors reach it — this keybinding and the
    // 'Chat with other AI' item in Claude Code's own right-click menu — and the command tells them
    // apart by what VS Code hands it, because only one of them can copy the selection itself.
    vscode.commands.registerCommand('coai.chatWithOtherAi', (...args: unknown[]) => {
      // RECORDED FIRST, in every one of these five. The count on the spending page is of how often
      // the chat was reached for, so a door that then refuses — no CLI, nothing captured — is still
      // one of them. Nothing waits for the write.
      noteChatDoor('key');
      void chatWithOtherAi(chatPanels, context.extensionUri, args);
    }),
    // The two menu items, which differ from the chord in one way: each SAYS what it will do, so
    // neither reads `coai.chatAutoSend`. An item whose behaviour depends on a setting in another
    // window is an item nobody can predict from its own label.
    vscode.commands.registerCommand('coai.chatNow', (...args: unknown[]) => {
      noteChatDoor('default');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, true);
    }),
    vscode.commands.registerCommand('coai.chatChoose', (...args: unknown[]) => {
      noteChatDoor('choose');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, false);
    }),
    // Take the question Claude Code is asking and hand it to a second model. Its own door, because
    // the passage comes from Claude's session file rather than from a selection — that box cannot
    // be selected, which is why a screenshot was the only way before this.
    //
    // The rejection is CAUGHT rather than dropped: a command that fails silently at the activation
    // boundary is a keypress that does nothing and explains nothing. (CodeRabbit, PR #206.)
    vscode.commands.registerCommand('coai.takeTheQuestion', () => {
      noteChatDoor('take');
      takeTheQuestion(chatPanels, context.extensionUri).catch((reason: unknown) => {
        // The DETAIL to the console, a short sentence to the person. A stack or a filesystem path in
        // a toast is neither readable nor theirs to act on, and the path rule says so; dropping it
        // altogether would leave a keypress that does nothing and explains nothing. (CodeRabbit.)
        console.error('coai.takeTheQuestion failed', reason);
        void vscode.window.showWarningMessage('The question could not be taken.');
      });
    }),
    // The same reader, the other verb: ADDED to what the composer already holds rather than put in
    // its place. Asked for after using the pair — `choose` had just composed a turn and `take` threw
    // it away, when what was wanted was the question underneath it as more material.
    vscode.commands.registerCommand('coai.addTheQuestion', () => {
      noteChatDoor('add');
      takeTheQuestion(chatPanels, context.extensionUri, true).catch((reason: unknown) => {
        console.error('coai.addTheQuestion failed', reason);
        void vscode.window.showWarningMessage('The question could not be added.');
      });
    }),
    // Deactivation is not a tab closing: nobody has told VS Code about these panels, so both the
    // panel and the vendor process behind it have to be ended here or they outlive the extension.
    { dispose: () => chatPanels.closeAll() },
    // How a chat tab comes back after a window reload. VS Code hands back the panel and whatever the
    // page saved with `setState` — here, the conversation's own id — and the transcript is looked up
    // by that. A panel whose conversation is not in the store is disposed rather than left as an
    // empty tab pretending to be one: the record can have been pruned, or written by a build that
    // stored a different shape.
    vscode.window.registerWebviewPanelSerializer('coaiChat', {
      deserializeWebviewPanel: (panel: vscode.WebviewPanel, state: unknown) => {
        const id = (state as { id?: unknown } | null)?.id;
        const saved = typeof id === 'string' ? chatTabMemory.saved(id) : undefined;
        if (saved === undefined) {
          panel.dispose();

          return Promise.resolve();
        }
        restoreConversation(chatPanels, panel, saved, context.extensionUri);

        return Promise.resolve();
      },
    }),
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

export function deactivate(): Promise<void> {
  // The watcher is a subscription; VS Code disposes it. No server, no port — but the chat ledger's
  // writes are deliberately NOT awaited by the turn that made them (a person's answer must not wait
  // on a disk), so a host closed the instant a turn ends could take the record with it. VS Code
  // awaits what this returns, which is the one moment the queue can be drained for free.
  // (codex and the local reviewer, the code round.)
  return flushChatUsage();
}

/**
 * The pending retry, and there is at most one.
 *
 * <p>A window that finds another one in the file writes nothing — which is right, because nobody
 * waits for a settings mirror — and then nothing fires again on its own. A configuration change that
 * happened to land while another window was writing would sit unwritten until the person changed
 * something else, and there may not be a next time. So one deferred attempt, scheduled just past the
 * window in which any lock is either released or breakable as stale.</p>
 *
 * <p>ONE, and not a ladder: by the time it runs there is no lock left that this window cannot take,
 * so a second failure is a different problem and the next configuration change will carry it.
 * Accepted finding, this story's plan round.</p>
 */
let deferred: ReturnType<typeof setTimeout> | undefined;

const RETRY_AFTER_MS = LOCK_STALE_AFTER_MS + 2_000;

function mirrorSettings(settingsSync: ServerSettingsSync, isTheRetry = false): void {
  void settingsSync.sync().then((outcome) => {
    if (outcome !== 'busy' || isTheRetry || deferred !== undefined) {
      return;
    }
    deferred = setTimeout(() => {
      deferred = undefined;
      mirrorSettings(settingsSync, true);
    }, RETRY_AFTER_MS);
  });
}

/**
 * The `coai.*` settings as the sync wants them: one read, both halves, no VS Code type leaving.
 *
 * <p><b>Read as THIS SIDE has them.</b> The file this feeds is what `coai-mcp` reads, so it decides
 * which binaries the GATE's reviewers are launched from — and it used to be filled from the shared
 * configuration alone. With *Separate settings for each side* on, that ran a WSL window's rounds off
 * the vendor rows of another side: the widest blast radius of the same bypass the chat had, because
 * a review is what the product is for. The reader is the one in `sideSettings`, the same one the
 * panel and the chat go through.</p>
 */
function readCoaiConfiguration(context: vscode.ExtensionContext) {
  const config: ConfigReader = readerFor(context, vscode.workspace.getConfiguration('coai'));

  return {
    settings: settingsFrom(config),
    vendors: vendorsFrom(config('vendors')),
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

  // The last thing before the rename: are we still the holder? A process suspended past the stale
  // window — a laptop closing mid-write is the ordinary way — is broken as dead by another window,
  // and would otherwise wake up and land a payload decided before that window's write existed. This
  // narrows that to the microseconds between this check and the rename, which is as far as it can be
  // taken without a lease that renews. Raised on this story's plan round.
  if (held.length > 0 && !(await lockHolds(held))) {
    await vscode.workspace.fs.delete(temp).then(undefined, () => undefined);
    throw new Error(
      'the settings lock was taken over while this write was in flight, so these settings are not on '
        + 'disk; another window is writing and an attempt is scheduled',
    );
  }

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
 * <p><b>The lock is an O_EXCL create, and the reason is worth keeping.</b> The first version claimed
 * exclusion from `vscode.workspace.fs.rename(…, { overwrite: false })` — and whether THAT is atomic
 * is not documented anywhere; the disk provider checks for existence and then renames, which is
 * check-then-act, so the whole guarantee rested on an implementation detail. `fs.open(path, 'wx')`
 * is O_EXCL on every platform this runs on, which is a promise the operating system makes.</p>
 *
 * <p>A lock that cannot be taken is not an error and nothing WAITS — but the caller is told, because
 * nothing else will fire on its own and the configuration would otherwise sit unwritten until the
 * person happened to change something else.</p>
 */
async function underSettingsLock(work: () => Promise<void>): Promise<boolean> {
  await vscode.workspace.fs.createDirectory(dataDir());
  const token = await takeLock();
  if (token === '') {
    return false;
  }

  held = token;
  try {
    await work();
  } finally {
    held = '';
    // ONLY while it is still ours. A window whose lock was broken as stale would otherwise delete
    // its SUCCESSOR's lock on the way out, and a third window would walk in while the second was
    // still writing — the race, produced by the release. Raised on this story's plan round.
    if (await lockHolds(token)) {
      await fsp.rm(lockPath()).catch(() => undefined);
    }
  }

  return true;
}

/** The token this window currently holds, or empty. Read by the write, just before it renames. */
let held = '';

function lockPath(): string {
  return path.join(coaiDataDir(), 'settings.lock');
}

/** Whether the lock on disk is still the one we took. */
async function lockHolds(token: string): Promise<boolean> {
  return await fsp.readFile(lockPath(), 'utf8').then((t) => t === token, () => false);
}

/**
 * Take the lock, or answer empty.
 *
 * <p>The token is this process and this moment, so a window can tell its OWN lock from the one that
 * replaced it. `wx` fails with `EEXIST` when the file is there, which is the whole mechanism.</p>
 */
async function takeLock(mayBreakAStaleOne = true): Promise<string> {
  // `randomUUID`, not `Math.random`: uniqueness is all this needs, but a pseudorandom generator in a
  // token is a shape worth not having — and an analyser is right to ask about one without reading
  // what the token is for. The crypto one is the same line and answers the question.
  const token = `${process.pid}:${Date.now()}:${randomUUID()}`;
  try {
    await fsp.writeFile(lockPath(), token, { encoding: 'utf8', flag: 'wx' });

    return token;
  } catch (e) {
    // Only EEXIST means somebody holds it. A permissions error or a dead volume is not contention,
    // and sending those into stale-breaking would have this window reading and deleting a lock it
    // had no business touching. Accepted finding, this story's code round.
    if (!isAlreadyThere(e)) {
      return '';
    }

    // ONE attempt at breaking a stale lock, never a loop: a lock somebody keeps re-taking between
    // our delete and our create would otherwise spin the extension host instead of skipping a write
    // nobody would have missed.
    return mayBreakAStaleOne ? breakIfStale() : '';
  }
}

function isAlreadyThere(e: unknown): boolean {
  return typeof e === 'object' && e !== null && (e as { code?: unknown }).code === 'EEXIST';
}

/**
 * A lock older than any write could take belonged to a window that died.
 *
 * <p>The token is read before and after the staleness decision, so a lock that was RELEASED and
 * re-taken while this looked at it is left alone rather than deleted out from under its new owner.
 * That narrows the window; it does not close it, and the limit is recorded honestly in
 * `module_extension.md` — deleting a file by path is check-then-act, and there is no
 * compare-and-unlink to be had.</p>
 */
async function breakIfStale(): Promise<string> {
  try {
    const before = await fsp.readFile(lockPath(), 'utf8');
    const onDisk = await fsp.stat(lockPath());
    if (!lockIsStale(onDisk.mtimeMs, Date.now())) {
      return '';
    }
    if ((await fsp.readFile(lockPath(), 'utf8')) !== before) {
      return '';
    }
    await fsp.rm(lockPath());
  } catch {
    return '';
  }

  // Deleted it; take it the ordinary way rather than assuming the gap is ours. Two windows can
  // reach this line together and only one of their exclusive creates can succeed, which is the point.
  return takeLock(false);
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
    await logRows(panel, undefined),
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
    await logRows(panel, fresh),
    watcher.openQuestions,
    await panel.usageTab(),
    force,
    blindSpotsHtml(fresh),
    fresh.totals);
}

/**
 * Every row the log shows: the review rounds and the conversations, in one table, newest first.
 *
 * <p>One function because there are two call sites — the first paint and every tick after it — and
 * they must not drift. The first paint used to pass `undefined` for the database and the tick a real
 * one, and that difference is the whole of what varies between them; everything else being written
 * out twice is how a column ends up on one of the two.</p>
 *
 * <p><b>The same {@link PanelProvider.modelPrice} prices both halves.</b> A conversation and a review
 * round on one model are then worked out by one rule, which is the point of merging them into one
 * table at all: the answer to "what did today cost" is a sum, and a sum of two differently-derived
 * numbers is not one.</p>
 */
async function logRows(panel: PanelProvider, database: DbLog | undefined): Promise<LogRow[]> {
  const priceOf = await panel.modelPrice();

  return mergedRows(
    rowsFrom(
      await readSessions(), Date.now(), priceOf, await panel.usageLines(), database, panel.vendorIds()),
    chatRows(await panel.chatLines(), priceOf),
  );
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
