import { mkdirSync, statSync } from 'node:fs';
import { mkdir, stat } from 'node:fs/promises';
import { join, relative, resolve, sep } from 'node:path';
import { type NamedRoot, dataDirFinding, namedRoot } from './dataDir';
import { isUnder, missingRootRefusal } from './findingsBoard';

/**
 * The ONE way this extension makes a directory that may lie under its data directory (todo/PLAN_paths_per_side.md E5.7,
 * D3; the risk consultation of story 5.7, item F).
 *
 * <p><b>Why the guard alone was not enough.</b> Every writer asked the storage guard (`findingsBoard`) and then made the
 * directory with a RECURSIVE `mkdir`. The guard is a question asked before an act: a root removed between the two — a
 * drive unmounted, a share asleep — was made again by the `mkdir`, into a bare mount point, and the condition the notice
 * reports was hidden by the very write it was meant to stop. So a NAMED root is never made by a writer at all:</p>
 *
 * <ul>
 *   <li>a directory under a named root is made one level at a time, from the root DOWN, each a non-recursive `mkdir`, so
 *   the first level fails with `ENOENT` when the root is not there — at the moment of the write, not before it — and that
 *   failure is the finding's own refusal;</li>
 *   <li>the named root itself is never made, only asked whether it is there;</li>
 *   <li>the default location (nothing named) and any directory outside the named root are made as before, recursively —
 *   a first start must never be refused (D3).</li>
 * </ul>
 *
 * <p>The one exception is the person's own act: the move flow makes the folder a person just picked in the system dialog
 * (`dataCommands.ts`), which is consent rather than a side effect. A person whose named folder is gone recovers by
 * making it again — or choosing it again — and the next probe lets every writer through (`pathWatch.ts`).</p>
 */

/** The folders to make, top-down, under the named root — or nothing to ask of a named root. */
type Plan =
  | { readonly kind: 'free' }
  | { readonly kind: 'named'; readonly named: NamedRoot; readonly levels: readonly string[] };

/** Makes `dir` — recursively when no named root holds it; level by level below an EXISTING named root otherwise. */
export async function makeDataDirectory(dir: string): Promise<void> {
  const plan = planFor(dir);
  if (plan.kind === 'free') {
    await mkdir(dir, { recursive: true });

    return;
  }
  await rootIsThere(plan.named);
  for (const level of plan.levels) {
    await mkdir(level).catch((reason: unknown) => { keptOrRefused(reason, plan.named); });
  }
}

/** {@link makeDataDirectory} for the one caller that cannot wait — the sync atomic write, which races a force-kill. */
export function makeDataDirectorySync(dir: string): void {
  const plan = planFor(dir);
  if (plan.kind === 'free') {
    mkdirSync(dir, { recursive: true });

    return;
  }
  rootIsThereSync(plan.named);
  for (const level of plan.levels) {
    makeOneLevelSync(level, plan.named);
  }
}

/** The plan for `dir`: free when nothing is named or `dir` is outside the named root; the levels below it otherwise. */
export function planFor(dir: string): Plan {
  const named = namedRoot();
  if (named === undefined || !isUnder(dir, named.root)) {
    return { kind: 'free' };
  }

  return { kind: 'named', named, levels: levelsBelow(named.root, dir) };
}

/** Every folder from just below `root` down to `dir`, in order — none when `dir` IS the root. */
function levelsBelow(root: string, dir: string): readonly string[] {
  const top = resolve(root);
  const steps = relative(top, resolve(dir)).split(sep).filter((step) => step.length > 0);

  return steps.map((_, at) => join(top, ...steps.slice(0, at + 1)));
}

/** A named root that is not there refuses, in the finding's words — it is never made. */
async function rootIsThere(named: NamedRoot): Promise<void> {
  await stat(named.root).catch((reason: unknown) => { throw refusalOr(reason, named); });
}

function rootIsThereSync(named: NamedRoot): void {
  try {
    statSync(named.root);
  } catch (reason) {
    throw refusalOr(reason, named);
  }
}

function makeOneLevelSync(level: string, named: NamedRoot): void {
  try {
    mkdirSync(level);
  } catch (reason) {
    keptOrRefused(reason, named);
  }
}

/** A level already there is kept; one whose parent went away is the root's refusal; anything else is the disk's. */
function keptOrRefused(reason: unknown, named: NamedRoot): void {
  if (codeOf(reason) !== 'EEXIST') {
    throw refusalOr(reason, named);
  }
}

/** `ENOENT` is the named root gone: the finding's own sentence. Any other failure is the disk's, unchanged. */
function refusalOr(reason: unknown, named: NamedRoot): unknown {
  const missing = dataDirFinding(named, 'absent');

  return codeOf(reason) === 'ENOENT' && missing !== undefined ? missingRootRefusal(missing) : reason;
}

function codeOf(reason: unknown): string {
  return reason instanceof Error && 'code' in reason ? String(reason.code) : '';
}
