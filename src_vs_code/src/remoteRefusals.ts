import type { Claim } from './chatStoreLock';
import type { Committed } from './remoteHosts';

/**
 * The refusals file of one side of the machine (E1.S1, the parent's entry 1.2): the persons "no" for a host and the Stop of the
 * accounts round, kept in a file under the extension's storage folder and not in `globalState`, because a window sends its WHOLE
 * `globalState` object on any write and a stale window then puts old values back.
 *
 * <p>The rules are pure; the one writer goes through a disk seam, so a unit test runs it over a fake. The writer claims, writes and
 * reads back. It never says "kept" for a change it cannot see in the file. No path and no disk error text leaves this module.</p>
 */

/** The cap of refusals, the same number the list of answers keeps. */
export const REFUSALS_MAX = 100;

/** The file may not be larger than this, read or written. A full file is about 8 KB with hosts of 30 characters. */
export const REFUSALS_BYTES_MAX = 64 * 1024;

/** A held claim is asked for again this often, 200 ms apart: about 1.6 s in all. */
export const CLAIM_ROUNDS = 5;
/** A claim older than this is not written under: the writer starts again (step 5). */
export const CLAIM_FRESH_MS = 10_000;
/** A change that is not there at the read-back is written again this often. */
export const WRITE_ROUNDS = 3;
/** A beside-file older than this was left by a crash. */
export const BESIDE_STALE_MS = 60_000;
/** Markers in the folder; past it none is made. */
export const UNSURE_MAX = 32;
/** One marker; a larger one is `torn`. */
export const UNSURE_BYTES_MAX = 1024;

const CLAIM_PAUSE_MS = 200;
const READ_PAUSE_MS = 20;
const READ_TRIES = 3;
/** Restarts at step 1 that are not a lost change (a stale claim, a lock taken over): bounded so a writer never spins. */
const RESTARTS_MAX = 5;

export interface Refusal {
  readonly host: string;
  readonly utc: string;
}

/** The refusals file of one side, version 1. Every time is UTC ISO. Other keys are kept through a write and not read. */
export interface RefusalsFile {
  readonly v: 1;
  /** A whole number, 1 or more, one higher with every write. The file that was never written counts as 0. */
  readonly gen: number;
  /** The token of the write that made this file. Two files of one `gen` and two `by` are two writers on one baseline. */
  readonly by: string;
  /** Set by the FIRST copy-in; a record that gates nothing. */
  readonly migratedUtc?: string;
  readonly refused: readonly Refusal[];
  /** Present while the accounts round is stopped. */
  readonly stop?: { readonly utc: string };
}

/** The file that was never written. */
export const EMPTY_FILE: RefusalsFile = { v: 1, gen: 0, by: '', refused: [] };

/** A change of the file, never a content. */
export type RefusalChange =
  | { readonly kind: 'refuse'; readonly host: string; readonly utc: string }
  | { readonly kind: 'lift'; readonly host: string }
  | { readonly kind: 'stop'; readonly utc: string }
  | { readonly kind: 'start' }
  /** The copy-in: the readable "no" rows of the list that the file does not hold. Adds only. */
  | { readonly kind: 'migrate'; readonly rows: readonly Refusal[]; readonly utc: string }
  /** A file that went back: what this window remembers of the newest file it read. Adds only; `gen` ends above both. */
  | { readonly kind: 'regain'; readonly gen: number; readonly rows: readonly Refusal[]; readonly stop?: { readonly utc: string } }
  /** A persons answer over a DAMAGED file: the new file. It always leaves the round stopped. */
  | { readonly kind: 'mend'; readonly gen: number; readonly rows: readonly Refusal[]; readonly utc: string };

/** What one marker holds: a refusal or a Stop that the file did not take. `torn` is a marker that cannot be read. */
export type Waiting =
  | { readonly name: string; readonly kind: 'refuse'; readonly host: string; readonly utc: string }
  | { readonly name: string; readonly kind: 'stop'; readonly utc: string }
  | { readonly name: string; readonly kind: 'torn' };

/** A change that waits, without the name of its marker. */
export type Unsure = { readonly kind: 'refuse'; readonly host: string; readonly utc: string } | { readonly kind: 'stop'; readonly utc: string };

/** The side as it was just read from the disk, or the fact that the file or its folder cannot be read. */
export type RefusalsRead =
  | { readonly kind: 'file'; readonly file: RefusalsFile; readonly waiting: readonly Waiting[] }
  | { readonly kind: 'damaged' };

// ─────────────────────────────── Reading ───────────────────────────────

const recordOf = (value: unknown): Readonly<Record<string, unknown>> | undefined =>
  (typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Readonly<Record<string, unknown>>) : undefined);

const isText = (value: unknown): value is string => typeof value === 'string';

const refusalOf = (value: unknown): Refusal | undefined => {
  const one = recordOf(value);

  return one !== undefined && isText(one['host']) && one['host'].length > 0 && isText(one['utc']) ? { host: one['host'], utc: one['utc'] } : undefined;
};

const stopOf = (value: unknown): { readonly utc: string } | undefined | 'bad' => {
  if (value === undefined) {
    return undefined;
  }
  const one = recordOf(value);

  return one !== undefined && isText(one['utc']) ? { utc: one['utc'] } : 'bad';
};

/** The refusals of a value, or `undefined` when it is not an array of at most `REFUSALS_MAX` well-made entries. */
function refusedOf(value: unknown): readonly Refusal[] | undefined {
  if (!Array.isArray(value) || value.length > REFUSALS_MAX) {
    return undefined;
  }
  const made = value.map(refusalOf);

  return made.every((one) => one !== undefined) ? (made as readonly Refusal[]) : undefined;
}

const isGen = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 1;

/** The ONE parser of the file's text. `undefined` is damage: anything that is not a whole file of version 1. */
export function refusalsOf(text: string): RefusalsFile | undefined {
  if (Buffer.byteLength(text, 'utf8') > REFUSALS_BYTES_MAX) {
    return undefined;
  }
  let raw: Readonly<Record<string, unknown>> | undefined;
  try {
    raw = recordOf(JSON.parse(text));
  } catch {
    return undefined;
  }
  const refused = refusedOf(raw?.['refused']);
  const stop = stopOf(raw?.['stop']);
  const migrated = raw?.['migratedUtc'];
  if (raw === undefined || raw['v'] !== 1 || !isGen(raw['gen']) || !isText(raw['by']) || raw['by'].length === 0 || refused === undefined || stop === 'bad' || (migrated !== undefined && !isText(migrated))) {
    return undefined;
  }

  return { ...raw, v: 1, gen: raw['gen'], by: raw['by'], refused, ...(stop === undefined ? {} : { stop }), ...(isText(migrated) ? { migratedUtc: migrated } : {}) } as RefusalsFile;
}

/** The marker's text as a change that waits, or `torn`. The name is the file's name. */
export function waitingOf(name: string, text: string | undefined): Waiting {
  const torn: Waiting = { name, kind: 'torn' };
  if (text === undefined || Buffer.byteLength(text, 'utf8') > UNSURE_BYTES_MAX) {
    return torn;
  }
  let one: Readonly<Record<string, unknown>> | undefined;
  try {
    one = recordOf(JSON.parse(text));
  } catch {
    return torn;
  }
  if (one?.['v'] !== 1 || !isText(one['utc'])) {
    return torn;
  }
  if (one['kind'] === 'stop') {
    return { name, kind: 'stop', utc: one['utc'] };
  }

  return one['kind'] === 'refuse' && isText(one['host']) && one['host'].length > 0 ? { name, kind: 'refuse', host: one['host'], utc: one['utc'] } : torn;
}

export const textOf = (file: RefusalsFile): string => JSON.stringify(file);

// ─────────────────────────────── The merge ───────────────────────────────

export type Changed =
  | { readonly kind: 'written'; readonly file: RefusalsFile }
  | { readonly kind: 'kept' }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' };

const KEPT: Changed = { kind: 'kept' };
const FULL: Changed = { kind: 'full' };

const hostsOf = (rows: readonly Refusal[]): ReadonlySet<string> => new Set(rows.map((one) => one.host));

/** One entry for each host, the older `utc` staying. */
function oneEach(rows: readonly Refusal[]): readonly Refusal[] {
  const seen = new Map<string, Refusal>();
  for (const one of rows) {
    const had = seen.get(one.host);
    if (had === undefined || one.utc < had.utc) {
      seen.set(one.host, one);
    }
  }

  return [...seen.values()];
}

/** Rows whose host the file lacks, newest first, up to the cap. `[]` when none is missing or none fits. */
function addable(file: RefusalsFile, rows: readonly Refusal[]): { readonly add: readonly Refusal[]; readonly missing: number } {
  const have = hostsOf(file.refused);
  const missing = oneEach(rows).filter((one) => !have.has(one.host)).sort((a, b) => Number(a.utc < b.utc) - Number(a.utc > b.utc));

  return { add: missing.slice(0, Math.max(0, REFUSALS_MAX - file.refused.length)), missing: missing.length };
}

const next = (file: RefusalsFile, by: string, parts: Partial<RefusalsFile>, genFloor = 0): Changed =>
  ({ kind: 'written', file: { ...file, ...parts, v: 1, gen: Math.max(file.gen, genFloor) + 1, by } });

const withoutStop = (file: RefusalsFile): RefusalsFile => {
  const { stop: _stop, ...rest } = file;

  return rest;
};

type Merger<K extends RefusalChange['kind']> = (file: RefusalsFile, change: Extract<RefusalChange, { kind: K }>, by: string) => Changed;

const refuse: Merger<'refuse'> = (file, change, by) => {
  if (hostsOf(file.refused).has(change.host)) {
    return KEPT;
  }

  return file.refused.length >= REFUSALS_MAX ? FULL : next(file, by, { refused: [...file.refused, { host: change.host, utc: change.utc }] });
};

const lift: Merger<'lift'> = (file, change, by) =>
  (hostsOf(file.refused).has(change.host) ? next(file, by, { refused: file.refused.filter((one) => one.host !== change.host) }) : KEPT);

const stop: Merger<'stop'> = (file, change, by) => (file.stop === undefined ? next(file, by, { stop: { utc: change.utc } }) : KEPT);

const start: Merger<'start'> = (file, _change, by) => (file.stop === undefined ? KEPT : next(withoutStop(file), by, {}));

const migrate: Merger<'migrate'> = (file, change, by) => {
  const { add, missing } = addable(file, change.rows);
  if (missing === 0) {
    return KEPT;
  }

  return add.length === 0 ? FULL : next(file, by, { refused: [...file.refused, ...add], migratedUtc: file.migratedUtc ?? change.utc });
};

const regain: Merger<'regain'> = (file, change, by) => {
  const { add } = addable(file, change.rows);
  const stopToo = change.stop !== undefined && file.stop === undefined;

  return add.length === 0 && !stopToo ? KEPT : next(file, by, { refused: [...file.refused, ...add], ...(stopToo ? { stop: change.stop } : {}) }, change.gen);
};

const MERGERS: { readonly [K in RefusalChange['kind']]: Merger<K> | undefined } = {
  refuse, lift, stop, start, migrate, regain, mend: undefined,
};

/** The new file of a `mend`: only over a damaged file. */
function mended(change: Extract<RefusalChange, { kind: 'mend' }>, by: string): Changed {
  const rows = oneEach(change.rows).slice(0, REFUSALS_MAX);

  return { kind: 'written', file: { v: 1, gen: change.gen + 1, by, migratedUtc: change.utc, refused: rows, stop: { utc: change.utc } } };
}

/**
 * The file after one change, or why there is none. It never returns a file with fewer refusals unless the change is a `lift`, and
 * never removes `stop` unless the change is a `start`. Every file it returns has a `gen` higher than the one it was given.
 * `'damaged'` is the file that cannot be read: only a `mend` is written over it.
 */
export function withChange(file: RefusalsFile | 'damaged', change: RefusalChange, by: string): Changed {
  if (file === 'damaged') {
    return change.kind === 'mend' ? sized(mended(change, by)) : { kind: 'damaged' };
  }
  if (change.kind === 'mend') {
    return KEPT;
  }
  const merge = MERGERS[change.kind] as Merger<typeof change.kind>;

  return sized(merge(file, change as never, by));
}

/** A result over the size cap is `full`, and nothing is written. */
function sized(result: Changed): Changed {
  return result.kind === 'written' && Buffer.byteLength(textOf(result.file), 'utf8') > REFUSALS_BYTES_MAX ? FULL : result;
}

/** Whether a change is in the file as it now is — step 8 when another writer wrote since. `before` is the file the change was merged into. */
export function holds(change: RefusalChange, before: RefusalsFile | 'damaged', after: RefusalsFile): boolean {
  switch (change.kind) {
    case 'refuse': return hostsOf(after.refused).has(change.host);
    case 'stop': return after.stop !== undefined;
    case 'lift': {
      const gone = new Set(before === 'damaged' ? [] : before.refused.filter((one) => one.host === change.host).map((one) => one.utc));

      return !after.refused.some((one) => one.host === change.host && gone.has(one.utc));
    }
    case 'start': return before === 'damaged' || before.stop === undefined || after.stop?.utc !== before.stop.utc;
    case 'migrate':
    case 'regain': return addable(after, change.rows).missing === 0 && (change.kind !== 'regain' || change.stop === undefined || after.stop !== undefined);
    case 'mend': return after.migratedUtc !== undefined;
  }
}

// ─────────────────────────────── The writer ───────────────────────────────

/** The disk, as the writer needs it. The host half binds it to ONE folder; a test hands in a fake. */
export interface RefusalsDisk {
  /** The file's text. At most `REFUSALS_BYTES_MAX + 1` bytes are read, so a larger file is seen as too large and not loaded. */
  readonly read: () => Promise<{ readonly kind: 'absent' } | { readonly kind: 'text'; readonly text: string } | { readonly kind: 'failed' }>;
  /** `claimFile` of `chatStoreLock.ts`, bound to the folder, the id `refused` and its rule. `take: 'person'` takes the lock whatever its owner. */
  readonly claim: (doing: string, take?: 'person') => Promise<Claim>;
  /** Whether the lock on the disk still carries this claim's token. */
  readonly stillOurs: (claim: Extract<Claim, { kind: 'claimed' }>) => Promise<boolean>;
  /** `writeFileAtomically` of `atomicFile.ts`, bound to the file. It throws what the disk refused. */
  readonly replace: (text: string) => Promise<void>;
  /** Removes this folder's beside-files older than `ms`. Never throws. */
  readonly sweep: (ms: number) => Promise<void>;
  /** Makes one marker by an EXCLUSIVE CREATE, with no claim. `undefined`: the disk refused, or `UNSURE_MAX` markers are there. */
  readonly mark: (text: string) => Promise<string | undefined>;
  /** Every marker of the folder, read NOW. `failed`: the folder could not be listed, three times. */
  readonly marks: () => Promise<readonly { readonly name: string; readonly text: string | undefined }[] | 'failed'>;
  /** Removes one marker by its name. Never throws. */
  readonly unmark: (name: string) => Promise<void>;
}

export interface RefusalsStore {
  /** The file and the markers, read from the disk NOW. No claim. Never a value kept from an earlier read. */
  readonly read: () => Promise<RefusalsRead>;
  /** One change, under the claim, verified by a read-back. */
  readonly commit: (change: RefusalChange, take?: 'person') => Promise<Committed>;
  /** One marker for a change that was `unwritten`. `false`: no marker could be made. */
  readonly mark: (waiting: Unsure) => Promise<boolean>;
  readonly unmark: (name: string) => Promise<void>;
}

const realSleep = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

/** `restart`: the claim went stale or was taken over, so nothing was written. `lost`: the write is not in the file at the read-back. */
type Step = 'restart' | 'lost' | Committed;

/** The writer over a disk. `sleep` is the pause between tries; a test hands in one that does not wait. */
export function refusalsStore(disk: RefusalsDisk, clock: () => number, sleep: (ms: number) => Promise<void> = realSleep): RefusalsStore {
  /** The file, or `undefined` when it cannot be read; `absent` is the empty file. A failing read is asked again twice. */
  async function readFileOnce(): Promise<RefusalsFile | undefined> {
    for (let tries = 1; tries <= READ_TRIES; tries += 1) {
      const got = await disk.read();
      if (got.kind === 'absent') {
        return EMPTY_FILE;
      }
      if (got.kind === 'text') {
        return refusalsOf(got.text);
      }
      if (tries < READ_TRIES) {
        await sleep(READ_PAUSE_MS);
      }
    }

    return undefined;
  }

  async function read(): Promise<RefusalsRead> {
    const file = await readFileOnce();
    const marks = await disk.marks();
    if (file === undefined || marks === 'failed') {
      return { kind: 'damaged' };
    }

    return { kind: 'file', file, waiting: marks.map((one) => waitingOf(one.name, one.text)) };
  }

  async function claimed(doing: string, take: 'person' | undefined): Promise<Claim> {
    let got: Claim = { kind: 'held' };
    for (let round = 1; round <= CLAIM_ROUNDS && got.kind === 'held'; round += 1) {
      got = await disk.claim(doing, take);
      if (got.kind === 'held' && round < CLAIM_ROUNDS) {
        await sleep(CLAIM_PAUSE_MS);
      }
    }

    return got;
  }

  /** Steps 2 to 8 under a claim that is ours. */
  async function underClaim(change: RefusalChange, claim: Extract<Claim, { kind: 'claimed' }>, began: number): Promise<Step> {
    const before = await readFileOnce();
    const merged = withChange(before ?? 'damaged', change, claim.token);
    if (merged.kind !== 'written') {
      await claim.release();

      return merged.kind === 'kept' ? { kind: 'kept', row: undefined } : merged;
    }
    await disk.sweep(BESIDE_STALE_MS);
    if (clock() - began >= CLAIM_FRESH_MS || !await disk.stillOurs(claim)) {
      await claim.release();

      return 'restart';
    }
    try {
      await disk.replace(textOf(merged.file));
    } catch {
      await claim.release();

      return { kind: 'unwritten', why: 'disk' };
    }
    const ours = await disk.stillOurs(claim);
    await claim.release();

    return ours ? readBack(change, before ?? 'damaged', merged.file) : 'restart';
  }

  /** Step 8: the file as it now is shows this write, or this change. */
  async function readBack(change: RefusalChange, before: RefusalsFile | 'damaged', wrote: RefusalsFile): Promise<Step> {
    const now = await readFileOnce();
    if (now === undefined) {
      return 'lost';
    }
    const ours = now.gen === wrote.gen && now.by === wrote.by;

    return ours || holds(change, before, now) ? { kind: 'written', row: undefined, evicted: [] } : 'lost';
  }

  async function commit(change: RefusalChange, take?: 'person'): Promise<Committed> {
    let lost = 0;
    for (let round = 0; round < RESTARTS_MAX + WRITE_ROUNDS && lost < WRITE_ROUNDS; round += 1) {
      const claim = await claimed(change.kind, take);
      if (claim.kind !== 'claimed') {
        return { kind: 'unwritten', why: claim.kind === 'held' ? 'busy' : 'disk' };
      }
      const step = await underClaim(change, claim, clock());
      if (step !== 'restart' && step !== 'lost') {
        return step;
      }
      lost += step === 'lost' ? 1 : 0;
    }

    return { kind: 'unwritten', why: lost > 0 ? 'lost' : 'busy' };
  }

  return {
    read,
    commit,
    mark: async (waiting) => (await disk.mark(JSON.stringify({ v: 1, ...waiting }))) !== undefined,
    unmark: (name) => disk.unmark(name),
  };
}
