import { mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { isSafeId } from './chatStore';

/**
 * Claiming ONE conversation for one mutation, so that two windows cannot both change it at once.
 *
 * <h2>The atomic operation this rests on: an exclusive create</h2>
 *
 * <p>A rename is atomic, but it is not CONDITIONAL — it does not care what was read before it. So a
 * compare-and-swap written as "read the rev, then rename" is not one: two windows that both last read
 * rev 5 both observe 5, both choose 6, both rename, and the second silently replaces the first —
 * precisely the lost update the rev was introduced to make detectable. (codex, story A2's first code
 * round, as the blocking finding.)</p>
 *
 * <p>The one thing a filesystem offers that IS an atomic test-and-set is <b>an exclusive create</b>:
 * `open` with `O_EXCL` — Node's flag `'wx'` — creates the file or fails with `EEXIST`, atomically,
 * across processes, on both filesystems this ships to. Measured here before it was relied on: eight
 * writers racing for one name, exactly one admitted, seven `EEXIST`. So a mutation must first own
 * `<id>.lock`, and only one can.</p>
 *
 * <h2>One lock per CONVERSATION, not per revision</h2>
 *
 * <p>The first draft named the lock for the transition — `<id>.6.lock` — which serialised two writers
 * of rev 6 against each other and nothing else. A `forget` did not take it and raced a save into
 * either resurrecting a deleted conversation or leaving a row that opens onto nothing; a reader's
 * metadata reconciliation did not take it and could write a rev-1 index beside a rev-2 transcript.
 * (gemini and codex, the second round, independently.) So the lock is now `<id>.lock`, every mutation
 * of a conversation takes it — a save, a forget, the metadata write a read performs when it finds the
 * index stale — and what the holder is doing, the target revision included, is written INSIDE the note
 * rather than into the name. A reader that only reads takes nothing.</p>
 *
 * <h2>Release and breaking are fenced by POSITIVE verification</h2>
 *
 * <p>A read-then-unlink is not atomic, and the first draft's fence was one: a stalled writer read its
 * own token, a rival broke the stale lock and created a fresh one at the same path, and the stalled
 * writer's unconditional `rm` then deleted the NEW owner's lock — after which a third writer walked in.
 * (Three findings, two vendors.) So: {@link Claim.release} removes the file only when the note reads
 * back and its token is POSITIVELY ours — a note that cannot be read, is torn, or names anyone else is
 * left alone, because "not verifiably mine" is never a licence to delete. And a breaker re-reads the
 * lock immediately before unlinking and removes it only if it is still the very lock it aged — the
 * same token, or for a torn note the same `mtime` — then re-claims by exclusive create, which is
 * atomic, so two breakers cannot both win the creation.</p>
 *
 * <h2>Staleness, the wait, and THE RESIDUAL — stated where a caller reads it</h2>
 *
 * <p>A writer killed between claim and release leaves its lock behind, and without a rule it would
 * wedge that conversation for ever. A lock whose note is older than {@link LOCK_STALE_MS} — thirty
 * seconds, generous for two small file writes — may be broken as above. A lock that is merely HELD is
 * usually a reconciliation or a save a few milliseconds from done, so a claimer waits briefly
 * ({@link HELD_TRIES} tries, pauses growing from {@link HELD_PAUSE_MS}, about a tenth of a second in
 * all) before reporting `held`; a caller that treated the first `EEXIST` as a rival would re-mint a
 * conversation over an index write that was never a rival at all.</p>
 *
 * <p><b>The residual, plainly.</b> Between a breaker verifying that a stale lock still carries the
 * token it aged and the `rm` that follows, a rival breaker that verified a microsecond earlier can have
 * ALREADY unlinked that lock and created its own fresh one at the same path — and the first breaker's
 * `rm` then removes the rival's fresh lock, and its own exclusive create succeeds. Two writers then
 * hold one conversation; both probe, both see the same baseline, both write the same new revision,
 * and the last rename wins: <b>the loser's turn is overwritten on disk.</b> It is accepted because the
 * alternative is a lock the operating system holds for the process — an open `O_EXCL` descriptor or
 * `flock` — which Node's `fs` does not offer portably, and because the window is microseconds wide and
 * opens only against a lock that has already been abandoned for thirty seconds. <b>What a caller
 * sees:</b> both saves report `ok` (or `partial`) with the same rev; the divergence surfaces on the
 * next save, where one window's baseline is now behind the disk and it is `refused` — at which point
 * story A3 re-mints it under a new id with the transcript it still holds in memory, so the overwritten
 * turn is lost from the disk copy of one conversation and kept in the window that wrote it.</p>
 *
 * <p>A lock left by a crash is broken on the next claim after the window, or collected by the sweep
 * (story B1) with the other debris in the directory. No `vscode`, and no judgement about records: this
 * module knows a directory, an id, a sentence about what the holder is doing, and a clock.</p>
 */

/** The suffix every claim file carries, and the one the sweep looks for beside `.tmp`. */
export const LOCK_SUFFIX = '.lock';

/** How old a lock's note may be before it is presumed abandoned. See the header for what that buys and costs. */
export const LOCK_STALE_MS = 30_000;

/** How many times a HELD lock is re-tried before it is reported, and how the pauses grow. Bounded: ~120 ms in all. */
export const HELD_TRIES = 6;
export const HELD_PAUSE_MS = 8;

/** The file that owns every mutation of `id`. Throws on an unsafe id, like `recordName`. */
export function lockName(id: string): string {
  if (!isSafeId(id)) {
    throw new Error(`a conversation id that is not a safe filename: ${id}`);
  }

  return `${id}${LOCK_SUFFIX}`;
}

/** What a claim came to. `held` is somebody else mutating this conversation, still, after the wait. */
export type Claim =
  | { readonly kind: 'claimed'; readonly release: () => Promise<void> }
  | { readonly kind: 'held' }
  | { readonly kind: 'failed'; readonly reason: string };

/** What is written INTO a lock: who, when, a token the release checks, and what the holder is doing. Everything is UTC. */
interface LockNote {
  readonly pid: number;
  readonly at: string;
  readonly token: string;
  readonly doing: string;
}

/**
 * What a lock on disk looks like to a breaker: when it was taken, and a SIGNATURE that changes when
 * it is replaced — the token when the note reads, the `mtime` when it is torn.
 */
interface LockSeen {
  readonly at: number;
  readonly signature: string;
}

/** Which claim this is, within this process — the token's second half, so two claims from one pid differ. */
let claimSequence = 0;

const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? code : '';
};

const pause = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

/** The note inside a lock, or nothing when the file is torn or not ours in shape. */
function noteFrom(text: string): LockNote | undefined {
  try {
    const row = JSON.parse(text) as Partial<LockNote> | null;
    if (row === null || typeof row !== 'object' || typeof row.token !== 'string' || typeof row.at !== 'string') {
      return undefined;
    }

    return {
      pid: typeof row.pid === 'number' ? row.pid : 0,
      at: row.at,
      token: row.token,
      doing: typeof row.doing === 'string' ? row.doing : '',
    };
  } catch {
    return undefined;
  }
}

/**
 * The lock as it is right now, or nothing when it is gone or will not be read.
 *
 * <p>A lock opened by a reader in the instant between the exclusive create and the content landing
 * is empty; it is still a real, fresh lock, and the file's own time says so — so a torn note is aged
 * and signed by its `mtime`. A caller treats `undefined` as "not provably stale, and not mine".</p>
 */
async function seen(path: string): Promise<LockSeen | undefined> {
  try {
    const note = noteFrom(await readFile(path, 'utf8'));
    if (note !== undefined) {
      const at = Date.parse(note.at);

      return { at: Number.isFinite(at) ? at : (await stat(path)).mtimeMs, signature: `token:${note.token}` };
    }
    const { mtimeMs } = await stat(path);

    return { at: mtimeMs, signature: `mtime:${mtimeMs}` };
  } catch {
    return undefined;
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
 * Remove a lock that is provably older than the window — and provably still the one that was aged.
 *
 * <p>`false` when it is fresh, when its age cannot be read, or when the re-read immediately before the
 * `rm` shows a DIFFERENT lock at the path: somebody else broke it first and re-claimed, and their fresh
 * claim is not this side's to delete. The only safe error is to wait thirty seconds, never to unlink a
 * live writer. A removal that fails is the same answer — still held. The window that remains between
 * the re-read and the `rm` is the residual the header states.</p>
 */
async function breakIfStale(path: string, now: number): Promise<boolean> {
  const aged = await seen(path);
  if (aged === undefined || now - aged.at <= LOCK_STALE_MS) {
    return false;
  }
  const again = await seen(path);
  if (again === undefined || again.signature !== aged.signature) {
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
 * Let go of a claim — but only if the lock reads back as POSITIVELY ours.
 *
 * <p>The fence from the header. A note that names another token means this holder stalled past the
 * window and somebody broke and re-took the conversation; a note that cannot be read may be that
 * replacement still being written. Either way it is left alone — deleting it would open the door for a
 * third writer — and the positively-observed take-over is said out loud, because a mutation that took
 * over thirty seconds is a fact worth knowing. A lock already gone is nothing to do; a removal that
 * fails is logged, never thrown — this runs in a `finally` over a save that may have succeeded.</p>
 */
async function releaseOwn(path: string, token: string): Promise<void> {
  let text: string;
  try {
    text = await readFile(path, 'utf8');
  } catch (reason) {
    if (codeOf(reason) !== 'ENOENT') {
      console.error(`ConnectOtherAIs: a conversation lock could not be read to release it, so it was left: ${path}`, reason);
    }

    return;
  }
  const note = noteFrom(text);
  if (note?.token !== token) {
    if (note !== undefined) {
      console.error(`ConnectOtherAIs: a mutation outlived its lock window and the lock was taken over (${note.doing}): ${path}`);
    }

    return;
  }
  try {
    await rm(path, { force: true });
  } catch (reason) {
    console.error(`ConnectOtherAIs: a conversation lock could not be released: ${path}`, reason);
  }
}

/**
 * Claim `id` for one mutation, described by `doing` — `save 6`, `forget`, `reconcile 2`.
 *
 * <p>The directory is made if it is not there — a first save is the thing that creates a store, and
 * an exclusive create needs somewhere to create. `EEXIST` is retried after breaking a lock older than
 * the window, and otherwise waited on for about a tenth of a second before it is `held`. Any other
 * failure is `failed` with a sentence a person can read (no path — that goes to the console, beside
 * it, for whoever is debugging several profiles).</p>
 *
 * @param now the clock, an argument so a test can pin it; it stamps the note AND ages a rival's lock.
 */
export async function claimConversation(dir: string, id: string, doing: string, now = Date.now()): Promise<Claim> {
  const path = join(dir, lockName(id));
  claimSequence += 1;
  const note: LockNote = { pid: process.pid, at: new Date(now).toISOString(), token: `${process.pid}:${claimSequence}`, doing };
  try {
    await mkdir(dir, { recursive: true });
    for (let attempt = 1; ; attempt += 1) {
      if (await tryCreate(path, note) || (await breakIfStale(path, now) && await tryCreate(path, note))) {
        return { kind: 'claimed', release: () => releaseOwn(path, note.token) };
      }
      if (attempt >= HELD_TRIES) {
        return { kind: 'held' };
      }
      await pause(HELD_PAUSE_MS * attempt);
    }
  } catch (reason) {
    console.error(`ConnectOtherAIs: a conversation lock could not be created: ${path}`, reason);
    const code = codeOf(reason);

    return { kind: 'failed', reason: code.length > 0 ? `the conversation could not be locked (${code})` : 'the conversation could not be locked' };
  }
}
