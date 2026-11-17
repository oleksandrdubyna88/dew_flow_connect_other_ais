import { isRecord } from './catalogLaunch';
import { overlayKeyOf } from './coaiInstall';
import { unknownKeysOf } from './catalogMigrationRun';
import { InForce } from './onExhaustedTold';

/**
 * The one-time migration that resets every saved *When the rounds run out* to Auto (E2.S2; the decisions are
 * todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.1, the run is § S2.4). Pure and `vscode`-free: the host hands in a
 * store, the person's own settings layer and a few functions, and every test drives it that way, without a panel.
 */

export const ON_EXHAUSTED_MIGRATION_KEY = 'coai.onExhaustedAutoMigration';
export const OVERLAY_WITNESS = 'onExhaustedAutoMigration';
/** The user layer's witness as the configuration names it under `coai` — the manifest's `coai.onExhaustedResetToAuto`. */
export const USER_WITNESS = 'onExhaustedResetToAuto';
/** What the witness says: one fixed word, the same on every machine and in every version (the parent's entry 3.1). */
export const USER_WITNESS_WORD = 'auto';
export const SETTLE_MS = 2000;
export const CLAIM_STALE_MS = 60_000;
export const CLAIM_TRIES = 3;
export const OVERLAY_TURNS = 64;

export type MigrationOutcome = 'migrated' | 'already' | 'not-ready' | 'busy' | 'incomplete' | 'refused';

export interface MigrationRecord {
  readonly completed?: string;
  readonly by?: string;
  readonly told?: true;
  readonly claim?: { readonly by: string; readonly at: number };
  readonly held?: Readonly<Record<string, string>>;
  readonly refusedTold?: true;
}

/** The part of `vscode.Memento` the run uses. `update(key, undefined)` deletes. */
export interface MigrationStore {
  keys(): readonly string[];
  get(key: string): unknown;
  update(key: string, value: unknown): PromiseLike<void>;
}

/** The person's own layer of `coai.onExhausted` and of its witness, and nothing of a workspace or a folder. */
export interface UserLayer {
  known(): boolean;
  value(): unknown;
  witness(): unknown;
  writeWitness(): PromiseLike<void>;
  withdrawWitness(): PromiseLike<void>;
  clear(): PromiseLike<void>;
}

/** The two members of `vscode.WorkspaceConfiguration` the user layer needs. */
export interface LayeredSetting {
  inspect(key: string): { readonly defaultValue?: unknown; readonly globalValue?: unknown } | undefined;
  update(key: string, value: unknown, target: unknown): PromiseLike<void>;
}

export interface MigrationPorts {
  readonly store: MigrationStore;
  readonly user: UserLayer;
  readonly version: string;
  readonly window: string;
  readonly now: () => number;
  readonly pause: (ms: number) => Promise<void>;
  readonly turn: <T>(work: () => Promise<T>) => Promise<T>;
  readonly after: () => void;
  readonly inForce: () => InForce;
  readonly tell: (sentence: string) => Promise<void>;
  readonly refused: (why: string) => Promise<void>;
  readonly log: (what: string, error?: unknown) => void;
}

/** The id the user layer has in `held` — an overlay's id is its whole `globalState` key. */
export const USER_ID = 'user';

/** What is stored, read as a record. A bare string is a completed marker; anything unreadable is an empty record. */
export function recordOf(stored: unknown): MigrationRecord {
  if (typeof stored === 'string') {
    return { completed: stored };
  }
  if (!isRecord(stored)) {
    return {};
  }

  return { ...namesOf(stored), ...flagsOf(stored), ...claimOf(stored), ...heldOf(stored) };
}

function namesOf(stored: Record<string, unknown>): Pick<MigrationRecord, 'completed' | 'by'> {
  return {
    ...(typeof stored['completed'] === 'string' ? { completed: stored['completed'] } : {}),
    ...(typeof stored['by'] === 'string' ? { by: stored['by'] } : {}),
  };
}

function flagsOf(stored: Record<string, unknown>): Pick<MigrationRecord, 'told' | 'refusedTold'> {
  return {
    ...(stored['told'] === true ? { told: true as const } : {}),
    ...(stored['refusedTold'] === true ? { refusedTold: true as const } : {}),
  };
}

function claimOf(stored: Record<string, unknown>): Pick<MigrationRecord, 'claim'> {
  const claim = stored['claim'];

  return isRecord(claim) && typeof claim['by'] === 'string' && typeof claim['at'] === 'number' ? { claim: { by: claim['by'], at: claim['at'] } } : {};
}

function heldOf(stored: Record<string, unknown>): Pick<MigrationRecord, 'held'> {
  const held = stored['held'];
  if (!isRecord(held)) {
    return {};
  }

  return { held: Object.fromEntries(Object.entries(held).filter((entry): entry is [string, string] => typeof entry[1] === 'string')) };
}

/** The value as `held` keeps it, or `undefined` when there is nothing to migrate (absent, or `auto`). */
export function heldText(value: unknown): string | undefined {
  if (value === undefined || value === 'auto') {
    return undefined;
  }

  return typeof value === 'string' ? value : JSON.stringify(value);
}

/**
 * What every location holds now: `'user'` and each overlay record found by its key — witnessed or not (D6). The overlays
 * are found by walking the store's keys for the overlay prefix, never through the side registry: a side the registry
 * forgot keeps its overlay (D1).
 */
export function planOf(store: MigrationStore, userValue: unknown): Readonly<Record<string, string>> {
  const prefix = overlayKeyOf('');
  const held: Record<string, string> = {};
  addHeld(held, USER_ID, userValue);
  for (const key of store.keys().filter((one) => one.startsWith(prefix)).sort()) {
    const slot = store.get(key);
    addHeld(held, key, isRecord(slot) ? slot['onExhausted'] : undefined);
  }

  return held;
}

function addHeld(held: Record<string, string>, id: string, value: unknown): void {
  const text = heldText(value);
  if (text !== undefined) {
    held[id] = text;
  }
}

/** § S2.2's table. The one place a witness is asked: unwitnessed, holds a value to migrate, and that value is the recorded one. */
export function isDue(witnessed: boolean, current: unknown, held: string | undefined): boolean {
  const text = heldText(current);

  return !witnessed && text !== undefined && text === held;
}

/** The user layer over a configuration that is asked for AGAIN at every call, writing the Global target only. */
export function userLayerOf(config: () => LayeredSetting, globalTarget: unknown): UserLayer {
  const globalValue = (key: string): unknown => config().inspect(key)?.globalValue;

  return {
    known: () => unknownKeysOf([USER_WITNESS], (key) => config().inspect(key)?.defaultValue).length === 0,
    value: () => globalValue('onExhausted'),
    witness: () => globalValue(USER_WITNESS),
    writeWitness: () => config().update(USER_WITNESS, USER_WITNESS_WORD, globalTarget),
    withdrawWitness: () => config().update(USER_WITNESS, undefined, globalTarget),
    clear: () => config().update('onExhausted', undefined, globalTarget),
  };
}

export function migrateOnExhausted(_ports: MigrationPorts): Promise<MigrationOutcome> {
  return Promise.resolve('not-ready');
}
