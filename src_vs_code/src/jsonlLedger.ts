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
 * <h2>One queue for every ledger, not one each</h2>
 *
 * <p>The queue exists so two appends issued from one process reach the file in the order they were
 * asked for. Sharing it across ledgers is not a compromise: it is what lets `deactivate` drain
 * everything by waiting on one promise, and a second queue would be a second thing to remember to
 * flush. Writes to different files do not wait on each other in any way that matters — an append is
 * microseconds, and nothing on the answering path waits for one at all.</p>
 *
 * <p>The measurement that makes several PROCESSES safe on one file is recorded in
 * `chatUsageFile.ts`, where the file it was measured on lives: `O_APPEND` (which `appendFile`'s
 * `'a'` is) does not tear, measured at 8 × 1000 records including 60 KB lines.</p>
 */

/** Appends, in order, one line at a time — every ledger in this process through the one chain. */
let queue: Promise<void> = Promise.resolve();

/**
 * Write one line down. Never rejects, and never delays the caller.
 *
 * <p>Returns the promise so a test can wait for the write it just asked for; the paths that record
 * a person's activity ignore it, because nothing anybody is reading may wait on a disk.</p>
 *
 * @param what names the ledger in the console line when a write fails — "a chat turn", "a door".
 */
export function appendLine(path: string, line: string, what: string): Promise<void> {
  queue = queue.then(async () => {
    try {
      // The directory may genuinely not exist: on a machine where nobody has run a review round, the
      // extension's chat is the FIRST thing to write anything under the coai data directory.
      await mkdir(dirname(path), { recursive: true });
      await appendFile(path, line, 'utf8');
    } catch (reason: unknown) {
      // Said out loud rather than swallowed, per `coding-style.md`. What is lost is one line of
      // accounting; what would be lost by throwing is the thing the person is actually doing.
      console.error(`ConnectOtherAIs: ${what} could not be written to its ledger`, reason);
    }
  });

  return queue;
}

/**
 * Wait for every queued write, to every ledger, to reach the disk.
 *
 * <p>Called from `deactivate`. Nothing waits for a write while the window is alive, which means a
 * host closed the instant something is recorded can take the line with it; VS Code awaits what
 * `deactivate` returns, so this is the one moment the queue can be drained without making anybody
 * wait for it.</p>
 */
export function flushLedgers(): Promise<void> {
  return queue;
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
