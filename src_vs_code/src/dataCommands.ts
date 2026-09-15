import { hostname } from 'node:os';
import * as vscode from 'vscode';
import { adoptionSentence, defaultSideName, FolderReport, sideRefusal, sidesIn } from './dataChoice';
import { coaiDataDir, DATABASE_FILE, DATA_TO_MOVE } from './dataDir';
import { destinationRefusal, mayDeleteTheOldCopy, MoveRecord, sourceRefusal, StorageFingerprint, verificationFailure } from './dataMove';
import { reportRefusal, saveSetting, storageReadsThisSide } from './sideConfig';
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

  const found = await whatIsIn(folder);
  const side = await askForSideName(found);
  if (side === undefined) {
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
  void vscode.window.showInformationMessage(adoptionSentence(folder.fsPath, found));
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
    const entries = await vscode.workspace.fs.readDirectory(folder);
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

  const busy = sourceRefusal(await whatIsRunningIn(from));
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
  const go = 'Copy it';
  const confirmed = await vscode.window.showWarningMessage(
    `Copy ${before.rounds} rounds and ${before.sessions} sessions to ${destination.fsPath}?`,
    {
      modal: true,
      detail: `Nothing is deleted. ${from} is left exactly as it is, and you can delete it yourself `
        + 'once you have seen your history in the new folder.\n\nWhat is copied: '
        + `${DATA_TO_MOVE.join(', ')}.`,
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
export async function deleteTheOldDataFolder(context: vscode.ExtensionContext): Promise<void> {
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
  const go = 'Delete it';
  const confirmed = await vscode.window.showWarningMessage(
    `Delete ${old}?`,
    {
      modal: true,
      detail: `Its contents were copied to ${record!.to} and read back the same. This cannot be `
        + 'undone, and it is the last copy of anything the move did not take — the sign-in tokens '
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
      return `${name} could not be copied: ${asText(error)}. Nothing has been deleted, and `
        + `${from.fsPath} is unchanged — it is still your data.`;
    }
  }

  return '';
}
