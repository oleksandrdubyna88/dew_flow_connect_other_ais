import { mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  ChildRecord,
  forgotten,
  imageOf,
  killOutcome,
  ledgerName,
  ledgerText,
  ownerOf,
  parseLedger,
  recorded,
  settled,
  verifyAndKill,
  worthAsking,
} from './chatLedger';
import { capture } from './versionProbe';

/**
 * The ledger's world-facing half: the files, the command, and the log line.
 *
 * <p>Every rule about WHETHER a process may be killed lives in `chatLedger.ts` and is tested there.
 * This file does the things that need a machine — read and write small files, run one command per
 * candidate, say what happened — and holds no judgement of its own.</p>
 *
 * <p><b>One file per extension host.</b> `globalStorageUri` is shared by every VS Code window, and a
 * single shared file meant one window reading another window's LIVE children and killing them as
 * orphans. The owner's pid is in the file name, a window only reads files whose owner is gone, and
 * two windows never write the same file — which also removes the cross-process write race that no
 * in-memory promise could have covered. (gemini and local, one plan round, three findings.)</p>
 */

/** How long one verify-and-kill may take before this side gives up and keeps the record. */
const COMMAND_MS = 8_000;

/** Where this extension host writes. Nobody else writes here; nobody else reads it while we live. */
export function ledgerPath(storageDir: string, ownerPid = process.pid): string {
  return join(storageDir, ledgerName(ownerPid));
}

let queue: Promise<unknown> = Promise.resolve();

/**
 * Run a read-modify-write with nothing else in it.
 *
 * <p>`codex` starts a process per turn, so a busy conversation records and forgets often and two
 * overlapping read-modify-writes inside this process would lose one. Across processes the file name
 * does the same job. A failure is SAID rather than swallowed — a ledger that cannot be written means
 * a child that cannot be cleaned up, and that is worth a line in the host log even though it is not
 * worth failing a conversation over.</p>
 */
function serialised(what: string, work: () => Promise<void>): Promise<void> {
  const mine = queue.then(work).catch((reason: unknown) => {
    console.warn(`[coai] the chat ledger could not be ${what}: ${reason instanceof Error ? reason.message : reason}`);
  });
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
  if (pid <= 0 || storageDir.length === 0) {
    return Promise.resolve();
  }

  return serialised('written', async () => {
    await mkdir(storageDir, { recursive: true });
    const path = ledgerPath(storageDir);
    const entries = recorded(await entriesIn(path), { pid, image: imageOf(executable), startedMs: now });
    await writeFile(path, ledgerText(entries), 'utf8');
  });
}

/** Strike it out. Called when the child ends, however it ends — an exit, an error, a kill. */
export function forget(storageDir: string, pid: number): Promise<void> {
  if (pid <= 0 || storageDir.length === 0) {
    return Promise.resolve();
  }

  return serialised('struck out', async () => {
    const path = ledgerPath(storageDir);
    await writeFile(path, ledgerText(forgotten(await entriesIn(path), pid)), 'utf8');
  });
}

/**
 * Is the process that wrote this ledger still running?
 *
 * <p>Signal 0 asks without sending anything. A pid that is alive belongs to another VS Code window
 * that is using its own children right now, and its file is not ours to read. A pid that has been
 * recycled onto something else reads as alive, and the answer is then to leave the file alone —
 * which loses a tidy-up and kills nothing, the direction this whole module errs in.</p>
 */
function ownerAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);

    return true;
  } catch {
    return false;
  }
}

/** One candidate: check the three facts and end its tree, inside a single command. */
async function endIfOurs(record: ChildRecord): Promise<ReturnType<typeof killOutcome>> {
  if (process.platform !== 'win32') {
    // The verify-and-kill is PowerShell, and this feature's selection capture is Windows-only
    // anyway. Nothing is claimed rather than guessed, so nothing is killed and the record is kept.
    return 'unknown';
  }
  const ran = await capture(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', verifyAndKill(record)],
    false,
    COMMAND_MS,
  );

  return killOutcome(ran.code, ran.output);
}

/**
 * Clean up after a crash: end what is still provably ours, and keep what nobody could ask about.
 *
 * <p>Called once at activation, and never awaited by it. In the ordinary case there is one file —
 * this host's own, which is not read — and nothing else to do.</p>
 *
 * @returns what was ended — each one named, so a person who sees a line about it can tell WHICH
 *   process went. A count alone is the one thing nobody can act on. (local, the plan round.)
 */
export async function reconcile(storageDir: string, now = Date.now()): Promise<readonly string[]> {
  if (storageDir.length === 0) {
    return [];
  }
  let names: string[];
  try {
    names = await readdir(storageDir);
  } catch {
    return [];
  }

  const killed: string[] = [];
  for (const name of names) {
    const owner = ownerOf(name);
    // Not a ledger, our own, or one whose window is still using it. The last is the important one:
    // those children are alive on purpose.
    if (owner === 0 || owner === process.pid || ownerAlive(owner)) {
      continue;
    }

    const path = join(storageDir, name);
    const entries = await entriesIn(path);
    const kept: ChildRecord[] = [];
    for (const record of worthAsking(entries, now)) {
      const outcome = await endIfOurs(record);
      if (outcome === 'killed') {
        killed.push(`${record.image} (pid ${record.pid}, started ${new Date(record.startedMs).toISOString()})`);
      }
      if (!settled(outcome)) {
        // Nobody could tell. Kept and retried, because dropping it is how an orphan becomes
        // permanent — a machine with no PowerShell would otherwise lose the record on the first
        // activation after the crash. (codex, the plan round.)
        kept.push(record);
      }
    }

    await serialised('tidied', async () => {
      if (kept.length === 0) {
        await rm(path, { force: true });
      } else {
        await writeFile(path, ledgerText(kept), 'utf8');
      }
    });
  }

  return killed;
}
