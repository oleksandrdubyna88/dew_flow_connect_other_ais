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

/** A string from a file a person edits, exactly as they wrote it — empty for anything that is not one. */
export function rawText(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** A string from a file a person edits: trimmed, and empty for anything that is not one. */
export function text(value: unknown): string {
  return rawText(value).trim();
}

/**
 * Whether a value is a string with anything in it but whitespace — without copying it to find out.
 *
 * <p>A trim to test for emptiness allocates a second string the size of the first, and a saved row
 * can hold a pasted transcript. The test is the same question asked without the copy.</p>
 */
export function hasWords(value: unknown): boolean {
  return typeof value === 'string' && /\S/.test(value);
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
 *
 * <p><b>And the positional name is checked against the ones already given out</b>, which it was not
 * until the code round of 2026-09-14 said so. A row that writes `phrase-2` by hand and a later row
 * with no id at index 1 both answered to `phrase-2`, and the second could not be copied at all: the
 * lookup returned the first. A hand-written id that happens to look positional is not exotic —
 * anybody who has seen this file's own repaired ids will type one.</p>
 */
export function withId(candidate: string, index: number, taken: Set<string>, prefix: string): string {
  const id = candidate.length > 0 && !taken.has(candidate) ? candidate : freeId(prefix, index + 1, taken);
  taken.add(id);

  return id;
}

/** The first `prefix-n` from `n` upwards that nobody in this list is already using. */
function freeId(prefix: string, from: number, taken: Set<string>): string {
  let n = from;
  while (taken.has(`${prefix}-${n}`)) {
    n += 1;
  }

  return `${prefix}-${n}`;
}

/**
 * A name for a row that has none, taken from its own first words.
 *
 * <p>So a person recognises it on a button as the thing THEY wrote — a generic label would make
 * their own writing look like something this product put there.</p>
 *
 * <p><b>The first line that has WORDS in it, not simply the first line.</b> A body that opens with a
 * blank line — a pasted query, a snippet someone kept a gap above — named the button with the empty
 * string, and an empty label is a button nobody can see. (Code round, 2026-09-14.)</p>
 *
 * <p>It reads one line at a time rather than splitting: only the first line is wanted, and splitting
 * allocates every line of a body that may be a pasted transcript.</p>
 */
export function nameFor(body: string): string {
  const line = firstWords(body);

  return line.length <= NAME_LIMIT ? line : `${line.slice(0, NAME_LIMIT - 1)}…`;
}

/** Each line of a body, one at a time — the rest is never allocated if the first one answers. */
function* eachLine(body: string): Generator<string> {
  let from = 0;
  while (from <= body.length) {
    const nl = body.indexOf('\n', from);
    yield body.slice(from, nl === -1 ? body.length : nl);
    if (nl === -1) {
      return;
    }
    from = nl + 1;
  }
}

/** The first line with anything but whitespace on it, trimmed; empty when the body has none. */
function firstWords(body: string): string {
  for (const line of eachLine(body)) {
    const said = line.trim();
    if (said.length > 0) {
      return said;
    }
  }

  return '';
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
