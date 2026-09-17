import { appendFile, mkdir, readFile } from 'node:fs/promises';
import { dirname } from 'node:path';

/**
 * Appending to a JSONL ledger, and reading one back — the half that needs a disk.
 *
 * <p>Extracted when the door ledger arrived and wanted the same three things the turn ledger has:
 * an ordered append that never throws into its caller, a drain for `deactivate`, and a read that
 * tells a missing file from an unreadable one. Copying them would have been thirty lines and two
 * places for the same bargain to drift — which is exactly what the reuse rule is about. Every
 * judgement about what a RECORD means stays in the module that owns the record; this holds none.</p>
 *
 * <h2>One queue per CHAIN — which used to be one for everything</h2>
 *
 * <p>The queue exists so two appends issued from one process reach the file in the order they were
 * asked for. It was a single chain for every ledger here, justified on the grounds that an append is
 * microseconds and nothing on the answering path waits for one. The notifications ledger breaks both
 * halves of that: on a NAS an append is tens of milliseconds and can hang for SMB's timeout, and a
 * repeating fault queues hundreds of them — so a stalled notification append would delay a CHAT TURN
 * behind it, and `deactivate` would wait for the whole backlog before the window could close, after
 * which VS Code kills the host and the tail is lost anyway.</p>
 *
 * <p>So there are two chains and `flushLedgers` drains both: one drain for everything, which was the
 * point of sharing, without one ledger's disk trouble becoming another's. (The plan round on
 * `todo/PLAN_every_message_is_written_down.md`.)</p>
 *
 * <h2>What is measured, and under which conditions</h2>
 *
 * <p>`npm run measure:append` forks real processes at one file and checks every line back. Three
 * runs, and the conditions are part of each result:</p>
 *
 * <ul>
 *   <li><b>2026-09-09</b>, `appendFileSync`, 8 × 1000 records including 60 KB lines, local NTFS:
 *       8000 whole records, 0 torn. Recorded in `chatUsageFile.ts`, where the file it was measured
 *       on lives.</li>
 *   <li><b>2026-09-16</b>, the PROMISE-based `appendFile` — which is what this module actually
 *       calls — 8 × 1000, 116 MB, local NTFS: 8000 of 8000, 0 torn. The earlier run measured a
 *       different function, and the notifications plan had cited it as though it did not; the same
 *       argument about `O_APPEND` is not the same measurement.</li>
 *   <li><b>2026-09-16</b>, the same writer over <b>SMB</b> to a NAS share, 4 × 200, 11.6 MB:
 *       800 of 800, 0 torn. This is the condition that mattered most here — the data directory is
 *       relocatable and has been a NAS share on this machine, and SMB does not guarantee atomic
 *       append in general. It held on this one.</li>
 * </ul>
 *
 * <p>None of that is a claim about every filesystem. `--dir=` is how the next person asks the same
 * question of theirs, and the sanctioned answer if it ever tears is one ledger per process
 * (`notifications-&lt;pid&gt;.jsonl`, the `chatOrphans.ts` precedent) with a glob in the reader.</p>
 */

/** Which ordered queue an append joins. */
export type LedgerChain = 'chat' | 'notifications';

const CHAINS: readonly LedgerChain[] = ['chat', 'notifications'];

/** Appends, in order, one line at a time — per chain. */
const queues: Record<LedgerChain, Promise<void>> = {
  chat: Promise.resolve(),
  notifications: Promise.resolve(),
};

/**
 * How long `deactivate` may spend draining before it gives the window back.
 *
 * <p>A drain without a ceiling is a window that will not close, and VS Code answers that by killing
 * the host — which loses more than the ceiling gives up.</p>
 */
export const FLUSH_CEILING_MS = 2_000;

export interface AppendOptions {
  /** Which chain to join. Defaults to the chat chain, which is what every earlier caller used. */
  readonly chain?: LedgerChain;
  /**
   * Told when the line could not be written, so a caller can COUNT what was lost.
   *
   * <p>Without it the loss is invisible to everybody but a devtools console nobody opens: this
   * function catches internally and hands its caller a resolved promise, so a notifications funnel
   * built on it as it stood would have reported "0 records could not be written" straight through a
   * disk failure. A log that lies by omission is worse than no log, and that is the thesis of the
   * ledger this parameter was added for. (The plan round, round 2.)</p>
   */
  readonly onFailure?: (reason: unknown) => void;
}

/**
 * Write one line down. Never rejects, and never delays the caller.
 *
 * <p>Returns the promise so a test — or a caller that has promised to RECORD before it shows
 * something — can wait for the write it just asked for; the paths that record a person's activity
 * ignore it, because nothing anybody is reading may wait on a disk.</p>
 *
 * @param what names the ledger in the console line when a write fails — "a chat turn", "a door".
 */
export function appendLine(
  path: string,
  line: string,
  what: string,
  options: AppendOptions = {},
): Promise<void> {
  const chain = options.chain ?? 'chat';
  queues[chain] = queues[chain].then(async () => {
    try {
      // The directory may genuinely not exist: on a machine where nobody has run a review round, the
      // extension's chat is the FIRST thing to write anything under the coai data directory.
      await mkdir(dirname(path), { recursive: true });
      await appendFile(path, line, 'utf8');
    } catch (reason: unknown) {
      // Said out loud rather than swallowed, per `coding-style.md`. What is lost is one line of
      // accounting; what would be lost by throwing is the thing the person is actually doing.
      console.error(`ConnectOtherAIs: ${what} could not be written to its ledger`, reason);
      options.onFailure?.(reason);
    }
  });

  return queues[chain];
}

/**
 * Wait for every queued write, on every chain, to reach the disk — or for the ceiling.
 *
 * <p>Called from `deactivate`. This used to return the CURRENT queue, which is a snapshot and not a
 * drain: every `appendLine` reassigns its chain, so anything recorded after that line — a dispose
 * handler, a watcher teardown, the settings sync's last attempt, anything VS Code runs while
 * awaiting deactivate — chained onto a promise nobody was awaiting and went with the host. With one
 * caller and a handful of chat turns it never showed; with a funnel in front of a hundred call sites
 * it would have, and the records lost would be the ones written as the window died. So it drains to
 * QUIESCENCE: await, then ask whether anything joined while we waited, and stop only when nothing
 * did.</p>
 *
 * <p><b>Bounded against the CLOCK, not against the writes.</b> The first version checked the
 * deadline only after `await Promise.all(before)` had resolved — which bounds a drain that is making
 * progress and does nothing at all about the one case the bound exists for. An append pending on a
 * data directory that has become unreachable (the operator's is a NAS) never resolves, so the await
 * never returns, the deadline is never read, and the window will not close until VS Code kills the
 * host — losing the whole tail instead of the part that could not be written. The docstring above
 * promised a ceiling the code did not have. Found on the code round by codex, as Blocking, and by
 * the local reviewer from the performance side.</p>
 *
 * <p>It answers whether everything reached the disk, so a caller can record the gap rather than
 * assume there is none.</p>
 */
export async function flushLedgers(withinMs: number = FLUSH_CEILING_MS): Promise<boolean> {
  const deadline = Date.now() + withinMs;
  for (;;) {
    const before = CHAINS.map((chain) => queues[chain]);
    if (!await withinTheClock(Promise.all(before), deadline - Date.now())) {
      return false;
    }
    const joined = CHAINS.some((chain, i) => queues[chain] !== before[i]);
    if (!joined) {
      return true;
    }
    if (Date.now() >= deadline) {
      return false;
    }
  }
}

/**
 * Whether `work` finished inside the time left.
 *
 * <p>The timer is `unref`'d, because a pending timer is itself a reason a Node process will not
 * exit, and a helper for shutting down cleanly that holds the process open would be a joke at its
 * own expense. It is cleared on the winning path too: a two-second timer left armed after a fast
 * drain keeps a handle alive for no reason.</p>
 *
 * <p>The losing path does NOT cancel the write — nothing can — it stops WAITING for it. If the
 * directory comes back before the host dies, the append still lands.</p>
 */
async function withinTheClock(work: Promise<unknown>, msLeft: number): Promise<boolean> {
  if (msLeft <= 0) {
    return false;
  }
  let timer: NodeJS.Timeout | undefined;
  const clock = new Promise<false>((resolve) => {
    timer = setTimeout(() => resolve(false), msLeft);
    timer.unref?.();
  });
  try {
    return await Promise.race([work.then(() => true), clock]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

/**
 * Every record in one ledger, or nothing at all.
 *
 * <p>A file being appended to WHILE it is read can end in half a line, which the parsers drop; that
 * costs the newest record a place on the page until the next tick, which is the bargain `usage.ts`
 * already makes for the server's ledger.</p>
 *
 * <p><b>A missing file and an unreadable one are different facts, and only one of them is
 * ordinary.</b> No file means nothing has happened yet. A permission error, a directory where the
 * file should be, a disk that stopped answering — those mean the history EXISTS and could not be
 * read, and returning empty for them shows a person "nothing here" when the truth is "not readable".
 * It still returns empty, because a page must not fail to open over one of its data sources; what it
 * does not do is stay quiet about it.</p>
 */
export async function readLedger<T>(
  path: string,
  parse: (text: string) => T[],
  what: string,
): Promise<readonly T[]> {
  try {
    return parse(await readFile(path, 'utf8'));
  } catch (reason: unknown) {
    if ((reason as { code?: unknown } | null)?.code !== 'ENOENT') {
      console.error(`ConnectOtherAIs: the ${what} ledger exists but could not be read`, reason);
    }

    return [];
  }
}
