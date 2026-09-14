/**
 * The rules every list a PERSON composes shares, in one place.
 *
 * <p>Pure, with no `vscode` handle, so each rule below is a unit test rather than a claim — the same
 * reason `chatPresets.ts` gives for its own purity, and these functions are its own: they were
 * private there until a second list needed exactly them.</p>
 *
 * <p><b>Why this module exists at all.</b> A phrase library and a prompt library are different
 * features with different homes, and they are the same BOUNDARY: an array in `settings.json` that
 * somebody edits by hand, where a mistyped row must never take the rest of the list with it and an
 * absent id must never make two buttons ambiguous. Writing those rules twice is how the two copies
 * start to disagree — the second one is the defect from the moment it compiles. So the rules moved
 * here and both lists call them; `chatPresets.ts` behaves exactly as it did, which its own tests are
 * what prove.</p>
 *
 * <p>The only thing that CHANGED in the move is that `withId` and `freshId` take the id prefix they
 * used to hard-code, because `preset-3` is not a name a phrase should wear.</p>
 */

/** How much of a name fits on a button in a row of buttons, before the row stops being a row. */
export const NAME_LIMIT = 60;

/** A string from a file a person edits: trimmed, and empty for anything that is not one. */
export function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

/** A record, or nothing — `settings.json` can hold a string, a number or a null in an array. */
export function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/**
 * An id for every row, unique across the list.
 *
 * <p>A person editing `settings.json` by hand will not write one, and two rows sharing an id makes a
 * click ambiguous — a button names the row it chose and the host looks it up. So a missing or
 * repeated id is replaced by a positional one, which is stable for as long as the list is not
 * reordered and is regenerated the moment it is.</p>
 */
export function withId(candidate: string, index: number, taken: Set<string>, prefix: string): string {
  const id = candidate.length > 0 && !taken.has(candidate) ? candidate : `${prefix}-${index + 1}`;
  taken.add(id);

  return id;
}

/**
 * A name for a row that has none, taken from its own first words.
 *
 * <p>So a person recognises it on a button as the thing THEY wrote — a generic label would make
 * their own writing look like something this product put there.</p>
 */
export function nameFor(body: string): string {
  const firstLine = body.split('\n')[0] ?? body;

  return firstLine.length <= NAME_LIMIT ? firstLine : `${firstLine.slice(0, NAME_LIMIT - 1)}…`;
}

/** An id for a row being created now, which no existing row can already hold. */
export function freshId(taken: readonly { readonly id: string }[], prefix: string): string {
  return `${prefix}-${Date.now().toString(36)}-${taken.length + 1}`;
}

/** The row a button named, or nothing — a click naming an id that is gone chooses nothing. */
export function rowById<T extends { readonly id: string }>(
  rows: readonly T[],
  id: string,
): T | undefined {
  return id.length === 0 ? undefined : rows.find((row) => row.id === id);
}
