import { mkdirSync, renameSync, writeFileSync } from 'node:fs';
import { mkdir, rename, writeFile } from 'node:fs/promises';
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
 * Where a write goes before it is renamed into place.
 *
 * <p>It carries the WRITING PROCESS, not only the destination. Two extension hosts write the same
 * conversation directory, and a beside-file named for the destination alone would have one window
 * filling the file the other is about to rename — so one of the two renames would move a half-written
 * record into place. It also carries the destination's own name, so a temporary stranded by a crash
 * says which record it belonged to.</p>
 */
export function besideName(path: string, pid: number = process.pid): string {
  return `${path}.${pid}${BESIDE_SUFFIX}`;
}

/** Write it, atomically, without holding the host. The directory is made if it is not there. */
export async function writeFileAtomically(path: string, text: string): Promise<void> {
  const beside = besideName(path);
  await mkdir(dirname(path), { recursive: true });
  await writeFile(beside, text, 'utf8');
  await rename(beside, path);
}

/** The same bargain for a caller that cannot wait for a promise. See the note above. */
export function writeFileAtomicallySync(path: string, text: string): void {
  const beside = besideName(path);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(beside, text, 'utf8');
  renameSync(beside, path);
}
