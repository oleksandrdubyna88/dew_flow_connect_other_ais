import { byCodeUnit } from './codeUnitOrder';
import type { DoorAnswer } from './remotePermission';
import type { KeyValueStore } from './sideSettings';

/**
 * The store of the hosts the extension may read over ssh (E1.S1): one pure module holds the rows, the refusal half of the one door (`consentOf`;
 * the door itself is the keeper's `may`, E2.S1), and the ONE write (`commit`) with its merge and its cap. It exports no function that takes a
 * list to write: what it exports reads a value, commits a CHANGE for one host, or says words.
 *
 * <p>A "no" is a person's refusal and nothing automatic changes it. The file of refusals (`remoteRefusals.ts`) is where a side keeps
 * one; the "no" row of the list is its copy, honoured on every side and in an older build. A value that cannot be read is not an empty
 * value: it contacts nobody and is never written over, except by a person's own answer (`mend`).</p>
 */

/**
 * One host's row, as it is kept in `globalState` under `coai.terminalHosts`. A "yes" row (a person's of 0.87.0 to 2.0.0, or an automatic one of the
 * pre-release) is still READ, so a list that holds one is not damaged, and it is NEVER an answer: no function builds one and none reads one as consent.
 */
export interface HostAnswer {
  readonly host: string;
  readonly answer: 'yes' | 'no';
  /** UTC ISO. */
  readonly askedUtc: string;
  /** Only on an automatic "yes" of the pre-release. A person's row has no `how`. */
  readonly how?: 'auto';
}

/**
 * What `consentOf` needs of the refusals file as it was just read: its refused hosts, or the fact that it cannot be read. The file's own
 * `RefusalsRead` (`remoteRefusals.ts`) fits it; it is declared here so this module imports nothing from that one.
 */
export type SideSeen =
  | { readonly kind: 'file'; readonly file: { readonly refused: readonly { readonly host: string }[] } }
  | { readonly kind: 'damaged' };

/** The refusal half of the door: `open` is every answer that is not a refusal and not damage — a "yes" row and no row alike. */
export type Consent = 'no' | 'damaged' | 'open';

/** The most hosts the list keeps. */
export const HOSTS_MAX = 100;

/** A change for ONE host, never a list. `mend` is sent only by the change question, over a damaged list or a damaged file. No change writes a "yes". */
export type HostChange =
  | { readonly kind: 'person'; readonly host: string; readonly answer: 'no' }
  | { readonly kind: 'forget'; readonly host: string }
  | { readonly kind: 'mend'; readonly host: string; readonly answer: 'no' | 'forget' };

export type Committed =
  | { readonly kind: 'written'; readonly row: HostAnswer | undefined; readonly evicted: readonly string[] }
  | { readonly kind: 'kept'; readonly row: HostAnswer | undefined }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' }
  /** The refusals file did not take the change. `why` is one of three fixed words. */
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

/**
 * The round's OLD view of the door, called just before every accounts call; deleted by E2.S4 with the round's own door call. The door is
 * the keeper's `may` (`remotePermission.ts`) and `admissionOf` maps its answer to this type while the round still reads it.
 */
export type Admission =
  | { readonly kind: 'read'; readonly how: 'person' | 'auto'; readonly allowedNow: boolean }
  | { readonly kind: 'refused' }
  | { readonly kind: 'not-asked' }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' }
  /** A "no" or a Stop is not in the file yet: no host is contacted. */
  | { readonly kind: 'unwritten' };

/** The door's answer as the round's old view (the bridge of E2.S1, deleted by E2.S4 with `Admission`). */
export function admissionOf(answer: DoorAnswer): Admission {
  return ADMISSION_OF[answer];
}

const ADMISSION_OF: Readonly<Record<DoorAnswer, Admission>> = {
  'yes': { kind: 'read', how: 'person', allowedNow: false },
  'no': { kind: 'refused' },
  'damaged': { kind: 'damaged' },
  'waiting': { kind: 'unwritten' },
  'no-permission': { kind: 'not-asked' },
  'stopped': { kind: 'unwritten' },
};

export interface HostStanding {
  readonly host: string;
  readonly answer: 'yes' | 'no';
  readonly how: 'person' | 'auto';
  readonly askedUtc: string;
}

// ─────────────────────────────── Reading ───────────────────────────────

const asRecord = (value: unknown): Readonly<Record<string, unknown>> =>
  (typeof value === 'object' && value !== null ? (value as Readonly<Record<string, unknown>>) : {});

const hasHost = (one: Readonly<Record<string, unknown>>): boolean => typeof one['host'] === 'string' && one['host'].length > 0;

const hasAnswer = (one: Readonly<Record<string, unknown>>): boolean => one['answer'] === 'yes' || one['answer'] === 'no';

/** `how` is absent, or `'auto'` beside a "yes": any other marker is unknown. */
const hasKnownHow = (one: Readonly<Record<string, unknown>>): boolean => one['how'] === undefined || (one['how'] === 'auto' && one['answer'] === 'yes');

/** A row is whole or it is not a row. A marker this build does not know makes the list damaged. */
function isRow(row: unknown): row is HostAnswer {
  const one = asRecord(row);

  return hasHost(one) && hasAnswer(one) && typeof one['askedUtc'] === 'string' && hasKnownHow(one);
}

interface Hosts {
  /** The rows that can be read. */
  readonly rows: readonly HostAnswer[];
  /** How many entries cannot be read. */
  readonly unreadable: number;
  readonly damaged: boolean;
}

/** The ONE parser of the list. `undefined` (never written) is empty; anything that is not an array of valid rows is damaged. */
function hostsOf(value: unknown): Hosts {
  if (value === undefined) {
    return { rows: [], unreadable: 0, damaged: false };
  }
  if (!Array.isArray(value)) {
    return { rows: [], unreadable: 1, damaged: true };
  }
  const rows = value.filter(isRow);

  return { rows, unreadable: value.length - rows.length, damaged: rows.length !== value.length };
}

/** What a damaged list still shows: the rows that can be read, and how many cannot. */
export const readableOf = (value: unknown): { readonly rows: readonly HostAnswer[]; readonly unreadable: number; readonly damaged: boolean } => hostsOf(value);

const RANK: Readonly<Record<string, number>> = { no: 3, person: 2, auto: 1 };

const rankOf = (row: HostAnswer): number => RANK[row.answer === 'no' ? 'no' : row.how === 'auto' ? 'auto' : 'person'] ?? 0;

/** The strongest row of a host: a "no", then a person's "yes", then an automatic "yes". */
function strongest(rows: readonly HostAnswer[], host: string): HostAnswer | undefined {
  return rows.filter((row) => row.host === host).sort((a, b) => rankOf(b) - rankOf(a))[0];
}

/**
 * The refusal half of the door (E2.S1, § P8): whether this host is refused or the side cannot be read. `file` is the side as it was just read
 * from the disk. In this order: a refusal in the file or a readable "no" in the list is `no`; a file or a list that cannot be read is `damaged`;
 * everything else — a "yes" row, an automatic row, no row — is `open`, like no row: a per-host "yes" is never an answer.
 */
export function consentOf(file: SideSeen, value: unknown, host: string): Consent {
  const list = hostsOf(value);
  if (isRefused(file, strongest(list.rows, host), host)) {
    return 'no';
  }

  return cannotRead(file, list) ? 'damaged' : 'open';
}

const isRefused = (file: SideSeen, row: HostAnswer | undefined, host: string): boolean =>
  row?.answer === 'no' || (file.kind === 'file' && file.file.refused.some((one) => one.host === host));

const cannotRead = (file: SideSeen, list: Hosts): boolean => file.kind === 'damaged' || list.damaged;

/** Whether the terminal's two overflow items are shown: a host that is not answered "no". Used with a read file. */
export const refusedBy = (file: SideSeen, value: unknown, host: string): boolean => consentOf(file, value, host) === 'no';

const byAsked = (a: HostAnswer, b: HostAnswer): number => Number(a.askedUtc > b.askedUtc) - Number(a.askedUtc < b.askedUtc) || Number(a.host > b.host) - Number(a.host < b.host);

const standingOf = (row: HostAnswer): HostStanding => ({ host: row.host, answer: row.answer, how: row.how === 'auto' ? 'auto' : 'person', askedUtc: row.askedUtc });

/** Each host's standing from the list alone, by host name; over a damaged list, the rows that can be read. */
export function standingsOf(value: unknown): readonly HostStanding[] {
  const rows = hostsOf(value).rows;
  const hosts = [...new Set(rows.map((row) => row.host))].sort(byCodeUnit);

  return hosts.map((host) => standingOf(strongest(rows, host) as HostAnswer));
}

/** The calendar day of an instant on this machine, `YYYY-MM-DD`, or *an unknown day*. */
export function dayOf(askedUtc: string): string {
  const at = new Date(askedUtc);
  if (Number.isNaN(at.getTime())) {
    return 'an unknown day';
  }
  const two = (n: number): string => String(n).padStart(2, '0');

  return `${at.getFullYear()}-${two(at.getMonth() + 1)}-${two(at.getDate())}`;
}

/**
 * The hosts the *CoAI: hosts read over ssh* list shows (E2.S1, § P10): the hosts with a readable answer and the hosts of the live `Creds:`
 * terminals that have none — so a person can refuse a host here before anything is read, and a list in which no answer can be read is still mended
 * from there. The refused hosts of the file join them in `choicesOf`.
 */
export function listedHosts(value: unknown, liveHosts: readonly string[]): readonly { readonly host: string; readonly standing: HostStanding | undefined }[] {
  const known = standingsOf(value).map((standing) => ({ host: standing.host, standing }));
  const have = new Set(known.map((one) => one.host));
  const extra = [...new Set(liveHosts)].filter((host) => !have.has(host)).sort(byCodeUnit).map((host) => ({ host, standing: undefined }));

  return [...known, ...extra];
}

/**
 * The choices of the hosts list: the listed hosts and the hosts of the keeper's standings (a refusal that only the side file holds is one),
 * the keeper's standing first, in code-unit order.
 */
export function choicesOf(
  listed: readonly { readonly host: string; readonly standing: HostStanding | undefined }[],
  standings: readonly HostStanding[],
): readonly { readonly host: string; readonly standing: HostStanding | undefined }[] {
  const byHost = new Map(listed.map((one) => [one.host, one.standing]));
  for (const standing of standings) {
    byHost.set(standing.host, standing);
  }

  return [...byHost].sort(([a], [b]) => byCodeUnit(a, b)).map(([host, standing]) => ({ host, standing }));
}

// ─────────────────────────────── The write ───────────────────────────────

/** One commit of one key's value: the stored value is read now, `merge` is pure, and nothing is awaited before the write is called. */
export type Merge<C, R> = (stored: unknown, change: C) => { readonly next: { readonly value: unknown } | undefined; readonly result: R };

/**
 * One commit of one key: read now, merge, write. The write is called in the step of the read, with no `await` between. It makes a
 * stale VARIABLE harmless; a stale WINDOW is the file's business (`remoteRefusals.ts`).
 */
export async function commit<C, R>(state: KeyValueStore, key: string, change: C, merge: Merge<C, R>): Promise<R> {
  const { next, result } = merge(state.get<unknown>(key), change);
  if (next !== undefined) {
    await state.update(key, next.value);
  }

  return result;
}

const plusMs = (utc: string, ms: number): number => (Number.isNaN(Date.parse(utc)) ? Number.NEGATIVE_INFINITY : Date.parse(utc) + ms);

/** A person's row is stamped the later of `now` and one millisecond after the row it replaces, so a row's time never goes back. */
function stampFor(now: string, replaced: readonly HostAnswer[]): string {
  const floor = Math.max(...replaced.map((row) => plusMs(row.askedUtc, 1)), Number.NEGATIVE_INFINITY);

  return floor > Date.parse(now) ? new Date(floor).toISOString() : now;
}

interface Rows {
  readonly rows: readonly HostAnswer[];
  /** The hosts whose row went at the cap. */
  readonly evicted: readonly string[];
}

/** A write of the list leaves out every automatic row of the pre-release (J7): such a row allows nothing, so losing one loses nothing. Every other row is kept by reference. */
const withoutAuto = (rows: readonly HostAnswer[]): readonly HostAnswer[] => rows.filter((one) => one.how !== 'auto');

/** The rows with `row` put in, the cap kept: the oldest "yes" row goes, never a "no"; with nothing to drop the list is full. */
function fitted(others: readonly HostAnswer[], row: HostAnswer): Rows | undefined {
  const over = others.length + 1 - HOSTS_MAX;
  if (over <= 0) {
    return { rows: [...others, row], evicted: [] };
  }
  const drop = others.filter((one) => one.answer === 'yes').sort(byAsked).slice(0, over);

  return drop.length < over ? undefined : { rows: [...others.filter((one) => !drop.includes(one)), row], evicted: drop.map((one) => one.host) };
}

type Outcome = { readonly next: { readonly value: unknown } | undefined; readonly result: Committed };

const DAMAGED: Outcome = { next: undefined, result: { kind: 'damaged' } };
const FULL: Outcome = { next: undefined, result: { kind: 'full' } };
const keptOf = (row: HostAnswer | undefined): Outcome => ({ next: undefined, result: { kind: 'kept', row } });

function writtenOf(rows: Rows | undefined, row: HostAnswer | undefined): Outcome {
  return rows === undefined ? FULL : { next: { value: rows.rows }, result: { kind: 'written', row, evicted: rows.evicted } };
}

const without = (rows: readonly HostAnswer[], host: string): readonly HostAnswer[] => rows.filter((one) => one.host !== host);

/** A person's "no": the only row any function builds. */
function personal(rows: readonly HostAnswer[], host: string, now: string): Outcome {
  const row: HostAnswer = { host, answer: 'no', askedUtc: stampFor(now, rows.filter((one) => one.host === host)) };

  return writtenOf(fitted(without(withoutAuto(rows), host), row), row);
}

function forgotten(rows: readonly HostAnswer[], host: string): Outcome {
  const had = rows.some((one) => one.host === host);

  return had ? { next: { value: without(withoutAuto(rows), host) }, result: { kind: 'written', row: undefined, evicted: [] } } : keptOf(undefined);
}

/** The readable rows of a damaged list, one for each host, at most `HOSTS_MAX`, the refusals first. */
function mendedRows(readable: readonly HostAnswer[]): readonly HostAnswer[] {
  const rows = withoutAuto(readable);
  const hosts = [...new Set(rows.map((row) => row.host))];
  const one = hosts.map((host) => strongest(rows, host) as HostAnswer);

  return [...one.filter((row) => row.answer === 'no').sort(byAsked), ...one.filter((row) => row.answer === 'yes').sort(byAsked).reverse()].slice(0, HOSTS_MAX);
}

type Of<K extends HostChange['kind']> = Extract<HostChange, { readonly kind: K }>;
type Apply = (rows: readonly HostAnswer[], change: HostChange, now: string) => Outcome;

const APPLY: { readonly [K in HostChange['kind']]: (rows: readonly HostAnswer[], change: Of<K>, now: string) => Outcome } = {
  person: (rows, change, now) => personal(rows, change.host, now),
  forget: (rows, change) => forgotten(rows, change.host),
  mend: (rows, change, now) => (change.answer === 'forget' ? forgotten(rows, change.host) : personal(rows, change.host, now)),
};

const appliedTo: Apply = (rows, change, now) => (APPLY[change.kind] as Apply)(rows, change, now);

/** A mend over a damaged list: the readable rows are written whatever the answer — a *Forget* for a host with no row repairs the list too. */
function mended(rows: readonly HostAnswer[], change: Of<'mend'>, now: string): Outcome {
  const kept = mendedRows(rows);

  return change.answer === 'forget' ? { next: { value: without(kept, change.host) }, result: { kind: 'written', row: undefined, evicted: [] } } : appliedTo(kept, change, now);
}

/** The merge of the list, for one host, at the instant `now`: the table of the spec. A damaged list is written over by a `mend` only. */
export const hostMerge = (now: string): Merge<HostChange, Committed> => (stored, change) => {
  const list = hostsOf(stored);
  if (!list.damaged) {
    return appliedTo(list.rows, change, now);
  }

  return change.kind === 'mend' ? mended(list.rows, change, now) : DAMAGED;
};

// ─────────────────────────────── The change question's buttons (§ P10) ───────────────────────────────

/** The three choices a host can have; each is offered only where it does something, and there is no *Read*. */
export type HostButton = 'not-this-host' | 'read-again' | 'forget';

/**
 * The buttons of the change question for one host: a refused host has *Read this host again*; a host that is not refused has *Not this host*, and *Forget*
 * too when an older "yes" row is stored; over a list or a file that cannot be read the two answers that can each be the person's for a host whose
 * standing cannot be read. `refused`: the host is refused in the file, as a row, held or in a marker.
 */
const storedYes = (standing: HostStanding | undefined): boolean => standing?.answer === 'yes';

export function buttonsOf(standing: HostStanding | undefined, refused: boolean, damaged: boolean): readonly HostButton[] {
  if (damaged) {
    return ['not-this-host', 'read-again'];
  }

  return refused ? ['read-again'] : storedYes(standing) ? ['not-this-host', 'forget'] : ['not-this-host'];
}

/** The change a button sends. Over damage a `mend` (it writes no "yes"), else a plain change; *Read this host again* lifts the refusal. */
export function changeOfButton(button: HostButton, host: string, damaged: boolean): HostChange {
  if (damaged) {
    return { kind: 'mend', host, answer: button === 'not-this-host' ? 'no' : 'forget' };
  }

  return button === 'not-this-host' ? { kind: 'person', host, answer: 'no' } : { kind: 'forget', host };
}
