import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { readdir, rm, stat } from 'node:fs/promises';
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
  tooOld,
  verifyAndKill,
  worthAsking,
} from './chatLedger';
import { capture } from './versionProbe';

/**
 * The ledger's world-facing half: the files, the command, and the log line.
 *
 * <p>Every rule about WHETHER a process may be killed lives in `chatLedger.ts` and is tested there.
 * This file does the things that need a machine — write one small file, run one command per
 * candidate, say what happened — and holds no judgement of its own.</p>
 *
 * <p><b>One file per extension host.</b> `globalStorageUri` is shared by every VS Code window, and a
 * single shared file meant one window reading another window's LIVE children and killing them as
 * orphans. The owner's pid is in the file name, a window only reads files whose owner is gone, and
 * two windows never write the same file. (gemini and local, the plan round, three findings.)</p>
 *
 * <p><b>Our own file is written SYNCHRONOUSLY, from memory.</b> The window this feature guards
 * against is a force-kill, and a queued asynchronous write is exactly what a force-kill does not
 * wait for: a child launched and killed a millisecond later would never have been written down. The
 * list of this host's children lives in memory — it is the only writer of its own file, so there is
 * nothing to read back — and every change is one small `writeFileSync`. Measured in the order of a
 * millisecond, against a guarantee that is otherwise not a guarantee. (codex, the code round.)</p>
 */

/** How long one verify-and-kill may take before this side gives up and keeps the record. */
const COMMAND_MS = 8_000;

/**
 * How long the whole sweep may take.
 *
 * <p>One command per candidate, and a candidate exists only after a crash — so the ordinary sweep is
 * empty and a bad one is two or three. A bound anyway: nothing that runs at activation should be
 * able to run for minutes because a machine's PowerShell is unwell. What is left unasked stays in
 * the file and is retried next time, which is the same answer as any other unresolved record.</p>
 */
const SWEEP_MS = 30_000;

/** This host's own children, in memory. It is the only writer of its own file. */
let mine: readonly ChildRecord[] = [];

/** Where this extension host writes. Nobody else writes here; nobody else reads it while we live. */
export function ledgerPath(storageDir: string, ownerPid = process.pid): string {
  return join(storageDir, ledgerName(ownerPid));
}

/**
 * Write a ledger file so that a crash cannot leave half of one.
 *
 * <p>A plain write truncates first, and a force-kill in that window leaves a file that parses to
 * nothing — losing the very record it exists to keep. Written beside it and renamed over it instead;
 * a rename within one directory is atomic on both filesystems this ships to. (codex, the code round.)</p>
 */
function writeAtomically(path: string, text: string): void {
  const beside = `${path}.${process.pid}.tmp`;
  writeFileSync(beside, text, 'utf8');
  renameSync(beside, path);
}

/** Say what went wrong rather than swallow it: a ledger that cannot be written is a child nobody can clean up. */
function saved(storageDir: string): void {
  try {
    mkdirSync(storageDir, { recursive: true });
    writeAtomically(ledgerPath(storageDir), ledgerText(mine));
  } catch (reason) {
    console.warn(`[coai] the chat ledger could not be written: ${reason instanceof Error ? reason.message : reason}`);
  }
}

/** Write this child down, before it can be orphaned. Synchronous, on purpose — see the note above. */
export function remember(storageDir: string, pid: number, executable: string, now = Date.now()): void {
  if (pid <= 0 || storageDir.length === 0) {
    return;
  }
  mine = recorded(mine, { pid, image: imageOf(executable), startedMs: now });
  saved(storageDir);
}

/** Strike it out. Called when the child ends, however it ends — an exit, an error, a kill. */
export function forget(storageDir: string, pid: number): void {
  if (pid <= 0 || storageDir.length === 0) {
    return;
  }
  mine = forgotten(mine, pid);
  saved(storageDir);
}

/**
 * Is the process that wrote this ledger still running?
 *
 * <p>Signal 0 asks without sending anything, and the ERROR is the answer: `ESRCH` means no such
 * process, and anything else — `EPERM` most of all — means a process that exists and will not be
 * inspected by us. Treating that as dead would be the one mistake this module cannot afford: it
 * would read another window's ledger and kill the children it is using right now. So only `ESRCH`
 * is death, and everything else is life. (gemini, the code round.)</p>
 */
function ownerAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);

    return true;
  } catch (reason) {
    return (reason as NodeJS.ErrnoException).code !== 'ESRCH';
  }
}

/** One candidate: check the three facts and end its tree, inside a single command. */
async function endIfOurs(record: ChildRecord): Promise<ReturnType<typeof killOutcome>> {
  const command = verifyAndKill(record);
  if (process.platform !== 'win32' || command.length === 0) {
    // The verify-and-kill is PowerShell, and a record whose image could not appear in a command is
    // one this side refuses to build one for. Nothing claimed, nothing killed, the record kept.
    return 'unknown';
  }
  const ran = await capture(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', command],
    false,
    COMMAND_MS,
  );

  return killOutcome(ran.code, ran.output);
}

/** Everything in this file is older than the bound — nothing in it can be asked about ever again. */
async function longDead(path: string, entries: readonly ChildRecord[], now: number): Promise<boolean> {
  if (entries.length === 0) {
    try {
      // An empty ledger from a dead window is finished business; one being written right now is not.
      const age = now - (await stat(path)).mtimeMs;

      return age > SWEEP_MS;
    } catch {
      return false;
    }
  }

  return entries.every((record) => tooOld(record, now));
}

/**
 * Clean up after a crash: end what is still provably ours, and keep what nobody could ask about.
 *
 * <p>Called once at activation and never awaited by it. In the ordinary case there is one file —
 * this host's own, which is not read — and nothing else to do.</p>
 *
 * @returns what was ended, each one named: a count is the one thing nobody can act on if it was
 *   ever the wrong process. (local, the plan round.)
 */
export async function reconcile(storageDir: string, now = Date.now()): Promise<readonly string[]> {
  if (storageDir.length === 0) {
    return [];
  }
  let names: string[];
  try {
    names = await readdir(storageDir);
  } catch (reason) {
    const code = (reason as NodeJS.ErrnoException).code;
    if (code !== 'ENOENT') {
      // A directory that cannot be read is not the same as one with nothing in it, and the
      // difference is a cleanup that silently never happens. (codex, the code round.)
      console.warn(`[coai] the chat ledger directory could not be read: ${code ?? reason}`);
    }

    return [];
  }

  const killed: string[] = [];
  const until = now + SWEEP_MS;
  for (const name of names) {
    const owner = ownerOf(name);
    const path = join(storageDir, name);
    // Not a ledger, our own, or one whose window is still using it. The last is the important one:
    // those children are alive on purpose.
    if (owner === 0 || owner === process.pid) {
      continue;
    }
    if (ownerAlive(owner)) {
      // A pid the operating system has since handed to something long-lived would make this file
      // invisible for ever. It cannot be acted on — but it can stop growing. (codex.)
      await removeIfLongDead(path, now);
      continue;
    }

    await tidy(path, now, until, killed);
  }

  return killed;
}

/** One dead window's ledger: ask about what is worth asking about, keep what nobody could answer. */
async function tidy(path: string, now: number, until: number, killed: string[]): Promise<void> {
  let entries: readonly ChildRecord[];
  try {
    entries = parseLedger(readFileSync(path, 'utf8'));
  } catch (reason) {
    // Unreadable is NOT empty. Deleting it here would throw away the only record of a process this
    // side could not reach — the exact outcome the ledger exists to prevent. (codex.)
    console.warn(`[coai] a chat ledger could not be read: ${(reason as NodeJS.ErrnoException).code ?? reason}`);

    return;
  }

  const kept: ChildRecord[] = [];
  let asked = true;
  for (const record of worthAsking(entries, now)) {
    if (Date.now() > until) {
      // Out of time. What was not asked stays, and is asked at the next activation.
      asked = false;
      kept.push(record);
      continue;
    }
    const outcome = await endIfOurs(record);
    if (outcome === 'killed') {
      killed.push(`${record.image} (pid ${record.pid}, started ${new Date(record.startedMs).toISOString()})`);
    }
    if (!settled(outcome)) {
      // Nobody could tell. Kept and retried, because dropping it is how an orphan becomes permanent.
      kept.push(record);
    }
  }

  try {
    if (kept.length === 0 && asked) {
      await rm(path, { force: true });
    } else {
      writeAtomically(path, ledgerText(kept));
    }
  } catch (reason) {
    console.warn(`[coai] a chat ledger could not be tidied: ${reason instanceof Error ? reason.message : reason}`);
  }
}

/** A file nobody will ever be able to act on, removed so it cannot accumulate. */
async function removeIfLongDead(path: string, now: number): Promise<void> {
  try {
    if (await longDead(path, parseLedger(readFileSync(path, 'utf8')), now)) {
      await rm(path, { force: true });
    }
  } catch {
    // Unreadable, or gone already. Either way there is nothing to do and nothing to say.
  }
}
