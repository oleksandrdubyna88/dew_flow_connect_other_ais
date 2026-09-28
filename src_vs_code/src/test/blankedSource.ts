/**
 * TypeScript source with every comment, string and regular expression blanked, for the structural tests.
 *
 * <p>Moved out of `panelsAreSearchable.test.ts` (2026-09-28) when a second scan needed exactly it: the
 * queued-write guard in `aQueuedWriteNeverWaitsOnItself.test.ts` counts braces and matches calls, and
 * both go wrong when a `}` or a `this.render(` sits inside a string or a comment.</p>
 */

/** Where a `/` opens a regular expression rather than dividing. The standard previous-token rule. */
const BEFORE_REGEX = /[(,=:[!&|?{};+\-*%~^]/;

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
  const blank = (from: number, to: number): void => {
    for (let k = from; k < to && k < out.length; k += 1) {
      if (out[k] !== '\n') {
        out[k] = ' ';
      }
    }
  };
  const closes = (at: number, quote: string): number => {
    for (let j = at + 1; j < source.length; j += 1) {
      if (source[j] === '\\') {
        j += 1;
      } else if (source[j] === quote) {
        return j;
      }
    }

    return source.length;
  };
  const endOfRegex = (at: number): number => {
    let inClass = false;
    for (let j = at + 1; j < source.length; j += 1) {
      const c = source[j];
      if (c === '\\') {
        j += 1;
      } else if (c === '[') {
        inClass = true;
      } else if (c === ']') {
        inClass = false;
      } else if (c === '\n' || (c === '/' && !inClass)) {
        return j;
      }
    }

    return source.length;
  };

  let previous = '';
  let i = 0;
  while (i < source.length) {
    const here = source[i];
    const after = source[i + 1] ?? '';
    if (here === '/' && after === '/') {
      const stop = source.indexOf('\n', i);
      const end = stop < 0 ? source.length : stop;
      blank(i, end);
      i = end;
    } else if (here === '/' && after === '*') {
      const stop = source.indexOf('*/', i + 2);
      const end = stop < 0 ? source.length : stop + 2;
      blank(i, end);
      i = end;
    } else if (here === '\'' || here === '"' || here === '`') {
      const end = closes(i, here);
      blank(i + 1, end);
      i = end + 1;
    } else if (here === '/' && (previous === '' || BEFORE_REGEX.test(previous))) {
      const end = endOfRegex(i);
      blank(i + 1, end);
      i = end + 1;
    } else {
      if (here.trim().length > 0) {
        previous = here;
      }
      i += 1;
    }
  }

  return out.join('');
}
