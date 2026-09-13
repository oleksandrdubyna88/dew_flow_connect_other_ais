import { Dirent } from 'node:fs';
import { mkdir, readFile, readdir, rename, rm, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { abreast } from './abreast';
import { BESIDE_SUFFIX, writeFileAtomically } from './atomicFile';
import { besideMeta, idOfMeta, isRecordName, isSafeId, recordName } from './chatStore';
import { ChatStoreFile, QUARANTINE_DIR } from './chatStoreFile';
import { LOCK_SUFFIX, claimConversation } from './chatStoreLock';
import {
  HOUSEKEEPING_DIR,
  HeartbeatFile,
  Named,
  SWEEP_BUDGET_MS,
  SWEEP_MARKER,
  Stamp,
  Survey,
  SweepMarker,
  SweepPlan,
  SweepReport,
  heartbeatName,
  heartbeatOwner,
  heartbeatText,
  markerText,
  parseHeartbeat,
  parseMarker,
  planSweep,
  quarantinedAt,
  sweepDue,
} from './chatStoreSweep';

/**
 * The store's housekeeping half: the survey the sweep and the index decide over, the window's
 * heartbeat, the marker of the last sweep, and every removal of debris — bound to the store's own
 * directory and knowing its layout through the same three modules the store does.
 *
 * <p>Story A4 had to undo a caller that reached into the directory behind the store, and this module
 * is how B1 avoids doing it again: the sweep (`runSweep` below, driven by the rules in
 * `chatStoreSweep.ts`) names ids and file names, never paths, and every path is built HERE from the
 * layout `chatStore.ts` (`recordName`, `besideMeta`, `idOfMeta`), `chatStoreLock.ts` (`lockName`, the
 * claim) and `atomicFile.ts` (the beside-suffix) already own. It is a second class rather than more
 * methods on `ChatStoreFile` for one reason only: that file is at the size limit, and the ONE operation
 * that deletes a conversation — `retireIfExpired` — is on it, because that operation needs its private
 * probe, its metadata write and its two deletes in their order.</p>
 *
 * <h2>Every mutation of a conversation's files is under its lock, and re-checked there</h2>
 *
 * <p>An orphaned index entry is removed only if, with the conversation claimed, its record is STILL
 * absent — a save at baseline zero writes the record first, and the claim is what keeps this from
 * running between its two writes. An orphaned transcript is set aside only if, claimed, its index entry
 * is still absent. A lock is collected by CLAIMING the conversation: the claim breaks a lock older than
 * the lock module's window through that module's own verified path — the only lock-breaking path there
 * is — and the release removes what the claim took; a lock still held after the wait is a live writer's
 * and is kept. Nothing here removes a `.lock` by name.</p>
 *
 * <h2>What is set aside is moved, never copied</h2>
 *
 * <p>An orphaned transcript goes into the quarantine subdirectory by a rename — atomic within one
 * directory tree, and the bytes are the bytes: nothing is parsed, so a transcript this build cannot read
 * is set aside exactly as it was. The name carries the id and the instant, the same `<label>-<at>.json`
 * shape the migration's quarantine writes, so one rule dates them all.</p>
 *
 * <p><b>Nothing here throws.</b> Every failure is a typed outcome and a console line NAMING THE PATH;
 * the sentence in the outcome never carries one. `ENOENT` on the store's directory or a subdirectory is
 * the ordinary "nothing here yet"; anything else the disk refuses is `unavailable`, and a survey that
 * cannot read the housekeeping directory is unavailable as a whole — a sweep that could not read the
 * heartbeats would believe nothing is protected.</p>
 */

/** The directory, classified — or a directory that would not answer. */
export type SurveyOutcome =
  | { readonly kind: 'surveyed'; readonly survey: Survey }
  | { readonly kind: 'unavailable'; readonly reason: string };

/** What one removal came to. `kept` names why the thing was left, for the report and the next sweep. */
export type DebrisOutcome =
  | { readonly kind: 'removed' }
  | { readonly kind: 'kept'; readonly why: string }
  | { readonly kind: 'failed'; readonly reason: string };

/** How many files are stat-ed or read at once. The store's own listing width, for the same reason. */
const STAT_WIDTH = 8;

const EMPTY_SURVEY: Survey = {
  records: new Map(),
  metas: new Map(),
  locks: new Map(),
  temps: [],
  quarantined: [],
  heartbeats: [],
  foreign: [],
};

const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? code : '';
};

const withCode = (sentence: string, code: string): string => (code.length > 0 ? `${sentence} (${code})` : sentence);

const UNREADABLE = 'the conversation store could not be read';

function parsed(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/** Whether a name is one bare filename component — a name from a listing must not walk out of the directory it was listed in. */
const isBareName = (name: string): boolean => name.length > 0 && !/[\\/]/u.test(name) && name !== '.' && name !== '..';

/** The store's own subdirectories, which a survey of the root does not report as foreign. */
const isOurs = (name: string): boolean => name === QUARANTINE_DIR || name === HOUSEKEEPING_DIR;

type Listed = { readonly kind: 'files'; readonly files: readonly Named[] } | { readonly kind: 'unavailable'; readonly reason: string };

type Sorted = Pick<Survey, 'records' | 'metas' | 'locks' | 'temps' | 'foreign'>;

/**
 * Every file in the root, sorted into what the layout says it is. Pure over names and stamps.
 *
 * <p>The order of the tests is the order of specificity: `<id>.meta.json` before `<id>.json`, because
 * a metadata name also ends in `.json`, and `isRecordName` already refuses it — but the reader should
 * not have to know that to read this.</p>
 */
function sortRoot(files: readonly (readonly [string, Stamp | undefined])[]): Sorted {
  const records = new Map<string, Stamp>();
  const metas = new Map<string, Stamp>();
  const locks = new Map<string, Stamp>();
  const temps: Named[] = [];
  const foreign: string[] = [];
  for (const [name, stamp] of files) {
    if (stamp === undefined) {
      continue; // gone between the listing and the stat — a rename landed, or a sweep in another window
    }
    const metaId = idOfMeta(name);
    if (metaId.length > 0) {
      metas.set(metaId, stamp);
    } else if (isRecordName(name)) {
      records.set(name.slice(0, -'.json'.length), stamp);
    } else if (name.endsWith(LOCK_SUFFIX) && isSafeId(name.slice(0, -LOCK_SUFFIX.length))) {
      locks.set(name.slice(0, -LOCK_SUFFIX.length), stamp);
    } else if (name.endsWith(BESIDE_SUFFIX)) {
      temps.push({ name, stamp });
    } else {
      foreign.push(name);
    }
  }

  return { records, metas, locks, temps, foreign };
}

export class ChatStoreKeeper {
  private readonly housekeeping: string;

  public constructor(private readonly dir: string) {
    this.housekeeping = join(dir, HOUSEKEEPING_DIR);
  }

  // -------------------------------------------------------------------------------------------
  // Reading: the survey, the heartbeats, the marker.
  // -------------------------------------------------------------------------------------------

  /**
   * The directory as it is now: every file in the root by kind with its stamp, every quarantined
   * value, every heartbeat parsed. One listing of the root and one of each subdirectory, plus a stat
   * per file eight abreast; NO transcript is opened, and no metadata is either — that is the index's
   * job, one entry at a time, by the stamps this hands it.
   */
  public async survey(): Promise<SurveyOutcome> {
    let entries: readonly Dirent[];
    try {
      entries = await readdir(this.dir, { withFileTypes: true });
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { kind: 'surveyed', survey: EMPTY_SURVEY };
      }
      console.error(`ConnectOtherAIs: the conversation store could not be surveyed: ${this.dir}`, reason);

      return { kind: 'unavailable', reason: withCode(UNREADABLE, codeOf(reason)) };
    }
    const files = entries.filter((entry) => entry.isFile());
    const stamps = await abreast(files.map((entry) => () => this.stamp(join(this.dir, entry.name))), STAT_WIDTH);
    const root = sortRoot(files.map((entry, at) => [entry.name, stamps[at]] as const));
    const quarantined = await this.filesIn(join(this.dir, QUARANTINE_DIR));
    if (quarantined.kind === 'unavailable') {
      return quarantined;
    }
    const heartbeats = await this.heartbeats();
    if (heartbeats.kind === 'unavailable') {
      return heartbeats;
    }

    return {
      kind: 'surveyed',
      survey: {
        ...root,
        quarantined: quarantined.files,
        heartbeats: heartbeats.files,
        // Not files, and not our two subdirectories: a directory somebody made here, a symlink, a device.
        foreign: [...root.foreign, ...entries.filter((entry) => !entry.isFile() && !isOurs(entry.name)).map((entry) => entry.name)],
      },
    };
  }

  /** A file's stamp, or nothing when it is gone. Anything but gone is said with the path. */
  private async stamp(path: string): Promise<Stamp | undefined> {
    try {
      const { mtimeMs, size } = await stat(path);

      return { mtimeMs, size };
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: a file in the conversation store could not be examined: ${path}`, reason);
      }

      return undefined;
    }
  }

  /** Every file in one subdirectory with its stamp. Missing is empty; unreadable is unavailable. */
  private async filesIn(dir: string): Promise<Listed> {
    let entries: readonly Dirent[];
    try {
      entries = await readdir(dir, { withFileTypes: true });
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { kind: 'files', files: [] };
      }
      console.error(`ConnectOtherAIs: a conversation store subdirectory could not be listed: ${dir}`, reason);

      return { kind: 'unavailable', reason: withCode(UNREADABLE, codeOf(reason)) };
    }
    const files = entries.filter((entry) => entry.isFile());
    const stamps = await abreast(files.map((entry) => () => this.stamp(join(dir, entry.name))), STAT_WIDTH);

    return {
      kind: 'files',
      files: files.flatMap((entry, at) => {
        const stamp = stamps[at];

        return stamp === undefined ? [] : [{ name: entry.name, stamp }];
      }),
    };
  }

  /** Every window's heartbeat, parsed where it can be — a torn one keeps its name and stamp, and protects nothing. */
  private async heartbeats(): Promise<{ readonly kind: 'files'; readonly files: readonly HeartbeatFile[] } | Extract<Listed, { kind: 'unavailable' }>> {
    const listed = await this.filesIn(this.housekeeping);
    if (listed.kind === 'unavailable') {
      return listed;
    }
    const beats = listed.files.filter((file) => heartbeatOwner(file.name) !== 0);

    return { kind: 'files', files: await abreast(beats.map((file) => () => this.readHeartbeat(file)), STAT_WIDTH) };
  }

  private async readHeartbeat(file: Named): Promise<HeartbeatFile> {
    const path = join(this.housekeeping, file.name);
    let text: string;
    try {
      text = await readFile(path, 'utf8');
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: a window's heartbeat could not be read: ${path}`, reason);
      }

      return { ...file, beat: undefined };
    }
    const out = parseHeartbeat(parsed(text));
    if (out.kind === 'invalid') {
      console.error(`ConnectOtherAIs: a window's heartbeat is torn or not one this build can read — ${out.reason}: ${path}`);

      return { ...file, beat: undefined };
    }

    return { ...file, beat: out.beat };
  }

  /**
   * The marker the last sweep left, or nothing — for a file that is not there, one that cannot be
   * read, or one that is not a marker. The last two are said with the path and the reason, and all
   * three mean the sweep runs: a marker nobody can read protects nothing, and a value somebody could
   * have hand-edited must not vanish silently into "no marker". (The code round.)
   */
  public async marker(): Promise<SweepMarker | undefined> {
    const path = join(this.housekeeping, SWEEP_MARKER);
    let text: string;
    try {
      text = await readFile(path, 'utf8');
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: the conversation sweep marker could not be read, so the sweep will run: ${path}`, reason);
      }

      return undefined;
    }
    const out = parseMarker(parsed(text));
    if (out.kind === 'invalid') {
      console.error(`ConnectOtherAIs: the conversation sweep marker is torn or not one this build can read, so the sweep will run — ${out.reason}: ${path}`);

      return undefined;
    }

    return out.marker;
  }

  // -------------------------------------------------------------------------------------------
  // Writing: this window's heartbeat, and the marker.
  // -------------------------------------------------------------------------------------------

  /**
   * Announce what this window holds open. Atomically, so a sweep never reads half a list; an id that
   * could not be a filename is left out and said, because one bad id would make the whole file invalid
   * and protect nothing. `false` when it could not be written — the caller is a timer, and the next
   * beat tries again.
   */
  public async beat(pid: number, ids: readonly string[], now: number): Promise<boolean> {
    const path = join(this.housekeeping, heartbeatName(pid));
    const safe = ids.filter((id) => isSafeId(id));
    if (safe.length < ids.length) {
      console.error(`ConnectOtherAIs: ${ids.length - safe.length} open conversation id(s) could not be announced in the heartbeat because they cannot be filenames: ${path}`);
    }
    try {
      await writeFileAtomically(path, heartbeatText(pid, safe, now));

      return true;
    } catch (reason) {
      console.error(`ConnectOtherAIs: this window's heartbeat could not be written, so its open conversations are not announced: ${path}`, reason);

      return false;
    }
  }

  /**
   * Write the marker: a CLAIM before the work, so the windows opening beside this one stand down while
   * a sweep can still be running; a FINISHED marker after it, and only from a sweep that walked its
   * whole plan — that is the one that holds for a day. `false` when it could not be written.
   */
  public async writeMarker(marker: SweepMarker): Promise<boolean> {
    const path = join(this.housekeeping, SWEEP_MARKER);
    try {
      await writeFileAtomically(path, markerText(marker));

      return true;
    } catch (reason) {
      console.error(`ConnectOtherAIs: the conversation sweep marker could not be written; other windows may sweep too: ${path}`, reason);

      return false;
    }
  }

  // -------------------------------------------------------------------------------------------
  // Removing debris — each under the conversation's lock where a conversation is involved.
  // -------------------------------------------------------------------------------------------

  /** Remove an index entry whose record is gone — if, with the conversation claimed, it is still gone. */
  public async removeOrphanMeta(id: string, now: number): Promise<DebrisOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }

    return this.underClaim(id, 'sweep orphan index', now, async () => {
      const record = await this.presence(join(this.dir, recordName(id)));
      if (record !== 'absent') {
        return { kind: 'kept', why: record === 'present' ? 'a record has appeared beside it' : 'its record could not be examined' };
      }

      return this.remove(join(this.dir, besideMeta(id)), 'an orphaned conversation index entry');
    });
  }

  /**
   * Set aside a transcript with no index entry — if, with the conversation claimed, there is still
   * none. Moved into the quarantine subdirectory as `<id>-orphan-<now>.json`, bytes untouched; said on
   * the console with the path, at information level, because it is a record kept and not a failure.
   */
  public async quarantineOrphanRecord(id: string, now: number): Promise<DebrisOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }

    return this.underClaim(id, 'sweep orphan transcript', now, async () => {
      const meta = await this.presence(join(this.dir, besideMeta(id)));
      if (meta !== 'absent') {
        return { kind: 'kept', why: meta === 'present' ? 'an index entry has appeared beside it' : 'its index entry could not be examined' };
      }
      const from = join(this.dir, recordName(id));
      const to = join(this.dir, QUARANTINE_DIR, `${id}-orphan-${now}.json`);
      try {
        await mkdir(join(this.dir, QUARANTINE_DIR), { recursive: true });
        await rename(from, to);
        console.info(`ConnectOtherAIs: a conversation transcript with no index entry was set aside, unchanged, at ${to}`);

        return { kind: 'removed' };
      } catch (reason) {
        if (codeOf(reason) === 'ENOENT') {
          return { kind: 'kept', why: 'the transcript is gone' };
        }
        console.error(`ConnectOtherAIs: an orphaned conversation transcript could not be set aside: ${from}`, reason);

        return { kind: 'failed', reason: withCode('an orphaned transcript could not be set aside', codeOf(reason)) };
      }
    });
  }

  /** Remove a stale temporary from the root. The name must be one of the atomic write's, and nothing else. */
  public async removeTemp(name: string): Promise<DebrisOutcome> {
    if (!isBareName(name) || !name.endsWith(BESIDE_SUFFIX)) {
      return { kind: 'failed', reason: `not a temporary of this store — a bare file name ending in ${BESIDE_SUFFIX}: ${name}` };
    }

    return this.remove(join(this.dir, name), 'a stale conversation temporary');
  }

  /**
   * Collect an abandoned lock — by CLAIMING the conversation and letting go.
   *
   * <p>The claim breaks a lock older than the lock module's window through that module's own verified
   * path, which is the one lock-breaking path this feature has; the release then removes the lock the
   * claim took. A lock still held after the claim's wait belongs to a live writer and is kept. Nothing
   * else here touches a `.lock`.</p>
   */
  public async collectLock(id: string, now: number): Promise<DebrisOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const claim = await claimConversation(this.dir, id, 'sweep abandoned lock', now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'kept', why: 'the lock is held by a live writer' };
    }
    await claim.release();

    return { kind: 'removed' };
  }

  /** Remove a quarantined value past the retention window. The name must carry an instant, or it is not ours to remove. */
  public async removeQuarantined(name: string): Promise<DebrisOutcome> {
    if (!isBareName(name) || quarantinedAt(name) === undefined) {
      return { kind: 'failed', reason: `not a quarantined value of this store — a bare file name ending in -<instant>.json: ${name}` };
    }

    return this.remove(join(this.dir, QUARANTINE_DIR, name), 'an old quarantined value');
  }

  /** Remove a heartbeat nobody refreshed. The name must be a window's, or it is not ours to remove. */
  public async removeHeartbeat(name: string): Promise<DebrisOutcome> {
    if (heartbeatOwner(name) === 0) {
      return { kind: 'failed', reason: `not a heartbeat — a heartbeat is named window-<pid>.json: ${name}` };
    }

    return this.remove(join(this.housekeeping, name), 'a stale window heartbeat');
  }

  /** Whether something is at a path: present, absent, or — anything but `ENOENT` — not knowable, which no decision may act on. */
  private async presence(path: string): Promise<'present' | 'absent' | 'unknown'> {
    try {
      await stat(path);

      return 'present';
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return 'absent';
      }
      console.error(`ConnectOtherAIs: a file in the conversation store could not be examined: ${path}`, reason);

      return 'unknown';
    }
  }

  /** Do something with the conversation claimed, and let go in a `finally`. */
  private async underClaim(id: string, doing: string, now: number, act: () => Promise<DebrisOutcome>): Promise<DebrisOutcome> {
    const claim = await claimConversation(this.dir, id, doing, now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'kept', why: 'another window is changing this conversation' };
    }
    try {
      return await act();
    } finally {
      await claim.release();
    }
  }

  /** One removal. Already gone counts as removed; a refusal is said with the path and handed back without it. */
  private async remove(path: string, what: string): Promise<DebrisOutcome> {
    try {
      await rm(path, { force: true });

      return { kind: 'removed' };
    } catch (reason) {
      console.error(`ConnectOtherAIs: ${what} could not be removed: ${path}`, reason);

      return { kind: 'failed', reason: withCode(`${what} could not be removed`, codeOf(reason)) };
    }
  }
}

// -----------------------------------------------------------------------------------------------
// The sweep itself.
// -----------------------------------------------------------------------------------------------

/** What one sweep needs of the world. */
export interface SweepDeps {
  readonly store: ChatStoreFile;
  readonly keeper: ChatStoreKeeper;
  /** What this window holds open, read at EVERY decision — a tab registered a moment ago counts. */
  readonly held: () => ReadonlySet<string>;
  readonly pid: number;
  /** The clock's reading when the sweep began; every age is measured from it. A test pins it. */
  readonly now: number;
  /** The running clock the budget is measured on. Defaults to the wall clock. */
  readonly clock?: () => number;
  /** Sweep even behind a recent marker — for a caller that knows better, such as a test. */
  readonly force?: boolean;
}

type Swept = Extract<SweepReport, { kind: 'swept' }>;

type Counter = Exclude<keyof Swept, 'kind' | 'unfinished'>;

const NO_TALLY: Readonly<Record<Counter, number>> = {
  retired: 0,
  keptUnderLock: 0,
  orphanMetasRemoved: 0,
  recordsQuarantined: 0,
  tempsRemoved: 0,
  locksCollected: 0,
  quarantineRemoved: 0,
  heartbeatsRemoved: 0,
  failed: 0,
};

/** Which counter a debris outcome lands on: the given one when removed, `failed` when it failed, none when kept. */
const counterOf = (out: DebrisOutcome, removed: Counter): Counter | undefined =>
  out.kind === 'removed' ? removed : out.kind === 'failed' ? 'failed' : undefined;

/**
 * One expired nomination: skipped if this window has opened it since the plan was made, else the store
 * decides under the lock.
 *
 * <p>A record that CHANGED since the listing is kept — and then READ through the store, which is what
 * regenerates a stale index entry (the crash between a save's two renames) under the read's own lock.
 * The retire itself writes nothing; the repair is this caller's decision, made in so many words (two
 * reviewers, the code round). The record is not nominated again by this sweep: the next one lists
 * the revision the record actually has, and retires it on a listing that agrees.</p>
 */
async function retireOne(deps: SweepDeps, id: string, rev: number): Promise<Counter | undefined> {
  if (deps.held().has(id)) {
    return undefined;
  }
  const out = await deps.store.retireIfExpired(id, rev, deps.now);
  if (out.kind === 'kept' && out.why === 'changed') {
    await deps.store.read(id, deps.now);
  }

  return out.kind === 'retired' ? 'retired' : out.kind === 'kept' ? 'keptUnderLock' : 'failed';
}

/**
 * Carry a plan out, in the order that loses least if the budget runs out first: conversations, then the
 * files that could confuse a reader, then the debris nothing reads.
 */
async function carryOut(plan: SweepPlan, deps: SweepDeps): Promise<Swept> {
  const clock = deps.clock ?? Date.now;
  const until = clock() + SWEEP_BUDGET_MS;
  const done: Counter[] = [];
  let finished = true;
  const run = async <T>(items: readonly T[], act: (item: T) => Promise<Counter | undefined>): Promise<void> => {
    for (const item of items) {
      if (clock() > until) {
        finished = false;

        return;
      }
      const counter = await act(item);
      if (counter !== undefined) {
        done.push(counter);
      }
    }
  };
  await run(plan.expired, ({ id, rev }) => retireOne(deps, id, rev));
  await run(plan.orphanMetas, async (id) => counterOf(await deps.keeper.removeOrphanMeta(id, deps.now), 'orphanMetasRemoved'));
  await run(plan.orphanRecords, async (id) => counterOf(await deps.keeper.quarantineOrphanRecord(id, deps.now), 'recordsQuarantined'));
  await run(plan.staleTemps, async (name) => counterOf(await deps.keeper.removeTemp(name), 'tempsRemoved'));
  await run(plan.abandonedLocks, async (id) => counterOf(await deps.keeper.collectLock(id, deps.now), 'locksCollected'));
  await run(plan.oldQuarantine, async (name) => counterOf(await deps.keeper.removeQuarantined(name), 'quarantineRemoved'));
  await run(plan.staleHeartbeats, async (name) => counterOf(await deps.keeper.removeHeartbeat(name), 'heartbeatsRemoved'));
  const tally = done.reduce((sum, key) => ({ ...sum, [key]: sum[key] + 1 }), NO_TALLY);

  return { kind: 'swept', ...tally, unfinished: !finished };
}

/**
 * Sweep the store once — or say why not.
 *
 * <p>In order: the store's state, because a directory that exists and will not answer must not be read
 * as a directory full of expired things, and a store that is not there yet has nothing to sweep. Then
 * the marker, because one window sweeps and not six. Then the CLAIM — written before the work so the
 * windows opening beside this one stand down, and standing only for as long as a sweep can run; a
 * window that cannot write it does not sweep, because six windows that cannot see each other's claims
 * would all sweep. Then the survey and the listing, the plan, and the plan carried out under a budget.
 * The FINISHED marker — the one that holds for a day — is written last, and only by a sweep that walked
 * its whole plan: a survey that fails, or a budget that runs out on a backlog, leaves the claim to age
 * out and the store due, rather than a day's marker on the strength of work that was never done (codex
 * and the local round). Every deletion of a conversation goes through `ChatStoreFile.retireIfExpired`,
 * which re-checks under the lock; nothing here deletes a conversation any other way. Never throws: the
 * store and the keeper answer in outcomes.</p>
 */
export async function runSweep(deps: SweepDeps): Promise<SweepReport> {
  const state = await deps.store.state();
  if (state.kind === 'unavailable') {
    return { kind: 'skipped', why: 'unavailable', reason: state.reason };
  }
  if (state.kind === 'empty') {
    return { kind: 'skipped', why: 'empty', reason: '' };
  }
  const marker = await deps.keeper.marker();
  if (deps.force !== true && !sweepDue(marker, deps.now)) {
    return { kind: 'skipped', why: marker?.kind === 'claimed' ? 'sweeping' : 'recent', reason: '' };
  }
  if (!await deps.keeper.writeMarker({ kind: 'claimed', pid: deps.pid, began: deps.now })) {
    return { kind: 'skipped', why: 'unclaimed', reason: '' };
  }
  const surveyed = await deps.keeper.survey();
  if (surveyed.kind === 'unavailable') {
    return { kind: 'skipped', why: 'unavailable', reason: surveyed.reason };
  }
  const listed = await deps.store.listMeta();
  if (listed.kind === 'unavailable') {
    return { kind: 'skipped', why: 'unavailable', reason: listed.reason };
  }
  const report = await carryOut(planSweep(surveyed.survey, listed.metas, deps.now, deps.held(), deps.pid), deps);
  if (!report.unfinished) {
    await deps.keeper.writeMarker({ kind: 'finished', pid: deps.pid, began: deps.now, finished: (deps.clock ?? Date.now)() });
  }

  return report;
}
