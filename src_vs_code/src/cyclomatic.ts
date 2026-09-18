import { type CorpusLanguage, corpusLanguage } from './corpusLanguage';

/**
 * Cyclomatic complexity of ONE method, computed from its SKELETON.
 *
 * <p>Pure, and free of `node:` and `vscode`, because the review page calls it — and computed here
 * rather than stored, because nothing about it needs the server. The skeleton is enough: `Normalise`
 * renames identifiers and leaves every other token exactly as it was, so `if`, `for`, `case`,
 * `catch`, `&&`, `||`, `??` and a ternary's `?` all survive anonymisation verbatim (measured on the
 * normaliser's own fixtures; `cyclomatic.test.ts` asserts it over a normalised method). A number that
 * could be read off a column the page already holds is not worth a column of its own.</p>
 *
 * <p><b>What it counts.</b> McCabe's number the way the family's own analysers count it: one, plus
 * one per `if`, loop (`for`, `foreach`, `while` — `do` is counted at its `while`), `case` label,
 * `catch`, C# `when` guard, `&&`, `||`, `??` (and `??=`), and conditional `? :`. `else`, `default`,
 * `finally` and `try` open no new path and add nothing.</p>
 *
 * <p><b>What it does not count, said rather than hidden.</b> The arms of a C# switch EXPRESSION
 * (`x switch { 1 => a, _ => b }`) and the `and`/`or` pattern combinators — both read as one
 * expression here, which undercounts a method written that way; the family's C# doctrine treats a
 * switch expression as the way to bring a number DOWN, so this errs on its side. A regex literal in
 * JavaScript or TypeScript is not told from division and its contents are scanned as code. The holes
 * of a template literal (`${…}`) and of a C# interpolated string are treated as text, so a ternary
 * inside one is missed; so is a conditional written without spaces (`a?b:c`), which real source in
 * the corpus does not do and minified source would. None of these is common in one method of the
 * corpus; all of them are why the page calls this a count of the SKELETON and labels which revision
 * it describes.</p>
 *
 * <p><b>Comments and string literals are blanked first, and that is the trap rather than an edge
 * case.</b> The corpus keeps both verbatim, so `// if this races` and `"for"` are things a skeleton
 * really contains, and a bare word count would report them as decisions. The scanner handles the
 * three languages' literal shapes: line comments and block comments, `"…"` with escapes, `'…'`,
 * template literals, and C#'s `@"…"` (with its doubled quote) and `"""…"""`. (The block-comment
 * closer is not spelled in this docblock, for the reason the TypeScript doctrine's second trap
 * gives about backticks: a closer inside a comment ends the comment, and the compiler reports it
 * forty lines away.)</p>
 *
 * <p><b>A number that could not be taken is not zero.</b> A language this page does not read gets
 * `known: false` and a sentence, never a count computed with the wrong keyword table — the
 * coding-style rule's "absent is not zero", and the highlighter's `plain` fallback, applied here.</p>
 */

/** The count, or why there is none. */
export type Complexity =
  | { readonly known: true; readonly value: number }
  | { readonly known: false; readonly why: string };

/**
 * The words that open a second path, per language.
 *
 * <p>A skeleton's only words are keywords, runtime vocabulary and placeholders, so a per-language
 * table is a small courtesy rather than a necessity: `foreach` and `when` cannot appear in a
 * TypeScript skeleton as anything but a runtime member name, which no runtime has. Kept explicit so
 * that the day a fourth language arrives, the table is a decision rather than a coincidence.</p>
 */
const BRANCH_WORDS: Readonly<Record<CorpusLanguage, RegExp>> = {
  csharp: /\b(?:if|for|foreach|while|case|catch|when)\b/gu,
  typescript: /\b(?:if|for|while|case|catch)\b/gu,
  javascript: /\b(?:if|for|while|case|catch)\b/gu,
};

/**
 * `&&`, `||`, `??` (which also matches the head of `??=`), and a conditional's `?`.
 *
 * <p>The conditional is the one spelled with whitespace on BOTH sides. That is what tells it from
 * a nullable type (`string?`, glued to its type), a null-conditional access (`x?.y`, `x?[0]`), a
 * TypeScript optional (`x?: T`) and the null-coalescing pair itself — in `a ?? b` neither `?` has
 * whitespace on both sides, so the pair is counted once, by its own alternative.</p>
 */
const BRANCH_OPERATORS = /&&|\|\||\?\?|(?<=\s)\?(?=\s)/gu;

const occurrences = (text: string, pattern: RegExp): number => [...text.matchAll(pattern)].length;

/** The name a refusal reports — the column's own text, or the words for an empty one. */
const named = (language: unknown): string =>
  (typeof language === 'string' && language.trim().length > 0 ? language.trim() : 'no language');

/**
 * The complexity of one skeleton, in the language its row says it is in.
 *
 * @param code the skeleton — the text the page shows, not the original source, which it never has.
 * @param language the row's `language` column, in whatever spelling the column holds.
 */
export function cyclomatic(code: string, language: string): Complexity {
  const known = corpusLanguage(language);
  if (known === undefined) {
    return { known: false, why: `not computed: ${named(language)} is not a language this page reads` };
  }
  const bare = withoutLiterals(code);

  return {
    known: true,
    value: 1 + occurrences(bare, BRANCH_WORDS[known]) + occurrences(bare, BRANCH_OPERATORS),
  };
}

/**
 * The code with every comment and string literal blanked to one space, so a keyword inside one is
 * not a decision and a word boundary beside one still is.
 *
 * <p>Exported so the scanner can be tested on its own: which literal shapes it knows is the whole
 * of what makes the count honest, and a count-level test cannot say WHICH shape it misread.</p>
 */
export function withoutLiterals(code: string): string {
  let bare = '';
  let at = 0;
  while (at < code.length) {
    const end = literalEnd(code, at);
    if (end === undefined) {
      bare += code[at];
      at += 1;
    } else {
      // A literal that can INTERPOLATE keeps the expressions in its holes; everything else — a
      // plain string, a raw string, every comment — goes entirely. Both halves of that sentence
      // were learned the hard way: blanking wholesale lost the decision inside `${x ? 1 : 0}`,
      // and then keeping braces everywhere counted `// { if }` as a branch.
      const literal = code.slice(at, end);
      bare += interpolates(code, at) ? keptFrom(literal) : ' '.repeat(literal.length);
      at = end;
    }
  }

  return bare;
}

/**
 * One literal, reduced to just the expressions interpolated into it.
 *
 * <p>Everything that is not inside a brace pair becomes a space, so the literal's own punctuation —
 * a question mark in prose, a `:` in a URL — cannot be read as a branch. What IS inside comes
 * through unchanged and is then scanned like any other code, including nested literals, because
 * `withoutLiterals` runs over it in turn.</p>
 *
 * <p>`{{` and `}}` are an escaped brace in a C# interpolated string and open nothing; a `$` is not
 * required, because a verbatim `@"…"` can still interpolate when written `@$"…"`, and treating a
 * plain `{` in ordinary text as an interpolation costs only that its contents are scanned — they
 * are prose, and prose contains no keywords this counts.</p>
 */
function keptFrom(literal: string): string {
  const template = literal.startsWith('`');
  let kept = '';
  let depth = 0;
  let at = 0;
  while (at < literal.length) {
    const here = literal[at];
    // `{{` and `}}` are an escaped brace in a C# interpolated string and open nothing. They are
    // NOT that in a template literal, where `${{ a: 1 }}` is an object in a hole — reading the
    // doubled brace as an escape there skipped the hole entirely, and a hole ending `}}` left the
    // depth stuck open so every word after it counted. (Code round, antigravity.)
    if (!template && here !== undefined && literal[at + 1] === here && (here === '{' || here === '}')) {
      kept += '  ';
      at += 2;
      continue;
    }
    // A hole opens at `${` in a template literal and at a bare `{` in C#. Outside one, the two
    // characters of `${` are text like the rest.
    const opens = template ? here === '$' && literal[at + 1] === '{' : here === '{';
    if (opens && depth === 0) {
      depth = 1;
      kept += template ? '  ' : ' ';
      at += template ? 2 : 1;
      continue;
    }
    if (depth > 0 && here === '{') {
      depth += 1;
      kept += here;
    } else if (depth > 0 && here === '}') {
      depth -= 1;
      kept += depth === 0 ? ' ' : here;
    } else {
      kept += depth > 0 ? here : ' ';
    }
    at += 1;
  }

  return withoutLiterals(kept);
}

/** Where the literal or comment that starts at `at` ends (exclusive), or nothing when none starts there. */
function literalEnd(code: string, at: number): number | undefined {
  const here = code[at];
  const next = code[at + 1];
  if (here === '/' && next === '/') {
    return lineEnd(code, at);
  }
  if (here === '/' && next === '*') {
    return closingOf(code, at + 2, '*/');
  }

  return stringEnd(code, at, here, next);
}

/**
 * Whether what starts at `at` can INTERPOLATE — the only literal whose braces mean anything.
 *
 * <p>This distinction is the whole of a regress I introduced and the code round caught four times.
 * Fixing the template-literal case by keeping whatever sat inside braces applied that rule to every
 * literal AND to comments, so `// { if }` counted a branch and `"{ if for while }"` counted three —
 * a worse trade than the understatement it replaced, because braces in prose are common and a
 * complexity that moves when only a comment moves invents a change the control flow does not have.</p>
 *
 * <p>Three forms interpolate and nothing else does: a JS/TS template literal (backtick), and C#'s
 * `$"…"` and its verbatim pairings `$@"…"` / `@$"…"`. A plain `"…"`, a `'…'`, a `@"…"` without the
 * `$`, a raw `"""…"""` and every comment are text from end to end.</p>
 */
function interpolates(code: string, at: number): boolean {
  const here = code[at];
  if (here === '`') {
    return true;
  }
  if (here === '$') {
    return code[at + 1] === '"' || code.startsWith('@"', at + 1);
  }

  return here === '@' && code.startsWith('$"', at + 1);
}

/** The string literal that starts at `at`, in any of the three languages' spellings. */
function stringEnd(code: string, at: number, here: string | undefined, next: string | undefined): number | undefined {
  if (code.startsWith('"""', at)) {
    return closingOf(code, at + 3, '"""');
  }
  // C#'s verbatim forms: @"…", @$"…" and $@"…". A doubled quote is a quote, and a backslash is
  // just a backslash — the opposite of the escaped form, so it gets a scanner of its own.
  if (here === '@' && (next === '"' || code.startsWith('$"', at + 1))) {
    return verbatimEnd(code, code.indexOf('"', at) + 1);
  }
  if (here === '$' && code.startsWith('@"', at + 1)) {
    return verbatimEnd(code, at + 3);
  }
  // `$"…"` — C#'s plain interpolated string, with ordinary escapes. It was missing: `$@"…"` and
  // `@$"…"` were both handled and this one fell through, so the `$` passed as code and the quote
  // opened a bare string that ended at the first quote INSIDE the interpolation. It happened to
  // give the right answer while every literal was blanked wholesale, and stopped the moment holes
  // started being scanned. Found by the code round's C# case going red after the real fix.
  if (here === '$' && next === '"') {
    return quotedEnd(code, at + 2, '"');
  }
  if (here === '"' || here === '\'' || here === '`') {
    return quotedEnd(code, at + 1, here);
  }

  return undefined;
}

/** The end of the line a `//` comment sits on — the newline stays, so line counts elsewhere hold. */
function lineEnd(code: string, at: number): number {
  const newline = code.indexOf('\n', at);

  return newline < 0 ? code.length : newline;
}

/** Just past `close`, or the end of the text when it never closes. */
function closingOf(code: string, from: number, close: string): number {
  const shut = code.indexOf(close, from);

  return shut < 0 ? code.length : shut + close.length;
}

/**
 * Just past the closing `quote`, honouring backslash escapes.
 *
 * <p>A `"` or `'` literal cannot span a line in any of the three languages, so a newline ends it
 * too — which is also what keeps a stray apostrophe from swallowing the rest of a method. A template
 * literal spans lines by design and is read to its backtick.</p>
 */
function quotedEnd(code: string, from: number, quote: string): number {
  for (let at = from; at < code.length; at += 1) {
    const here = code[at];
    if (here === '\\') {
      at += 1;
    } else if (here === quote || (here === '\n' && quote !== '`')) {
      return at + 1;
    }
  }

  return code.length;
}

/** Just past the closing quote of a verbatim string, where `""` is one quote and `\` is nothing special. */
function verbatimEnd(code: string, from: number): number {
  for (let at = from; at < code.length; at += 1) {
    if (code[at] === '"') {
      if (code[at + 1] === '"') {
        at += 1;
      } else {
        return at + 1;
      }
    }
  }

  return code.length;
}
