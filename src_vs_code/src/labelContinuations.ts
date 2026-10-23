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
 * `run-parity.mjs` runs both over the same shapes with REAL line breaks. This half is split into small
 * functions because `eslint` holds `src/**` to a cyclomatic complexity of 4; the C# half keeps longer
 * methods. They are checked to answer identically, not to be shaped alike.</p>
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
 * <p><b>A label whose separator is `=` is a code or config ASSIGNMENT</b> (`WithoutTheKey =`, `type KeyRead =`,
 * `TheRoundsKey = """`): it never opens a block. It takes AT MOST the one next line, and only when that line is
 * shaped like a value (`isValueShaped`); an indicator or a triple quote on its line counts as the evidence. A
 * YAML list under a credential label has the value after each consecutive item's dash redacted, the dashes kept.</p>
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
const CASE_KEYWORD = 'case';

/** One line: where it starts, where its text ends (the break, or the end of the text) and where the next one starts. */
interface Line {
  readonly start: number;
  readonly end: number;
  readonly next: number;
}

/** What the end of a label's line asks for. */
type Opening = 'plain' | 'indicator' | 'triple-double' | 'triple-single';

/**
 * What a label's line ends with: the opening, whether a `\` ended it, and whether it is a CODE assignment — a separator of
 * `=` with something other than indentation and quotes before the label — which never opens a block (see `continueAfter`).
 */
interface Ending {
  readonly opening: Opening;
  readonly backslash: boolean;
  readonly assignment: boolean;
}

interface Opened {
  readonly opening: Opening;
  readonly at: number;
}

interface Separator {
  readonly at: number;
  readonly assignment: boolean;
}

/** The text with every continued credential value replaced by `redacted`, line breaks kept. */
export function redactLabelContinuations(text: string, redacted: string): string {
  if (!hasLineBreak(text)) {
    return text;
  }
  const writer = new Writer(text, redacted);
  let at = 0;
  while (at < text.length) {
    at = scanLine(writer, text, lineAt(text, at));
  }

  return writer.finish();
}

function hasLineBreak(text: string): boolean {
  return text.includes('\n') || text.includes('\r');
}

/** Reads one line as a label's line and answers where the scan goes on. */
function scanLine(writer: Writer, text: string, line: Line): number {
  const ending = line.end < text.length ? readEnding(text, line) : undefined;

  return ending === undefined ? line.next : continueAfter(writer, text, line, ending);
}

/**
 * Applies the evidence of one label's line and answers where the scan goes on.
 *
 * <p>A label whose separator is `=` is an ASSIGNMENT — `WithoutTheKey =`, `type KeyRead =`, `TheRoundsKey = """` — and what
 * follows it in code is an expression, not a secret scalar. It never opens a block: it takes AT MOST the one next line, and
 * only when that line is shaped like a value (`isValueShaped`); an opening indicator or triple quote on its line then counts as
 * evidence, since `password = """` LF `FAKE` is the TOML shape. The `:` labels (YAML, JSON, headers) keep their blocks.</p>
 */
function continueAfter(writer: Writer, text: string, line: Line, ending: Ending): number {
  switch (effectiveOpening(ending)) {
    case 'indicator':
      return maskIndented(writer, text, line.next, indentEnd(text, line.start, line.end) - line.start);
    case 'triple-double':
      return maskTriple(writer, text, line.next, TRIPLE_DOUBLE);
    case 'triple-single':
      return maskTriple(writer, text, line.next, TRIPLE_SINGLE);
    default:
      return maskPlain(writer, text, line, ending);
  }
}

function effectiveOpening(ending: Ending): Opening {
  return ending.assignment ? 'plain' : ending.opening;
}

// ---------- the label's line, read from its end ----------

/** Reads `label ["'] [ \t]{0,4} [:=] [ \t]{0,32} (indicator | quote run)? \? [ \t]{0,4}` backwards from the line's end. */
function readEnding(text: string, line: Line): Ending | undefined {
  const floor = line.start;
  const tail = readTail(text, floor, line.end);
  const separator = readSeparator(text, floor, tail.at);
  if (separator === undefined) {
    return undefined;
  }
  const beforeGap = skipBack(text, separator.at, floor, BEFORE_JOINER_GAP, isBlank);
  const labelEnd = skipBack(text, beforeGap, floor, QUOTE_RUN, isQuote);
  const start = credentialLabelStart(text, floor, labelEnd);

  return start >= 0 && isLabelHere(text, floor, start, separator)
    ? { opening: tail.opening, backslash: tail.backslash, assignment: isCodeAssignment(text, floor, start, separator) }
    : undefined;
}

/** A `case X:` label of a switch is not a mapping key; every `=` label and every other `:` label is. */
function isLabelHere(text: string, floor: number, start: number, separator: Separator): boolean {
  return separator.assignment || !isSwitchLabel(text, floor, start);
}

/**
 * A separator of `=` with an identifier before the label (`const string TheRoundsKey = """`, `type KeyRead =`) is CODE;
 * with only indentation and quotes before it (`password = """`) it is CONFIG — TOML, INI, dotenv — and keeps its blocks.
 */
function isCodeAssignment(text: string, floor: number, start: number, separator: Separator): boolean {
  return separator.assignment && !startsTheLine(text, floor, start);
}

function startsTheLine(text: string, floor: number, labelStart: number): boolean {
  return skipBack(text, labelStart, floor, labelStart - floor, isBlankOrQuote) === floor;
}

/** The keyword `case` (a whole word) stands before the label, past its quotes and blanks. */
function isSwitchLabel(text: string, floor: number, labelStart: number): boolean {
  const quoted = skipBack(text, labelStart, floor, QUOTE_RUN, isQuote);

  return endsWithCase(text, floor, skipBack(text, quoted, floor, quoted - floor, isBlank));
}

function endsWithCase(text: string, floor: number, at: number): boolean {
  const from = at - CASE_KEYWORD.length;

  return from >= floor && text.slice(from, at) === CASE_KEYWORD && wordStartsAt(text, floor, from);
}

function wordStartsAt(text: string, floor: number, from: number): boolean {
  return from === floor || !isLabelCharacter(text[from - 1]);
}

/** The trailing blanks, an optional backslash and the opening: where they begin, and what they were. */
function readTail(text: string, floor: number, end: number): Opened & { readonly backslash: boolean } {
  const at = skipBack(text, end, floor, TRAILING_GAP, isBlank);
  const backslash = at > floor && text[at - 1] === '\\';
  const opened = readOpening(text, floor, backslash ? at - 1 : at);

  return { ...opened, backslash };
}

/** The `:` or `=` before the gap that precedes the opening, or undefined when the line does not end in a joiner. */
function readSeparator(text: string, floor: number, from: number): Separator | undefined {
  const at = skipBack(text, from, floor, AFTER_JOINER_GAP, isBlank);
  const separator = at > floor ? text[at - 1] : '';

  return separator === ':' || separator === '=' ? { at: at - 1, assignment: separator === '=' } : undefined;
}

/** A block indicator (`[|>][-+]?[0-9]?`) or an opening quote run at the end; `at` is where it begins. */
function readOpening(text: string, floor: number, end: number): Opened {
  const indicator = indicatorStart(text, floor, end);

  return indicator >= 0 ? { opening: 'indicator', at: indicator } : readQuoteOpening(text, floor, end);
}

/** Where a block indicator ending at `end` begins, or -1. */
function indicatorStart(text: string, floor: number, end: number): number {
  const probe = stepBack(text, floor, stepBack(text, floor, end, isAsciiDigit), isSign);

  return probe > floor && isIndicator(text[probe - 1]) ? probe - 1 : -1;
}

function readQuoteOpening(text: string, floor: number, end: number): Opened {
  if (end <= floor || !isQuote(text[end - 1])) {
    return { opening: 'plain', at: end };
  }
  const quote = text[end - 1];
  const at = skipBack(text, end, floor, QUOTE_RUN, (character) => character === quote);

  return { opening: tripleOpening(quote, end - at), at };
}

function tripleOpening(quote: string, run: number): Opening {
  if (run < QUOTE_RUN) {
    return 'plain';
  }

  return quote === '"' ? 'triple-double' : 'triple-single';
}

/** Where the label ending at `labelEnd` — its last 64 label characters, cut to begin at a letter — starts, when it names a credential; else -1. */
function credentialLabelStart(text: string, floor: number, labelEnd: number): number {
  const start = skipForward(text, skipBack(text, labelEnd, floor, LABEL_LIMIT, isLabelCharacter), labelEnd, (character) => !isAsciiLetter(character));

  return start < labelEnd && namesACredential(text.slice(start, labelEnd)) ? start : -1;
}

// ---------- the plain continuation ----------

/**
 * Masks the value on the line after a label's line, when there is evidence and the line is not a child assignment —
 * the value token, or, for a YAML list item, the value after each item's dash. Answers where the scan goes on.
 */
function maskPlain(writer: Writer, text: string, label: Line, ending: Ending): number {
  const next = lineAt(text, label.next);
  const indent = acceptedIndent(text, label, next, ending);

  return indent < 0 ? label.next : maskAccepted(writer, text, label, next, indent, ending);
}

/** Where the value of the next line begins (after its indentation) when the label reaches it, else -1. */
function acceptedIndent(text: string, label: Line, next: Line, ending: Ending): number {
  const indent = indentEnd(text, next.start, next.end);

  return indent < next.end && reaches(text, label, next, indent, ending) ? indent : -1;
}

function reaches(text: string, label: Line, next: Line, indent: number, ending: Ending): boolean {
  return hasLeave(text, label, next, indent, ending) && !isAssignment(text, indent, next.end) && isValueLine(text, indent, next.end, ending);
}

/** Evidence for reaching the line: an opening on an assignment's line, or the evidence of any plain continuation. */
function hasLeave(text: string, label: Line, next: Line, indent: number, ending: Ending): boolean {
  const opened = ending.assignment && ending.opening !== 'plain';

  return opened || hasEvidence(text, label, indent - next.start, text[indent], ending.backslash);
}

/** An assignment's next line must read as one value; every other label takes whatever the evidence admits. */
function isValueLine(text: string, indent: number, to: number, ending: Ending): boolean {
  return !ending.assignment || isValueShaped(text, indent, to);
}

function maskAccepted(writer: Writer, text: string, label: Line, next: Line, indent: number, ending: Ending): number {
  if (!ending.assignment && listValueFrom(text, indent, next.end) >= 0) {
    return maskListItems(writer, text, next, indent - next.start);
  }
  maskValueToken(writer, text, indent, next.end);

  // The continuation line is read again as a label's line of its own: it may end in one.
  return label.next;
}

/** The quote run (up to three) and then the value token — the labelled rule's own class and bound — starting at `from`. */
function maskValueToken(writer: Writer, text: string, from: number, to: number): void {
  const valueStart = skipForward(text, from, Math.min(to, from + QUOTE_RUN), isQuote);
  const valueEnd = skipForward(text, valueStart, Math.min(to, valueStart + VALUE_LIMIT), isValueCharacter);
  if (valueEnd > valueStart) {
    writer.mask(valueStart, valueEnd);
  }
}

function hasEvidence(text: string, label: Line, nextIndent: number, first: string, backslash: boolean): boolean {
  return backslash || isQuote(first) || nextIndent > indentEnd(text, label.start, label.end) - label.start;
}

/**
 * Whether a line (after its indentation) reads as ONE value, not as code: an optional quote, a single token with no white
 * space, quote or bracket inside — so never `new X(…)`, `| { … }`, `[a, b]`, `(expr)`, a heredoc or a raw-string opener — an
 * optional closing quote, and at most a trailing `,` or `;`.
 */
function isValueShaped(text: string, from: number, to: number): boolean {
  const body = valueBody(text, from, to);

  return body.end > body.start && !isForbiddenStart(text[body.start]) && !hasForbiddenInside(text, body.start, body.end);
}

/** The bounds of the token between an optional opening quote and an optional closing quote, trailing blanks and `,`/`;` dropped. */
function valueBody(text: string, from: number, to: number): { readonly start: number; readonly end: number } {
  const trimmed = stepBack(text, from, skipBack(text, to, from, to - from, isBlank), isTailPunctuation);
  const start = trimmed > from && isQuote(text[from]) ? from + 1 : from;

  return { start, end: stepBack(text, start, trimmed, isQuote) };
}

function hasForbiddenInside(text: string, start: number, end: number): boolean {
  for (let at = start; at < end; at += 1) {
    if (isForbiddenInside(text[at])) {
      return true;
    }
  }

  return false;
}

/** A YAML list item: a dash, then white space, then something. Answers where the item's value starts, or -1. */
function listValueFrom(text: string, indent: number, to: number): number {
  if (!isListMarker(text, indent, to)) {
    return -1;
  }
  const valueFrom = indentEnd(text, indent + 1, to);

  return valueFrom < to ? valueFrom : -1;
}

function isListMarker(text: string, indent: number, to: number): boolean {
  return indent + 1 < to && text[indent] === '-' && isBlank(text[indent + 1]);
}

/**
 * A YAML list under a credential label: every consecutive item of the first item's indentation has the value after its
 * dash replaced and the dash kept; an item that is itself an assignment, a line that is not an item and a deeper or shallower
 * line end the list. Answers the first line not consumed.
 */
function maskListItems(writer: Writer, text: string, first: Line, itemIndent: number): number {
  let line = first;
  for (;;) {
    const valueFrom = itemValueFrom(text, line, itemIndent);
    if (valueFrom < 0) {
      return line.start;
    }
    maskValueToken(writer, text, valueFrom, line.end);
    if (line.end >= text.length) {
      return line.next;
    }
    line = lineAt(text, line.next);
  }
}

/** Where the value of a list item of exactly `itemIndent` indentation begins, or -1 when the line is not such an item (or is an assignment). */
function itemValueFrom(text: string, line: Line, itemIndent: number): number {
  const indent = indentEnd(text, line.start, line.end);
  const valueFrom = indent - line.start === itemIndent ? listValueFrom(text, indent, line.end) : -1;

  return valueFrom >= 0 && !isAssignment(text, valueFrom, line.end) ? valueFrom : -1;
}

/** Whether a line (after its indentation) is itself `["']? label ["']{0,3} [ \t]{0,4} [:=]` — and not base64 padding. */
function isAssignment(text: string, from: number, to: number): boolean {
  const labelStart = from + (from < to && isQuote(text[from]) ? 1 : 0);
  const labelEnd = assignmentLabelEnd(text, labelStart, to);

  return labelEnd >= 0 && endsWithSeparator(text, labelEnd, to);
}

/** Where a label that starts a line ends (at most 64 characters, then not another label character), or -1. */
function assignmentLabelEnd(text: string, at: number, to: number): number {
  if (at >= to || !isAsciiLetter(text[at])) {
    return -1;
  }
  const end = skipForward(text, at, Math.min(to, at + LABEL_LIMIT), isLabelCharacter);

  return runContinues(text, end, to) ? -1 : end;
}

function runContinues(text: string, end: number, to: number): boolean {
  return end < to && isLabelCharacter(text[end]);
}

/** `["']{0,3} [ \t]{0,4}` and then `:` or `=` — but not `==`. */
function endsWithSeparator(text: string, labelEnd: number, to: number): boolean {
  const afterQuotes = skipForward(text, labelEnd, Math.min(to, labelEnd + QUOTE_RUN), isQuote);
  const at = skipForward(text, afterQuotes, Math.min(to, afterQuotes + BEFORE_JOINER_GAP), isBlank);

  return at < to && isAssignmentSeparator(text, at, to);
}

function isAssignmentSeparator(text: string, at: number, to: number): boolean {
  return text[at] === ':' || (text[at] === '=' && !(at + 1 < to && text[at + 1] === '='));
}

// ---------- the blocks ----------

/** A block scalar: every following line that is blank or deeper than the label's line is value. Answers where the block ended. */
function maskIndented(writer: Writer, text: string, from: number, labelIndent: number): number {
  let at = from;
  while (at < text.length) {
    const line = lineAt(text, at);
    if (endsBlock(text, line, labelIndent)) {
      return at;
    }
    maskLine(writer, text, line, line.end);
    at = line.next;
  }

  return text.length;
}

/** The first non-blank line that is not deeper than the label's line. */
function endsBlock(text: string, line: Line, labelIndent: number): boolean {
  const indent = indentEnd(text, line.start, line.end);

  return indent < line.end && indent - line.start <= labelIndent;
}

/** One placeholder after the line's indentation, up to `to`; a blank line stays blank. */
function maskLine(writer: Writer, text: string, line: Line, to: number): void {
  const indent = indentEnd(text, line.start, to);
  if (indent < to) {
    writer.mask(indent, to);
  }
}

/** A triple-quoted value: every following line is value up to the line that holds the closing quotes. Answers that line, to be read as an ordinary one. */
function maskTriple(writer: Writer, text: string, from: number, quotes: string): number {
  let at = from;
  while (at < text.length) {
    const line = lineAt(text, at);
    const close = closingQuotes(text, line, quotes);
    maskLine(writer, text, line, close >= 0 ? close : line.end);
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
  while (end < text.length && !isBreak(text[end])) {
    end += 1;
  }

  return { start, end, next: afterBreak(text, end) };
}

/** Where the line after the break at `end` starts: past a CRLF as one break, past a lone CR or LF, or `end` itself at the end of the text. */
function afterBreak(text: string, end: number): number {
  if (end >= text.length) {
    return end;
  }

  return text[end] === '\r' && text[end + 1] === '\n' ? end + 2 : end + 1;
}

function indentEnd(text: string, from: number, to: number): number {
  return skipForward(text, from, to, isBlank);
}

function skipForward(text: string, from: number, to: number, accepts: (character: string) => boolean): number {
  let at = from;
  while (at < to && accepts(text[at])) {
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

/** One character back when it is accepted, else where it was. */
function stepBack(text: string, floor: number, at: number, accepts: (character: string) => boolean): number {
  return at > floor && accepts(text[at - 1]) ? at - 1 : at;
}

function isBreak(character: string): boolean {
  return character === '\n' || character === '\r';
}

function isBlankOrQuote(character: string): boolean {
  return isBlank(character) || isQuote(character);
}

function isBlank(character: string): boolean {
  return character === ' ' || character === '\t';
}

function isQuote(character: string): boolean {
  return character === '"' || character === "'";
}

function isSign(character: string): boolean {
  return character === '-' || character === '+';
}

function isIndicator(character: string): boolean {
  return character === '|' || character === '>';
}

function isTailPunctuation(character: string): boolean {
  return character === ',' || character === ';';
}

function isForbiddenStart(character: string): boolean {
  return character === '|' || character === '<';
}

function isForbiddenInside(character: string): boolean {
  return /\s/.test(character) || isQuote(character) || '()[]{}'.includes(character);
}

function isAsciiLetter(character: string): boolean {
  return /^[A-Za-z]$/.test(character);
}

function isAsciiDigit(character: string): boolean {
  return /^[0-9]$/.test(character);
}

function isLabelCharacter(character: string): boolean {
  return /^[A-Za-z0-9_.-]$/.test(character);
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
