import { stampOf } from './remotePermission';
import type { Committed, Merge } from './remoteHosts';

/**
 * The state row of the accounts round and the Stop (E1.S3, contract C5), moved out of `remoteAccountsRound.ts` (E3.S3: that file reached its line limit)
 * with no change of a rule. `remoteAccountsRound.ts` still exports every name of this file, so no importer moves.
 */

/** The `globalState` key of the state row (C5). */
export const STATE_KEY = 'coai.terminalAccountsState';

export interface AccountsState {
  /** E1's two notice times: no longer written (E2.S4); a row that holds them is read as before and keeps them. */
  readonly toldUtc?: string;
  readonly lastAutoNoticeUtc?: string;
  readonly stoppedUtc?: string;
  /** When the one notice `accounts-can-be-shown` was raised, for this installation. */
  readonly canBeShownUtc?: string;
  /** The copy of the newest withdrawal's stamp (E2.S1, § P5): a stamp, or a value that is not one — then it counts as a withdrawal. */
  readonly withdrawnStamp?: unknown;
  /** When the icon's question was first asked. */
  readonly askedUtc?: string;
  readonly [other: string]: unknown;
}

const isRecord = (value: unknown): value is Readonly<Record<string, unknown>> => typeof value === 'object' && value !== null && !Array.isArray(value);

const FIELDS: readonly string[] = ['toldUtc', 'lastAutoNoticeUtc', 'stoppedUtc', 'askedUtc', 'canBeShownUtc'];

const fieldsOk = (value: Readonly<Record<string, unknown>>): boolean => FIELDS.every((field) => value[field] === undefined || typeof value[field] === 'string');

/** `undefined` (never written) is the empty state; an object whose three fields are each absent or a string is that state; anything else is damaged. */
export function accountsStateOf(value: unknown): { readonly kind: 'state'; readonly state: AccountsState } | { readonly kind: 'damaged' } {
  if (value === undefined) {
    return { kind: 'state', state: {} };
  }

  return isRecord(value) && fieldsOk(value) ? { kind: 'state', state: value } : { kind: 'damaged' };
}

/** The Stop's copy in the row. A row that cannot be read is STOPPED, not empty: the stop is the way out when a read does harm on a VM. */
export function stoppedOf(value: unknown): boolean {
  const read = accountsStateOf(value);

  return read.kind === 'damaged' || typeof read.state.stoppedUtc === 'string';
}

/**
 * What the permission needs of the row (E2.S1, § P5). `withdrawn`: a stamp, nothing, or a value that is not a stamp (`'unreadable'`, which counts as a
 * withdrawal); a row that cannot be read at all is STOPPED and has no permission. `stopped`: the Stop's copy.
 */
export function permissionRowOf(value: unknown): { readonly withdrawn: number | 'unreadable' | undefined; readonly stopped: boolean; readonly askedUtc: string | undefined } {
  const read = accountsStateOf(value);
  if (read.kind === 'damaged') {
    return { withdrawn: 'unreadable', stopped: true, askedUtc: undefined };
  }
  const copy = read.state.withdrawnStamp;

  return { withdrawn: copy === undefined ? undefined : (stampOf(copy) ?? 'unreadable'), stopped: typeof read.state.stoppedUtc === 'string', askedUtc: read.state.askedUtc };
}

/** A FIELD change of the state row. The first two are E1's; `withdrawn`, `floor` and `asked` are the permission's (E2.S1); `canBeShown` is the one notice's (E2.S4). */
export type StateChange =
  | { readonly stop: string }
  | { readonly start: true }
  /** The one notice was raised. Written once. */
  | { readonly canBeShown: string }
  /** A withdrawal's stamp: the row's copy becomes the higher of the two. `press`: a person's press; `read`: a copy-out, which never replaces a copy that is not a stamp. */
  | { readonly withdrawn: number; readonly utc: string; readonly by: 'press' | 'read' }
  /** An *Allow*: the copy is raised to its floor; a copy that is not a stamp is REPLACED by it. `restart`: the copy is removed. */
  | { readonly floor: number; readonly utc: string; readonly restart?: true }
  /** The icon's question was asked. Written once. */
  | { readonly asked: string };

export type StateCommitted = { readonly kind: 'written' } | { readonly kind: 'kept' };

const without = (state: AccountsState, field: string): AccountsState => Object.fromEntries(Object.entries(state).filter(([key]) => key !== field));

type WithdrawnChange = Extract<StateChange, { readonly withdrawn: number }>;
type FloorChange = Extract<StateChange, { readonly floor: number }>;

/** The row an *Allow* leaves over a row that could not be read: the floor as the copy (when it is one), still STOPPED. */
const damagedFloor = (change: FloorChange): AccountsState => ({ ...(change.floor >= 1 && change.restart !== true ? { withdrawnStamp: change.floor } : {}), stoppedUtc: change.utc });

/** A withdrawal over a row that could not be read: a person's press writes the copy with the stop's time; a copy-out writes nothing. */
const damagedWithdrawn = (change: WithdrawnChange): AccountsState | undefined => (change.by === 'press' ? { withdrawnStamp: change.withdrawn, stoppedUtc: change.utc } : undefined);

/**
 * Over a row that cannot be read only a person's press writes: a stop writes a row with its time, a start an empty row, a withdrawal or an *Allow* the
 * copy with the stop's time (the row stays STOPPED); a notice time, a copy-out and the first asking write nothing.
 */
const DAMAGED_BY: Readonly<Record<string, (change: never) => AccountsState | undefined>> = {
  stop: (change: { readonly stop: string }) => ({ stoppedUtc: change.stop }),
  start: () => ({}),
  withdrawn: damagedWithdrawn,
  floor: damagedFloor,
};

/** The name of the field that says which change it is. */
const fieldOf = (change: StateChange): string | undefined => Object.keys(DAMAGED_BY).find((field) => field in change);

const damagedNext = (change: StateChange): AccountsState | undefined => {
  const field = fieldOf(change);

  return field === undefined ? undefined : (DAMAGED_BY[field] as (change: StateChange) => AccountsState | undefined)(change);
};

const stoppingNext = (state: AccountsState, change: { readonly stop: string } | { readonly start: true }): AccountsState | undefined =>
  ('stop' in change ? { ...state, stoppedUtc: change.stop } : state.stoppedUtc === undefined ? undefined : without(state, 'stoppedUtc'));

/** The first time stays: the notice is once for each installation. */
const canBeShownNext = (state: AccountsState, utc: string): AccountsState | undefined => (state.canBeShownUtc === undefined ? { ...state, canBeShownUtc: utc } : undefined);

const isCopy = (state: AccountsState): boolean => state.withdrawnStamp !== undefined;

/** The copy raised to `stamp` — only when it is absent or a LOWER stamp. A copy that is not a stamp is never replaced here. */
function copyNext(state: AccountsState, stamp: number): AccountsState | undefined {
  const had = stampOf(state.withdrawnStamp);

  return !isCopy(state) || (had !== undefined && had < stamp) ? { ...state, withdrawnStamp: stamp } : undefined;
}

/** An *Allow* over a copy that is not a stamp: it is replaced by the floor, or removed when there is no floor. */
const replacedCopy = (state: AccountsState, floor: number): AccountsState => (floor >= 1 ? { ...state, withdrawnStamp: floor } : without(state, 'withdrawnStamp'));

/** An *Allow*: a copy that is not a stamp is REPLACED by the floor; the restart removes the copy. Else the copy is raised to the floor when the floor is higher. */
function floorNext(state: AccountsState, change: FloorChange): AccountsState | undefined {
  if (change.restart === true) {
    return isCopy(state) ? without(state, 'withdrawnStamp') : undefined;
  }

  return isBadCopy(state) ? replacedCopy(state, change.floor) : raisedTo(state, change.floor);
}

/** A copy that is there and is not a stamp. */
const isBadCopy = (state: AccountsState): boolean => isCopy(state) && stampOf(state.withdrawnStamp) === undefined;

const raisedTo = (state: AccountsState, floor: number): AccountsState | undefined => (floor >= 1 ? copyNext(state, floor) : undefined);

const askedNext = (state: AccountsState, utc: string): AccountsState | undefined => (state.askedUtc === undefined ? { ...state, askedUtc: utc } : undefined);

/** One merge for each field that says which change it is. */
const READABLE_BY: Readonly<Record<string, (state: AccountsState, change: never) => AccountsState | undefined>> = {
  stop: stoppingNext,
  start: stoppingNext,
  canBeShown: (state: AccountsState, change: { readonly canBeShown: string }) => canBeShownNext(state, change.canBeShown),
  withdrawn: (state: AccountsState, change: WithdrawnChange) => copyNext(state, change.withdrawn),
  floor: floorNext,
  asked: (state: AccountsState, change: { readonly asked: string }) => askedNext(state, change.asked),
};

const readableNext = (state: AccountsState, change: StateChange): AccountsState | undefined => {
  const field = Object.keys(READABLE_BY).find((one) => one in change);

  return field === undefined ? undefined : (READABLE_BY[field] as (state: AccountsState, change: StateChange) => AccountsState | undefined)(state, change);
};

/**
 * The merge of the state row, given to `sessions.commitKey`: it reads the value in the step of the write. A stale VARIABLE is harmless; a stale
 * WINDOW is not, and what keeps the Stop is the refusals file (S1 X17): `stoppedUtc` is its copy.
 */
export const stateMerge: Merge<StateChange, StateCommitted> = (stored, change) => {
  const read = accountsStateOf(stored);
  const next = read.kind === 'damaged' ? damagedNext(change) : readableNext(read.state, change);

  return next === undefined ? { next: undefined, result: { kind: 'kept' } } : { next: { value: next }, result: { kind: 'written' } };
};

/** What the Stop needs of the road: the file's three answers and the one commit of a `globalState` key. */
export interface StopDoor {
  readonly stopRound: () => Promise<Committed>;
  readonly startRound: () => Promise<Committed>;
  readonly roundStop: () => Promise<'stopped' | 'running' | 'damaged'>;
  readonly commitKey: <C, R>(key: string, change: C, merge: Merge<C, R>) => Promise<R>;
}

/**
 * The round is stopped when the FILE says so (read from the disk) OR the copy in the row does. A `damaged` file is not a stop here: every
 * admission then answers `damaged` and the list says so.
 */
export async function isStopped(door: Pick<StopDoor, 'roundStop'>, readState: () => unknown): Promise<boolean> {
  return (await door.roundStop()) === 'stopped' || stoppedOf(readState());
}

/** The file FIRST, then the copy, whatever the first answered: a Stop that was not saved still stops this window. */
export async function stopTheRound(door: StopDoor, utc: string): Promise<void> {
  await door.stopRound();
  await door.commitKey(STATE_KEY, { stop: utc }, stateMerge);
}

/** The file FIRST; the copy is cleared only when the file took the start (`written` or `kept`). Answers whether it was. */
export async function startTheRound(door: StopDoor): Promise<boolean> {
  const started = await door.startRound();
  if (started.kind !== 'written' && started.kind !== 'kept') {
    return false;
  }
  await door.commitKey(STATE_KEY, { start: true }, stateMerge);

  return true;
}
