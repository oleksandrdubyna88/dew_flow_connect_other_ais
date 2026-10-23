import { namesACredential } from './credentialWords';

/**
 * A credential label that ENDS a line, and the value that continues on the next — taken out whole, the
 * layout kept. The pass `safeText` runs after the private-key blocks and BEFORE the strip that glues the
 * lines.
 *
 * <p><b>Why a pass of its own.</b> The labelled rule is a ONE-LINE shape: its value class excludes every
 * white-space character, a line break included, so `"api_key":` followed by its value on the next line was
 * served whole — and so was a YAML block scalar's second line, and a folded header's value. Letting the
 * regex joiner cross a line was evaluated and refused (`todo/PLAN_redaction_reaches_a_value_on_the_next_line.md`,
 * D2): in `service:` LF `  api_key: FAKE` the leftmost match would start at the ordinary word `service`, take
 * `api_key:` for its value and never see the credential. A regex cannot know a label names a credential and
 * cannot compare indentation; this pass can do both.</p>
 *
 * <p><b>The contract is `src_mcp/core/Notices/LabelContinuations.cs`, byte for byte</b>, as
 * `privateKeyBlocks.ts` is `PrivateKeyBlocks.cs`: both halves redact `server-notices.jsonl`, and
 * `run-parity.mjs` runs both over the same shapes with REAL line breaks.</p>
 *
 * <p><b>The matching model.</b> At every line break the pass reads the line it has just passed, from its END
 * backwards: at most four trailing spaces, an optional `\`, an optional YAML block indicator (`|` or `>`, a
 * sign, a digit) or an opening quote run, at most 32 spaces, `:` or `=`, at most four spaces, an optional
 * quote run, and the label — the LAST 64 label characters, cut on the left to begin at a letter, which is what
 * the regex's leftmost start produces. The label must name a credential. Five kinds of evidence let it reach
 * the next line, and with none of them the text is untouched: the next line is indented DEEPER (a tab counts
 * one); the next line begins with a quote; the label's line ended with `\`; the label's line ended with a
 * block indicator (every following line that is BLANK or deeper is value, until the first non-blank line that
 * is not, or the end of the text); the label's line ended with a triple quote (every following line is value
 * until the line holding the closing triple quote, kept, its text before the quotes masked).</p>
 *
 * <p><b>A plain continuation declines when the next line is itself a label assignment</b>: a
 * credential-named CONTAINER (`credentials:` LF `  password: FAKE`) must leave its child to the labelled rule,
 * which needs the child's label to mask its value. It crosses exactly ONE line break and never a blank line.
 * Inside a block every line is value, assignment-shaped or not.</p>
 *
 * <p><b>Linear by construction, with no length bound of its own.</b> One cursor that only advances; a line's
 * end is found once, its indentation counted once per reading, and the label window re-read at a line's end
 * is about a hundred characters. A block ends by its STRUCTURE or at the end of the text — a cap would leave
 * the tail of a longer scalar readable — so there is no "bound reached" state to fail closed from, and no
 * regular expression to run away.</p>
 */

/** The longest label: one letter and 63 more, the labelled rule's own bound. */
export const LABEL_LIMIT = 64;

/** The longest value token, the labelled rule's own bound — the tail past it stays "where both halves leave it". */
export const VALUE_LIMIT = 4096;

const TRAILING_GAP = 4;
const AFTER_JOINER_GAP = 32;
const BEFORE_JOINER_GAP = 4;
const QUOTE_RUN = 3;
const TRIPLE_DOUBLE = '"""';
const TRIPLE_SINGLE = "'''";

/** One line: where it starts, where its text ends (the break, or the end of the text) and where the next one starts. */
interface Line {
  readonly start: number;
  readonly end: number;
  readonly next: number;
}

/** What the end of a label's line asks for. */
type Opening = 'plain' | 'indicator' | 'triple-double' | 'triple-single';

interface Ending {
  readonly opening: Opening;
  readonly backslash: boolean;
}

/** The text with every continued credential value replaced by `redacted`, line breaks kept. */
export function redactLabelContinuations(text: string, redacted: string): string {
  if (!text.includes('\n') && !text.includes('\r')) {
    return text;
  }
  const writer = new Writer(text, redacted);
  let at = 0;
  while (at < text.length) {
    const line = lineAt(text, at);
    const ending = line.end < text.length ? readEnding(text, line) : undefined;
    at = ending === undefined ? line.next : continueAfter(writer, text, line, ending);
  }

  return writer.finish();
}

/** Applies the evidence of one label's line and answers where the scan goes on. */
function continueAfter(writer: Writer, text: string, line: Line, ending: Ending): number {
  switch (ending.opening) {
    case 'indicator':
      return maskIndented(writer, text, line.next, indentEnd(text, line.start, line.end) - line.start);
    case 'triple-double':
      return maskTriple(writer, text, line.next, TRIPLE_DOUBLE);
    case 'triple-single':
      return maskTriple(writer, text, line.next, TRIPLE_SINGLE);
    default:
      maskPlain(writer, text, line, ending.backslash);

      // The continuation line is read again as a label's line of its own: it may end in one.
      return line.next;
  }
}

// ---------- the label's line, read from its end ----------

/** Reads `label ["'] [ \t]{0,4} [:=] [ \t]{0,32} (indicator | quote run)? \? [ \t]{0,4}` backwards from the line's end. */
function readEnding(text: string, line: Line): Ending | undefined {
  const floor = line.start;
  let at = skipBack(text, line.end, floor, TRAILING_GAP, isBlank);
  const backslash = at > floor && text[at - 1] === '\\';
  at -= backslash ? 1 : 0;
  const opened = readOpening(text, floor, at);
  at = skipBack(text, opened.at, floor, AFTER_JOINER_GAP, isBlank);
  if (at <= floor || (text[at - 1] !== ':' && text[at - 1] !== '=')) {
    return undefined;
  }
  at = skipBack(text, at - 1, floor, BEFORE_JOINER_GAP, isBlank);
  at = skipBack(text, at, floor, QUOTE_RUN, isQuote);

  return namesACredentialBefore(text, floor, at) ? { opening: opened.opening, backslash } : undefined;
}

/** A block indicator (`[|>][-+]?[0-9]?`) or an opening quote run at the end; `at` is where it begins. */
function readOpening(text: string, floor: number, end: number): { readonly opening: Opening; readonly at: number } {
  let probe = end;
  probe -= probe > floor && isAsciiDigit(text[probe - 1]) ? 1 : 0;
  probe -= probe > floor && (text[probe - 1] === '-' || text[probe - 1] === '+') ? 1 : 0;
  if (probe > floor && (text[probe - 1] === '|' || text[probe - 1] === '>')) {
    return { opening: 'indicator', at: probe - 1 };
  }
  if (end <= floor || !isQuote(text[end - 1])) {
    return { opening: 'plain', at: end };
  }
  const quote = text[end - 1];
  let run = 0;
  while (run < QUOTE_RUN && end - run > floor && text[end - run - 1] === quote) {
    run += 1;
  }
  const at = end - run;
  if (run < QUOTE_RUN) {
    return { opening: 'plain', at };
  }

  return { opening: quote === '"' ? 'triple-double' : 'triple-single', at };
}

/** Whether the label ending at `labelEnd` — its last 64 label characters, cut to begin at a letter — names a credential. */
function namesACredentialBefore(text: string, floor: number, labelEnd: number): boolean {
  let start = labelEnd;
  while (start > floor && labelEnd - start < LABEL_LIMIT && isLabelCharacter(text[start - 1])) {
    start -= 1;
  }
  while (start < labelEnd && !isAsciiLetter(text[start])) {
    start += 1;
  }

  return start < labelEnd && namesACredential(text.slice(start, labelEnd));
}

// ---------- the plain continuation ----------

/** Masks the value token on the line after a label's line, when there is evidence and the line is not a child assignment. */
function maskPlain(writer: Writer, text: string, label: Line, backslash: boolean): void {
  const next = lineAt(text, label.next);
  const indent = indentEnd(text, next.start, next.end);
  if (indent >= next.end || !hasEvidence(text, label, indent - next.start, text[indent], backslash) || isAssignment(text, indent, next.end)) {
    return;
  }
  let valueStart = indent;
  while (valueStart < next.end && valueStart - indent < QUOTE_RUN && isQuote(text[valueStart])) {
    valueStart += 1;
  }
  let valueEnd = valueStart;
  while (valueEnd < next.end && valueEnd - valueStart < VALUE_LIMIT && isValueCharacter(text[valueEnd])) {
    valueEnd += 1;
  }
  if (valueEnd > valueStart) {
    writer.mask(valueStart, valueEnd);
  }
}

function hasEvidence(text: string, label: Line, nextIndent: number, first: string, backslash: boolean): boolean {
  return backslash || isQuote(first) || nextIndent > indentEnd(text, label.start, label.end) - label.start;
}

/** Whether a line (after its indentation) is itself `["']? label ["']{0,3} [ \t]{0,4} [:=]` — and not base64 padding. */
function isAssignment(text: string, from: number, to: number): boolean {
  let at = from;
  at += at < to && isQuote(text[at]) ? 1 : 0;
  if (at >= to || !isAsciiLetter(text[at])) {
    return false;
  }
  let labelEnd = at;
  while (labelEnd < to && labelEnd - at < LABEL_LIMIT && isLabelCharacter(text[labelEnd])) {
    labelEnd += 1;
  }
  if (labelEnd < to && isLabelCharacter(text[labelEnd])) {
    return false;
  }
  at = labelEnd;
  while (at < to && at - labelEnd < QUOTE_RUN && isQuote(text[at])) {
    at += 1;
  }
  const gap = at;
  while (at < to && at - gap < BEFORE_JOINER_GAP && isBlank(text[at])) {
    at += 1;
  }

  return at < to && (text[at] === ':' || (text[at] === '=' && !(at + 1 < to && text[at + 1] === '=')));
}

// ---------- the blocks ----------

/** A block scalar: every following line that is blank or deeper than the label's line is value. Answers where the block ended. */
function maskIndented(writer: Writer, text: string, from: number, labelIndent: number): number {
  let at = from;
  while (at < text.length) {
    const line = lineAt(text, at);
    const indent = indentEnd(text, line.start, line.end);
    if (indent < line.end && indent - line.start <= labelIndent) {
      return at;
    }
    if (indent < line.end) {
      writer.mask(indent, line.end);
    }
    at = line.next;
  }

  return text.length;
}

/** A triple-quoted value: every following line is value up to the line that holds the closing quotes. Answers that line, to be read as an ordinary one. */
function maskTriple(writer: Writer, text: string, from: number, quotes: string): number {
  let at = from;
  while (at < text.length) {
    const line = lineAt(text, at);
    const close = closingQuotes(text, line, quotes);
    const maskEnd = close >= 0 ? close : line.end;
    const indent = indentEnd(text, line.start, maskEnd);
    if (indent < maskEnd) {
      writer.mask(indent, maskEnd);
    }
    if (close >= 0) {
      return line.start;
    }
    at = line.next;
  }

  return text.length;
}

/** Where the closing quotes start within the line, or -1. */
function closingQuotes(text: string, line: Line, quotes: string): number {
  // Within the line, never to the end of the text: `indexOf` has no upper bound, and a scan past the line is quadratic.
  const found = text.slice(line.start, line.end).indexOf(quotes);

  return found >= 0 ? line.start + found : -1;
}

// ---------- lines and characters ----------

function lineAt(text: string, start: number): Line {
  let end = start;
  while (end < text.length && text[end] !== '\n' && text[end] !== '\r') {
    end += 1;
  }
  let next = end;
  if (end < text.length) {
    next = text[end] === '\r' && end + 1 < text.length && text[end + 1] === '\n' ? end + 2 : end + 1;
  }

  return { start, end, next };
}

function indentEnd(text: string, from: number, to: number): number {
  let at = from;
  while (at < to && isBlank(text[at])) {
    at += 1;
  }

  return at;
}

function skipBack(text: string, from: number, floor: number, limit: number, accepts: (character: string) => boolean): number {
  let at = from;
  while (at > floor && from - at < limit && accepts(text[at - 1])) {
    at -= 1;
  }

  return at;
}

function isBlank(character: string): boolean {
  return character === ' ' || character === '\t';
}

function isQuote(character: string): boolean {
  return character === '"' || character === "'";
}

function isAsciiLetter(character: string): boolean {
  return (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
}

function isAsciiDigit(character: string): boolean {
  return character >= '0' && character <= '9';
}

function isLabelCharacter(character: string): boolean {
  return isAsciiLetter(character) || isAsciiDigit(character) || character === '_' || character === '.' || character === '-';
}

/** The labelled rule's value class: not white space (JavaScript's own `\s`), and none of `"',;)}`. */
function isValueCharacter(character: string): boolean {
  return !/\s/.test(character) && !'"\',;)}'.includes(character);
}

/** Copies the text through, with ranges replaced by one placeholder each; the cursor of the replacements only advances. */
class Writer {
  readonly #text: string;
  readonly #redacted: string;
  readonly #parts: string[] = [];
  #written = 0;
  #changed = false;

  constructor(text: string, redacted: string) {
    this.#text = text;
    this.#redacted = redacted;
  }

  mask(from: number, to: number): void {
    this.#parts.push(this.#text.slice(this.#written, from), this.#redacted);
    this.#written = to;
    this.#changed = true;
  }

  finish(): string {
    return this.#changed ? this.#parts.join('') + this.#text.slice(this.#written) : this.#text;
  }
}
