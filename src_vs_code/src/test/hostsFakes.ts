import { byCodeUnit } from '../codeUnitOrder';
import type { Claim } from '../chatStoreLock';
import type { HostKeeper, KeeperSeams } from '../remoteKeeper';
import { hostKeeper } from '../remoteKeeper';
import type { PermissionRow } from '../remoteKeeperDoor';
import { WITHDRAWN_READ_MAX, nameStamp } from '../remotePermission';
import { STATE_KEY, permissionRowOf, stateMerge } from '../remoteAccountsRound';
import { Admission, admissionOf, commit } from '../remoteHosts';
import type { RefusalsDisk } from '../remoteRefusals';
import { refusalsStore } from '../remoteRefusalsStore';
import type { KeyValueStore } from '../sideSettings';

/**
 * The fakes of the hosts store tests (E1.S1): one `globalState` shared by several windows (`Backing` and `view`), one side's folder
 * (`FakeDisk`, which records every call by its name and argument), and a window that is a keeper over both. A view that was not synced is STALE; in `'whole-state'` mode an update writes
 * every key as that view holds it — what the record of this repository says an editor does.
 */

export const KEY = 'coai.terminalHosts';
export const START = Date.parse('2026-11-24T10:00:00.000Z');

export class Backing {
  readonly values = new Map<string, unknown>();
}

export interface View extends KeyValueStore {
  readonly sync: () => void;
  /** One entry for each `update`: the key, and whether it was called in the step of a `get`. */
  readonly updates: { readonly key: string; readonly inStep: boolean }[];
}

export function view(backing: Backing, mode: 'one-key' | 'whole-state' = 'one-key'): View {
  let copy = new Map(backing.values);
  let stepOpen = false;
  /** The key read in the open step: an update is "in the step of its read" only for that key. */
  let stepKey: string | undefined;
  const updates: { key: string; inStep: boolean }[] = [];
  const sync = (): void => { copy = new Map(backing.values); };

  return {
    sync,
    updates,
    get: <T>(key: string): T | undefined => {
      stepOpen = true;
      stepKey = key;
      void Promise.resolve().then(() => { stepOpen = false; stepKey = undefined; });

      return copy.get(key) as T | undefined;
    },
    update: async (key, value) => {
      updates.push({ key, inStep: stepOpen && stepKey === key });
      if (value === undefined) {
        copy.delete(key);
      } else {
        copy.set(key, value);
      }
      if (mode === 'one-key') {
        backing.values.set(key, value);
      } else {
        backing.values.clear();
        for (const [name, held] of copy) {
          backing.values.set(name, held);
        }
      }
    },
  };
}

/** One side's folder, in memory, with the switches the tests flip. */
export class FakeDisk implements RefusalsDisk {
  readonly files = new Map<string, string>();
  lock: { token: string } | undefined;
  /** Whether a lock that is held can be taken over: the owner is alive, so it cannot. */
  ownerAlive = true;
  now = START;
  failReplace = false;
  failMark = false;
  failMarks = false;
  failReads = 0;
  replaces = 0;
  claims: ('person' | undefined)[] = [];
  /** Every call of the disk by its name and argument, in order: `withdrawn:<name>`, `unwithdraw:<name>`, `replace`, `sweep`, `unmark:<name>`, `lockNow`, `claim:person`, `claim`, `row`. */
  calls: string[] = [];
  /** The lock as `lockNow` says it, when a test sets it; else it follows `lock` and `ownerAlive`. */
  lockSays: 'free' | 'held' | 'gone' | undefined;
  failWithdrawn = false;
  failUnwithdraw = false;
  failWithdrawals = false;
  /** Names whose next reads in the listing give no text (as a file seen empty): the count of reads left. */
  blind = new Map<string, number>();
  /** Called after each listing of the withdrawal files, with its number from 1: a test adds or removes a file between two listings. */
  afterListing: ((n: number) => void) | undefined;
  listings = 0;
  /** Called at every look at the lock, before the answer, with its number from 1. */
  beforeLockNow: ((n: number) => void) | undefined;
  /** Called after a withdrawal file was made. */
  afterWithdrawn: ((name: string) => void) | undefined;
  private looks = 0;
  /** Awaited just before the file is replaced: a test holds a writer here. */
  beforeReplace: (() => Promise<void>) | undefined;
  private sequence = 0;

  read: RefusalsDisk['read'] = async () => {
    if (this.failReads > 0) {
      this.failReads -= 1;

      return { kind: 'failed' };
    }
    const text = this.files.get('refused.json');

    return text === undefined ? { kind: 'absent' } : { kind: 'text', text };
  };

  claim: RefusalsDisk['claim'] = async (_doing, take) => {
    this.claims.push(take);
    this.calls.push(take === 'person' ? 'claim:person' : 'claim');
    if (this.lock !== undefined && take !== 'person' && this.ownerAlive) {
      return { kind: 'held' };
    }
    this.sequence += 1;
    const token = `t${this.sequence}`;
    this.lock = { token };

    return { kind: 'claimed', token, path: 'lock', release: async () => { if (this.lock?.token === token) { this.lock = undefined; } } } satisfies Claim;
  };

  stillOurs: RefusalsDisk['stillOurs'] = async (claim) => this.lock?.token === claim.token;

  replace: RefusalsDisk['replace'] = async (text) => {
    await this.beforeReplace?.();
    this.calls.push('replace');
    this.replaces += 1;
    if (this.failReplace) {
      throw new Error('the disk is full');
    }
    this.files.set('refused.json', text);
  };

  /** Awaited at the sweep, which comes just before the writer looks at its claim (step 5): a test holds a writer here. */
  beforeSweep: (() => Promise<void>) | undefined;

  sweep: RefusalsDisk['sweep'] = async () => { this.calls.push('sweep'); await this.beforeSweep?.(); };

  mark: RefusalsDisk['mark'] = async (text) => {
    const markers = [...this.files.keys()].filter((name) => name.startsWith('unsure-'));
    if (this.failMark || markers.length >= 32) {
      return undefined;
    }
    let name: string;
    do {
      this.sequence += 1;
      name = `unsure-${this.sequence}.json`;
    } while (this.files.has(name));
    this.files.set(name, text);

    return name;
  };

  marks: RefusalsDisk['marks'] = async () =>
    (this.failMarks ? 'failed' : [...this.files].filter(([name]) => name.startsWith('unsure-')).map(([name, text]) => ({ name, text })));

  unmark: RefusalsDisk['unmark'] = async (name) => { this.calls.push(`unmark:${name}`); this.files.delete(name); };

  withdrawnPrefix = 'withdrawn-';

  withdrawals: RefusalsDisk['withdrawals'] = async () => {
    this.listings += 1;
    this.calls.push('withdrawals');
    const n = this.listings;
    if (this.failWithdrawals) {
      this.afterListing?.(n);

      return 'failed';
    }
    const names = [...this.files.keys()].filter((name) => name.startsWith(this.withdrawnPrefix)).sort((a, b) => {
      const [x, y] = [nameStamp(this.withdrawnPrefix, a), nameStamp(this.withdrawnPrefix, b)];

      return x !== undefined && y !== undefined ? y - x : x !== undefined ? -1 : y !== undefined ? 1 : Number(a > b) - Number(a < b);
    });
    const seen = names.slice(0, WITHDRAWN_READ_MAX).map((name) => {
      const left = this.blind.get(name) ?? 0;
      if (left > 0) {
        this.blind.set(name, left - 1);

        return { name, text: undefined };
      }

      return { name, text: this.files.get(name) };
    });
    const result = { seen, rest: names.slice(WITHDRAWN_READ_MAX) };
    this.afterListing?.(n);

    return result;
  };

  withdrawn: RefusalsDisk['withdrawn'] = async (name, text) => {
    this.calls.push(`withdrawn:${name}`);
    if (this.failWithdrawn) {
      return 'refused';
    }
    if (this.files.has(name)) {
      return 'there';
    }
    this.files.set(name, text);
    this.afterWithdrawn?.(name);

    return 'made';
  };

  unwithdraw: RefusalsDisk['unwithdraw'] = async (name) => {
    this.calls.push(`unwithdraw:${name}`);
    if (this.failUnwithdraw) {
      return false;
    }
    this.files.delete(name);

    return true;
  };

  lockNow: RefusalsDisk['lockNow'] = async () => {
    this.calls.push('lockNow');
    this.looks += 1;
    this.beforeLockNow?.(this.looks);
    if (this.lockSays !== undefined) {
      return this.lockSays;
    }

    return this.lock === undefined ? 'free' : this.ownerAlive ? 'held' : 'gone';
  };

  /** The names of the withdrawal files on the fake folder, in code-unit order. */
  withdrawnNames = (): readonly string[] => [...this.files.keys()].filter((name) => name.startsWith(this.withdrawnPrefix)).sort();

  /** The calls whose name starts with `prefix`. */
  called = (prefix: string): readonly string[] => this.calls.filter((one) => one.startsWith(prefix));

  markers = (): readonly string[] => [...this.files.keys()].filter((name) => name.startsWith('unsure-'));

  file = (): { readonly gen: number; readonly by: string; readonly refused: readonly { readonly host: string; readonly utc: string }[]; readonly stop?: { readonly utc: string } } | undefined => {
    const text = this.files.get('refused.json');

    return text === undefined ? undefined : JSON.parse(text);
  };

  refusedHosts = (): readonly string[] => (this.file()?.refused ?? []).map((one) => one.host).sort(byCodeUnit);
}

/** The pause between tries, as the tests have it: a macrotask, so the other writers of one test run through their steps meanwhile. */
export const instant = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

export interface Window {
  readonly view: View;
  readonly keeper: HostKeeper;
  readonly unkept: { readonly host: string | undefined; readonly why: string; readonly marked: boolean }[];
  /** The notices of a withdrawal the disk did not take, by their reason. */
  readonly unkeptWithdrawal: string[];
  readonly changed: { count: number };
}

/** The state row's seam over a window's view, as the host half binds it over `globalState`; every change is recorded in the disk's calls as `row`. */
export function rowOver(v: View, disk: FakeDisk): PermissionRow {
  return {
    read: () => {
      const read = permissionRowOf(v.get(STATE_KEY));

      return { withdrawn: read.withdrawn, stopped: read.stopped };
    },
    change: (change) => { disk.calls.push('row'); return commit(v, STATE_KEY, change, stateMerge).then(() => undefined); },
  };
}

/** One window: a keeper over a view and a disk. A "reload" is a new window over the same backing and the same disk. */
export function windowOver(backing: Backing, disk: FakeDisk, options: { mode?: 'one-key' | 'whole-state' } = {}): Window {
  const v = view(backing, options.mode ?? 'one-key');
  const unkept: Window['unkept'][number][] = [];
  const unkeptWithdrawal: string[] = [];
  const changed = { count: 0 };
  const seams: KeeperSeams = {
    state: v,
    key: KEY,
    refusals: refusalsStore(disk, () => disk.now, instant),
    now: () => new Date(disk.now).toISOString(),
    row: rowOver(v, disk),
    changed: () => { changed.count += 1; },
    unkept: (host, why, marked) => { unkept.push({ host, why, marked }); },
    unkeptWithdrawal: (why) => { unkeptWithdrawal.push(why); },
  };

  return { view: v, keeper: hostKeeper(seams), unkept, unkeptWithdrawal, changed };
}

/** The session door as E1's `consent` read it, for the tests that hold a refusal rule: `no-permission` is `ask`, and `waiting` is `damaged` (E1's T8 answer, which E2.S1 gives its own sentence). */
export async function consent(w: Window, host: string): Promise<'yes' | 'no' | 'ask' | 'damaged'> {
  const answer = await w.keeper.may('session', host);

  return answer === 'yes' ? 'yes' : answer === 'no' ? 'no' : answer === 'no-permission' ? 'ask' : 'damaged';
}

/** The door as the round's old view reads it: `may('round', host)` mapped by `admissionOf`. */
export const admit = (w: Window, host: string): Promise<Admission> => w.keeper.may('round', host).then(admissionOf);

export const row = (host: string, answer: 'yes' | 'no', askedUtc = '2026-11-01T00:00:00.000Z', how?: 'auto'): Record<string, unknown> =>
  ({ host, answer, askedUtc, ...(how === undefined ? {} : { how }) });
