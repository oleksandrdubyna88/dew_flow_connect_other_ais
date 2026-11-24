import { Admission, Committed, Consent, HOSTS_MAX, HostAnswer, HostChange, HostStanding, Merge, commit, consentOf, hostMerge, readableOf, standingsOf } from './remoteHosts';
import type { RefusalChange, RefusalsFile, RefusalsRead, RefusalsStore, Unsure, Waiting } from './remoteRefusals';
import type { KeyValueStore } from './sideSettings';

/**
 * The keeper (E1.S1, § S1.5): one for each window, the bound form of the list (`remoteHosts.ts`) and the refusals file
 * (`remoteRefusals.ts`). Every decision reads the file from the disk; the keeper answers from no file it kept between two of them.
 * What it remembers of the newest file it has read is used for one thing only: to add back what a stale writer removed.
 *
 * <p>Every operation of one window goes through one queue, so no decision of a window runs in the middle of that window's own
 * change. The one exception is the copy of a "no" in the list, which is written in the step of the press, before the queue is awaited.
 * A change the file did not take is held in this window and marked in a file of the folder, so every window of the side stops.</p>
 */

export interface KeeperSeams {
  /** The profile's `globalState`. */
  readonly state: KeyValueStore;
  /** The `globalState` key of the list of hosts, handed in by the host file. */
  readonly key: string;
  /** The file of THIS side. */
  readonly refusals: RefusalsStore;
  /** UTC ISO. */
  readonly now: () => string;
  /** `coai.terminalAccountsAllowNewHosts`, read afresh at every call. */
  readonly allowNew: () => boolean;
  /** After every commit that wrote. */
  readonly changed: () => void;
  /** The notice of a refusal or Stop the file did not take. `host` `undefined` is the Stop. `marked` false: no marker could be made. */
  readonly unkept: (host: string | undefined, why: 'busy' | 'disk' | 'lost' | 'elsewhere', marked: boolean) => void;
}

export interface HostKeeper {
  readonly stored: () => unknown;
  readonly commit: (change: HostChange) => Promise<Committed>;
  readonly commitKey: <C, R>(key: string, change: C, merge: Merge<C, R>) => Promise<R>;
  readonly admit: (host: string) => Promise<Admission>;
  readonly consent: (host: string) => Promise<Consent>;
  /** The Stop, in the file. */
  readonly stopRound: () => Promise<Committed>;
  readonly startRound: () => Promise<Committed>;
  readonly roundStop: () => Promise<'stopped' | 'running' | 'damaged'>;
  /** Each host's standing, from the file and the list read NOW. */
  readonly standings: () => Promise<readonly HostStanding[]>;
  /** How many refusals, the Stop among them, wait on this side. */
  readonly held: () => Promise<number>;
  /** A PERSON'S PRESS ONLY: takes the lock over, lands what waits, removes torn markers and then stops the round. */
  readonly saveWaiting: () => Promise<Committed>;
  /** Whether the last `saveWaiting` removed a marker that could not be read (W9's third cure). */
  readonly savedTorn: () => boolean;
  /** TEST ONLY, through the test door: empties what this window holds as not saved and what it remembers of the file. */
  readonly forgetHeld: () => void;
}

/** A refusal or the Stop that the file did not take, held by this window. */
interface Held {
  readonly kind: 'refuse' | 'stop';
  readonly host: string;
  readonly utc: string;
}

const keyOf = (one: { readonly kind: 'refuse' | 'stop'; readonly host?: string }): string => (one.kind === 'stop' ? 'stop' : `refuse:${one.host}`);

const changeOf = (one: Held): RefusalChange => (one.kind === 'stop' ? { kind: 'stop', utc: one.utc } : { kind: 'refuse', host: one.host, utc: one.utc });

const isLanded = (result: Committed): boolean => result.kind === 'written' || result.kind === 'kept' || result.kind === 'full';

const validUtc = (utc: string, fallback: string): string => (Number.isNaN(Date.parse(utc)) ? fallback : utc);

/** Whether a marker's change is in the file just read. A torn marker never is. */
function inFile(one: Waiting, file: RefusalsFile): boolean {
  if (one.kind === 'torn') {
    return false;
  }

  return one.kind === 'stop' ? file.stop !== undefined : file.refused.some((entry) => entry.host === one.host);
}

const asHeld = (one: Waiting): Held | undefined => {
  if (one.kind === 'torn') {
    return undefined;
  }

  return one.kind === 'stop' ? { kind: 'stop', host: '', utc: one.utc } : { kind: 'refuse', host: one.host, utc: one.utc };
};

const regainOf = (had: RefusalsFile): RefusalChange =>
  ({ kind: 'regain', gen: had.gen, rows: had.refused, ...(had.stop === undefined ? {} : { stop: had.stop }) });

const heldOfFile = (had: RefusalsFile): readonly Held[] => [
  ...had.refused.map((one): Held => ({ kind: 'refuse', host: one.host, utc: one.utc })),
  ...(had.stop === undefined ? [] : [{ kind: 'stop', host: '', utc: had.stop.utc } satisfies Held]),
];

const unsureOf = (item: Held): Unsure => (item.kind === 'stop' ? { kind: 'stop', utc: item.utc } : { kind: 'refuse', host: item.host, utc: item.utc });

const heldFromMarker = (one: Waiting): readonly Held[] => {
  const held = asHeld(one);

  return held === undefined ? [] : [held];
};

const markersOf = (read: RefusalsRead, key: string): readonly Waiting[] =>
  (read.kind === 'file' ? read.waiting.filter((one) => heldFromMarker(one).some((held) => keyOf(held) === key)) : []);

const missingNo = (file: RefusalsFile, rows: readonly HostAnswer[], now: string): readonly { readonly host: string; readonly utc: string }[] => {
  const have = new Set(file.refused.map((one) => one.host));

  return rows.filter((row) => row.answer === 'no' && !have.has(row.host)).map((row) => ({ host: row.host, utc: validUtc(row.askedUtc, now) }));
};

const refusalOfMarker = (one: Waiting): readonly { readonly host: string; readonly utc: string }[] => (one.kind === 'refuse' ? [{ host: one.host, utc: one.utc }] : []);

const NOT_READ: Readonly<Record<Exclude<Consent, 'yes'>, Admission>> = { no: { kind: 'refused' }, damaged: { kind: 'damaged' }, ask: { kind: 'not-asked' } };

/** Nothing was done: the list or the file did not need a mend. */
const NOTHING: Committed = { kind: 'kept', row: undefined };

const stops = (result: Committed): boolean => result.kind !== 'written' && result.kind !== 'kept';

const isFullFor = (known: RefusalsFile | undefined, host: string): boolean =>
  known !== undefined && known.refused.length >= HOSTS_MAX && !known.refused.some((one) => one.host === host);

const rowOf = (done: Committed): HostAnswer | undefined => (done.kind === 'written' || done.kind === 'kept' ? done.row : undefined);

const answerAfterFile = (result: Committed, copy: Committed): Committed =>
  (result.kind === 'full' ? result : copy.kind === 'damaged' ? { kind: 'written', row: undefined, evicted: [] } : copy);

class Keeper implements HostKeeper {
  private tail: Promise<unknown> = Promise.resolve();
  /** What this window holds as not saved. */
  private readonly memory = new Map<string, { readonly item: Held; marked: boolean }>();
  /** The newest file this window has read. */
  private remembered: RefusalsFile | undefined;
  private readonly told = new Set<string>();
  private tornRemoved = false;

  constructor(private readonly seams: KeeperSeams) {}

  // ───────────── the queue ─────────────

  private queued<T>(work: () => Promise<T>): Promise<T> {
    const run = this.tail.then(work, work);
    this.tail = run.catch(() => undefined);

    return run;
  }

  stored = (): unknown => this.seams.state.get<unknown>(this.seams.key);

  commitKey = <C, R>(key: string, change: C, merge: Merge<C, R>): Promise<R> => commit(this.seams.state, key, change, merge);

// ───────────── reading the side ─────────────

  private wentBack(file: RefusalsFile): boolean {
    const had = this.remembered;

    return had !== undefined && (file.gen < had.gen || (file.gen === had.gen && file.by !== had.by));
  }

  private remember(file: RefusalsFile): void {
    if (this.remembered === undefined || file.gen > this.remembered.gen) {
      this.remembered = file;
    }
  }

  /** The file and the markers, read now. A file that went back is written forward first (the union), and a held regain is marked. */
  private async look(): Promise<RefusalsRead> {
    const read = await this.regained(await this.seams.refusals.read());
    if (read.kind === 'file') {
      this.remember(read.file);
    }

    return read;
  }

  private async regained(read: RefusalsRead): Promise<RefusalsRead> {
    const had = this.remembered;
    if (read.kind !== 'file' || had === undefined || !this.wentBack(read.file)) {
      return read;
    }
    await this.holdIf(await this.seams.refusals.commit(regainOf(had)), had);

    return this.seams.refusals.read();
  }

  private async holdIf(result: Committed, had: RefusalsFile): Promise<void> {
    if (result.kind !== 'unwritten') {
      return;
    }
    for (const item of heldOfFile(had)) {
      await this.hold(item, result.why);
    }
  }

  /** Holds one change in this window's memory, marks it for the side, and says so once. */
  private async hold(item: Held, why: 'busy' | 'disk' | 'lost'): Promise<void> {
    const key = keyOf(item);
    if (this.memory.has(key)) {
      return;
    }
    const marked = await this.seams.refusals.mark(unsureOf(item));
    this.memory.set(key, { item, marked });
    this.told.add(key);
    this.seams.unkept(item.kind === 'stop' ? undefined : item.host, why, marked);
  }

  private waiting(read: RefusalsRead): readonly Waiting[] {
    return read.kind === 'file' ? read.waiting.filter((one) => one.kind === 'torn' || !inFile(one, read.file)) : [];
  }

  private anyWaiting(read: RefusalsRead): boolean {
    return this.memory.size > 0 || this.waiting(read).length > 0;
  }

  private async unmarkAll(read: RefusalsRead, key: string): Promise<void> {
    for (const one of markersOf(read, key)) {
      await this.seams.refusals.unmark(one.name);
    }
  }

  /** The changes that wait: this window's memory, and every marker whose change is not in the file yet. */
  private todo(read: RefusalsRead): ReadonlyMap<string, Held> {
    const todo = new Map<string, Held>([...this.memory].map(([key, value]) => [key, value.item]));
    for (const item of this.waiting(read).flatMap(heldFromMarker)) {
      todo.set(keyOf(item), todo.get(keyOf(item)) ?? item);
    }

    return todo;
  }

  /** A marker whose change is already in the file stops nothing, and is removed. */
  private async dropLanded(read: RefusalsRead): Promise<void> {
    for (const one of read.kind === 'file' ? read.waiting.filter((marker) => inFile(marker, read.file)) : []) {
      await this.seams.refusals.unmark(one.name);
    }
  }

  /** Tries every change that waits through the ordinary commit of the file. A change that lands leaves the memory and its markers. */
  private async landAll(read: RefusalsRead, take?: 'person'): Promise<Committed | undefined> {
    await this.dropLanded(read);
    let last: Committed | undefined;
    for (const [key, item] of this.todo(read)) {
      last = (await this.settle(key, item, await this.seams.refusals.commit(changeOf(item), take), read)) ?? last;
    }

    return last;
  }

  /** `undefined` when the change landed; else the result that did not, after the notice. */
  private async settle(key: string, item: Held, result: Committed, read: RefusalsRead): Promise<Committed | undefined> {
    if (!isLanded(result)) {
      this.noticeOnce(key, item, result);

      return result;
    }
    this.memory.delete(key);
    await this.unmarkAll(read, key);

    return undefined;
  }

  /** A change that waits and was made in ANOTHER window is told once here, as `elsewhere`. */
  private noticeOnce(key: string, item: Held, result: Committed): void {
    if (!this.told.has(key) && result.kind === 'unwritten') {
      this.told.add(key);
      this.seams.unkept(item.kind === 'stop' ? undefined : item.host, 'elsewhere', true);
    }
  }

  /** The copy-in: the readable "no" rows of the list that the file does not hold, in one write. Adds only. */
  private async copyIn(read: RefusalsRead): Promise<RefusalsRead> {
    const list = readableOf(this.stored());
    if (read.kind !== 'file' || list.damaged) {
      return read;
    }
    const now = this.seams.now();
    const missing = missingNo(read.file, list.rows, now);
    if (missing.length === 0) {
      return read;
    }
    await this.holdMigrated(await this.seams.refusals.commit({ kind: 'migrate', rows: missing, utc: now }), missing);

    return this.look();
  }

  private async holdMigrated(result: Committed, missing: readonly { readonly host: string; readonly utc: string }[]): Promise<void> {
    if (result.kind !== 'unwritten') {
      return;
    }
    for (const one of missing) {
      await this.hold({ kind: 'refuse', host: one.host, utc: one.utc }, result.why);
    }
  }

  /** The side as one decision needs it: read, with what waits landed, and the copy-in done. */
  private async sync(): Promise<RefusalsRead> {
    let read = await this.look();
    await this.dropLanded(read);
    if (this.anyWaiting(read)) {
      await this.landAll(read);
      read = await this.look();
    }

    return this.anyWaiting(read) ? read : this.copyIn(read);
  }

  private namedByWaiting(read: RefusalsRead, host: string): boolean {
    return [...this.memory.values()].some((one) => one.item.kind === 'refuse' && one.item.host === host)
      || this.waiting(read).some((one) => one.kind === 'refuse' && one.host === host);
  }

  // ───────────── the doors ─────────────

  admit(host: string): Promise<Admission> {
    return this.queued(async () => {
      const read = await this.sync();

      return this.anyWaiting(read) ? { kind: 'unwritten' } : this.decide(read, host);
    });
  }

  private async decide(read: RefusalsRead, host: string): Promise<Admission> {
    const consent = consentOf(read, this.stored(), host, this.seams.allowNew());

    return consent === 'ask' && this.seams.allowNew() ? this.automatic(host) : this.answered(consent, host, false);
  }

  private howOf = (host: string): 'person' | 'auto' => standingsOf(this.stored()).find((one) => one.host === host)?.how ?? 'person';

  private answered(consent: Consent, host: string, allowedNow: boolean): Admission {
    return consent === 'yes' ? { kind: 'read', how: this.howOf(host), allowedNow } : NOT_READ[consent];
  }

  /** An automatic "yes", written before the call can be spawned; then the file and the list are read once more. */
  private async automatic(host: string): Promise<Admission> {
    const result = await commit(this.seams.state, this.seams.key, { kind: 'auto', host } satisfies HostChange, hostMerge(this.seams.now()));
    if (result.kind === 'full' || result.kind === 'damaged') {
      return result;
    }

    return this.afterAuto(host, this.written(result));
  }

  private async afterAuto(host: string, result: Committed): Promise<Admission> {
    const read = await this.look();

    return this.anyWaiting(read) ? { kind: 'unwritten' } : this.answered(consentOf(read, this.stored(), host, this.seams.allowNew()), host, result.kind === 'written');
  }

  consent(host: string): Promise<Consent> {
    return this.queued(async () => {
      const read = await this.sync();

      return this.anyWaiting(read) ? this.waitingConsent(read, host) : consentOf(read, this.stored(), host, this.seams.allowNew());
    });
  }

  private waitingConsent = (read: RefusalsRead, host: string): Consent => (this.namedByWaiting(read, host) ? 'no' : 'damaged');

  // ───────────── the changes ─────────────

  commit(change: HostChange): Promise<Committed> {
    return change.kind === 'person' && change.answer === 'no' ? this.refuse(change.host, change) : this.queued(() => this.apply(change));
  }

  /** A person's "no": the copy in the list FIRST, in this step, and the file after it. */
  private refuse(host: string, change: HostChange): Promise<Committed> {
    if (isFullFor(this.remembered, host)) {
      return Promise.resolve({ kind: 'full' });
    }
    const copy = commit(this.seams.state, this.seams.key, change, hostMerge(this.seams.now()));

    return this.queued(() => this.refuseAfterCopy(host, copy));
  }

  private async refuseAfterCopy(host: string, copy: Promise<Committed>): Promise<Committed> {
    const done = await copy;
    if (done.kind === 'full') {
      return done;
    }
    const utc = rowOf(done)?.askedUtc ?? this.seams.now();
    const result = await this.seams.refusals.commit({ kind: 'refuse', host, utc });
    this.seams.changed();

    return this.afterFile(result, { kind: 'refuse', host, utc }, done);
  }

  private async afterFile(result: Committed, item: Held, copy: Committed): Promise<Committed> {
    if (result.kind === 'unwritten' || result.kind === 'damaged') {
      return this.holdAndAnswer(result, item);
    }
    await this.look();

    return answerAfterFile(result, copy);
  }

  private async holdAndAnswer(result: Committed, item: Held): Promise<Committed> {
    const why = result.kind === 'unwritten' ? result.why : 'disk';
    await this.hold(item, why);

    return { kind: 'unwritten', why };
  }

  private async apply(change: HostChange): Promise<Committed> {
    return change.kind === 'auto' ? this.plain(change) : change.kind === 'mend' ? this.mend(change) : this.lifted(change);
  }

  /** The list's own merge for this change, and the change signal when it wrote. */
  private async plain(change: HostChange): Promise<Committed> {
    return this.written(await commit(this.seams.state, this.seams.key, change, hostMerge(this.seams.now())));
  }

  private written(result: Committed): Committed {
    if (result.kind === 'written') {
      this.seams.changed();
    }

    return result;
  }

  /** A *Read* or a *Forget*: the file first (the host's markers and held entry are dropped, then `lift`), the list only when the file took it. */
  private async lifted(change: HostChange): Promise<Committed> {
    const read = await this.sync();
    const key = keyOf({ kind: 'refuse', host: change.host });
    this.memory.delete(key);
    await this.unmarkAll(read, key);
    const lift = await this.seams.refusals.commit({ kind: 'lift', host: change.host });
    if (lift.kind === 'unwritten' || lift.kind === 'damaged') {
      return lift;
    }
    await this.look();

    return this.plain(change);
  }

  /** A person's answer over a damaged list or file: the list first, then the file from the mended list. */
  private async mend(change: Extract<HostChange, { kind: 'mend' }>): Promise<Committed> {
    const read = await this.look();

    return !readableOf(this.stored()).damaged && read.kind === 'file' ? this.lifted(change) : this.mendDamage(change, read);
  }

  private async mendDamage(change: Extract<HostChange, { kind: 'mend' }>, read: RefusalsRead): Promise<Committed> {
    const listResult = await this.mendList(change);
    if (stops(listResult)) {
      return listResult;
    }
    const fileResult = await this.mendFile(change, read);
    if (stops(fileResult)) {
      return fileResult;
    }

    return listResult === NOTHING ? this.plain(change) : listResult;
  }

  /** `NOTHING` when the list can be read. */
  private async mendList(change: HostChange): Promise<Committed> {
    return readableOf(this.stored()).damaged ? this.plain(change) : NOTHING;
  }

  /** `NOTHING` when the file can be read; else the new file, written from the mended list. */
  private async mendFile(change: Extract<HostChange, { kind: 'mend' }>, read: RefusalsRead): Promise<Committed> {
    if (read.kind !== 'damaged') {
      return NOTHING;
    }
    const rows = this.mendRows(change);
    const made = await this.seams.refusals.commit({ kind: 'mend', gen: this.remembered?.gen ?? 0, rows, utc: this.seams.now() });
    await this.look();

    return made;
  }

  /** The refusals a new file starts with: the readable "no" rows of the list, what this window remembers, and the answer when it is a "no". */
  private mendRows(change: Extract<HostChange, { kind: 'mend' }>): readonly { readonly host: string; readonly utc: string }[] {
    const now = this.seams.now();
    const rows = readableOf(this.stored()).rows.filter((row) => row.answer === 'no').map((row) => ({ host: row.host, utc: validUtc(row.askedUtc, now) }));
    const held = [...this.memory.values()].filter((one) => one.item.kind === 'refuse').map((one) => ({ host: one.item.host, utc: one.item.utc }));
    const answer = change.answer === 'no' ? [{ host: change.host, utc: now }] : [];

    return [...rows, ...(this.remembered?.refused ?? []), ...held, ...answer].filter((one) => change.answer === 'no' || one.host !== change.host);
  }

  // ───────────── the Stop ─────────────

  stopRound(): Promise<Committed> {
    return this.queued(async () => {
      const utc = this.seams.now();
      const result = await this.seams.refusals.commit({ kind: 'stop', utc });
      const held = { kind: 'stop' as const, host: '', utc };

      return result.kind === 'unwritten' || result.kind === 'damaged' ? this.afterFile(result, held, result) : result;
    });
  }

  startRound(): Promise<Committed> {
    return this.queued(async () => {
      const read = await this.look();
      this.memory.delete('stop');
      await this.unmarkAll(read, 'stop');

      return this.seams.refusals.commit({ kind: 'start' });
    });
  }

  roundStop(): Promise<'stopped' | 'running' | 'damaged'> {
    return this.queued(async () => {
      const read = await this.sync();

      return read.kind === 'damaged' ? 'damaged' : this.stoppedIn(read) ? 'stopped' : 'running';
    });
  }

  /** The Stop is in the file, in this window's memory, or in a marker. */
  private stoppedIn(read: Extract<RefusalsRead, { kind: 'file' }>): boolean {
    return read.file.stop !== undefined || this.memory.has('stop') || read.waiting.some((one) => one.kind === 'stop');
  }

  // ───────────── what is shown ─────────────

  standings(): Promise<readonly HostStanding[]> {
    return this.queued(async () => {
      const read = await this.sync();

      return this.merged(standingsOf(this.stored()), this.refusedNow(read));
    });
  }

  /** Every refusal of this side right now: the file's, this window's memory, and the markers'. */
  private refusedNow(read: RefusalsRead): readonly { readonly host: string; readonly utc: string }[] {
    const memory = [...this.memory.values()].map((one) => one.item).filter((item) => item.kind === 'refuse');

    return [...(read.kind === 'file' ? read.file.refused : []), ...memory.map((item) => ({ host: item.host, utc: item.utc })), ...this.waiting(read).flatMap(refusalOfMarker)];
  }

  private merged(rows: readonly HostStanding[], refused: readonly { readonly host: string; readonly utc: string }[]): readonly HostStanding[] {
    const refusedHosts = new Map(refused.map((one) => [one.host, one.utc]));
    const rest = rows.filter((one) => !refusedHosts.has(one.host)).map((one) => one);
    const asNo = [...refusedHosts].map(([host, utc]): HostStanding => {
      const row = rows.find((one) => one.host === host && one.answer === 'no');

      return { host, answer: 'no', how: 'person', askedUtc: row?.askedUtc ?? utc };
    });

    return [...rest, ...asNo].sort((a, b) => Number(a.host > b.host) - Number(a.host < b.host));
  }

  held(): Promise<number> {
    return this.queued(async () => {
      const read = await this.look();
      const keys = new Set(this.memory.keys());
      for (const one of this.waiting(read)) {
        const held = asHeld(one);
        keys.add(held === undefined ? `torn:${one.name}` : keyOf(held));
      }

      return keys.size;
    });
  }

  /** A person's press: takes the lock over whatever its owner, lands every change that waits and stops the round after a torn marker. */
  saveWaiting(): Promise<Committed> {
    return this.queued(async () => {
      const read = await this.look();
      const last = await this.landAll(read, 'person');
      const torn = this.waiting(read).filter((one) => one.kind === 'torn');
      if (last !== undefined) {
        return last;
      }
      for (const one of torn) {
        await this.seams.refusals.unmark(one.name);
      }
      this.tornRemoved = torn.length > 0;

      return torn.length > 0 ? this.seams.refusals.commit({ kind: 'stop', utc: this.seams.now() }, 'person') : { kind: 'written', row: undefined, evicted: [] };
    });
  }

  savedTorn = (): boolean => this.tornRemoved;

  forgetHeld = (): void => {
    this.memory.clear();
    this.told.clear();
    this.remembered = undefined;
  };
}

/** The keeper of one window. Its members are bound, so a caller may pass them on. */
export function hostKeeper(seams: KeeperSeams): HostKeeper {
  const keeper = new Keeper(seams);

  return {
    stored: keeper.stored,
    commit: (change) => keeper.commit(change),
    commitKey: keeper.commitKey,
    admit: (host) => keeper.admit(host),
    consent: (host) => keeper.consent(host),
    stopRound: () => keeper.stopRound(),
    startRound: () => keeper.startRound(),
    roundStop: () => keeper.roundStop(),
    standings: () => keeper.standings(),
    held: () => keeper.held(),
    saveWaiting: () => keeper.saveWaiting(),
    savedTorn: keeper.savedTorn,
    forgetHeld: keeper.forgetHeld,
  };
}
