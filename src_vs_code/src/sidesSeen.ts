import { sideKey, type Side } from './coaiInstall';
import { thisSideLabel } from './consultantSides';
import { familyOf, type PathFamily } from './pathFamily';

/**
 * The sides this profile has been used on (todo/PLAN_paths_per_side.md, design (a) option A, E2.1).
 *
 * <p><b>Why a record at all.</b> A path setting can hold a Windows value and a WSL value only once something says both
 * sides are in use — and nothing in VS Code says it: user settings are shared by a WSL window and a plain Windows window
 * on one machine, and neither is told the other exists. So every window stamps ITS side into `globalState`, the one store
 * all windows of a profile share (measured: `research/RESULTS_side_registry_reaches_the_other_window.md` — a stamp from
 * one side is seen by the other side's already-open window at its next read, no reload). `globalState` is not synced by
 * Settings Sync (no `setKeysForSync`), so another MACHINE's sides never appear here.</p>
 *
 * <p><b>One key per side</b> — `coai.sideSeen@<sideKey>` — so two windows never read-modify-write one value: each writes
 * only its own, and the list is `Memento.keys()` filtered by the prefix. The `coai.firstSeenControls` record
 * (`newTags.ts`) is the precedent: bounded, stamped at activation, pruned, and whatever was stored reads as a record or
 * as nothing, never as an error.</p>
 *
 * <p>Pure and `vscode`-free: the clock and the stored values are handed in. The thin store half below takes a structural
 * store, which a `vscode.Memento` is.</p>
 */

/** Every side's record lives under this prefix followed by its `sideKey` (`coaiInstall.ts`). */
export const SIDE_SEEN_PREFIX = 'coai.sideSeen@';

const DAY_MS = 24 * 60 * 60 * 1000;

/** A side seen within this long is KNOWN; one not seen for longer is pruned (a side gone for good stops counting). */
export const KNOWN_FOR_MS = 90 * DAY_MS;

/** A side is stamped at most once in this long — activation is frequent, and a write per window start is noise. */
export const STAMP_EVERY_MS = DAY_MS;

/** The furthest instant a JavaScript `Date` can hold, either side of 1970 (ECMA-262, 100 000 000 days). */
const LATEST_DATE_MS = 8.64e15;

/** What one side's record says. Times are UTC milliseconds (`Date.now()`); a person reads them as a local day. */
export interface SideRecord {
  readonly family: PathFamily;
  /** The side named as the This side page names it (`thisSideLabel`: `Windows`, `WSL: Ubuntu`). */
  readonly label: string;
  readonly firstSeen: number;
  readonly lastSeen: number;
}

/** A record with the side it belongs to — its `sideKey`, which is also what **Forget** posts. */
export interface SeenSide extends SideRecord {
  readonly side: string;
}

/** A record to write, and where. */
export interface SideStamp {
  readonly key: string;
  readonly record: SideRecord;
}

/** This window's side, as the registry needs it: its key, its family and its name. */
export interface SideHere {
  readonly key: string;
  readonly family: PathFamily;
  readonly label: string;
}

/**
 * This window's side as the registry records it: the key every per-side record already uses (`sideKey` — `installedKey`
 * and `overlayKey` fold the same one), the family `platform` spells paths in, and the name the This side page says
 * (`thisSideLabel`: `WSL: Ubuntu`, or `Windows` in a local window rather than "this machine"). ONE place, called by
 * activation's stamp and by the Settings page, so the row the page marks as this window is the row activation wrote.
 *
 * @param side `installer.thisSide(context.globalStorageUri)` in the host
 * @param platform `process.platform` in the host
 */
export function sideHereOf(side: Side, platform: string): SideHere {
  return { key: sideKey(side), family: familyOf(platform), label: thisSideLabel(side.remoteName, side.distro ?? '', platform) };
}

/** The `globalState` key of one side's record. */
export function sideSeenKey(side: string): string {
  return SIDE_SEEN_PREFIX + side;
}

/**
 * The record this window writes for its side, or `undefined` when there is nothing to write — the side was stamped
 * less than {@link STAMP_EVERY_MS} ago. A stored value that is not a record is stamped afresh; a record keeps its
 * `firstSeen`.
 */
export function stampSide(stored: unknown, side: string, family: PathFamily, label: string, now: number): SideStamp | undefined {
  const before = recordOf(stored, now);
  if (stampedLately(before, now)) {
    return undefined;
  }

  return { key: sideSeenKey(side), record: { family, label, firstSeen: before === undefined ? now : before.firstSeen, lastSeen: now } };
}

/** Stamped less than {@link STAMP_EVERY_MS} ago — the at-most-once-a-day rule. */
function stampedLately(before: SideRecord | undefined, now: number): boolean {
  return before !== undefined && now - before.lastSeen < STAMP_EVERY_MS;
}

/** The sides seen within {@link KNOWN_FOR_MS}, newest first. Keys outside the registry and unreadable values are ignored. */
export function seenSides(entries: readonly (readonly [string, unknown])[], now: number): readonly SeenSide[] {
  return entries
    .flatMap(([key, stored]): SeenSide[] => {
      const record = key.startsWith(SIDE_SEEN_PREFIX) ? recordOf(stored, now) : undefined;

      return record !== undefined && known(record, now) ? [{ ...record, side: key.slice(SIDE_SEEN_PREFIX.length) }] : [];
    })
    .sort((a, b) => b.lastSeen - a.lastSeen);
}

/** The registry's keys to delete: a record not seen for longer than {@link KNOWN_FOR_MS}, or a value that is no record. */
export function prunedKeys(entries: readonly (readonly [string, unknown])[], now: number): readonly string[] {
  return entries
    .filter(([key, stored]) => key.startsWith(SIDE_SEEN_PREFIX) && !knownRecord(stored, now))
    .map(([key]) => key);
}

/** The families of the sides seen within {@link KNOWN_FOR_MS} — both, once a Windows and a WSL / Linux side were. */
export function knownFamilies(records: readonly SideRecord[], now: number): ReadonlySet<PathFamily> {
  return new Set(records.filter((record) => known(record, now)).map((record) => record.family));
}

/** Seen within the window — exactly {@link KNOWN_FOR_MS} ago still counts. */
function known(record: SideRecord, now: number): boolean {
  return now - record.lastSeen <= KNOWN_FOR_MS;
}

function knownRecord(stored: unknown, now: number): boolean {
  const record = recordOf(stored, now);

  return record !== undefined && known(record, now);
}

/**
 * The stored value as a record, or nothing — a hand edit or an older shape must never read as a side, and neither may a
 * time no `Date` can hold or one more than {@link STAMP_EVERY_MS} ahead of `now` (the code round on E2: `1e300` is
 * finite, counted as seen in the far future, held off every later stamp and threw `RangeError` on the page). Such a
 * record is ignored, and pruned at the next stamp — never thrown on. A day of slack keeps another clock's stamp.
 */
function recordOf(stored: unknown, now: number): SideRecord | undefined {
  if (typeof stored !== 'object' || stored === null) {
    return undefined;
  }
  const fields = stored as Readonly<Record<string, unknown>>;

  return readable(fields, now)
    ? { family: fields.family, label: fields.label, firstSeen: fields.firstSeen, lastSeen: fields.lastSeen }
    : undefined;
}

/** Every field a record needs, each of its type — the shape a hand edit or an older build may not have. */
function hasRecordFields(fields: Readonly<Record<string, unknown>>): fields is Readonly<Record<string, unknown>> & SideRecord {
  return isFamily(fields['family']) && typeof fields['label'] === 'string' && isTime(fields['firstSeen']) && isTime(fields['lastSeen']);
}

/** A record's fields, each of its type, with times that can be believed. */
function readable(fields: Readonly<Record<string, unknown>>, now: number): fields is Readonly<Record<string, unknown>> & SideRecord {
  return hasRecordFields(fields) && plausible(fields, now);
}

/** Both times within what a `Date` holds, and neither more than a day ahead of now. */
function plausible(record: SideRecord, now: number): boolean {
  return [record.firstSeen, record.lastSeen].every((at) => Math.abs(at) <= LATEST_DATE_MS && at - now <= STAMP_EVERY_MS);
}

function isFamily(value: unknown): value is PathFamily {
  return value === 'windows' || value === 'posix';
}

function isTime(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

// ---------------------------------------------------------------- the switch

/**
 * `coai.pathFieldsPerSide` (E2.3; design (a)'s override E): `auto` decides from the registry, `always` and `never` are a
 * person's answer. `application` scope — the person's, not a side's — so every window reads one value.
 */
export type PathFieldsMode = 'auto' | 'always' | 'never';

/** What Setup › This side draws, and what decides two path fields: this window, the sides seen, the switch, the clock. */
export interface SidesView {
  readonly here: SideHere;
  readonly seen: readonly SeenSide[];
  readonly mode: PathFieldsMode;
  readonly now: number;
}

/** The stored switch; anything but `always` or `never` — nothing set, a hand edit — is `auto`. */
export function pathFieldsModeOf(value: unknown): PathFieldsMode {
  return value === 'always' || value === 'never' ? value : 'auto';
}

/**
 * Whether a path setting shows a Windows field AND a WSL / Linux field (design (d)): the switch, or under `auto` both
 * families seen within the window — this window counting as seen now, so its own stamp landing late changes nothing.
 * Re-read on every render (E0: a stamp from the other side reaches an open window live).
 */
export function twoPathFields(view: SidesView): boolean {
  if (view.mode !== 'auto') {
    return view.mode === 'always';
  }

  return new Set([view.here.family, ...knownFamilies(view.seen, view.now)]).size === 2;
}

// ---------------------------------------------------------------- the store

/** The part of a `vscode.Memento` the registry uses — `context.globalState` is one. */
export interface SideStore {
  keys(): readonly string[];
  get(key: string): unknown;
  /** `undefined` deletes the key, as `Memento.update` does. */
  update(key: string, value: unknown): PromiseLike<void>;
}

/** The sides seen within the window, read from the store now — every render reads this afresh (E0: a stamp lands live). */
export function sidesIn(store: SideStore, now: number): readonly SeenSide[] {
  return seenSides(entriesOf(store), now);
}

/**
 * What activation does (E2.2): this side's record, written at most once a day, then every record past the window removed.
 * Never awaited by activation — a slow store must not delay a window's start.
 *
 * <p><b>It never rejects.</b> A rejection nobody awaits leaves no trace (the coordinator's accepted finding on E2), so a
 * store that refuses is handed to `failed` — the extension host's log, never a toast: a missing stamp only means two
 * fields appear one start later. Nothing is written in a half-state that would stop the next start: a refused write
 * leaves no record, so the next stamp is due and is tried again.</p>
 *
 * @param failed what to do with a refusal — activation logs it through the extension host's console
 */
export async function stampThisSide(store: SideStore, here: SideHere, now: number, failed: (error: unknown) => void): Promise<void> {
  try {
    await stampAndPrune(store, here, now);
  } catch (error: unknown) {
    failed(error);
  }
}

async function stampAndPrune(store: SideStore, here: SideHere, now: number): Promise<void> {
  const stamp = stampSide(store.get(sideSeenKey(here.key)), here.key, here.family, here.label, now);
  if (stamp !== undefined) {
    await store.update(stamp.key, stamp.record);
  }
  for (const key of prunedKeys(entriesOf(store), now)) {
    await store.update(key, undefined);
  }
}

/**
 * **Forget** on Setup › This side: another side's record removed, so a side gone for good stops counting before its 90
 * days are up. This window's own is refused — it is in use now — and would only be stamped again at the next start.
 */
export async function forgetSide(store: SideStore, side: string, here: string): Promise<boolean> {
  if (side === here) {
    return false;
  }
  await store.update(sideSeenKey(side), undefined);

  return true;
}

/** The store's registry keys with their values — the registry's slice of `Memento.keys()`. */
function entriesOf(store: SideStore): readonly (readonly [string, unknown])[] {
  return store.keys().filter((key) => key.startsWith(SIDE_SEEN_PREFIX)).map((key) => [key, store.get(key)] as const);
}
