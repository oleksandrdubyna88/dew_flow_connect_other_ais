import type { Claim } from './chatStoreLock';
import type { Committed } from './remoteHosts';
import { Allowed, Grant, Withdrawal, Withdrawn, grantOf, stampOf } from './remotePermission';

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

export const CLAIM_PAUSE_MS = 200;
export const READ_PAUSE_MS = 20;
export const READ_TRIES = 3;
/** Restarts at step 1 that are not a lost change (a stale claim, a lock taken over): bounded so a writer never spins. */
export const RESTARTS_MAX = 5;
/** A withdrawal that an *Allow* wrote under, or whose read-back met a claim, goes again from its step 4 this often. */
export const WITHDRAW_ROUNDS = 3;

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
  /**
   * The permission of this side (E2.S1, § P3), typed `unknown` ON PURPOSE: the file carries the value as it is stored, and only `grantOf` of
   * `remotePermission.ts` turns it into a `Grant`. A value that is malformed is no grant, and is kept through every write but a `mend`.
   */
  readonly grant?: unknown;
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
  | { readonly kind: 'mend'; readonly gen: number; readonly rows: readonly Refusal[]; readonly utc: string }
  /** The permission, sent ONLY by the store's `allow` (§ P6): `commit` cannot send it. A `stamp` that is not above `floor` is refused (`low`). */
  | { readonly kind: 'grant'; readonly utc: string; readonly stamp: number; readonly floor: number };

/** What `commit` may send: every change but the grant. */
export type PlainChange = Exclude<RefusalChange, { readonly kind: 'grant' }>;

/** What one marker holds: a refusal or a Stop that the file did not take. `torn` is a marker that cannot be read. */
export type Waiting =
  | { readonly name: string; readonly kind: 'refuse'; readonly host: string; readonly utc: string }
  | { readonly name: string; readonly kind: 'stop'; readonly utc: string }
  | { readonly name: string; readonly kind: 'torn' };

/** A change that waits, without the name of its marker. */
export type Unsure = { readonly kind: 'refuse'; readonly host: string; readonly utc: string } | { readonly kind: 'stop'; readonly utc: string };

/** The side as it was just read from the disk, or the fact that the file or its folder cannot be read. */
export type RefusalsRead =
  | { readonly kind: 'file'; readonly file: RefusalsFile; readonly waiting: readonly Waiting[]; readonly withdrawn: WithdrawnSeen }
  | { readonly kind: 'damaged' };

/** The withdrawal files of the folder as they were read; `crowded`: more names than were read, which count as one unreadable withdrawal. */
export interface WithdrawnSeen {
  readonly files: readonly Withdrawal[];
  readonly crowded: boolean;
}

// ─────────────────────────────── Reading ───────────────────────────────

const recordOf = (value: unknown): Readonly<Record<string, unknown>> | undefined =>
  (typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Readonly<Record<string, unknown>>) : undefined);

const isText = (value: unknown): value is string => typeof value === 'string';

type Rec = Readonly<Record<string, unknown>>;

const hostOk = (one: Rec): boolean => isText(one['host']) && one['host'].length > 0;

const refusalOf = (value: unknown): Refusal | undefined => {
  const one = recordOf(value);

  return one !== undefined && hostOk(one) && isText(one['utc']) ? { host: one['host'] as string, utc: one['utc'] } : undefined;
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

const isGen = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 1;

/** The record in a text, or nothing: too large, not JSON, or not an object. */
function recordFromText(text: string): Rec | undefined {
  if (Buffer.byteLength(text, 'utf8') > REFUSALS_BYTES_MAX) {
    return undefined;
  }
  try {
    return recordOf(JSON.parse(text));
  } catch {
    return undefined;
  }
}

/** What every file of version 1 holds: the version, the generation, the writer's token, and a record or nothing for the time of the copy-in. */
const HEADER: readonly ((raw: Rec) => boolean)[] = [
  (raw) => raw['v'] === 1,
  (raw) => isGen(raw['gen']),
  (raw) => isText(raw['by']) && raw['by'].length > 0,
  (raw) => raw['migratedUtc'] === undefined || isText(raw['migratedUtc']),
];

interface Parts {
  readonly refused: readonly Refusal[];
  readonly stop: { readonly utc: string } | undefined;
}

function partsOf(raw: Rec): Parts | undefined {
  const refused = refusedOf(raw['refused']);
  const stop = stopOf(raw['stop']);

  return refused === undefined || stop === 'bad' ? undefined : { refused, stop };
}

function validated(text: string): { readonly raw: Rec; readonly parts: Parts } | undefined {
  const raw = recordFromText(text);
  if (raw === undefined || !HEADER.every((ok) => ok(raw))) {
    return undefined;
  }
  const parts = partsOf(raw);

  return parts === undefined ? undefined : { raw, parts };
}

/** The ONE parser of the file's text. `undefined` is damage: anything that is not a whole file of version 1. */
export function refusalsOf(text: string): RefusalsFile | undefined {
  const read = validated(text);
  if (read === undefined) {
    return undefined;
  }
  const { raw, parts } = read;
  const migrated = raw['migratedUtc'];

  return { ...raw, v: 1, gen: raw['gen'] as number, by: raw['by'] as string, refused: parts.refused, ...(parts.stop === undefined ? {} : { stop: parts.stop }), ...(isText(migrated) ? { migratedUtc: migrated } : {}) };
}

const hasUtc = (one: Rec): boolean => one['v'] === 1 && isText(one['utc']);

type MarkerBuilder = (name: string, one: Rec) => Waiting | undefined;

const MARKERS: ReadonlyMap<string, MarkerBuilder> = new Map<string, MarkerBuilder>([
  ['stop', (name, one) => ({ name, kind: 'stop', utc: one['utc'] as string })],
  ['refuse', (name, one) => (hostOk(one) ? { name, kind: 'refuse', host: one['host'] as string, utc: one['utc'] as string } : undefined)],
]);

const noMarker: MarkerBuilder = () => undefined;

/** The marker's text as a change that waits, or `torn`. The name is the file's name. */
export function waitingOf(name: string, text: string | undefined): Waiting {
  return (text === undefined ? undefined : markerFrom(name, text)) ?? { name, kind: 'torn' };
}

function markerFrom(name: string, text: string): Waiting | undefined {
  const one = Buffer.byteLength(text, 'utf8') > UNSURE_BYTES_MAX ? undefined : recordFromText(text);

  return one !== undefined && hasUtc(one) ? builderOf(one)(name, one) : undefined;
}

const builderOf = (one: Rec): MarkerBuilder => MARKERS.get(String(one['kind'])) ?? noMarker;

export const textOf = (file: RefusalsFile): string => JSON.stringify(file);

// ─────────────────────────────── The merge ───────────────────────────────

export type Changed =
  | { readonly kind: 'written'; readonly file: RefusalsFile }
  | { readonly kind: 'kept' }
  /** A grant whose stamp is not above its floor: nothing is written. Only a `grant` change gets it. */
  | { readonly kind: 'low' }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' };

const KEPT: Changed = { kind: 'kept' };
const LOW: Changed = { kind: 'low' };
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

/** Some remembered refusals are missing and none can be added: the answer is `full`, never `kept` (§ S1.5: a regain the file does not take is held). */
const regain: Merger<'regain'> = (file, change, by) => {
  const { add, missing } = addable(file, change.rows);

  return missing > 0 && add.length === 0 ? FULL : regained(file, change, by, add);
};

const regained = (file: RefusalsFile, change: Extract<RefusalChange, { kind: 'regain' }>, by: string, add: readonly Refusal[]): Changed => {
  const stop = stopToAdd(file, change.stop);

  return add.length === 0 && stop === undefined ? KEPT : next(file, by, { refused: [...file.refused, ...add], ...(stop === undefined ? {} : { stop }) }, change.gen);
};

/** The Stop a regain carries, when the file has none. */
const stopToAdd = (file: RefusalsFile, stop: { readonly utc: string } | undefined): { readonly utc: string } | undefined => (file.stop === undefined ? stop : undefined);

/** The permission (§ P3): refused when its stamp is not above the floor; kept when the file holds a well-made grant at that stamp or higher. */
const lowGrant = (change: Extract<RefusalChange, { readonly kind: 'grant' }>): boolean => stampOf(change.stamp) === undefined || change.stamp <= change.floor;

const grantHeld = (file: RefusalsFile, change: Extract<RefusalChange, { readonly kind: 'grant' }>): boolean => (grantOf(file)?.stamp ?? 0) >= change.stamp;

const grant: Merger<'grant'> = (file, change, by) => {
  if (lowGrant(change)) {
    return LOW;
  }

  return grantHeld(file, change) ? KEPT : next(file, by, { grant: { utc: change.utc, stamp: change.stamp } satisfies Grant });
};

const MERGERS: { readonly [K in RefusalChange['kind']]: Merger<K> | undefined } = {
  refuse, lift, stop, start, migrate, regain, mend: undefined, grant,
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

const refusedBy = (before: RefusalsFile | 'damaged', host: string): readonly string[] =>
  (before === 'damaged' ? [] : before.refused.filter((one) => one.host === host).map((one) => one.utc));

const stillThere = (after: RefusalsFile, host: string, utcs: readonly string[]): boolean => after.refused.some((one) => one.host === host && utcs.includes(one.utc));

const stopOfBefore = (before: RefusalsFile | 'damaged'): string | undefined => (before === 'damaged' ? undefined : before.stop?.utc);

type Holds<K extends RefusalChange['kind']> = (change: Extract<RefusalChange, { readonly kind: K }>, before: RefusalsFile | 'damaged', after: RefusalsFile) => boolean;

const HOLDS: { readonly [K in RefusalChange['kind']]: Holds<K> } = {
  refuse: (change, _before, after) => hostsOf(after.refused).has(change.host),
  stop: (_change, _before, after) => after.stop !== undefined,
  lift: (change, before, after) => !stillThere(after, change.host, refusedBy(before, change.host)),
  start: (_change, before, after) => stopOfBefore(before) === undefined || after.stop?.utc !== stopOfBefore(before),
  migrate: (change, _before, after) => addable(after, change.rows).missing === 0,
  regain: (change, _before, after) => addable(after, change.rows).missing === 0 && (change.stop === undefined || after.stop !== undefined),
  mend: (_change, _before, after) => after.migratedUtc !== undefined,
  grant: (change, _before, after) => (grantOf(after)?.stamp ?? 0) >= change.stamp,
};

/** Whether a change is in the file as it now is — step 8 when another writer wrote since. `before` is the file the change was merged into. */
export const holds = (change: RefusalChange, before: RefusalsFile | 'damaged', after: RefusalsFile): boolean =>
  (HOLDS[change.kind] as Holds<RefusalChange['kind']>)(change, before, after);

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
  /**
   * Every withdrawal file of the folder, read NOW (§ P4): the names that fit the grammar first, the highest stamp first, then the rest in code-unit
   * order; the first `WITHDRAWN_READ_MAX` are read and the names of the others are `rest`. `failed`: the folder could not be listed, three times.
   */
  readonly withdrawals: () => Promise<{ readonly seen: readonly { readonly name: string; readonly text: string | undefined }[]; readonly rest: readonly string[] } | 'failed'>;
  /** An EXCLUSIVE CREATE of one withdrawal file, no claim. `there`: the name exists. `refused`: the disk refused. */
  readonly withdrawn: (name: string, text: string) => Promise<'made' | 'there' | 'refused'>;
  /** Removes one withdrawal file by its name. `false`: it could not be removed. Never throws. */
  readonly unwithdraw: (name: string) => Promise<boolean>;
  /** The prefix this disk was bound with (`DiskNames.withdrawnPrefix`). The store hands it to `withdrawalName` and `withdrawalOf`. */
  readonly withdrawnPrefix: string;
  /**
   * The lock as it is NOW, read with NO claim; it makes, takes over and removes nothing. `free`: there is no lock. `gone`: the rule a claim asks
   * (`ownerGone`) says its owner is gone — never its age. `held`: every other case, a note that cannot be read among them.
   */
  readonly lockNow: () => Promise<'free' | 'held' | 'gone'>;
}

export interface RefusalsStore {
  /** The file, the withdrawal files and the markers, read from the disk NOW. No claim. Never a value kept from an earlier read. */
  readonly read: () => Promise<RefusalsRead>;
  /** One change, under the claim, verified by a read-back. A `grant` cannot be sent through it: only `allow` writes one. */
  readonly commit: (change: PlainChange, take?: 'person') => Promise<Committed>;
  /** One marker for a change that was `unwritten`. `false`: no marker could be made. */
  readonly mark: (waiting: Unsure) => Promise<boolean>;
  readonly unmark: (name: string) => Promise<void>;
  /** A person's *Allow* (§ P6). `copy` reads the row's copy NOW, each time it is called. */
  readonly allow: (input: { readonly utc: string; readonly copy: () => number | 'unreadable' | undefined }) => Promise<Allowed>;
  /** A person's *Withdraw* (§ P7). `above`: the highest stamp the pressing window knew; the withdrawal's stamp is at least one ABOVE it. */
  readonly withdraw: (input: { readonly utc: string; readonly above: number }) => Promise<Withdrawn>;
  /** The copy-in: one withdrawal file for a copy that is above the folder. */
  readonly copyIn: (stamp: number, utc: string) => Promise<'written' | 'kept' | 'unwritten'>;
  /** A PERSON'S PRESS ONLY (`saveWaiting`): one claim with `take: 'person'`, released at once. Writes nothing. */
  readonly free: () => Promise<void>;
}

export type { Allowed, Withdrawn };
