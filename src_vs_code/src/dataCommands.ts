import { hostname } from 'node:os';
import * as vscode from 'vscode';
import { adoptionSentence, defaultSideName, FolderReport, sideRefusal, sidesIn } from './dataChoice';
import { coaiDataDir, DATABASE_FILE, DATA_TO_MOVE, directoryFor, serverEnv } from './dataDir';
import { destinationRefusal, mayDeleteTheOldCopy, MoveRecord, sourceChangedSince, sourceRefusal, sourceWarning, StorageFingerprint, verificationFailure } from './dataMove';
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
export async function askWhereDataLives(context: vscode.ExtensionContext): Promise<void> {
  const DEFAULT = 'Keep it in the default folder';
  const CHOOSE = 'Choose a folder…';
  const answer = await vscode.window.showQuickPick([DEFAULT, CHOOSE], {
    title: 'Where should ConnectOtherAIs keep its data?',
    placeHolder: `${coaiDataDir()} — rounds, sessions, chats and spending`,
    ignoreFocusOut: true,
  });
  if (answer !== CHOOSE) {
    return;
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
    return;
  }

  // The ROOT is read first only to offer the sides already in it, which is a fact about the root.
  // What is ADOPTED is `<root>/<side>`, and asking the root about that was the defect: "this folder
  // already holds a database" could be true of the root and false of the directory about to be used,
  // and a history sitting in `<root>/<side>` was not found at all. (codex, plan round.)
  const inTheRoot = await whatIsIn(folder);
  const side = await askForSideName(inTheRoot);
  if (side === undefined) {
    return;
  }

  const resolved = directoryFor(folder.fsPath, side);
  const found = side.trim().length === 0 ? inTheRoot : await whatIsIn(vscode.Uri.file(resolved));

  // SHOWN BEFORE ANYTHING IS SAVED. It used to be a notification afterwards, which told a person
  // what they had adopted only once adopting it was done — and adopting the wrong folder is not
  // obviously recoverable. (local, plan round.)
  const go = found.hasDatabase ? 'Continue this history' : 'Use this folder';
  const confirmed = await vscode.window.showInformationMessage(
    `Keep this installation's data in ${resolved}?`,
    { modal: true, detail: adoptionSentence(resolved, found) },
    go,
  );
  if (confirmed !== go) {
    return;
  }

  const config = vscode.workspace.getConfiguration('coai');
  try {
    await saveSetting(context, config, 'dataDirectory', folder.fsPath);
    await saveSetting(context, config, 'dataSide', side);
  } catch (error) {
    // The choice did not land, so nothing may claim it did — and the block copied next must carry
    // what is actually in effect rather than what was asked for.
    reportRefusal(context, 'dataDirectory', error);

    return;
  }

  // In effect for THIS window immediately: the block about to be copied is built from it, and so is
  // every path the panel resolves from here on.
  storageReadsThisSide(context);
  await tellClientsToCatchUp(context, resolved);
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
    void vscode.window.showInformationMessage(
      `This window now reads ${directory}. Install the MCP server, then paste the block it gives you `
      + 'into your MCP client — until you do, the server writes where its own entry tells it to.');

    return;
  }

  await vscode.env.clipboard.writeText(mcpServerBlock(server.fsPath, serverEnv()));
  void vscode.window.showWarningMessage(
    `This window now reads ${directory}, and the updated MCP config block is on your clipboard.`,
    {
      modal: true,
      detail: 'Paste it into your MCP client and restart it. Until you do, the server it starts keeps '
        + 'writing to the folder its own entry names — which is the old one.\n\nThe path has to be the '
        + 'one that names this folder where the SERVER runs: the same drive is reached by a different '
        + 'route from Windows and from WSL.\n\nOther windows of this side keep the old folder until '
        + 'they are reloaded.',
    },
    'I have pasted it',
  );
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
    const entries = await withinReason(vscode.workspace.fs.readDirectory(folder));
    const inside = await Promise.all(entries.map(async ([name, kind]) => ({
      name,
      isDirectory: kind === vscode.FileType.Directory,
      hasDatabase: kind === vscode.FileType.Directory
        && await exists(vscode.Uri.joinPath(folder, name, DATABASE_FILE)),
    })));

    return {
      hasDatabase: entries.some(([name, kind]) => name === DATABASE_FILE && kind === vscode.FileType.File),
      sides: sidesIn(inside),
    };
  } catch {
    return { hasDatabase: false, sides: [] };
  }
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
    void vscode.window.showWarningMessage(busy);

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

  const clash = destinationRefusal(await historyIn(destination));
  if (clash.length > 0) {
    void vscode.window.showWarningMessage(clash);

    return;
  }

  const before = await countAt(from);
  // Said rather than refused. A sidecar means something has the database open or was stopped while
  // it did — and since the sidecars are copied WITH it, the rounds in them travel too. Refusing on
  // one made this feature unreachable for every installation that had ever been killed. (codex.)
  const sidecars = sourceWarning(activity);
  const go = 'Copy it';
  const confirmed = await vscode.window.showWarningMessage(
    `Copy ${before.rounds} rounds and ${before.sessions} sessions to ${destination.fsPath}?`,
    {
      modal: true,
      detail: `Nothing is deleted. ${from} is left exactly as it is, and you can delete it yourself `
        + 'once you have seen your history in the new folder.\n\nWhat is copied: '
        + `${DATA_TO_MOVE.join(', ')}.${sidecars.length === 0 ? '' : `\n\n${sidecars}`}`,
    },
    go,
  );
  if (confirmed !== go) {
    return;
  }

  const copied = await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: 'Copying your data…', cancellable: false },
    // The WHOLE wait, including the verification: a progress bar that ends before the slow part
    // leaves the longest half looking like nothing happening.
    async (progress) => {
      const failure = await copyInventory(vscode.Uri.file(from), destination, progress);
      if (failure.length > 0) {
        return failure;
      }

      progress.report({ message: 'checking it arrived…' });

      return verificationFailure(before, await countAt(destination.fsPath));
    },
  );

  await context.globalState.update(MOVE_RECORD, {
    from,
    to: destination.fsPath,
    verified: copied.length === 0,
    // What the SOURCE held when it was copied, so the delete can tell later whether anything has
    // written to it since — the one defence against a server this extension cannot stop or see.
    held: before,
  } satisfies MoveRecord);

  if (copied.length > 0) {
    void vscode.window.showErrorMessage(copied);

    return;
  }

  // Only now does this window point at the new folder — after the copy AND after the check. Pointing
  // first and copying second would show an empty history for as long as the copy took, and for ever
  // if it failed.
  const config = vscode.workspace.getConfiguration('coai');
  try {
    await saveSetting(context, config, 'dataDirectory', destination.fsPath);
    await saveSetting(context, config, 'dataSide', '');
  } catch (error) {
    reportRefusal(context, 'dataDirectory', error);

    return;
  }
  storageReadsThisSide(context);

  void vscode.window.showInformationMessage(
    `Your data is in ${destination.fsPath} and reads back the same ${before.rounds} rounds. `
    + `${from} still holds the original — delete it with "Delete the old data folder" once you are sure.`);
  await tellClientsToCatchUp(context, destination.fsPath);
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
    void vscode.window.showInformationMessage(
      record === undefined
        ? 'Nothing has been moved from this window, so there is no old folder to delete.'
        : 'That move did not check out, so its old folder is the only copy of anything it missed. '
          + 'Nothing will be deleted until a move verifies.');

    return;
  }

  const old = record!.from;

  // READ AGAIN, right now. The extension cannot stop the MCP client's server and cannot detect one
  // attached — the server opens the database per write and closes it, so an idle-looking folder
  // proves nothing. A move can therefore copy, verify, and then have a round appended to the SOURCE
  // before anybody presses this. Reading it once more turns that silent loss into a refusal, and it
  // is the only defence available here. (codex and gemini, plan round.)
  const moved = sourceChangedSince(record!, await countAt(old));
  if (moved.length > 0) {
    void vscode.window.showWarningMessage(moved);

    return;
  }

  const go = 'Delete it';
  const confirmed = await vscode.window.showWarningMessage(
    `Delete ${old}?`,
    {
      modal: true,
      detail: `Its contents were copied to ${record!.to}, read back the same, and nothing has written `
        + 'to it since.\n\nBefore you press this: every MCP client must already have been given the '
        + 'new block and restarted. A client still configured for this folder will recreate it and '
        + 'write there, and you will be reading one history while it writes another.\n\nThis cannot '
        + 'be undone, and it is the last copy of anything the move did not take — the sign-in tokens '
        + 'and the scratch worktrees are deliberately not copied.',
    },
    go,
  );
  if (confirmed !== go) {
    return;
  }

  try {
    await vscode.workspace.fs.delete(vscode.Uri.file(old), { recursive: true, useTrash: true });
  } catch (error) {
    void vscode.window.showErrorMessage(`${old} was not deleted: ${asText(error)}`);

    return;
  }

  await context.globalState.update(MOVE_RECORD, undefined);
  void vscode.window.showInformationMessage(`${old} is gone. Your data is in ${record!.to}.`);
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
