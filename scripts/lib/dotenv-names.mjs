// lib/dotenv-names.mjs — DL2's names grammar (todo/PLAN_kimi_and_vibe_on_linux.md) as a REFERENCE: what coai reads from the
// person's ~/.vibe/.env before it links that file into an owned Vibe home. Names only — everything after the `=` is the value,
// and the value is never parsed, kept or returned. The product's C# (E2) is held to the verdicts this reference recorded in
// shared/dotenv-name-vectors.json, and those verdicts are held to python-dotenv itself (L-V6b, scripts/dotenv-name-vectors.mjs).
//
// Per PHYSICAL line (python-dotenv's line break: CRLF, LF or a bare CR — nothing else, DL2): leading whitespace skipped; a blank
// line or a `#` line is nothing; an optional `export` followed by whitespace; the NAME — `'…'` (no `'` inside, on this line) or a
// run of characters other than `=`, `#` and whitespace; optional whitespace; `=`. Read without backtracking, as python-dotenv's
// reader does (`export =v` is unreadable, not a variable named `export`). Whitespace is python-dotenv's `\s` — Python's Unicode
// whitespace, NEL and the separators included — and the oracle run checks this set against Python's own.

/** The one name the person's Vibe .env may bind (DL2). */
export const ADMITTED_NAME = 'MISTRAL_API_KEY';

/** Python's `\s` for str patterns (Py_UNICODE_ISSPACE): what python-dotenv's `[^\S\r\n]`, `\s*` and `[^=\#\s]` are built on. */
export const PYTHON_WHITESPACE = new Set([
  ...[0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x85, 0xa0, 0x1680],
  ...Array.from({ length: 0x200a - 0x2000 + 1 }, (_, i) => 0x2000 + i),
  ...[0x2028, 0x2029, 0x202f, 0x205f, 0x3000],
].map((code) => String.fromCodePoint(code)));

/** The file's physical lines: split at CRLF, LF or a bare CR, exactly as python-dotenv's `_newline` (and Python's universal newlines) do. */
export const physicalLines = (text) => String(text).split(/\r\n|\n|\r/u);

const isSpace = (ch) => PYTHON_WHITESPACE.has(ch);
const skipSpace = (line, at) => {
  let i = at;
  while (i < line.length && isSpace(line[i])) i++;
  return i;
};

const SKIP = { kind: 'skip' };
const UNREADABLE = { kind: 'unreadable' };

/** `export` + at least one whitespace character at `at`: where the name starts after it, or `at` when there is no such prefix. */
function afterExport(line, at) {
  return line.startsWith('export', at) && isSpace(line[at + 6] ?? '') ? skipSpace(line, at + 6) : at;
}

/** The name at `at` and where it ends, or null: `'…'` closed on this line and not empty, else a run up to `=`, `#` or whitespace. */
function nameAt(line, at) {
  if (line[at] === "'") {
    const close = line.indexOf("'", at + 1);
    return close > at + 1 ? { name: line.slice(at + 1, close), end: close + 1 } : null;
  }
  let end = at;
  while (end < line.length && line[end] !== '=' && line[end] !== '#' && !isSpace(line[end])) end++;
  return end > at ? { name: line.slice(at, end), end } : null;
}

/** One physical line: nothing (blank or comment), a binding of a name, or unreadable. */
export function readLine(line) {
  const start = skipSpace(line, 0);
  if (start === line.length || line[start] === '#') return SKIP;
  const found = nameAt(line, afterExport(line, start));
  if (found === null) return UNREADABLE;
  return line[skipSpace(line, found.end)] === '=' ? { kind: 'binding', name: found.name } : UNREADABLE;
}

/**
 * coai's verdict on a whole file: `admitted`, or `refused` at the FIRST refused line — `unreadable` (a line that binds no name:
 * a bare word, a pasted key, a line starting with `=`) or `another-variable` (a name that is not MISTRAL_API_KEY). Every refused
 * line is in `refusals`. A sentence built from it names line numbers only — never a name or a byte of the file (DL2).
 */
export function coaiVerdict(text) {
  const names = [];
  const refusals = [];
  physicalLines(text).forEach((line, index) => {
    const read = readLine(line);
    if (read.kind === 'skip') return;
    if (read.kind === 'unreadable') {
      refusals.push({ line: index + 1, why: 'unreadable' });
      return;
    }
    names.push(read.name);
    if (read.name !== ADMITTED_NAME) refusals.push({ line: index + 1, why: 'another-variable' });
  });
  return refusals.length === 0
    ? { verdict: 'admitted', refusals: [], names }
    : { verdict: 'refused', line: refusals[0].line, why: refusals[0].why, refusals, names };
}
