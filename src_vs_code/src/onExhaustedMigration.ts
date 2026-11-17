import { isRecord } from './catalogLaunch';
import { overlayKeyOf } from './coaiInstall';
import { unknownKeysOf, unregisteredKeyIn } from './catalogMigrationRun';
import { InForce, sentenceFor } from './onExhaustedTold';

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

/** A stop part way: what the run ends with and — for a refusal of the settings file — the reason to tell. */
interface Stop {
  readonly outcome: MigrationOutcome;
  readonly why?: string;
}

/** What step 1 hands on: an outcome to end with, the claim taken, or a completed run that never told. */
type Entry = MigrationOutcome | 'claimed' | 'tell';

function stop(outcome: MigrationOutcome, why?: string): Stop {
  return why === undefined ? { outcome } : { outcome, why };
}

/** The record as it is now. Read where it is used, never earlier: no value of it crosses an `await` (D4, D10). */
function read(ports: MigrationPorts): MigrationRecord {
  return recordOf(ports.store.get(ON_EXHAUSTED_MIGRATION_KEY));
}

/** This window holds the claim, and nobody completed. A best effort: it reads this window's own copy (D4). */
function mine(ports: MigrationPorts, seen: MigrationRecord): boolean {
  return seen.completed === undefined && seen.claim?.by === ports.window;
}

/**
 * The claim and `held` are two writes, so a stale window can put back a copy with this window's claim and no `held`.
 * Such a copy cannot say what is due, and "nothing" is the wrong answer: the run stops and the next start plans again.
 */
function planLost(ports: MigrationPorts, seen: MigrationRecord, step: string): Stop | undefined {
  if (seen.held !== undefined) {
    return undefined;
  }
  ports.log(`what each location held was lost to a stale write; the next start plans again (${step})`);

  return stop('incomplete');
}

/** What stops a step that reads the record: the claim is not this window's, or the plan is not in the copy. */
function stoppedFor(ports: MigrationPorts, seen: MigrationRecord, step: string): Stop | undefined {
  return mine(ports, seen) ? planLost(ports, seen, step) : stop('busy');
}

function versionOf(ports: MigrationPorts): string {
  return ports.version === '' ? 'unknown' : ports.version;
}

function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * The run (§ S2.4). It never rejects, and nothing in it rests on the claim: what correctness rests on is the witnesses —
 * an overlay's is in the record it clears, the user layer's is a setting in the settings file — and that no value read
 * from the record or the settings is carried across an `await`. The catch at the top writes NOTHING: a claim left behind
 * goes stale, exactly as a dead host's does.
 */
export async function migrateOnExhausted(ports: MigrationPorts): Promise<MigrationOutcome> {
  try {
    return await runSteps(ports);
  } catch (error: unknown) {
    ports.log('the migration stopped on an error; the next start runs it again', error);

    return 'incomplete';
  }
}

async function runSteps(ports: MigrationPorts): Promise<MigrationOutcome> {
  if (!ports.user.known()) {
    return 'not-ready';
  }
  const entry = await enter(ports);
  if (entry === 'tell') {
    return tellFromEntry(ports);
  }

  return entry === 'claimed' ? claimedRun(ports) : entry;
}

/** Steps 2–6 and what follows them, for the window that took the claim. */
async function claimedRun(ports: MigrationPorts): Promise<MigrationOutcome> {
  const stopped = (await settle(ports)) ?? (await mark(ports));

  return stopped === undefined ? tellAfterPause(ports) : leave(ports, stopped);
}

/** Step 1: up to `CLAIM_TRIES` times, wait out a fresh claim of another window or take the claim. */
async function enter(ports: MigrationPorts): Promise<Entry> {
  for (let attempt = 0; attempt < CLAIM_TRIES; attempt += 1) {
    const entry = await tryClaim(ports);
    if (entry !== undefined) {
      return entry;
    }
  }

  return 'busy';
}

function completedEntry(seen: MigrationRecord): 'already' | 'tell' | undefined {
  if (seen.completed === undefined) {
    return undefined;
  }

  return seen.told === true ? 'already' : 'tell';
}

/** How long to wait for another window's fresh claim — capped, because `at` is another host's clock; 0 when there is none. */
function waitFor(ports: MigrationPorts, seen: MigrationRecord): number {
  const claim = seen.claim;
  if (claim === undefined || claim.by === ports.window) {
    return 0;
  }
  const age = ports.now() - claim.at;

  return age < CLAIM_STALE_MS ? Math.min(CLAIM_STALE_MS, CLAIM_STALE_MS - age) : 0;
}

async function tryClaim(ports: MigrationPorts): Promise<Entry | undefined> {
  const seen = read(ports);
  const done = completedEntry(seen);
  if (done !== undefined) {
    return done;
  }
  const wait = waitFor(ports, seen);
  if (wait > 0) {
    await ports.pause(wait);

    return undefined;
  }
  await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, { ...seen, claim: { by: ports.window, at: ports.now() } });
  await ports.pause(SETTLE_MS);

  return afterTheClaim(ports);
}

/** The read after the claim's pause: someone completed, or this window's own name is still there, or neither. */
function afterTheClaim(ports: MigrationPorts): Entry | undefined {
  const back = read(ports);

  return completedEntry(back) ?? (mine(ports, back) ? 'claimed' : undefined);
}

/**
 * Steps 2–4, the pause, and step 5: `after` is called ONCE on every way out of a run that took the claim — after the
 * clears and the pause, and at once when a step stops or throws (D5).
 */
async function settle(ports: MigrationPorts): Promise<Stop | undefined> {
  try {
    const stopped = await clears(ports);
    if (stopped === undefined) {
      await ports.pause(SETTLE_MS);
    }

    return stopped;
  } finally {
    ports.after();
  }
}

async function clears(ports: MigrationPorts): Promise<Stop | undefined> {
  return (await plan(ports)) ?? (await overlays(ports)) ?? (await userLayer(ports));
}

function sameHeld(stored: Readonly<Record<string, string>> | undefined, planned: Readonly<Record<string, string>>): boolean {
  const text = (held: Readonly<Record<string, string>>): string => JSON.stringify(Object.entries(held).sort());

  return stored !== undefined && text(stored) === text(planned);
}

/** Step 2: what each location held, written ONCE before the first clear and read back (N8). A resumed run reuses it. */
async function plan(ports: MigrationPorts): Promise<Stop | undefined> {
  const seen = read(ports);
  if (!mine(ports, seen)) {
    return stop('busy');
  }
  if (seen.held !== undefined) {
    return undefined;
  }
  const held = planOf(ports.store, ports.user.value());
  await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, { ...seen, held });
  if (sameHeld(read(ports).held, held)) {
    return undefined;
  }
  ports.log('what each location held did not read back; the next start tries again');

  return stop('incomplete');
}

/** Whether a location is due NOW (§ S2.2) — the overlay from the store, the user layer from the settings. */
function dueNow(ports: MigrationPorts, id: string, held: string | undefined): boolean {
  if (id === USER_ID) {
    return isDue(ports.user.witness() !== undefined, ports.user.value(), held);
  }
  const slot = ports.store.get(id);

  return isRecord(slot) && isDue(slot[OVERLAY_WITNESS] !== undefined, slot['onExhausted'], held);
}

/** The first overlay id of `held`, in key order, that is due at this moment. */
function nextDue(ports: MigrationPorts, held: Readonly<Record<string, string>>): string | undefined {
  return Object.keys(held).filter((id) => id !== USER_ID).sort().find((id) => dueNow(ports, id, held[id]));
}

/** The overlay record without `onExhausted` and with its witness — every other member kept. */
function clearedOverlay(ports: MigrationPorts, id: string): Record<string, unknown> {
  const slot = ports.store.get(id);
  const kept = isRecord(slot) ? Object.entries(slot).filter(([name]) => name !== 'onExhausted') : [];

  return { ...Object.fromEntries(kept), [OVERLAY_WITNESS]: versionOf(ports) };
}

/**
 * ONE turn of the catalog's queue: the record and the overlay are read and the overlay written with no `await` between,
 * and the clear and its witness are one `update` (D7, D9). Which overlay is chosen from a fresh read of this turn.
 */
async function clearNextOverlay(ports: MigrationPorts): Promise<Stop | 'cleared' | undefined> {
  const seen = read(ports);
  const blocked = stoppedFor(ports, seen, 'overlay turn');
  if (blocked !== undefined) {
    return blocked;
  }
  const id = nextDue(ports, seen.held ?? {});
  if (id === undefined) {
    return undefined;
  }
  await ports.store.update(id, clearedOverlay(ports, id));

  return 'cleared';
}

/** Step 3: one overlay per turn until none is due — bounded, so a write that never lasts ends the run `incomplete`. */
async function overlays(ports: MigrationPorts): Promise<Stop | undefined> {
  for (let turn = 0; turn < OVERLAY_TURNS; turn += 1) {
    const result = await ports.turn(() => clearNextOverlay(ports));
    if (result !== 'cleared') {
      return result;
    }
  }
  ports.log('an overlay was still due after every turn; the next start goes on');

  return stop('incomplete');
}

/** Step 4: the witness is in the user layer first, whether or not the layer holds a value; then the value test decides. */
async function userLayer(ports: MigrationPorts): Promise<Stop | undefined> {
  const blocked = stoppedFor(ports, read(ports), 'user layer');
  if (blocked !== undefined) {
    return blocked;
  }
  if (ports.user.witness() !== undefined) {
    return undefined;
  }
  const refused = await writeWitness(ports);

  return refused ?? removeUnlessChosen(ports);
}

async function writeWitness(ports: MigrationPorts): Promise<Stop | undefined> {
  try {
    await ports.user.writeWitness();

    return undefined;
  } catch (error: unknown) {
    return witnessRefused(ports, error);
  }
}

/**
 * Step 4r. A refusal that names an unknown key is a wait, never a warning (D13); any other is the settings file's, and the
 * person is told only when the user layer holds a value now — "your earlier choice there is still in force" (D12).
 */
function witnessRefused(ports: MigrationPorts, error: unknown): Stop {
  if (unregisteredKeyIn(error) === USER_WITNESS || !ports.user.known()) {
    ports.log('this window does not know the witness setting yet; the next start tries again', error);

    return stop('not-ready');
  }
  if (heldText(ports.user.value()) === undefined) {
    ports.log('the settings file refused the witness and holds no value to reset; the next start tries again', error);

    return stop('refused');
  }

  return stop('refused', reasonOf(error));
}

/** The value as it is NOW is the one recorded — read again after the awaited witness write; the value test alone. */
function stillTheRecordedValue(ports: MigrationPorts, held: Readonly<Record<string, string>>): boolean {
  const value = heldText(ports.user.value());

  return value !== undefined && value === held[USER_ID];
}

/**
 * After the awaited witness write the record is read ONCE, in the same synchronous stretch as the value test and the
 * removal. A claim that passed to another window leaves the value; a copy that lost `held` takes the witness out again,
 * so the next start plans again and clears the value (§ S2.10).
 */
async function removeUnlessChosen(ports: MigrationPorts): Promise<Stop | undefined> {
  const seen = read(ports);
  if (!mine(ports, seen)) {
    ports.log('the claim passed to another window while the witness was written; the value stays');

    return stop('busy');
  }
  if (seen.held === undefined) {
    return lostAfterWitness(ports);
  }

  return stillTheRecordedValue(ports, seen.held) ? removeValue(ports) : undefined;
}

async function lostAfterWitness(ports: MigrationPorts): Promise<Stop> {
  ports.log('what each location held was lost to a stale write while the witness was written; it is withdrawn');
  await tryWithdraw(ports);

  return stop('incomplete');
}

async function removeValue(ports: MigrationPorts): Promise<Stop | undefined> {
  try {
    await ports.user.clear();

    return undefined;
  } catch (error: unknown) {
    return withdrawn(ports, error);
  }
}

/** The witness comes out again; a withdrawal the file refuses is logged, and the next start keeps the value. */
async function tryWithdraw(ports: MigrationPorts): Promise<void> {
  try {
    await ports.user.withdrawWitness();
  } catch (again: unknown) {
    ports.log('the witness could not be withdrawn either; the next start keeps the value', again);
  }
}

/** A removal the settings file refused: the witness comes out again, so the next start finds the value and tries once more. */
async function withdrawn(ports: MigrationPorts, error: unknown): Promise<Stop> {
  await tryWithdraw(ports);

  return stop('refused', reasonOf(error));
}

/**
 * Step 6: the marker is a NEW record, written only from a copy in which every recorded location is settled — the read,
 * `mine`, the due test of every id and the `update` have no `await` among them (D10).
 */
async function mark(ports: MigrationPorts): Promise<Stop | undefined> {
  const seen = read(ports);
  const blocked = stoppedFor(ports, seen, 'marker');
  if (blocked !== undefined) {
    return blocked;
  }
  const left = Object.entries(seen.held ?? {}).filter(([id, held]) => dueNow(ports, id, held)).map(([id]) => id);
  if (left.length > 0) {
    ports.log(`still due at the marker: ${left.join(', ')}`);

    return stop('incomplete');
  }
  await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, { completed: versionOf(ports), by: ports.window });

  return undefined;
}

/** What a stop leaves in the record: the claim comes out, and `refusedTold` stays while the refusal lasts. */
function released(seen: MigrationRecord, refusedTold: boolean): MigrationRecord {
  const { claim: _claim, ...rest } = seen;

  return refusedTold ? { ...rest, refusedTold: true } : rest;
}

async function tellRefusal(ports: MigrationPorts, stopped: Stop): Promise<boolean> {
  if (stopped.why === undefined || read(ports).refusedTold === true) {
    return false;
  }
  await ports.refused(stopped.why);

  return true;
}

/** Step 5s: tell a refusal once, release the claim — and write nothing when it is no longer this window's. */
async function leave(ports: MigrationPorts, stopped: Stop): Promise<MigrationOutcome> {
  const toldNow = await tellRefusal(ports, stopped);
  const seen = read(ports);
  if (mine(ports, seen)) {
    await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, released(seen, toldNow || seen.refusedTold === true));
  }

  return stopped.outcome;
}

/** Step 7 from step 1: a completed run that never told — this window takes it over. */
async function tellFromEntry(ports: MigrationPorts): Promise<MigrationOutcome> {
  await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, { ...read(ports), by: ports.window });

  return tellAfterPause(ports);
}

/** Step 7: tell only after the marker was read back with this window's name, and choose the sentence THEN (D11). */
async function tellAfterPause(ports: MigrationPorts): Promise<MigrationOutcome> {
  await ports.pause(SETTLE_MS);
  const seen = read(ports);
  if (seen.completed === undefined) {
    ports.log('the marker was lost before it was read back; the next start runs again');

    return 'incomplete';
  }
  if (seen.by !== ports.window || seen.told === true) {
    return 'already';
  }
  await ports.tell(sentenceFor(ports.inForce()));
  await ports.store.update(ON_EXHAUSTED_MIGRATION_KEY, { ...read(ports), told: true });

  return 'migrated';
}
