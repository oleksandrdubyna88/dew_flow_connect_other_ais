/**
 * A stored text field, trimmed — or nothing when it is absent, not a string, or only spaces.
 *
 * <p>Guarded by `typeof`, not by `!== undefined`: this is JSON a person edits, and a hand-written
 * `"remoteVendor": null` passes an undefined check and then has `.trim()` called on it, which throws
 * inside the settings sync — and a sync that throws leaves the server on the file's previous contents
 * with nothing saying the write never happened. A name made only of spaces is absent for the same
 * reason. (Both raised on the remote-vendor code round.)</p>
 */
export function saidText(value: unknown, casing: 'as-is' | 'lower' = 'as-is'): string | undefined {
  const text = typeof value === 'string' ? value.trim() : '';

  return text.length === 0 ? undefined : CASED[casing](text);
}

const CASED: Readonly<Record<'as-is' | 'lower', (text: string) => string>> = {
  'as-is': (text) => text,
  lower: (text) => text.toLowerCase(),
};
