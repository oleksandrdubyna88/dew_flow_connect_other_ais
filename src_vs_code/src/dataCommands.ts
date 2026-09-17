import { hostname } from 'node:os';
import { join, resolve, sep } from 'node:path';
import * as vscode from 'vscode';
import { adoptionSentence, defaultSideName, FolderReport, sideRefusal, sidesIn } from './dataChoice';
import { chosenRoot, coaiDataDir, DATABASE_FILE, DATA_TO_MOVE, dataSideName, defaultDataDir, directoryFor, serverEnv } from './dataDir';
import { destinationPlaceRefusal, destinationRefusal, logsStrandedInTheRoot, mayDeleteTheOldCopy, MoveRecord, sourceChangedSince, sourceRefusal, sourceWarning, StorageFingerprint, verificationFailure } from './dataMove';
import { notify, notifyAndAsk, notifyThen } from './notify';
import { reportRefusal, saveSetting, storageReadsThisSide } from './sideConfig';
import { mcpServerBlock } from './mcpBlock';
import { serverPath } from './installer';
import { asText } from './asText';

/**
 * The three things a person can do to the data directory, and the dialogs that do them.
 *
 * <p>Its own module rather than more of `extension.ts`, which activation had already grown past a
 * thousand lines of: where the data lives is one subject, it has three commands, and every one of
 * them is a dialog over a decision made somewhere else. The decisions are in `dataChoice.ts` (what a
 * chosen folder holds, what this side is called) and `dataMove.ts` (whether a move may start, and
 * whether it worked) — both free of `vscode`, both tested.</p>
 *
 * <p><b>The asymmetry that runs through all three.</b> Adopting a folder wants one that is NOT
 * empty; moving into a folder wants one that is. They are the same dialog about the same directory
 * and the opposite question, and a sentence carried from one to the other destroys a history.</p>
 */

/**
 * The one question this product asks at install time: where should its data live?
 *
 * <p><b>Asked once per side of a machine, and asked HERE</b>, because installing is the moment a
 * person is already configuring this product and already has a paste to make. Anywhere later is a
 * setting nobody knows to look for, and the cost of not asking is paid silently: a default folder
 * lives on the system drive, and a system drive is the thing that gets reformatted.</p>
 *
 * <p><b>A folder that already holds a history is ADOPTED.</b> That is the whole point — it is how a
 * reinstalled machine picks its own rounds back up — and it is the exact opposite of the rule a MOVE
 * must follow, where a non-empty destination is refused because copying over it destroys what is
 * there. Both rules are about the same folder and neither may be phrased as "the destination is
 * checked". See `dataChoice.ts`, which holds the sentences.</p>
 *
 * <p>Answering nothing keeps the default, on purpose: a dismissed dialog must leave an installation
 * that works, not one that is half-configured.</p>
 */
export async function askWhereDataLives(
  context: vscode.ExtensionContext,
  /**
   * Whether this is the FIRST install on this side, which changes two things.
   *
   * <p>The install path hands the block over itself, so this flow must not — two modals about the
   * same paste, the first describing it as an update to a client that does not exist yet (codex).
   * And "keep the default" means "change nothing" there, where from the Change command it has to
   * mean "put it back", which is what the advertised command was silently failing to do.</p>
   */
  installing = false,
): Promise<ChoiceOutcome> {
  const DEFAULT = installing ? 'Keep it in the default folder' : 'Put it back in the default folder';
  const CHOOSE = 'Choose a folder…';
  const answer = await vscode.window.showQuickPick([DEFAULT, CHOOSE], {
    title: 'Where should ConnectOtherAIs keep its data?',
    placeHolder: `${coaiDataDir()} — rounds, sessions, chats and spending`,
    ignoreFocusOut: true,
  });
  if (answer === undefined) {
    return 'unchanged';
  }
  if (answer === DEFAULT) {
    // "The default" has to be MADE the default, not assumed to be it. A shared `coai.dataDirectory`
    // set on another side is what this window resolves when it has no override — so an install told
    // to keep the default would otherwise adopt that other side's NAS, and a reset would return
    // having changed nothing at all. Both are cured by writing an explicit empty pair for THIS side.
    // (codex and gemini, code round 2.)
    if (coaiDataDir() === defaultDataDir()) {
      return 'unchanged';
    }

    const reset = await saveChoice(context, '', '');
    if (reset === 'saved' && !installing) {
      // The client is still starting its server with the old folder, and a reset is exactly the
      // moment that matters: the handover was skipped here, which reproduced the desynchronisation
      // this whole subsystem exists to end, on the one path meant to undo it. (gemini.)
      await tellClientsToCatchUp(context, coaiDataDir());
    }

    return reset;
  }

  const picked = await vscode.window.showOpenDialog({
    canSelectFolders: true,
    canSelectFiles: false,
    canSelectMany: false,
    openLabel: 'Keep my data here',
    title: 'A folder that survives reinstalling this machine — a network drive or a NAS',
  });
  const folder = picked?.[0];
  if (folder === undefined) {
    return 'unchanged';
  }

  // The ROOT is read first only to offer the sides already in it, which is a fact about the root.
  // What is ADOPTED is `<root>/<side>`, and asking the root about that was the defect: "this folder
  // already holds a database" could be true of the root and false of the directory about to be used,
  // and a history sitting in `<root>/<side>` was not found at all. (codex, plan round.)
  const inTheRoot = await whatIsIn(folder);
  const side = await askForSideName(inTheRoot);
  if (side === undefined) {
    return 'unchanged';
  }

  const resolved = directoryFor(folder.fsPath, side);
  const found = side.trim().length === 0 ? inTheRoot : await whatIsIn(vscode.Uri.file(resolved));

  // SHOWN BEFORE ANYTHING IS SAVED. It used to be a notification afterwards, which told a person
  // what they had adopted only once adopting it was done — and adopting the wrong folder is not
  // obviously recoverable. (local, plan round.)
  const go = found.hasDatabase ? 'Continue this history' : 'Use this folder';
  const confirmed = await notifyAndAsk({
    as: 'information',
    class: 'confirmation',
    source: 'dataDirectory',
    code: 'adopt-a-data-directory',
    subject: resolved,
    modal: true,
    title: `Keep this installation's data in ${resolved}?`,
    detail: adoptionSentence(resolved, found),
    action: go,
  });
  if (confirmed !== go) {
    return 'unchanged';
  }

  const saved = await saveChoice(context, folder.fsPath, side);
  if (saved === 'saved' && !installing) {
    // Not on the install path: that flow copies the block itself, and doing it here as well showed
    // two modals about one paste — the first describing it as an update to a client that is not
    // configured yet. (codex, code round.)
    await tellClientsToCatchUp(context, resolved);
  }

  return saved;
}

/**
 * Offer the reload that makes the rest of this window agree with the new folder.
 *
 * <p>Offered rather than performed: a reload closes every editor's unsaved state and a chat in
 * flight, and this is not the extension's decision to take. The sentence says what is stale and what
 * is not, so declining is an informed choice rather than a shrug — the panel and the server are
 * already correct; it is the watchers and the chat store that hold the old path.</p>
 */
async function offerAReload(directory: string): Promise<void> {
  const RELOAD = 'Reload Window';
  const choice = await notifyAndAsk({
    as: 'information',
    class: 'offer',
    source: 'dataDirectory',
    code: 'reload-after-the-folder-changed',
    subject: directory,
    title: `Reload this window so everything in it reads ${directory}?`,
    detail: 'The panel and the config block are already correct. The escalation watcher, the '
      + 'consultation watcher and the chat store were built when this window opened and still hold '
      + 'the old folder, so questions and chats would be read from there until a reload.',
    action: RELOAD,
  });
  if (choice === RELOAD) {
    await vscode.commands.executeCommand('workbench.action.reloadWindow');
  }
}

/** What a chooser did, so its caller can tell "nothing to do" from "it did not work". */
export type ChoiceOutcome = 'saved' | 'unchanged' | 'refused';

/**
 * Write the pair and put it into effect, or report that it was refused.
 *
 * <p>Both settings or neither is the intent, and the honest limitation is that VS Code gives no
 * transaction: if the second write is refused the first has landed. That is why the outcome is
 * REPORTED rather than swallowed — `install()` used to go on and copy a block built from a state
 * nobody had asked for, while the install record it had just written stopped the question ever being
 * asked again. (codex, code round.)</p>
 */
async function saveChoice(
  context: vscode.ExtensionContext,
  directory: string,
  side: string,
): Promise<ChoiceOutcome> {
  const config = vscode.workspace.getConfiguration('coai');
  try {
    await saveSetting(context, config, 'dataDirectory', directory);
    await saveSetting(context, config, 'dataSide', side);
  } catch (error) {
    reportRefusal(context, 'dataDirectory', error);
    // Whatever landed is now in effect, and the panel must show THAT rather than what was asked for.
    storageReadsThisSide(context);

    return 'refused';
  }

  // In effect for THIS window immediately: every path the panel resolves from here on, and the block
  // anybody copies next, are built from it.
  storageReadsThisSide(context);

  return 'saved';
}

/**
 * Hand over the block that makes a client's server agree, and say plainly that it must be pasted.
 *
 * <p><b>Without this the feature is a trap</b>, and three reviewers found it independently on the
 * plan round. A person changes the folder, the panel follows immediately, and the MCP client goes on
 * starting its server with the environment it was given months ago — so the server keeps writing to
 * the OLD folder while everything on screen says otherwise. After a MOVE it is worse than confusing:
 * deleting the old folder then deletes a directory something is still writing to.</p>
 *
 * <p>The extension cannot update that client entry — it has never written another program's config
 * file and this change does not start — so what it can do is put the replacement on the clipboard at
 * the moment it becomes necessary, and say what happens if it is not pasted.</p>
 *
 * <p>Two things are named that a person cannot see for themselves: the path has to be the one that
 * names this folder where the SERVER runs, which on WSL is a different string for the same drive
 * (codex); and other windows of this side keep the old folder until they are reloaded, because a
 * per-side choice lives in `globalState`, which raises no configuration event (gemini).</p>
 */
async function tellClientsToCatchUp(context: vscode.ExtensionContext, directory: string): Promise<void> {
  const server = serverPath(context.globalStorageUri);
  if (server === undefined) {
    void notify({
      as: 'information',
      class: 'outcome',
      source: 'dataDirectory',
      code: 'folder-changed-server-not-installed',
      subject: directory,
      title: `This window now reads ${directory}. Install the MCP server, then paste the block it gives you `
        + 'into your MCP client — until you do, the server writes where its own entry tells it to.',
    });

    return;
  }

  await vscode.env.clipboard.writeText(mcpServerBlock(server.fsPath, serverEnv()));
  // The modal FIRST, and awaited. It used to be raised beside a reload offer already on screen, and
  // a modal disables the whole workbench — so the *Reload Window* button on that toast could not be
  // pressed until the modal was dismissed, which is a dialog holding a notification hostage. They
  // are two steps of one instruction and they now happen in that order: paste the block, then
  // decide about the reload. (gemini, the code round; the overlap predates the funnel.)
  await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'dataDirectory',
    code: 'paste-the-updated-block',
    subject: directory,
    modal: true,
    title: `This window now reads ${directory}, and the updated MCP config block is on your clipboard.`,
    detail: 'Paste it into your MCP client and restart it. Until you do, the server it starts keeps '
      + 'writing to the folder its own entry names — which is the old one.\n\nThe path has to be the '
      + 'one that names this folder where the SERVER runs: the same drive is reached by a different '
      + 'route from Windows and from WSL.\n\nOther windows of this side keep the old folder until '
      + 'they are reloaded.',
    action: 'I have pasted it',
  });

  // Offered rather than performed, and offered LAST. The window itself has to catch up too and it
  // cannot do so in place: the two watchers, the panel and the chat store were all constructed at
  // activation from the directory of the moment, and `storageReadsThisSide` moves what the
  // RESOLVER answers, not what those instances already hold, so they go on watching and writing
  // the old folder. Rebuilding them one by one would be four lifetimes to get right for a thing
  // that happens once; a reload is honest and complete. (CodeRabbit, Major.)
  void offerAReload(directory);
}

/**
 * The side name, offered rather than demanded — and validated the way the server validates it.
 *
 * <p>`undefined` means the person backed out, which abandons the whole choice; an empty string is an
 * answer, and it means the folder is not divided.</p>
 */
async function askForSideName(found: FolderReport): Promise<string | undefined> {
  const offered = defaultSideName({
    remoteName: vscode.env.remoteName ?? '',
    distro: process.env['WSL_DISTRO_NAME'] ?? '',
    hostname: hostname(),
    platform: process.platform,
  });
  const sharing = found.sides.length > 0
    ? ` Already in this folder: ${found.sides.join(', ')}.`
    : '';

  return vscode.window.showInputBox({
    title: 'A name for this installation inside that folder',
    value: found.hasDatabase ? '' : offered,
    prompt: 'Two installations sharing one folder each keep their own database under their own name.'
      + ` Leave it empty if only this one uses the folder.${sharing}`,
    ignoreFocusOut: true,
    validateInput: (typed) => {
      const refusal = sideRefusal(typed);

      return refusal.length === 0 ? undefined : refusal;
    },
  });
}

/**
 * What a chosen folder already holds, read without blocking the host.
 *
 * <p>Asynchronously and through `vscode.workspace.fs`, because the folder this is for is a NAS: a
 * synchronous probe of a disconnected share hangs the extension host, which is the predecessor's
 * finding and the reason the panel's own probes were made async.</p>
 *
 * <p>A folder that cannot be read at all reports as empty. It is the honest reading — nothing was
 * found — and the sentence it produces tells somebody to check the path, which is what to do.</p>
 */
async function whatIsIn(folder: vscode.Uri): Promise<FolderReport> {
  try {
    // The WHOLE inspection inside the deadline, not only the listing. Bounding `readDirectory` and
    // then starting an unbounded `stat` per entry left the flow hanging exactly where it had been
    // hanging before, on the second call rather than the first. (codex, code round.)
    return await withinReason(inspect(folder));
  } catch {
    return { hasDatabase: false, sides: [] };
  }
}

/** How many child directories are asked about at once. A NAS is not a local disk. */
const PROBES_AT_ONCE = 8;

async function inspect(folder: vscode.Uri): Promise<FolderReport> {
  const entries = await vscode.workspace.fs.readDirectory(folder);
  const directories = entries.filter(([, kind]) => kind === vscode.FileType.Directory);
  const inside: { name: string; isDirectory: boolean; hasDatabase: boolean }[] = [];

  // In batches rather than all at once: a root with ten thousand subdirectories would otherwise open
  // ten thousand concurrent requests against one share, which is slower than doing it in order and
  // is how a probe stops answering at all. (codex, code round.)
  for (let at = 0; at < directories.length; at += PROBES_AT_ONCE) {
    const batch = directories.slice(at, at + PROBES_AT_ONCE);
    inside.push(...await Promise.all(batch.map(async ([name]) => ({
      name,
      isDirectory: true,
      hasDatabase: await exists(vscode.Uri.joinPath(folder, name, DATABASE_FILE)),
    }))));
  }

  return {
    hasDatabase: entries.some(([name, kind]) => name === DATABASE_FILE && kind === vscode.FileType.File),
    sides: sidesIn(inside),
  };
}

/** How long a folder has to answer before the flow stops waiting for it. */
const PROBE_MS = 8_000;

/**
 * A read that cannot wait for ever, because the folder being read is a network share.
 *
 * <p>Asynchronous was never the whole answer (codex, plan round): a disconnected SMB share does not
 * reject, it waits — far longer than a person will, and the install flow had no bound at all, so a
 * bad path looked like a dialog that had simply stopped. The timeout turns that into the same
 * outcome as a permission error, which the caller already handles: nothing was found, and the
 * sentence it produces says to check the path.</p>
 *
 * <p>The underlying call is not cancelled, because `workspace.fs` gives no handle to cancel it with;
 * what is bounded is how long anybody waits on the answer.</p>
 */
function withinReason<T>(work: Thenable<T>): Promise<T> {
  return Promise.race([
    Promise.resolve(work),
    new Promise<T>((_, reject) =>
      setTimeout(() => reject(new Error(`that folder did not answer within ${PROBE_MS / 1000} seconds`)), PROBE_MS)),
  ]);
}

/** Whether a path is there, as a question rather than an exception. */
async function exists(path: vscode.Uri): Promise<boolean> {
  try {
    await vscode.workspace.fs.stat(path);

    return true;
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------------------------------------
// Moving what is already there
// ---------------------------------------------------------------------------------------------

/** Where the last move is remembered, so the delete can be offered — or refused — after a reload. */
const MOVE_RECORD = 'coai.lastDataMove';

/**
 * Copy this directory's history into another folder, check it arrived, and point this window there.
 *
 * <p><b>Copy, never move.</b> Nothing is deleted here at all: the old folder is left exactly as it
 * was, and deleting it is a separate action that this one unlocks by verifying. A one-click move
 * that deletes as it goes is the only shape in which a half-failure loses a history, and a
 * half-failure is what a network drive is for.</p>
 *
 * <p>Every refusal below is a decision made in `dataMove.ts` and tested there. What is here is the
 * order they run in, which matters: the source is checked before a person is asked to pick
 * anything, because "you cannot do this now" is worth knowing before a folder dialog rather than
 * after one.</p>
 */
export async function moveDataDirectory(
  context: vscode.ExtensionContext,
  countAt: (resolvedDirectory: string) => Promise<StorageFingerprint>,
): Promise<void> {
  const from = coaiDataDir();

  const activity = await whatIsRunningIn(from);
  const busy = sourceRefusal(activity);
  if (busy.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'dataDirectory',
      code: 'move-refused-something-is-running',
      title: busy,
    });

    return;
  }

  const picked = await vscode.window.showOpenDialog({
    canSelectFolders: true,
    canSelectFiles: false,
    canSelectMany: false,
    openLabel: 'Move my data here',
    title: `Move everything in ${from} to another folder`,
  });
  const destination = picked?.[0];
  if (destination === undefined) {
    return;
  }

  // The side travels with the data. Saving an empty one silently unpartitioned an installation that
  // had been partitioned on purpose — and on a shared NAS that is exactly the collision a side
  // exists to prevent. (gemini, code round 1.)
  //
  // `landing` is resolved FIRST and everything below takes it: the placement check, the destination
  // check, the copy, the verification, the record and the sentence. Round 1 resolved it and then
  // left the copy and the verification on the picked root, so a partitioned move put the history in
  // the root while the settings pointed at an empty side directory beside it — and the delete that
  // followed left the installation with nothing. Three reviewers found it in round 2.
  const side = dataSideName();
  const landing = vscode.Uri.file(directoryFor(destination.fsPath, side));

  // WHERE it is, before what is in it. `C:\coai` into `C:\coai\new` passes every other check and
  // then loses the copy to the delete that follows. (codex, Blocking.) Against `landing` rather than
  // the picked folder, or moving between two sides of one shared parent is refused although the two
  // directories are disjoint.
  const place = destinationPlaceRefusal(from, landing.fsPath, isInside);
  if (place.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'dataDirectory',
      code: 'move-refused-where-it-would-land',
      subject: landing.fsPath,
      title: place,
    });

    return;
  }

  const clash = destinationRefusal(await historyIn(landing));
  if (clash.length > 0) {
    // A different CODE from the one above: "you cannot put it there" and "there is already a
    // history there" send a person to two different places.
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'dataDirectory',
      code: 'move-refused-destination-holds-a-history',
      subject: landing.fsPath,
      title: clash,
    });

    return;
  }

  const before = await countAt(from);
  // Said rather than refused. A sidecar means something has the database open or was stopped while
  // it did — and since the sidecars are copied WITH it, the rounds in them travel too. Refusing on
  // one made this feature unreachable for every installation that had ever been killed. (codex.)
  const sidecars = sourceWarning(activity);
  // The logs of a PARTITIONED installation are in the ROOT, not in the folder being copied, so this
  // move cannot take them — and it must not let that be discovered afterwards. All three reviewers
  // of the plan round found it, one rating it Blocking: the logs would stay on the machine somebody
  // is about to reformat, which is the exact thing they moved their data to avoid.
  //
  // Decided from the fingerprint just read, with NO filesystem call: `chosenRoot()` is empty on a
  // default directory, `exists()` cannot tell a permission error from an absence, and a probe here
  // runs while a modal is open. The code round found all three. `|| defaultDataDir()` is what the
  // root actually is when nothing was chosen.
  const rootLogsPath = join(chosenRoot().length === 0 ? defaultDataDir() : chosenRoot(), 'logs');
  const stranded = logsStrandedInTheRoot(side, rootLogsPath, before);
  const go = 'Copy it';
  const confirmed = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'dataDirectory',
    code: 'copy-the-data-directory',
    subject: landing.fsPath,
    modal: true,
    title: `Copy ${before.rounds} rounds and ${before.sessions} sessions to ${landing.fsPath}?`,
    detail: `Nothing is deleted. ${from} is left exactly as it is, and you can delete it yourself `
      + 'once you have seen your history in the new folder.\n\nOn a network drive this can take '
      + 'minutes: the chat conversations and the sessions are thousands of small files, and the '
      + 'progress names each entry as it starts rather than each file.\n\nWhat is copied: '
      + `${DATA_TO_MOVE.join(', ')}.${sidecars.length === 0 ? '' : `\n\n${sidecars}`}`
      + `${stranded.length === 0 ? '' : `\n\n${stranded}`}`,
    action: go,
  });
  if (confirmed !== go) {
    return;
  }

  const copied = await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: 'Copying your data…', cancellable: false },
    // The WHOLE wait, including the verification: a progress bar that ends before the slow part
    // leaves the longest half looking like nothing happening.
    async (progress) => {
      // Created first: `<destination>/<side>` does not exist yet, and the first copy into it has
      // nowhere to go. A destination with no side is `landing` too, and creating it is a no-op.
      await vscode.workspace.fs.createDirectory(landing);
      const failure = await copyInventory(vscode.Uri.file(from), landing, progress);
      if (failure.length > 0) {
        return failure;
      }

      progress.report({ message: 'checking it arrived…' });

      return verificationFailure(before, await countAt(landing.fsPath));
    },
  );

  if (copied.length > 0) {
    // NOTHING is recorded. A record written here could never be cleared except by another whole
    // move, and a record that cannot be cleared is a state a person is trapped in. (gemini.)
    await context.globalState.update(MOVE_RECORD, undefined);
    void notify({
      as: 'error',
      class: 'failure',
      source: 'dataDirectory',
      code: 'copy-did-not-complete',
      subject: landing.fsPath,
      title: copied,
    });

    return;
  }

  // Only now does this window point at the new folder — after the copy AND after the check. Pointing
  // first and copying second would show an empty history for as long as the copy took, and for ever
  // if it failed.
  if (await saveChoice(context, destination.fsPath, side) !== 'saved') {
    // The settings did not land, so this window still reads the OLD folder. Recording the move as
    // verified here would enable a delete of the directory in use. (codex, code round.)
    await context.globalState.update(MOVE_RECORD, undefined);
    void notify({
      as: 'warning',
      class: 'failure',
      source: 'dataDirectory',
      code: 'copied-but-the-window-was-not-pointed-at-it',
      subject: landing.fsPath,
      title: `Everything was copied to ${landing.fsPath} and reads back the same, but this window could not `
        + 'be pointed at it. Nothing has been deleted; set the folder yourself in Settings, as '
        + 'coai.dataDirectory.',
      cure: 'Set coai.dataDirectory in Settings. Nothing was deleted.',
    });

    return;
  }

  // The record is written LAST, when the copy verified and the settings landed — the two conditions
  // under which deleting the old folder is a safe thing to offer.
  await context.globalState.update(MOVE_RECORD, {
    from,
    to: landing.fsPath,
    verified: true,
    // What the SOURCE held when it was copied, so the delete can tell later whether anything has
    // written to it since — the one defence against a server this extension cannot stop or see.
    held: before,
  } satisfies MoveRecord);

  void notify({
    as: 'information',
    class: 'outcome',
    source: 'dataDirectory',
    code: 'data-directory-moved',
    subject: landing.fsPath,
    title: `Your data is in ${landing.fsPath} and reads back the same ${before.rounds} rounds. ${from} still `
      + 'holds the original — delete it from the Command Palette, with "ConnectOtherAIs: Delete the old '
      + 'data folder", once you are sure.',
  });

  if (stranded.length > 0) {
    // "Copy that folder yourself" named a path and offered nothing, which two reviewers called close
    // to not telling somebody at all. Opening it is one line and turns finding it into a click; a
    // second copy flow for one directory — its own destination, refusals and verification — would be
    // a feature rather than a fix.
    const SHOW = 'Show me the logs';
    void notifyThen(
      {
        as: 'warning',
        class: 'offer',
        source: 'dataDirectory',
        code: 'logs-stranded-in-the-root',
        subject: rootLogsPath,
        title: stranded,
        action: SHOW,
      },
      (choice) => {
        if (choice === SHOW) {
          void vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(rootLogsPath));
        }
      },
    );
  }
  await tellClientsToCatchUp(context, landing.fsPath);
}

/**
 * Whether one path is the other, or sits inside it — the platform's own comparison.
 *
 * <p>Case-insensitive on Windows, where `C:\Coai` and `c:\coai` are one directory, and a separator
 * is appended before the prefix test so `C:\coai2` is not read as living inside `C:\coai`.</p>
 */
function isInside(outer: string, inner: string): boolean {
  const fold = (path: string): string => (process.platform === 'win32' ? path.toLowerCase() : path);
  const a = fold(resolve(outer));
  const b = fold(resolve(inner));

  return b === a || b.startsWith(a.endsWith(sep) ? a : `${a}${sep}`);
}

/**
 * Delete what a verified move left behind, and only that.
 *
 * <p>Separate from the move on purpose, and gated on a record the move wrote: a copy that did not
 * verify is a copy that may have lost something, and the old folder is then the only place that
 * something still exists. The gate survives a window reload because the record does — an in-memory
 * flag would make this offer disappear at exactly the moment somebody went away to check their
 * history and came back.</p>
 */
export async function deleteTheOldDataFolder(
  context: vscode.ExtensionContext,
  countAt: (resolvedDirectory: string) => Promise<StorageFingerprint>,
): Promise<void> {
  const record = context.globalState.get<MoveRecord>(MOVE_RECORD);
  if (!mayDeleteTheOldCopy(record)) {
    void notify({
      as: 'information',
      class: 'refusal',
      source: 'dataDirectory',
      code: 'nothing-to-delete',
      subject: record === undefined ? 'no-move' : 'move-did-not-verify',
      title: record === undefined
        ? 'Nothing has been moved from this window, so there is no old folder to delete.'
        : 'That move did not check out, so its old folder is the only copy of anything it missed. '
          + 'Nothing will be deleted until a move verifies.',
    });

    return;
  }

  const old = record!.from;

  const go = 'Delete it';
  // The most destructive question this product asks, so both the asking and the answer are written
  // down — including a decline, which is the case nobody can otherwise account for afterwards.
  const confirmed = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'dataDirectory',
    code: 'delete-the-old-data-folder',
    subject: old,
    modal: true,
    title: `Delete ${old}?`,
    detail: `Its contents were copied to ${record!.to} and read back the same.\n\nBefore you press `
      + 'this: every MCP client must already have been given the new block and restarted. A client '
      + 'still configured for this folder will recreate it and write there, and you will be reading '
      + 'one history while it writes another.\n\nWhat is checked is that nothing has been ADDED to '
      + 'or removed from the old folder since the copy. A file edited in place is not something '
      + 'this can see.\n\nThis cannot be undone, and it is the last copy of anything the move did '
      + 'not take — the sign-in tokens and the scratch worktrees are deliberately not copied.',
    action: go,
  });
  if (confirmed !== go) {
    return;
  }

  // READ AGAIN, and AFTER the confirmation rather than before it. The extension cannot stop the MCP
  // client's server and cannot detect one attached — the server opens the database per write and
  // closes it, so an idle-looking folder proves nothing. A move can copy, verify, and then have a
  // round appended to the SOURCE before anybody presses this; reading it once more turns that silent
  // loss into a refusal. (codex and gemini, plan round.)
  //
  // The modal is a wait of unbounded length, and a write arriving DURING it used to pass a check
  // made before it opened. The last thing before the delete is the read. (CodeRabbit, Minor.)
  const moved = sourceChangedSince(record!, await countAt(old));
  if (moved.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'dataDirectory',
      code: 'delete-refused-the-old-folder-changed',
      subject: old,
      title: moved,
    });

    return;
  }

  try {
    await vscode.workspace.fs.delete(vscode.Uri.file(old), { recursive: true, useTrash: true });
  } catch (error) {
    void notify({
      as: 'error',
      class: 'failure',
      source: 'dataDirectory',
      code: 'old-folder-not-deleted',
      subject: old,
      title: `${old} was not deleted: ${asText(error)}`,
      detail: asText(error),
    });

    return;
  }

  await context.globalState.update(MOVE_RECORD, undefined);
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'dataDirectory',
    code: 'old-folder-deleted',
    subject: old,
    title: `${old} is gone. Your data is in ${record!.to}.`,
  });
}

/** What the data directory says is running against it right now, and what was left beside it. */
async function whatIsRunningIn(directory: string): Promise<{ sidecars: string[]; livePids: number }> {
  const root = vscode.Uri.file(directory);
  const sidecars: string[] = [];
  for (const name of [`${DATABASE_FILE}-wal`, `${DATABASE_FILE}-shm`]) {
    if (await exists(vscode.Uri.joinPath(root, name))) {
      sidecars.push(name);
    }
  }

  let livePids = 0;
  try {
    livePids = (await vscode.workspace.fs.readDirectory(vscode.Uri.joinPath(root, 'running')))
      .filter(([, kind]) => kind === vscode.FileType.File).length;
  } catch {
    // No `running/` is the normal case: nothing has started a reviewer from this directory.
  }

  return { sidecars, livePids };
}

/** Which parts of a history a destination already holds — the names `destinationRefusal` judges. */
async function historyIn(destination: vscode.Uri): Promise<string[]> {
  const held: string[] = [];
  for (const entry of DATA_TO_MOVE) {
    if (await exists(vscode.Uri.joinPath(destination, entry.replace(/\/$/u, '')))) {
      held.push(entry);
    }
  }

  return held;
}

/**
 * Copy every entry of the inventory, and say what stopped it if anything did.
 *
 * <p>Entry by entry rather than a whole-directory copy, because the inventory is the point: what is
 * NOT in it must not travel — the scratch worktrees, and the sign-in tokens that belong to the side
 * that made them. A missing entry is not a failure; most installations have never written a
 * `documents/` or an `escalations/`.</p>
 */
async function copyInventory(
  from: vscode.Uri,
  to: vscode.Uri,
  progress: vscode.Progress<{ message?: string }>,
): Promise<string> {
  for (const entry of DATA_TO_MOVE) {
    const name = entry.replace(/\/$/u, '');
    const source = vscode.Uri.joinPath(from, name);
    if (!await exists(source)) {
      continue;
    }

    progress.report({ message: name });
    try {
      await vscode.workspace.fs.copy(source, vscode.Uri.joinPath(to, name), { overwrite: false });
    } catch (error) {
      // The destination is now PARTLY written, and saying so is the whole of this sentence's job:
      // the next attempt refuses a folder holding any part of a history, so somebody who is not told
      // meets a refusal they cannot explain and cannot clear. (local, plan round.)
      return `${name} could not be copied: ${asText(error)}.\n\nNothing has been deleted and `
        + `${from.fsPath} is unchanged — it is still your data. ${to.fsPath} now holds a PARTIAL `
        + 'copy, which is not a history: empty it before trying again, or choose another folder.';
    }
  }

  return '';
}
