import { savedCliPath } from './cliPathSave';
import { hostFamily } from './hostSide';
import { storedForThisSide, type PathFamily } from './pathFamily';
import { cliPathFields, type Vendor } from './vendors';

/**
 * What a catalog row's LAUNCH is, said once (PLAN_one_model_catalog.md E1.3/E1.4; PR #681's code round).
 *
 * <p>The migration asks "may this definition join that row?" and the panel's save asks "did this edit change the
 * row it owns?" — the same question, and it was answered twice, with different fields: the save's copy left out
 * `dialect`. Each new launch field (a thinking flag, a second effort knob) would have had to be added to both. Now
 * both read {@link sameLaunch}, and a rewrite writes the fields {@link withLaunch} names.</p>
 *
 * <p>Also here, the raw-row readers both files had their own copy of.</p>
 */

/** A row of `coai.vendors` as it is stored. */
export type RawRow = Record<string, unknown>;

/** What a definition gives a row: its launch fields, and the name its key is filed under. */
export interface Launch {
  readonly runtime: string;
  readonly model: string;
  readonly baseUrl: string;
  readonly executablePath: string;
  /** The definition's `vendor` — the vault and ledger name the row must keep. */
  readonly vault: string;
  /**
   * The side whose value `executablePath` is — a definition is written back RESOLVED for the side that wrote it
   * (`storedForThisSide`, paths per side E3). Absent: this host's.
   */
  readonly family?: PathFamily;
}

/**
 * Whether the row launches exactly what the definition does, its key filed under the same name. A definition carries
 * no dialect, so a row with one never equals it.
 */
export function sameLaunch(row: Vendor, launch: Launch): boolean {
  const theirs = [launch.runtime, launch.model, launch.baseUrl, launch.executablePath.trim(), '', launch.vault];

  return launchOf(row, sideOf(launch)).every((field, index) => field === theirs[index]);
}

/** A row's launch fields, in {@link sameLaunch}'s order — its CLI path as `family`'s field holds it. */
function launchOf(row: Vendor, family: PathFamily): readonly string[] {
  return [row.runtime, row.model, row.baseUrl, storedForThisSide(row, family), row.dialect ?? '', row.vaultKeyName ?? row.id];
}

/** The raw row given the definition's launch — a dialect the definition does not carry dropped, everything else kept. */
export function withLaunch(raw: RawRow, id: string, launch: Launch): RawRow {
  const { vaultKeyName: _old, dialect: _dialect, ...rest } = raw;

  return {
    ...rest,
    runtime: launch.runtime,
    model: launch.model,
    baseUrl: launch.baseUrl,
    ...cliPathOf(raw, launch),
    ...(launch.vault === id ? {} : { vaultKeyName: launch.vault }),
  };
}

/**
 * The row's CLI-path fields given the definition's value, which is ITS side's (`Launch.family`; paths per side E3):
 * untouched when that side already runs it — so an edit of a model, a prompt or a switch never moves a path — and
 * otherwise saved as that side's own field (`savedCliPath`), which keeps every other side running what it did. Writing
 * the value into the legacy field alone took the other side's path away: `mirrored` fell behind, so the write read as
 * an older extension's save (the E3 code round's own review).
 */
function cliPathOf(raw: RawRow, launch: Launch): Readonly<Record<string, unknown>> {
  const stored = cliPathFields(raw);
  const family = sideOf(launch);

  return storedForThisSide(stored, family) === launch.executablePath.trim() ? {} : { ...savedCliPath(stored, launch.executablePath, family, family) };
}

/** The side a definition's CLI path belongs to — this host's unless it says. */
function sideOf(launch: Launch): PathFamily {
  return launch.family ?? hostFamily();
}

/** A raw row's id, trimmed and lower-cased as `vendorsFrom` reads it. */
export function rawId(raw: RawRow): string {
  return typeof raw['id'] === 'string' ? raw['id'].trim().toLowerCase() : '';
}

export function listOf(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? value : [];
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function asRecord(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}
