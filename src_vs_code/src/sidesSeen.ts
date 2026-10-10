import { type PathFamily } from './pathFamily';

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
  const before = recordOf(stored);
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
      const record = key.startsWith(SIDE_SEEN_PREFIX) ? recordOf(stored) : undefined;

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
  const record = recordOf(stored);

  return record !== undefined && known(record, now);
}

/** The stored value as a record, or nothing — a hand edit or an older shape must never read as a side. */
function recordOf(stored: unknown): SideRecord | undefined {
  if (typeof stored !== 'object' || stored === null) {
    return undefined;
  }
  const fields = stored as Readonly<Record<string, unknown>>;

  return hasRecordFields(fields)
    ? { family: fields.family, label: fields.label, firstSeen: fields.firstSeen, lastSeen: fields.lastSeen }
    : undefined;
}

/** Every field a record needs, each of its type — the shape a hand edit or an older build may not have. */
function hasRecordFields(fields: Readonly<Record<string, unknown>>): fields is Readonly<Record<string, unknown>> & SideRecord {
  return isFamily(fields['family']) && typeof fields['label'] === 'string' && isTime(fields['firstSeen']) && isTime(fields['lastSeen']);
}

function isFamily(value: unknown): value is PathFamily {
  return value === 'windows' || value === 'posix';
}

function isTime(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}
