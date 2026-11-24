import type { KeyValueStore } from './sideSettings';
import type { RefusalsRead } from './remoteRefusals';

/**
 * The store of the hosts the extension may read over ssh (E1.S1): one pure module holds the rows, the ONE function that says whether
 * a host may be contacted (`consentOf`), and the ONE write (`commit`) with its merge and its cap. It exports no function that takes a
 * list to write: what it exports reads a value, commits a CHANGE for one host, or says words.
 *
 * <p>A "no" is a person's refusal and nothing automatic changes it. The file of refusals (`remoteRefusals.ts`) is where a side keeps
 * one; the "no" row of the list is its copy, honoured on every side and in an older build. A value that cannot be read is not an empty
 * value: it contacts nobody and is never written over, except by a person's own answer (`mend`).</p>
 */

/** One host's row, as it is kept in `globalState` under `coai.terminalHosts`. */
export interface HostAnswer {
  readonly host: string;
  readonly answer: 'yes' | 'no';
  /** UTC ISO; for an automatic row, the instant the round wrote it. */
  readonly askedUtc: string;
  /** Only on a "yes" the round's door wrote. A person's row has no `how`. */
  readonly how?: 'auto';
}

export type Consent = 'yes' | 'no' | 'ask' | 'damaged';

/** The most hosts the list keeps. */
export const HOSTS_MAX = 100;

/** A change for ONE host, never a list. `mend` is sent only by the change question, over a damaged list or a damaged file. */
export type HostChange =
  | { readonly kind: 'person'; readonly host: string; readonly answer: 'yes' | 'no' }
  | { readonly kind: 'forget'; readonly host: string }
  | { readonly kind: 'auto'; readonly host: string }
  | { readonly kind: 'mend'; readonly host: string; readonly answer: 'yes' | 'no' | 'forget' };

export type Committed =
  | { readonly kind: 'written'; readonly row: HostAnswer | undefined; readonly evicted: readonly string[] }
  | { readonly kind: 'kept'; readonly row: HostAnswer | undefined }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' }
  /** The refusals file did not take the change. `why` is one of three fixed words. */
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

/** The answer of the round's door, called just before every accounts call. */
export type Admission =
  | { readonly kind: 'read'; readonly how: 'person' | 'auto'; readonly allowedNow: boolean }
  | { readonly kind: 'refused' }
  | { readonly kind: 'not-asked' }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' }
  /** A "no" or a Stop is not in the file yet: no host is contacted. */
  | { readonly kind: 'unwritten' };

export interface HostStanding {
  readonly host: string;
  readonly answer: 'yes' | 'no';
  readonly how: 'person' | 'auto';
  readonly askedUtc: string;
}

// ─────────────────────────────── Reading ───────────────────────────────

const asRecord = (value: unknown): Readonly<Record<string, unknown>> =>
  (typeof value === 'object' && value !== null ? (value as Readonly<Record<string, unknown>>) : {});

/** A row is whole or it is not a row. `how` is absent, or `'auto'` beside a "yes": any other marker is unknown and makes the list damaged. */
function isRow(row: unknown): row is HostAnswer {
  const one = asRecord(row);
  const how = one['how'];

  return typeof one['host'] === 'string' && one['host'].length > 0 && (one['answer'] === 'yes' || one['answer'] === 'no')
    && typeof one['askedUtc'] === 'string' && (how === undefined || (how === 'auto' && one['answer'] === 'yes'));
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
 * Whether this host may be contacted. The ONE function that says so; a host is contacted only on `yes`. `file` is the side as it was
 * just read from the disk. In this order: a refusal in the file or a readable "no" in the list is `no`; a file or a list that cannot
 * be read is `damaged`; a person's "yes" is `yes`; an automatic "yes" is `yes` only when `allowNew`, else `ask`; no row is `ask`.
 */
export function consentOf(file: RefusalsRead, value: unknown, host: string, allowNew: boolean): Consent {
  const list = hostsOf(value);
  const row = strongest(list.rows, host);
  if (row?.answer === 'no' || (file.kind === 'file' && file.file.refused.some((one) => one.host === host))) {
    return 'no';
  }
  if (file.kind === 'damaged' || list.damaged) {
    return 'damaged';
  }
  if (row === undefined) {
    return 'ask';
  }

  return row.how === 'auto' && !allowNew ? 'ask' : 'yes';
}

/** Whether the terminal's two overflow items are shown: a host that is not answered "no". Used with a read file. */
export const refusedBy = (file: RefusalsRead, value: unknown, host: string): boolean => consentOf(file, value, host, true) === 'no';

const byAsked = (a: HostAnswer, b: HostAnswer): number => Number(a.askedUtc > b.askedUtc) - Number(a.askedUtc < b.askedUtc) || Number(a.host > b.host) - Number(a.host < b.host);

const standingOf = (row: HostAnswer): HostStanding => ({ host: row.host, answer: row.answer, how: row.how === 'auto' ? 'auto' : 'person', askedUtc: row.askedUtc });

/** Each host's standing from the list alone, by host name; over a damaged list, the rows that can be read. */
export function standingsOf(value: unknown): readonly HostStanding[] {
  const rows = hostsOf(value).rows;
  const hosts = [...new Set(rows.map((row) => row.host))].sort();

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
 * The hosts the *CoAI: hosts read over ssh* list shows: the hosts with a readable answer; over a damaged list also the hosts of the
 * live `Creds:` terminals that have none, so a list in which no answer can be read is still mended from there.
 */
export function listedHosts(value: unknown, liveHosts: readonly string[]): readonly { readonly host: string; readonly standing: HostStanding | undefined }[] {
  const list = hostsOf(value);
  const known = standingsOf(value).map((standing) => ({ host: standing.host, standing }));
  if (!list.damaged) {
    return known;
  }
  const have = new Set(known.map((one) => one.host));
  const extra = [...new Set(liveHosts)].filter((host) => !have.has(host)).sort().map((host) => ({ host, standing: undefined }));

  return [...known, ...extra];
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

/** The rows with `row` put in, the cap kept: the oldest automatic row goes, else (a person's change) the oldest person's "yes", else nothing fits. */
function fitted(others: readonly HostAnswer[], row: HostAnswer, personal: boolean): Rows | undefined {
  const over = others.length + 1 - HOSTS_MAX;
  if (over <= 0) {
    return { rows: [...others, row], evicted: [] };
  }
  const dropOrder = [...others].filter((one) => one.how === 'auto').sort(byAsked);
  const people = personal ? [...others].filter((one) => one.answer === 'yes' && one.how !== 'auto').sort(byAsked) : [];
  const drop = [...dropOrder, ...people].slice(0, over);

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

function personal(rows: readonly HostAnswer[], host: string, answer: 'yes' | 'no', now: string): Outcome {
  const row: HostAnswer = { host, answer, askedUtc: stampFor(now, rows.filter((one) => one.host === host)) };

  return writtenOf(fitted(without(rows, host), row, true), row);
}

function forgotten(rows: readonly HostAnswer[], host: string): Outcome {
  const had = rows.some((one) => one.host === host);

  return had ? { next: { value: without(rows, host) }, result: { kind: 'written', row: undefined, evicted: [] } } : keptOf(undefined);
}

function automatic(rows: readonly HostAnswer[], host: string, now: string): Outcome {
  const fresh = strongest(rows, host);
  if (fresh !== undefined) {
    return keptOf(fresh);
  }
  const row: HostAnswer = { host, answer: 'yes', askedUtc: now, how: 'auto' };

  return writtenOf(fitted(rows, row, false), row);
}

/** The readable rows of a damaged list, one for each host, at most `HOSTS_MAX`, the refusals first. */
function mendedRows(rows: readonly HostAnswer[]): readonly HostAnswer[] {
  const hosts = [...new Set(rows.map((row) => row.host))];
  const one = hosts.map((host) => strongest(rows, host) as HostAnswer);

  return [...one.filter((row) => row.answer === 'no').sort(byAsked), ...one.filter((row) => row.answer === 'yes').sort(byAsked).reverse()].slice(0, HOSTS_MAX);
}

function appliedTo(rows: readonly HostAnswer[], change: HostChange, now: string): Outcome {
  switch (change.kind) {
    case 'person': return personal(rows, change.host, change.answer, now);
    case 'forget': return forgotten(rows, change.host);
    case 'auto': return automatic(rows, change.host, now);
    case 'mend': return change.answer === 'forget' ? forgotten(rows, change.host) : personal(rows, change.host, change.answer, now);
  }
}

/** The merge of the list, for one host, at the instant `now`: the table of the spec. A damaged list is written over by a `mend` only. */
export const hostMerge = (now: string): Merge<HostChange, Committed> => (stored, change) => {
  const list = hostsOf(stored);
  if (!list.damaged) {
    return appliedTo(list.rows, change, now);
  }

  return change.kind === 'mend' ? appliedTo(mendedRows(list.rows), change, now) : DAMAGED;
};
