import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { readFile, readdir, rm, stat } from 'node:fs/promises';
import { join } from 'node:path';
import {
  ChildRecord,
  KillOutcome,
  afterSweep,
  bootSecondsIn,
  filesToSweep,
  forgotten,
  imageOf,
  killOutcome,
  ledgerName,
  ledgerText,
  namesOf,
  ownerOf,
  parseLedger,
  procStat,
  recorded,
  settled,
  startedAtMs,
  stillOurs,
  tooOld,
  verifyAndKill,
  worthAsking,
} from './chatLedger';
import { capture } from './versionProbe';
import { hostPlatform } from './hostSide';

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

/**
 * Where this host writes, bound ONCE.
 *
 * <p>It used to travel as an argument through six functions — the command, the conversation, the
 * process factory — which meant a module-level list of children paired with a per-call directory:
 * two things that must agree, with nothing making them. An extension host has one
 * `globalStorageUri` for its whole life, so it is set at activation and never asked for again, and
 * the code that starts a child no longer has to know where a ledger lives. (codex and gemini, the
 * second code round.)</p>
 *
 * <p>Empty means no ledger: the tests, and any caller that never opened one.</p>
 */
let home = '';

/** Bind the ledger's directory. Called once, from `activate`. */
export function openLedger(storageDir: string): void {
  home = storageDir;
}

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
function saved(): void {
  try {
    mkdirSync(home, { recursive: true });
    writeAtomically(ledgerPath(home), ledgerText(mine));
  } catch (reason) {
    console.warn(`[coai] the chat ledger could not be written: ${reason instanceof Error ? reason.message : reason}`);
  }
}

/** Write this child down, before it can be orphaned. Synchronous, on purpose — see the note above. */
export function remember(pid: number, executable: string, now = Date.now()): void {
  if (pid <= 0 || home.length === 0) {
    return;
  }
  mine = recorded(mine, { pid, image: imageOf(executable), startedMs: now });
  saved();
}

/** Strike it out. Called when the child ends, however it ends — an exit, an error, a kill. */
export function forget(pid: number): void {
  if (pid <= 0 || home.length === 0) {
    return;
  }
  mine = forgotten(mine, pid);
  saved();
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

/**
 * What `/proc` could say about one pid.
 *
 * <p>`absent` and `unknown` are deliberately different answers, and collapsing them was a real
 * defect in the first draft of this: a process that exits while its files are being read is GONE and
 * its row can be struck out, while a read that failed for any other reason is a question nobody
 * answered — and a row struck out on that would be an orphan nobody ever looks for again.</p>
 */
export type ProcEntry =
  | {
    readonly kind: 'found';
    readonly names: readonly string[];
    /** Epoch milliseconds, or `-1` when either half of the sum could not be read. */
    readonly startedMs: number;
    /** The kernel's own field 22, kept unconverted so it can be compared EXACTLY. See `endVerified`. */
    readonly startTicks: number;
  }
  | { readonly kind: 'absent' }
  | { readonly kind: 'unknown' };

/**
 * When this machine booted, read ONCE.
 *
 * <p>`btime` is host-wide and cannot change while this process lives, and the sweep asks about every
 * candidate — so reading and regex-parsing `/proc/stat` per row was one redundant file read per
 * orphan at activation. Two reviewers found it independently. A read that fails leaves the cache
 * unset rather than poisoning it with a `-1` nothing could recover from.</p>
 */
let bootSeconds = -1;

async function bootOnce(): Promise<number> {
  if (bootSeconds < 0) {
    bootSeconds = bootSecondsIn(await readFile('/proc/stat', 'utf8').catch(() => ''));
  }

  return bootSeconds;
}

/** The two files that identify a Linux process, against the boot time its start is counted from. */
async function procEntry(pid: number): Promise<ProcEntry> {
  try {
    const [statText, cmdline] = await Promise.all([
      readFile(`/proc/${pid}/stat`, 'utf8'),
      readFile(`/proc/${pid}/cmdline`, 'utf8'),
    ]);
    const { comm, startTicks } = procStat(statText);

    return {
      kind: 'found',
      // NUL-separated, and the trailing separator leaves an empty last element that is not an argument.
      names: namesOf(comm, cmdline.split('\0').filter((part) => part.length > 0)),
      startedMs: startedAtMs(startTicks, await bootOnce()),
      startTicks,
    };
  } catch (reason) {
    return (reason as NodeJS.ErrnoException).code === 'ENOENT' ? { kind: 'absent' } : { kind: 'unknown' };
  }
}

/**
 * The kernel's own start ticks for a pid — the cheapest identifying read there is, and the LAST
 * thing done before a signal.
 *
 * <p>It exists to narrow the pid-reuse window, which two reviewers filed as Blocking. The window
 * cannot be closed in Node — `pidfd_open` is the only thing that closes it and needs a native module
 * — but it can be made small and exact: the identity read that used to precede a kill was two files
 * and two parses, and this is one small file compared byte-for-byte. `-1` for every failure, which
 * never equals a real tick count, so a process that vanished in that window is not killed.</p>
 */
async function startTicksOf(pid: number): Promise<number> {
  try {
    return procStat(await readFile(`/proc/${pid}/stat`, 'utf8')).startTicks;
  } catch {
    return -1;
  }
}

/**
 * One candidate on a POSIX host: check the same three facts, then end it.
 *
 * <p><b>Why this exists at all.</b> The Windows path asks its three facts and kills inside ONE
 * PowerShell command, and everything that was not Windows used to answer `'unknown'` — which is the
 * one outcome that does not settle a row. So in a WSL window a vendor CLI orphaned by a force-kill
 * was never ended, and its record was re-asked at every activation for ever.</p>
 *
 * <p><b>Both sides of the world are injected</b>, so all seven branches are a test on a Windows
 * machine with no `/proc` and no signals to send. That is the one place this deviates from the
 * neighbouring Windows path, which is untested because a PowerShell round trip cannot be faked
 * cheaply; there was no such excuse here.</p>
 *
 * <p><b>One pid, not a tree, and that is evidence rather than preference.</b> Windows needs
 * `taskkill /t` because a vendor CLI there is a shim tree — `codex.cmd` to `cmd.exe` to `node`.
 * `cliVersions.needsShell` gates that to `win32` and a `.cmd`/`.bat` name, so on Linux `spawn` never
 * goes through a shell and the pid we wrote down IS the vendor process.
 * `/proc/<pid>/task/<pid>/children` was measured to exist and answer, and is where a walk would go if
 * a vendor is ever found to daemonise — on that evidence, not before it.</p>
 *
 * <p><b>The pid-reuse window is narrowed, not claimed away.</b> Nothing happens between the read and
 * the signal — no await, no second read — and a replacement would have to carry the same executable
 * name AND have started within ten seconds of the recorded start to be mistaken for ours. `pidfd_open`
 * would close it properly and is not reachable from Node without a native module.</p>
 */
export async function endIfOursPosix(
  record: ChildRecord,
  observe: (pid: number) => Promise<ProcEntry> = procEntry,
  end: (pid: number) => void = (pid) => process.kill(pid, 'SIGKILL'),
  ticks: (pid: number) => Promise<number> = startTicksOf,
): Promise<KillOutcome> {
  const seen = await observe(record.pid);
  if (seen.kind !== 'found') {
    return seen.kind === 'absent' ? 'gone' : 'unknown';
  }
  // A start time nobody could COMPUTE is not evidence that this is a stranger. Reading it as one
  // answers 'not ours', which SETTLES the row — so a permission failure or an unparsable `/proc`
  // would strike out the record of a process that is still running, which is the outcome this whole
  // sweep exists to prevent. Unknown keeps it and asks again. (gemini, the code round.)
  if (seen.startedMs < 0) {
    return 'unknown';
  }
  if (!stillOurs(record, seen)) {
    return 'not ours';
  }

  return endVerified(record.pid, seen.startTicks, end, ticks);
}

/**
 * Re-read the one identifying number, then signal — with nothing between the two.
 *
 * <p>This is what is left of the pid-reuse race after it was narrowed as far as Node allows. The
 * comparison is the kernel's own tick count and is EXACT, where `stillOurs` necessarily works to a
 * ten-second tolerance (the ledger records `Date.now()` at spawn, and `btime` is whole seconds).
 * A recycled pid would have to be re-created between this read and the next statement AND land on
 * the identical start tick to survive it.</p>
 */
async function endVerified(
  pid: number,
  startTicks: number,
  end: (pid: number) => void,
  ticks: (pid: number) => Promise<number>,
): Promise<KillOutcome> {
  if (await ticks(pid) !== startTicks) {
    return 'not ours';
  }

  try {
    end(pid);

    return 'killed';
  } catch (reason) {
    // It exited between the read and the signal — a benign race, and the same answer the Windows
    // path gives for a pid that is no longer there. Anything else, `EPERM` above all, is a process
    // we may not touch: unknown, so the row is kept and asked about again.
    return (reason as NodeJS.ErrnoException).code === 'ESRCH' ? 'gone' : 'unknown';
  }
}

/** One candidate on Windows: the three facts and the tree kill, inside a single command. */
async function endIfOursWindows(record: ChildRecord): Promise<KillOutcome> {
  const command = verifyAndKill(record);
  if (command.length === 0) {
    // A record whose image could not appear in a command is one this side refuses to build one for.
    // Nothing claimed, nothing killed, the record kept.
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

/** One candidate, asked in the way the side this host is on can answer. */
async function endIfOurs(record: ChildRecord): Promise<KillOutcome> {
  const here = hostPlatform();
  if (here === 'linux') {
    return endIfOursPosix(record);
  }

  // darwin has no `/proc` and no verified equivalent yet, so it answers exactly as it did before:
  // nothing claimed, nothing killed, the record kept for a build that can answer.
  return here === 'win32' ? endIfOursWindows(record) : 'unknown';
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
  // Which files may be opened is a decision, and it lives with the other decisions: a file whose
  // owner is still running belongs to a window using its children right now.
  const ours = filesToSweep(names, process.pid, ownerAlive);
  for (const file of ours) {
    await tidy(join(storageDir, file.name), now, until, killed);
  }
  // The ones NOT swept because their owner looked alive: a pid the operating system has since handed
  // to something long-lived would make a file invisible for ever. It cannot be acted on — but it can
  // stop growing. (codex.)
  const swept = new Set(ours.map((file) => file.name));
  for (const name of names) {
    if (ownerOf(name) !== 0 && ownerOf(name) !== process.pid && !swept.has(name)) {
      await removeIfLongDead(join(storageDir, name), now);
    }
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
    if (afterSweep(kept, asked) === 'remove') {
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
