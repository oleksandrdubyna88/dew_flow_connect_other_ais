/**
 * The settings file as R7's tests hold it (research/PLAN_one_model_catalog.md, epic 5 prerequisite (a)) — written, then read
 * back as a window that reloads reads it. Typed by a guard, never by a cast (R7's code round, finding 1): a value read back
 * from JSON is `unknown` until something has looked at it.
 */

/** The settings file, as far as R7 reads and writes it. */
export type SettingsFile = Readonly<Record<string, unknown>>;

/** A raw row or record entry, as the file holds it. */
export type RawRecord = Readonly<Record<string, unknown>>;

function isRecord(value: unknown): value is RawRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Each write landed in order, then the file read back as JSON — what `settings.json` is. */
export function afterWrites(file: SettingsFile, writes: readonly { readonly key: string; readonly value: unknown }[]): SettingsFile {
  const after = writes.reduce<SettingsFile>((acc, one) => ({ ...acc, [one.key]: one.value }), file);
  const parsed: unknown = JSON.parse(JSON.stringify(after));

  return isRecord(parsed) ? parsed : {};
}

/** A reader over the file, as a side's reader answers a key. */
export const readerOf = (file: SettingsFile) => (key: string): unknown => file[key];

/** The records a value holds — a written `vendors` or record — and nothing else. */
export function recordsIn(value: unknown): readonly RawRecord[] {
  return Array.isArray(value) ? value.filter(isRecord) : [];
}
