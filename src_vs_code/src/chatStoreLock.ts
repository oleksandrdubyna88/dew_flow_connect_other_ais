import { mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { isSafeId } from './chatStore';

/**
 * Claiming ONE revision of ONE conversation, so that two windows cannot both write it.
 *
 * <h2>The atomic operation this rests on: an exclusive create</h2>
 *
 * <p>A rename is atomic, but it is not CONDITIONAL — it does not care what was read before it. So a
 * compare-and-swap written as "read the rev, then rename" is not one: two windows that both last read
 * rev 5 both observe 5, both choose 6, both rename, and the second silently replaces the first —
 * precisely the lost update the rev was introduced to make detectable. (codex, story A2's code
 * round, as the blocking finding.)</p>
 *
 * <p>The one thing a filesystem offers that IS an atomic test-and-set is <b>an exclusive create</b>:
 * `open` with `O_EXCL` — Node's flag `'wx'` — creates the file or fails with `EEXIST`, atomically,
 * across processes, on both filesystems this ships to. Measured here before it was relied on: eight
 * writers racing for one name, exactly one admitted, seven `EEXIST`. So the thing that is claimed is
 * the TRANSITION: to write rev 6 a window must first own `<id>.6.lock`, and only one can.</p>
 *
 * <h2>Release is fenced by the revision in the lock's own name</h2>
 *
 * <p>The lock for rev 6 is `<id>.6.lock` and nothing else is ever removed by the writer of rev 6 or
 * by whoever breaks its stale lock. A breaker can therefore never delete the lock of the writer that
 * REPLACED the stalled one, because that writer, having read 6, is holding `<id>.7.lock`. On top of
 * the name, the lock's content carries a token unique to the claim, and {@link Claim.release} removes
 * the file only when the token is still its own — so a writer whose lock was broken as stale does
 * not, on its way out, delete the breaker's.</p>
 *
 * <h2>A stale lock, and the residual</h2>
 *
 * <p>A writer killed between claim and release leaves its lock behind, and without a rule it would
 * wedge that conversation for ever. So a lock whose timestamp is older than {@link LOCK_STALE_MS} —
 * thirty seconds, generous for two small file writes — may be broken: read, aged, removed, and the
 * claim retried ONCE. <b>The residual is a writer that genuinely stalls for longer than the
 * window</b> — a disk that hangs, a host suspended mid-save. Its lock is then broken, a second writer
 * takes the same transition, and whichever rename lands last wins; the other's turns are lost, which
 * is the defect this module exists to prevent, narrowed from "any two windows" to "a writer stalled
 * over thirty seconds across two writes of a few kilobytes". That is stated here rather than claimed
 * away, and the token fence above keeps even that writer from widening the window for a third.</p>
 *
 * <p>A lock left by a crash is broken on the next claim of its revision, or collected by the sweep
 * (story B1) with the other debris in the directory. No `vscode`, and no judgement about records: this
 * module knows a directory, an id, a number and a clock.</p>
 */

/** The suffix every claim file carries, and the one the sweep looks for beside `.tmp`. */
export const LOCK_SUFFIX = '.lock';

/** How old a lock may be before it is presumed abandoned. See the header for what that buys and costs. */
export const LOCK_STALE_MS = 30_000;

/** The file that owns the transition to `rev` for `id`. Throws on an unsafe id, like `recordName`. */
export function lockName(id: string, rev: number): string {
  if (!isSafeId(id)) {
    throw new Error(`a conversation id that is not a safe filename: ${id}`);
  }

  return `${id}.${rev}${LOCK_SUFFIX}`;
}

/** What a claim came to. `held` is somebody else performing this exact transition right now. */
export type Claim =
  | { readonly kind: 'claimed'; readonly release: () => Promise<void> }
  | { readonly kind: 'held' }
  | { readonly kind: 'failed'; readonly reason: string };

/** What is written INTO a lock: who, when, and a token the release checks. Everything is UTC. */
interface LockNote {
  readonly pid: number;
  readonly at: string;
  readonly token: string;
}

/** Which claim this is, within this process — the token's second half, so two claims from one pid differ. */
let claimSequence = 0;

const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? code : '';
};

/** The note inside a lock, or nothing when the file is torn or not ours in shape. */
function noteFrom(text: string): LockNote | undefined {
  try {
    const row = JSON.parse(text) as Partial<LockNote> | null;
    if (row === null || typeof row !== 'object' || typeof row.token !== 'string' || typeof row.at !== 'string') {
      return undefined;
    }

    return { pid: typeof row.pid === 'number' ? row.pid : 0, at: row.at, token: row.token };
  } catch {
    return undefined;
  }
}

/**
 * When a lock was taken, as read from its note — or from its `mtime` when the note is unreadable.
 *
 * <p>A lock opened by a reader in the instant between the exclusive create and the content landing
 * is empty; it is still a real, fresh lock, and the file's own time says so. `-1` when even that cannot
 * be read, which the caller treats as "not provably stale".</p>
 */
async function takenAt(path: string): Promise<number> {
  try {
    const note = noteFrom(await readFile(path, 'utf8'));
    const parsed = note === undefined ? Number.NaN : Date.parse(note.at);

    return Number.isFinite(parsed) ? parsed : (await stat(path)).mtimeMs;
  } catch {
    return -1;
  }
}

/** Try the exclusive create once. `true` when it is ours, `false` on `EEXIST`, and a throw for anything else. */
async function tryCreate(path: string, note: LockNote): Promise<boolean> {
  try {
    await writeFile(path, JSON.stringify(note), { encoding: 'utf8', flag: 'wx' });

    return true;
  } catch (reason) {
    if (codeOf(reason) === 'EEXIST') {
      return false;
    }
    throw reason;
  }
}

/**
 * Remove a lock that is provably older than the window, so the claim can be retried.
 *
 * <p>`false` when it is fresh, or when its age cannot be read: a lock nobody can date is not one this
 * side may break, because the only safe error is to wait thirty seconds, never to write over a live
 * writer. A removal that fails is the same answer — still held.</p>
 */
async function breakIfStale(path: string, now: number): Promise<boolean> {
  const at = await takenAt(path);
  if (at < 0 || now - at <= LOCK_STALE_MS) {
    return false;
  }
  try {
    await rm(path, { force: true });
    console.error(`ConnectOtherAIs: a conversation lock older than ${LOCK_STALE_MS / 1000}s was broken: ${path}`);

    return true;
  } catch (reason) {
    console.error(`ConnectOtherAIs: a stale conversation lock could not be removed: ${path}`, reason);

    return false;
  }
}

/**
 * Let go of a claim — but only if the lock is still OURS.
 *
 * <p>The token fence from the header. If the content names another token, this writer stalled past
 * the window and somebody broke and re-took the transition; deleting their lock now would open the
 * door for a third writer. Said out loud, because a save that took over thirty seconds is a fact worth
 * knowing. A lock that is already gone is nothing to do; a removal that fails is logged, never thrown —
 * this runs in a `finally` over a save that may have succeeded.</p>
 */
async function releaseOwn(path: string, token: string): Promise<void> {
  try {
    const note = noteFrom(await readFile(path, 'utf8'));
    if (note !== undefined && note.token !== token) {
      console.error(`ConnectOtherAIs: a save outlived its lock window and the lock was taken over: ${path}`);

      return;
    }
    await rm(path, { force: true });
  } catch (reason) {
    if (codeOf(reason) !== 'ENOENT') {
      console.error(`ConnectOtherAIs: a conversation lock could not be released: ${path}`, reason);
    }
  }
}

/**
 * Claim the transition of `id` to `rev`.
 *
 * <p>The directory is made if it is not there — a first save is the thing that creates a store, and
 * an exclusive create needs somewhere to create. `EEXIST` is `held`, after one attempt to break a lock
 * older than the window and one retry. Any other failure is `failed` with a sentence a person can read
 * (no path — that goes to the console, beside it, for whoever is debugging several profiles).</p>
 *
 * @param now the clock, an argument so a test can pin it; it stamps the note AND ages a rival's lock.
 */
export async function claimRevision(dir: string, id: string, rev: number, now = Date.now()): Promise<Claim> {
  const path = join(dir, lockName(id, rev));
  claimSequence += 1;
  const note: LockNote = { pid: process.pid, at: new Date(now).toISOString(), token: `${process.pid}:${claimSequence}` };
  try {
    await mkdir(dir, { recursive: true });
    if (await tryCreate(path, note) || (await breakIfStale(path, now) && await tryCreate(path, note))) {
      return { kind: 'claimed', release: () => releaseOwn(path, note.token) };
    }

    return { kind: 'held' };
  } catch (reason) {
    console.error(`ConnectOtherAIs: a conversation lock could not be created: ${path}`, reason);
    const code = codeOf(reason);

    return { kind: 'failed', reason: code.length > 0 ? `the conversation could not be locked for saving (${code})` : 'the conversation could not be locked for saving' };
  }
}
