/**
 * TypeScript source with every comment, string and regular expression blanked, for the structural tests.
 *
 * <p>Moved out of `panelsAreSearchable.test.ts` (2026-09-28) when a second scan needed exactly it: the
 * queued-write guard in `aQueuedWriteNeverWaitsOnItself.test.ts` counts braces and matches calls, and
 * both go wrong when a `}` or a `this.render(` sits inside a string or a comment.</p>
 */

/** Where a `/` opens a regular expression rather than dividing. The standard previous-token rule. */
const BEFORE_REGEX = /[(,=:[!&|?{};+\-*%~^]/;

/** What one comment or literal occupies: the range to blank, and where the scan resumes. */
interface Span {
  readonly from: number;
  readonly to: number;
  readonly next: number;
}

/** The index of the quote that closes the string opened at `at`, skipping escapes. */
function closes(source: string, at: number, quote: string): number {
  for (let j = at + 1; j < source.length; j += 1) {
    if (source[j] === '\\') {
      j += 1;
    } else if (source[j] === quote) {
      return j;
    }
  }

  return source.length;
}

/** The index of the `/` that closes the regular expression opened at `at` — one inside a class does not. */
function endOfRegex(source: string, at: number): number {
  let inClass = false;
  for (let j = at + 1; j < source.length; j += 1) {
    const c = source[j];
    if (c === '\\') {
      j += 1;
    } else if (c === '[' || c === ']') {
      inClass = c === '[';
    } else if (c === '\n' || (c === '/' && !inClass)) {
      return j;
    }
  }

  return source.length;
}

/** A line or block comment starting at `i`, delimiters included. */
function commentAt(source: string, i: number): Span | undefined {
  const opener = source.slice(i, i + 2);
  if (opener !== '//' && opener !== '/*') {
    return undefined;
  }
  const line = opener === '//';
  const stop = line ? source.indexOf('\n', i) : source.indexOf('*/', i + 2);
  const end = stop < 0 ? source.length : stop + (line ? 0 : 2);

  return { from: i, to: end, next: end };
}

/** A string or a regular expression starting at `i`, its delimiters kept. */
function literalAt(source: string, i: number, previous: string): Span | undefined {
  const here = source[i];
  if (here === '\'' || here === '"' || here === '`') {
    const end = closes(source, i, here);
    return { from: i + 1, to: end, next: end + 1 };
  }
  if (here === '/' && (previous === '' || BEFORE_REGEX.test(previous))) {
    const end = endOfRegex(source, i);
    return { from: i + 1, to: end, next: end + 1 };
  }

  return undefined;
}

/**
 * The source with every comment, string and regular expression blanked to spaces, delimiters kept.
 *
 * <p>Same length as the input on purpose: offsets stay meaningful, and a bracket inside a string can
 * no longer be counted by anything downstream. Not a parser — a `/` after an identifier is read as
 * division, so `return /x'/` would be misread. That case fails loudly rather than passing quietly,
 * which is the direction a guard is allowed to be wrong in.</p>
 */
export function blanked(source: string): string {
  const out = [...source];
  let previous = '';
  let i = 0;
  while (i < source.length) {
    const span = commentAt(source, i) ?? literalAt(source, i, previous);
    if (span === undefined) {
      const here = source.charAt(i);
      previous = here.trim().length > 0 ? here : previous;
      i += 1;
      continue;
    }
    for (let k = span.from; k < span.to && k < out.length; k += 1) {
      out[k] = out[k] === '\n' ? '\n' : ' ';
    }
    i = span.next;
  }

  return out.join('');
}
