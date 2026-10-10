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

/**
 * What the maker answers: the folder is there now, or the NAMED root it lies under is missing — an expected outcome, so a
 * value every caller handles (the E5 code round: expected failures are values, not exceptions). Anything else the disk
 * refuses is unexpected and is thrown, unchanged.
 */
export type Made =
  | { readonly made: true }
  | { readonly made: false; readonly refused: 'missing-named-root'; readonly root: string; readonly sentence: string };

/** The refused answer. */
export type Refused = Extract<Made, { readonly made: false }>;

const MADE: Made = { made: true };

/** Makes `dir` — recursively when no named root holds it; level by level below an EXISTING named root otherwise. */
export async function makeDataDirectory(dir: string): Promise<Made> {
  const plan = planFor(dir);
  if (plan.kind === 'free') {
    await mkdir(dir, { recursive: true });

    return MADE;
  }
  const root = await stat(plan.named.root).then(() => MADE, (reason: unknown) => outcomeOf(reason, plan.named));

  return root.made ? madeLevels(plan.levels, plan.named) : root;
}

/** {@link makeDataDirectory} for the one caller that cannot wait — the sync atomic write, which races a force-kill. */
export function makeDataDirectorySync(dir: string): Made {
  const plan = planFor(dir);
  if (plan.kind === 'free') {
    mkdirSync(dir, { recursive: true });

    return MADE;
  }
  const root = attemptSync(() => { statSync(plan.named.root); }, plan.named);

  return root.made ? madeLevelsSync(plan.levels, plan.named) : root;
}

/** The refusal as the Error a caller whose own contract is to throw raises — the finding's sentence, unchanged. */
export function refusalError(refused: Refused): Error {
  return new Error(refused.sentence);
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

/** Each level below the root, top-down; the first that finds the root gone answers the refusal. */
async function madeLevels(levels: readonly string[], named: NamedRoot): Promise<Made> {
  for (const level of levels) {
    const one = await mkdir(level).then(() => MADE, (reason: unknown) => outcomeOf(reason, named));
    if (!one.made) {
      return one;
    }
  }

  return MADE;
}

function madeLevelsSync(levels: readonly string[], named: NamedRoot): Made {
  for (const level of levels) {
    const one = attemptSync(() => { mkdirSync(level); }, named);
    if (!one.made) {
      return one;
    }
  }

  return MADE;
}

function attemptSync(work: () => void, named: NamedRoot): Made {
  try {
    work();

    return MADE;
  } catch (reason) {
    return outcomeOf(reason, named);
  }
}

/**
 * A level already there (`EEXIST`) is made; `ENOENT` is the named root gone — the refusal, a value, never made; anything
 * else the disk says is unexpected and is thrown unchanged.
 */
function outcomeOf(reason: unknown, named: NamedRoot): Made {
  const code = codeOf(reason);
  if (code === 'EEXIST') {
    return MADE;
  }
  if (code !== 'ENOENT') {
    throw reason;
  }

  return refusedAt(named);
}

/** The refusal for `named`, in the finding's own sentence. */
function refusedAt(named: NamedRoot): Refused {
  const missing = dataDirFinding(named, 'absent');
  const sentence = missing === undefined ? `The data folder ${named.root} is not there.` : missingRootRefusal(missing).message;

  return { made: false, refused: 'missing-named-root', root: named.root, sentence };
}

function codeOf(reason: unknown): string {
  return reason instanceof Error && 'code' in reason ? String(reason.code) : '';
}
