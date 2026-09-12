import { mkdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { mkdir, rename, rm, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

/**
 * Writing a file so that a crash cannot leave half of one.
 *
 * <p>A plain write TRUNCATES first, and a force-kill in that window leaves a file that parses to
 * nothing — losing the very record it exists to keep. Written beside it and renamed over it instead;
 * a rename within one directory is atomic on both filesystems this ships to. (codex, the code round
 * on the orphan ledger, where this rule was first written and lived privately.)</p>
 *
 * <p><b>Two forms, and the difference is a requirement rather than a convenience.</b> The orphan
 * ledger writes SYNCHRONOUSLY on purpose: a child must be written down before it can be orphaned,
 * and a queued asynchronous write is exactly what a force-kill does not wait for
 * (`chatOrphans.ts`, *Our own file is written SYNCHRONOUSLY*). The conversation store has no such
 * constraint and must not block the extension host on a disk — a transcript is tens of kilobytes and
 * it is written on every turn. One rule, two shapes; what is shared is the rule and the name of the
 * beside-file, which is what the store's sweep looks for.</p>
 *
 * <p><b>Nothing here is caught.</b> A write that cannot happen is a fact its caller has to act on:
 * the store turns it into a refusal that keeps the conversation on screen, the ledger logs it. A
 * swallow here would report success to both.</p>
 */

/** The extension every interrupted write leaves behind, and the one the sweep collects. */
export const BESIDE_SUFFIX = '.tmp';

/**
 * Which write this is, within this process.
 *
 * <p>See {@link besideName}: the pid alone is not enough, because one process can have two writes to
 * one destination in flight.</p>
 */
let writes = 0;

/**
 * Where a write goes before it is renamed into place.
 *
 * <p>Three things go into the name, and each of them was earned:</p>
 *
 * <p><b>The destination's own name</b>, so a temporary stranded by a crash says which record it
 * belonged to rather than being an anonymous file somebody has to guess about.</p>
 *
 * <p><b>The writing PROCESS.</b> Two extension hosts write the same conversation directory, and a
 * beside-file named for the destination alone would have one window filling the file the other is
 * about to rename — so one of the two renames would move a half-written record into place.</p>
 *
 * <p><b>And a counter, because the pid is not enough.</b> Two writes to ONE destination from one
 * host is an ordinary event — a turn lands while the previous save is still on the disk — and with a
 * shared name each would write and rename the same temporary: one call truncating what the other is
 * still writing, and the loser reporting success over content it did not write. Found by two
 * vendors' reviewers independently on this story's code round. The counter is per process and never
 * reused, so two names from one host differ even in the same millisecond.</p>
 */
export function besideName(path: string, pid: number = process.pid): string {
  writes += 1;

  return `${path}.${pid}.${writes}${BESIDE_SUFFIX}`;
}

/**
 * Write it, atomically, without holding the host. The directory is made if it is not there.
 *
 * <p>A failure is re-thrown — the store turns it into a refusal that keeps the conversation on
 * screen, the ledger logs it, and neither can if it is swallowed here — but the temporary is removed
 * first. A `.tmp` left behind by every disk error is a directory that accumulates one per failure,
 * and a sweep that has to tell those from a write that is genuinely in flight.</p>
 */
export async function writeFileAtomically(path: string, text: string): Promise<void> {
  const beside = besideName(path);
  await mkdir(dirname(path), { recursive: true });
  try {
    await writeFile(beside, text, 'utf8');
    await rename(beside, path);
  } catch (reason) {
    // Best-effort, and deliberately silent: the failure being reported is the one above, and a
    // cleanup that could not happen must not replace it with a different, less useful sentence.
    await rm(beside, { force: true }).catch(() => undefined);
    throw reason;
  }
}

/** The same bargain for a caller that cannot wait for a promise. See the note above. */
export function writeFileAtomicallySync(path: string, text: string): void {
  const beside = besideName(path);
  mkdirSync(dirname(path), { recursive: true });
  try {
    writeFileSync(beside, text, 'utf8');
    renameSync(beside, path);
  } catch (reason) {
    try {
      rmSync(beside, { force: true });
    } catch {
      // As above.
    }
    throw reason;
  }
}
