import assert from 'node:assert/strict';
import { CatalogTurns } from '../catalogTurns';
import { overlayKey, Side } from '../coaiInstall';
import {
  CLAIM_STALE_MS,
  LayeredSetting,
  MigrationOutcome,
  MigrationPorts,
  MigrationRecord,
  MigrationStore,
  migrateOnExhausted,
  ON_EXHAUSTED_MIGRATION_KEY,
  recordOf,
  userLayerOf,
} from '../onExhaustedMigration';
import { FEATURES } from '../binaryFeatures';
import { inForceOf } from '../onExhaustedTold';
import { readOverlay, writeOverlay } from '../sideSettings';

/**
 * The doubles every E2.S2 migration test stands on (todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.8.0).
 *
 * <p>They are a READING of VS Code's source, not VS Code: `ClientState` follows `extHostMemento.ts` (V1–V3) and
 * `FakeUserSettings` follows `configurationEditing.ts` and `extHostConfiguration.ts` (V5, V6). `T29` pins what they
 * promise, and says what it cannot prove. Nothing here reads the real clock and nothing sleeps: a pause is a promise a
 * test resolves.</p>
 */

/** `vscode.ConfigurationTarget` — the values, because this file cannot import `vscode`. */
export const GLOBAL = 1;
export const WORKSPACE = 2;
export const FOLDER = 3;

/** A value as VS Code stores it: its JSON clone, an `undefined` member dropped. */
export function cloneOf<T>(value: T): T {
  return value === undefined ? value : (JSON.parse(JSON.stringify(value)) as T);
}

/** A promise that never settles: the host died after the call. */
function forever(): PromiseLike<void> {
  return new Promise<void>(() => undefined);
}

/** Lets every promise continuation already queued run, so a test sees where a run is waiting. */
export function flush(): Promise<void> {
  return new Promise((resolve) => {
    setImmediate(resolve);
  });
}

/** Runs the queue until `done` holds, bounded — never a sleep. */
export async function until(done: () => boolean, what: string): Promise<void> {
  for (let turn = 0; turn < 200 && !done(); turn += 1) {
    await flush();
  }
  assert.ok(done(), `never reached: ${what}`);
}

/** The fake clock the `now` port reads. */
export class FakeClock {
  private at = 1_700_000_000_000;

  readonly now = (): number => this.at;

  advance(ms: number): void {
    this.at += ms;
  }
}

/** One write a window made: the key and the value it handed over. */
export interface Write {
  readonly key: string;
  readonly value: unknown;
}

/**
 * One window's copy of the client's `globalState` (V1–V3). `update` changes THIS copy at once and then replaces the
 * client's whole value with it; another window's copy changes only when the test delivers.
 */
export class FakeWindow implements MigrationStore {
  private copy: Record<string, unknown>;

  /** Every write that landed from this window, in order. */
  readonly writes: Write[] = [];

  constructor(private readonly client: ClientState, taken: Record<string, unknown>) {
    this.copy = cloneOf(taken);
  }

  keys(): readonly string[] {
    return Object.keys(this.copy);
  }

  get<T = unknown>(key: string): T | undefined {
    return cloneOf(this.copy[key]) as T | undefined;
  }

  update(key: string, value: unknown): PromiseLike<void> {
    return this.client.write(this, key, value);
  }

  /** The whole copy, for a test's assertion. */
  snapshot(): Record<string, unknown> {
    return cloneOf(this.copy);
  }

  /** The client calls this: the write changes this window's own copy at once; `undefined` deletes. */
  applyOwn(key: string, value: unknown): void {
    if (value === undefined) {
      delete this.copy[key];
    } else {
      this.copy[key] = cloneOf(value);
    }
    this.writes.push({ key, value: cloneOf(value) });
  }

  /** The client calls this: the whole copy is replaced with what the client holds. */
  replaceWith(stored: Record<string, unknown>): void {
    this.copy = cloneOf(stored);
  }

  /** The client calls this: what this window sends. */
  sent(): Record<string, unknown> {
    return cloneOf(this.copy);
  }
}

/**
 * One profile's client store on one machine, and its windows (V1–V3). Two of these are two machines.
 *
 * <p>The send V1 schedules 0 ms later is modelled as made at once: a test that needs two writes of one tick has none.</p>
 */
export class ClientState {
  private stored: Record<string, unknown>;

  private readonly all: FakeWindow[] = [];

  private count = 0;

  private fate: { readonly at: number; readonly lands: boolean } | undefined;

  constructor(initial: Record<string, unknown> = {}) {
    this.stored = cloneOf(initial);
  }

  /** A new window, whose copy is taken now. */
  window(): FakeWindow {
    const opened = new FakeWindow(this, this.stored);
    this.all.push(opened);

    return opened;
  }

  /** What the client stores, for a test's assertion. */
  value(): Record<string, unknown> {
    return cloneOf(this.stored);
  }

  /** The record a run keeps, as the client stores it. */
  record(): MigrationRecord {
    return recordOf(this.stored[ON_EXHAUSTED_MIGRATION_KEY]);
  }

  /** Every window's copy is replaced with the client's value — or only that window's. */
  deliver(only?: FakeWindow): void {
    for (const one of only === undefined ? this.all : [only]) {
      one.replaceWith(this.stored);
    }
  }

  /** The n-th update from now lands, and its promise never resolves: the host died right after. */
  dieAfter(n: number): void {
    this.fate = { at: this.count + n, lands: true };
  }

  /** The n-th update from now does not land and its promise never resolves: the host died right before. */
  dieBefore(n: number): void {
    this.fate = { at: this.count + n, lands: false };
  }

  /** A window calls this for every `update`. */
  write(from: FakeWindow, key: string, value: unknown): PromiseLike<void> {
    this.count += 1;
    const dies = this.fate?.at === this.count ? this.fate : undefined;
    if (dies !== undefined && !dies.lands) {
      return forever();
    }
    from.applyOwn(key, value);
    this.stored = from.sent();

    return dies === undefined ? Promise.resolve() : forever();
  }
}

/** What `WorkspaceConfiguration.update` is asked: the key, the value, the target. */
export interface UpdateCall {
  readonly key: string;
  readonly value: unknown;
  readonly target: unknown;
}

type LayerName = 'global' | 'workspace' | 'folder';

interface Entry {
  declared: boolean;
  defaultValue: unknown;
  global?: unknown;
  workspace?: unknown;
  folder?: unknown;
}

interface Pending {
  readonly key: string;
  readonly apply: () => void;
  readonly resolve: () => void;
}

/** VS Code's own words when a value is written to a key its registry does not hold (F17). */
export function unregisteredWords(key: string): string {
  return `Unable to write to User Settings because coai.${key} is not a registered configuration.`;
}

/**
 * ONE settings file, as V5 and V6 read it. A key is declared or not; an undeclared key has no default and its VALUE
 * cannot be written, its removal can. The windows of one machine share one; two machines share one when Settings Sync
 * has delivered — the test hands both the same object.
 */
export class FakeUserSettings {
  /** Every `update` asked, landed or refused. */
  readonly updates: UpdateCall[] = [];

  private readonly entries = new Map<string, Entry>();

  private readonly refusals = new Map<string, Error>();

  private readonly holds = new Map<string, 'landed' | 'unlanded'>();

  private readonly dying = new Set<string>();

  private pending: Pending[] = [];

  /** Registers a key; one declared with no default gets `''`, as a string setting does (V6). */
  declare(key: string, defaultValue?: unknown): void {
    const entry = this.entryOf(key);
    entry.declared = true;
    entry.defaultValue = defaultValue ?? '';
  }

  /** The registry no longer knows the key — a window that still holds the old manifest. */
  undeclare(key: string): void {
    this.entryOf(key).declared = false;
  }

  /** Sets a layer by hand, for a test's setup: nothing is logged. */
  set(key: string, layer: LayerName, value: unknown): void {
    this.entryOf(key)[layer] = value;
  }

  /** A layer as it is now. */
  layer(key: string, layer: LayerName): unknown {
    return this.entryOf(key)[layer];
  }

  /**
   * What `inspect` answers (V6): an object for any key. An unregistered key has no default, but VS Code keeps it in the
   * user model, so its layers are what the file holds.
   */
  inspect(key: string): { defaultValue: unknown; globalValue: unknown; workspaceValue: unknown; workspaceFolderValue: unknown } {
    const entry = this.entryOf(key);

    return {
      defaultValue: entry.declared ? entry.defaultValue : undefined,
      globalValue: entry.global,
      workspaceValue: entry.workspace,
      workspaceFolderValue: entry.folder,
    };
  }

  /** A SNAPSHOT of the configuration — `inspect` does not see a later write; `update` does reach the file. */
  config(): LayeredSetting {
    const taken = new Map<string, ReturnType<FakeUserSettings['inspect']>>();
    for (const key of this.entries.keys()) {
      taken.set(key, this.inspect(key));
    }

    return {
      inspect: (key) => taken.get(key) ?? this.inspect(key),
      update: (key, value, target) => this.update(key, value, target),
    };
  }

  update(key: string, value: unknown, target: unknown): PromiseLike<void> {
    this.updates.push({ key, value, target });
    const refusal = this.refusals.get(key);
    if (refusal !== undefined) {
      return Promise.reject(refusal);
    }
    if (value !== undefined && !this.entryOf(key).declared) {
      return Promise.reject(new Error(unregisteredWords(key)));
    }

    return this.land(key, () => {
      this.write(key, value, target);
    });
  }

  /** A write of that key rejects, and nothing lands. */
  refuse(key: string, error: Error): void {
    this.refusals.set(key, error);
  }

  cure(key: string): void {
    this.refusals.delete(key);
  }

  /**
   * `landed`: the next write of that key lands at once and its promise waits for `release()`. `unlanded`: every write of
   * that key lands only at `release()`, oldest first, until `unhold`.
   */
  hold(key: string, mode: 'landed' | 'unlanded'): void {
    this.holds.set(key, mode);
  }

  unhold(key: string): void {
    this.holds.delete(key);
  }

  /** Lands what is held and resolves it, oldest first — one key's, or every key's. */
  release(key?: string): void {
    const released = this.pending.filter((one) => key === undefined || one.key === key);
    this.pending = this.pending.filter((one) => !released.includes(one));
    for (const one of released) {
      one.apply();
      one.resolve();
    }
  }

  /** The next write of that key lands and its promise never resolves: the host died right after it. */
  dieAfter(key: string): void {
    this.dying.add(key);
  }

  private land(key: string, apply: () => void): PromiseLike<void> {
    const mode = this.holds.get(key);
    if (mode === 'unlanded') {
      return new Promise<void>((resolve) => {
        this.pending.push({ key, apply, resolve });
      });
    }
    apply();
    if (this.dying.delete(key)) {
      return forever();
    }
    if (mode === 'landed') {
      this.holds.delete(key);

      return new Promise<void>((resolve) => {
        this.pending.push({ key, apply: () => undefined, resolve });
      });
    }

    return Promise.resolve();
  }

  private write(key: string, value: unknown, target: unknown): void {
    const layer: LayerName = target === WORKSPACE ? 'workspace' : target === FOLDER ? 'folder' : 'global';
    this.entryOf(key)[layer] = value;
  }

  private entryOf(key: string): Entry {
    let entry = this.entries.get(key);
    if (entry === undefined) {
      entry = { declared: false, defaultValue: undefined };
      this.entries.set(key, entry);
    }

    return entry;
  }
}

/** The pauses of one window: each is a promise the test resolves, and resolving one advances the fake clock by it. */
export class Pauses {
  /** Every pause asked, in order. */
  readonly asked: number[] = [];

  private waiting: { readonly ms: number; readonly resolve: () => void }[] = [];

  private immediate = false;

  constructor(readonly clock: FakeClock = new FakeClock()) {}

  readonly pause = (ms: number): Promise<void> => {
    this.asked.push(ms);
    if (this.immediate) {
      this.clock.advance(ms);

      return Promise.resolve();
    }

    return new Promise<void>((resolve) => {
      this.waiting.push({ ms, resolve });
    });
  };

  /** How many pauses wait for the test. */
  count(): number {
    return this.waiting.length;
  }

  /** Ends the oldest waiting pause. */
  next(): void {
    const one = this.waiting.shift();
    if (one === undefined) {
      throw new Error('no pause is waiting');
    }
    this.clock.advance(one.ms);
    one.resolve();
  }

  /** Every pause from now on, and every one waiting, ends at once — for a one-window test. */
  auto(): void {
    this.immediate = true;
    while (this.waiting.length > 0) {
      this.next();
    }
  }
}

/** One thing a run did, in order. */
export type Event =
  | { readonly kind: 'write'; readonly key: string; readonly value: unknown }
  | { readonly kind: 'witness' }
  | { readonly kind: 'withdraw' }
  | { readonly kind: 'clear user' }
  | { readonly kind: 'pause'; readonly ms: number }
  | { readonly kind: 'after'; readonly record: MigrationRecord }
  | { readonly kind: 'tell'; readonly sentence: string }
  | { readonly kind: 'refused'; readonly why: string };

export interface RigOptions {
  readonly name?: string;
  readonly client: ClientState;
  /** The window to run in; a new one of the client is taken when none is given. */
  readonly window?: FakeWindow;
  readonly settings: FakeUserSettings;
  readonly pauses?: Pauses;
  readonly version?: string;
  /**
   * What the window's `FeaturesCache` says: `now` is its settled list (`undefined` = not settled; the default is a binary
   * that lists `onExhaustedAuto`), `later` the list it settles on when the tell waits for it. Without `later`, an
   * unsettled cache stays unsettled for the whole cap. With `deferred`, `settleFeatures` stays pending until the test calls
   * `releaseFeatures()`, which settles the list on `later` and resolves it; without it the wait resolves at once.
   */
  readonly features?: { readonly now: readonly string[] | undefined; readonly later?: readonly string[]; readonly deferred?: boolean };
  /** The per-side switch; off unless said. */
  readonly perSide?: boolean;
  readonly side?: Side;
  /** Runs inside the n-th turn of the catalog's queue, before its work (1-based). */
  readonly beforeTurn?: (n: number) => void;
  /** Every pause ends at once. */
  readonly auto?: boolean;
}

/** `MigrationPorts` over the fakes, with the ordered event log. */
export class Rig {
  readonly events: Event[] = [];

  readonly logs: string[] = [];

  readonly window: FakeWindow;

  readonly pauses: Pauses;

  readonly turns = new CatalogTurns();

  readonly ports: MigrationPorts;

  /** What the test runs just before the run's own `after` returns; replaceable. */
  onAfter: () => void = () => undefined;

  private turnCount = 0;
  private releaseSettle: (() => void) | undefined;
  private featureList: readonly string[] | undefined;
  /** The caps the tell waited for features with, in order. */
  readonly waits: number[] = [];

  constructor(private readonly options: RigOptions) {
    this.window = options.window ?? options.client.window();
    this.pauses = options.pauses ?? new Pauses();
    if (options.auto === true) {
      this.pauses.auto();
    }
    this.featureList = options.features === undefined ? [FEATURES.onExhaustedAuto] : options.features.now;
    this.ports = this.build();
  }

  run(): Promise<MigrationOutcome> {
    return migrateOnExhausted(this.ports);
  }

  /** Ends a deferred `settleFeatures` wait: the features settle on `later`. Does nothing when no wait is pending. */
  releaseFeatures(): void {
    this.releaseSettle?.();
    this.releaseSettle = undefined;
  }

  /** Every sentence `tell` was handed. */
  tells(): string[] {
    return this.events.flatMap((one) => (one.kind === 'tell' ? [one.sentence] : []));
  }

  /** Every reason `refused` was handed. */
  refusals(): string[] {
    return this.events.flatMap((one) => (one.kind === 'refused' ? [one.why] : []));
  }

  afters(): number {
    return this.events.filter((one) => one.kind === 'after').length;
  }

  /** The events that touched the user layer, in order. */
  userEvents(): Event[] {
    return this.events.filter((one) => one.kind === 'witness' || one.kind === 'withdraw' || one.kind === 'clear user');
  }

  /** The values this run wrote under one key, in order. */
  writesTo(key: string): unknown[] {
    return this.events.flatMap((one) => (one.kind === 'write' && one.key === key ? [one.value] : []));
  }

  private build(): MigrationPorts {
    const { options, events, window } = this;
    const base = userLayerOf(() => options.settings.config(), GLOBAL);
    const side = options.side ?? SIDE_X;

    return {
      store: {
        keys: () => window.keys(),
        get: (key) => window.get(key),
        update: (key, value) => {
          events.push({ kind: 'write', key, value: cloneOf(value) });

          return window.update(key, value);
        },
      },
      user: {
        ...base,
        writeWitness: () => {
          events.push({ kind: 'witness' });

          return base.writeWitness();
        },
        withdrawWitness: () => {
          events.push({ kind: 'withdraw' });

          return base.withdrawWitness();
        },
        clear: () => {
          events.push({ kind: 'clear user' });

          return base.clear();
        },
      },
      version: options.version ?? '0.88.0',
      window: options.name ?? 'A',
      now: this.pauses.clock.now,
      pause: (ms) => {
        events.push({ kind: 'pause', ms });

        return this.pauses.pause(ms);
      },
      turn: (work) => this.turns.edit(() => {
        this.turnCount += 1;
        options.beforeTurn?.(this.turnCount);

        return work();
      }),
      after: () => {
        events.push({ kind: 'after', record: recordOf(window.get(ON_EXHAUSTED_MIGRATION_KEY)) });
        this.onAfter();
      },
      inForce: () => inForceOf(options.settings.config().inspect('onExhausted'), options.perSide === true, readOverlay(window, side)),
      features: () => this.featureList,
      settleFeatures: (capMs) => {
        this.waits.push(capMs);
        if (options.features?.deferred !== true) {
          this.featureList = options.features?.later ?? this.featureList;

          return Promise.resolve();
        }

        return new Promise<void>((resolve) => {
          this.releaseSettle = () => {
            this.featureList = options.features?.later ?? this.featureList;
            resolve();
          };
        });
      },
      tell: (sentence) => {
        events.push({ kind: 'tell', sentence });

        return Promise.resolve();
      },
      refused: (why) => {
        events.push({ kind: 'refused', why });

        return Promise.resolve();
      },
      log: (what, error) => {
        this.logs.push(error === undefined ? what : `${what}: ${error instanceof Error ? error.message : String(error)}`);
      },
    };
  }
}

/** Three sides on one machine, whose overlay keys sort in this order: X, Y, Z. */
export const SIDE_X: Side = { remoteName: undefined, hostname: 'host', storagePath: '/sides/a' };
export const SIDE_Y: Side = { remoteName: undefined, hostname: 'host', storagePath: '/sides/b' };
export const SIDE_Z: Side = { remoteName: undefined, hostname: 'host', storagePath: '/sides/c' };
export const KEY_X = overlayKey(SIDE_X);
export const KEY_Y = overlayKey(SIDE_Y);
export const KEY_Z = overlayKey(SIDE_Z);
/** A slot under the overlay prefix that holds a string: no overlay, and never written. */
export const KEY_STRING = 'coai.settingsOverlay@a-string';

/** What overlay X keeps beside `onExhausted` — the run must leave it alone. */
export const X_BESIDE = { vendors: [{ id: 'codex' }], rounds: 3 };

/**
 * The usual `globalState` (the spec's fixture U): X holds `human` with `vendors` and `rounds` beside it, Y holds
 * `continue`, Z holds `auto`, and a slot holds a string.
 */
export function usualState(): Record<string, unknown> {
  return {
    [KEY_X]: { onExhausted: 'human', ...X_BESIDE },
    [KEY_Y]: { onExhausted: 'continue' },
    [KEY_Z]: { onExhausted: 'auto' },
    [KEY_STRING]: 'not an overlay',
  };
}

/** The usual settings: both keys declared, the user layer `human`, no witness; and, for U, two workspace values. */
export function usualSettings(workspace: boolean): FakeUserSettings {
  const settings = new FakeUserSettings();
  settings.declare('onExhausted', 'auto');
  settings.declare('onExhaustedResetToAuto');
  settings.set('onExhausted', 'global', 'human');
  if (workspace) {
    settings.set('onExhausted', 'workspace', 'human');
    settings.set('onExhausted', 'folder', 'continue');
  }

  return settings;
}

/** One world: the client store of one machine, the settings file and the clock the windows share. */
export interface World {
  readonly client: ClientState;
  readonly settings: FakeUserSettings;
  readonly clock: FakeClock;
}

export function world(workspace = true, state: Record<string, unknown> = usualState()): World {
  return { client: new ClientState(state), settings: usualSettings(workspace), clock: new FakeClock() };
}

/** One activation: a new window of the client, with every pause answered at once unless the options say otherwise. */
export function activation(of: World, name: string, options: Partial<RigOptions> = {}): Rig {
  const manual = options.pauses !== undefined || options.auto === false;

  return new Rig({
    client: of.client,
    settings: of.settings,
    name,
    pauses: options.pauses ?? new Pauses(of.clock),
    auto: !manual,
    ...options,
  });
}

/** The person chooses a value in a side's overlay, through the real writer, in a window of their own. */
export function chooseInOverlay(client: ClientState, side: Side, value: string): FakeWindow {
  const chooser = client.window();
  void writeOverlay(chooser, side, 'onExhausted', value);

  return chooser;
}

/** Another activation after a host died: the clock passes the claim's age and a new window of the same client starts. */
export function afterADeath(of: World): void {
  of.clock.advance(CLAIM_STALE_MS + 1);
}

export function overlayOf(client: ClientState, key: string): Record<string, unknown> {
  return client.value()[key] as Record<string, unknown>;
}
