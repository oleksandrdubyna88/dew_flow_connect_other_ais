import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  ChildRecord,
  LivingProcess,
  forgotten,
  imageOf,
  isOurs,
  ledgerText,
  livingQuery,
  parseLedger,
  parseLiving,
  recorded,
} from './chatLedger';
import { capture } from './versionProbe';
import { killByPid } from './processLauncher';

/**
 * The ledger's world-facing half: the file, the question, and the kill.
 *
 * <p>Every rule about WHETHER to kill lives in `chatLedger.ts` and is tested there. This file does
 * the three things that need a machine — read and write one small file, ask the operating system who
 * holds a pid, and end a process — and holds no judgement of its own.</p>
 *
 * <p><b>Writes are serialised through one promise.</b> `codex` starts a process per turn, so a busy
 * conversation records and forgets often, and two overlapping read-modify-writes would lose one of
 * them. The chain costs nothing and removes the whole class.</p>
 */

/** Where the ledger lives. One file per installation, beside everything else this extension keeps. */
export function ledgerPath(storageDir: string): string {
  return join(storageDir, 'chat-children.json');
}

/** How long the process query may take before this side gives up and kills nothing. */
const QUERY_MS = 5_000;

let queue: Promise<unknown> = Promise.resolve();

/** Run a read-modify-write with nothing else in it. Never rejects: a ledger is not worth a crash. */
function serialised(work: () => Promise<void>): Promise<void> {
  const mine = queue.then(work).catch(() => undefined);
  queue = mine;

  return mine;
}

async function entriesIn(path: string): Promise<readonly ChildRecord[]> {
  try {
    return parseLedger(await readFile(path, 'utf8'));
  } catch {
    // No file is the ordinary case: nothing has been started yet, or the last shutdown was clean.
    return [];
  }
}

/** Write this child down, before it can be orphaned. */
export function remember(storageDir: string, pid: number, executable: string, now = Date.now()): Promise<void> {
  if (pid <= 0) {
    return Promise.resolve();
  }

  return serialised(async () => {
    const path = ledgerPath(storageDir);
    const entries = recorded(await entriesIn(path), { pid, image: imageOf(executable), startedMs: now });
    await writeFile(path, ledgerText(entries), 'utf8');
  });
}

/** Strike it out. Called when the child ends, however it ends — an exit, an error, a kill. */
export function forget(storageDir: string, pid: number): Promise<void> {
  if (pid <= 0) {
    return Promise.resolve();
  }

  return serialised(async () => {
    const path = ledgerPath(storageDir);
    await writeFile(path, ledgerText(forgotten(await entriesIn(path), pid)), 'utf8');
  });
}

/** What the operating system says about a pid, or nothing when it will not say. */
async function living(pid: number): Promise<LivingProcess | undefined> {
  if (process.platform !== 'win32') {
    // `ps -o comm=,lstart=` differs between BSD and GNU and this feature's copy helper is Windows
    // only anyway. Nothing is claimed rather than guessed, so nothing is killed.
    return undefined;
  }
  const asked = await capture(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', livingQuery(pid)],
    false,
    QUERY_MS,
  );

  return asked.code === 0 ? parseLiving(asked.output) : undefined;
}

/**
 * Clean up after a crash: kill what is still ours, and forget the rest.
 *
 * <p>Called once at activation. In the ordinary case the ledger is empty — the previous shutdown
 * struck out every child — and this returns without asking the operating system anything.</p>
 *
 * @returns how many processes were actually killed, for the caller to log
 */
export async function reconcile(storageDir: string): Promise<number> {
  const path = ledgerPath(storageDir);
  const entries = await entriesIn(path);
  if (entries.length === 0) {
    return 0;
  }

  let killed = 0;
  for (const record of entries) {
    // Re-checked, every time, against the two facts the ledger kept. A pid alone would be a licence
    // to kill whatever the operating system has since handed that number to.
    if (isOurs(record, await living(record.pid))) {
      killByPid(record.pid);
      killed += 1;
    }
  }
  // Emptied whatever happened: an entry that could not be verified is an entry that will never be
  // verified, and carrying it forward would ask the same unanswerable question at every activation.
  await serialised(async () => {
    await writeFile(path, ledgerText([]), 'utf8');
  });

  return killed;
}
