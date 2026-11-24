import { byCodeUnit } from '../codeUnitOrder';
import type { Claim } from '../chatStoreLock';
import type { HostKeeper, KeeperSeams } from '../remoteKeeper';
import { hostKeeper } from '../remoteKeeper';
import type { RefusalsDisk } from '../remoteRefusals';
import { refusalsStore } from '../remoteRefusals';
import type { KeyValueStore } from '../sideSettings';

/**
 * The fakes of the hosts store tests (E1.S1): one `globalState` shared by several windows (`Backing` and `view`), one side's folder
 * (`FakeDisk`), and a window that is a keeper over both. A view that was not synced is STALE; in `'whole-state'` mode an update writes
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
  const updates: { key: string; inStep: boolean }[] = [];
  const sync = (): void => { copy = new Map(backing.values); };

  return {
    sync,
    updates,
    get: <T>(key: string): T | undefined => {
      stepOpen = true;
      void Promise.resolve().then(() => { stepOpen = false; });

      return copy.get(key) as T | undefined;
    },
    update: async (key, value) => {
      updates.push({ key, inStep: stepOpen });
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
    this.replaces += 1;
    if (this.failReplace) {
      throw new Error('the disk is full');
    }
    this.files.set('refused.json', text);
  };

  /** Awaited at the sweep, which comes just before the writer looks at its claim (step 5): a test holds a writer here. */
  beforeSweep: (() => Promise<void>) | undefined;

  sweep: RefusalsDisk['sweep'] = async () => { await this.beforeSweep?.(); };

  mark: RefusalsDisk['mark'] = async (text) => {
    const markers = [...this.files.keys()].filter((name) => name.startsWith('unsure-'));
    if (this.failMark || markers.length >= 32) {
      return undefined;
    }
    this.sequence += 1;
    const name = `unsure-${this.sequence}.json`;
    this.files.set(name, text);

    return name;
  };

  marks: RefusalsDisk['marks'] = async () =>
    (this.failMarks ? 'failed' : [...this.files].filter(([name]) => name.startsWith('unsure-')).map(([name, text]) => ({ name, text })));

  unmark: RefusalsDisk['unmark'] = async (name) => { this.files.delete(name); };

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
  readonly setting: { on: boolean };
  readonly changed: { count: number };
}

/** One window: a keeper over a view and a disk. A "reload" is a new window over the same backing and the same disk. */
export function windowOver(backing: Backing, disk: FakeDisk, options: { mode?: 'one-key' | 'whole-state'; on?: boolean } = {}): Window {
  const v = view(backing, options.mode ?? 'one-key');
  const unkept: Window['unkept'][number][] = [];
  const setting = { on: options.on ?? true };
  const changed = { count: 0 };
  const seams: KeeperSeams = {
    state: v,
    key: KEY,
    refusals: refusalsStore(disk, () => disk.now, instant),
    now: () => new Date(disk.now).toISOString(),
    allowNew: () => setting.on,
    changed: () => { changed.count += 1; },
    unkept: (host, why, marked) => { unkept.push({ host, why, marked }); },
  };

  return { view: v, keeper: hostKeeper(seams), unkept, setting, changed };
}

export const row = (host: string, answer: 'yes' | 'no', askedUtc = '2026-11-01T00:00:00.000Z', how?: 'auto'): Record<string, unknown> =>
  ({ host, answer, askedUtc, ...(how === undefined ? {} : { how }) });
