/**
 * A PEM private-key block taken out whole, header to footer — the pass `safeText` runs before every
 * pattern.
 *
 * <p><b>Why a pass of its own.</b> The five redaction patterns are ONE-LINE shapes, and a private key
 * is base64 on lines of its own — real line breaks in a `.pem`-like fixture, `\n` escapes on one line
 * inside a service account's JSON. Nothing named those lines, so the server served a committed
 * `service-account.json` with only its `-----BEGIN` taken out by the labelled pass and the key body
 * reached the vendor (found by epic 3's code round, 2026-09-26). Every variant is one shape:
 * `-----BEGIN [kind ]PRIVATE KEY[ BLOCK]-----` … `-----END [kind ]PRIVATE KEY[ BLOCK]-----`, the kind
 * being `RSA`, `EC`, `DSA`, `OPENSSH`, `ENCRYPTED`, `PGP` or nothing.</p>
 *
 * <p><b>The contract is `src_mcp/core/Notices/PrivateKeyBlocks.cs`, byte for byte</b>, as every pass of
 * the notice redaction is shared: both halves redact `server-notices.jsonl`, and `run-parity.mjs`
 * runs both over the same key shapes.</p>
 *
 * <p><b>Procedural and linear, not a pattern.</b> A body between two markers is a lazy or negated loop
 * of tens of kilobytes, and against text made of headers WITHOUT footers every engine searches
 * `headers × bound` — which on this side, with no match ceiling, is a frozen extension host. Here a
 * header is found by `indexOf`, its footer by `indexOf` from the furthest point already known to hold
 * none, and a header with no footer in reach consumes the run of key-looking text after it — so every
 * character is examined a bounded number of times whatever the text is shaped like.</p>
 *
 * <p><b>Every line break inside the block is kept</b>, and each line's text becomes one placeholder
 * after its indentation, so a served file's line count survives; an escaped one-line block collapses
 * to one placeholder between its markers. A header with no footer within `BODY_LIMIT` loses the run of
 * body-looking characters after it: base64, whitespace, the backslash of an escape, and the
 * `Proc-Type:`/`DEK-Info:` lines of an encrypted body. A parser's header constant followed by code loses
 * nothing, because a quote ends the run at once.</p>
 */

const BEGIN = '-----BEGIN ';
const END = '-----END ';
const PRIVATE_KEY = 'PRIVATE KEY';
const BLOCK_SUFFIX = ' BLOCK';
const DASHES = '-----';

/** What may stand between `-----BEGIN ` and `PRIVATE KEY`: the kind and its space, or nothing. */
export const KIND_LIMIT = 64;

/** How far past a header its footer is looked for — an RSA-16384 key is under 13 K characters in PEM, escaped or not. */
export const BODY_LIMIT = 65_536;

interface Block {
  readonly text: string;
  readonly end: number;
  readonly searchedTo: number;
}

interface Footer {
  readonly footer: number;
  readonly searchedTo: number;
}

/** The text with every private-key block's body replaced by `redacted`, line breaks kept. */
export function redactPrivateKeyBlocks(text: string, redacted: string): string {
  let out = '';
  let at = 0;
  let noFooterBefore = 0;
  let begin = text.indexOf(BEGIN);
  while (begin >= 0) {
    const headerEnd = markerEnd(text, begin + BEGIN.length);
    if (headerEnd < 0) {
      out += text.slice(at, begin + 1);
      at = begin + 1;
    } else {
      const block = redactBlock(text, headerEnd, noFooterBefore, redacted);
      out += text.slice(at, headerEnd) + block.text;
      at = block.end;
      noFooterBefore = block.searchedTo;
    }
    begin = text.indexOf(BEGIN, at);
  }

  return out + text.slice(at);
}

/** The block after a header: its body redacted to its footer, or to the end of the key-looking run when it has none. */
function redactBlock(text: string, headerEnd: number, noFooterBefore: number, redacted: string): Block {
  const { footer, searchedTo } = footerAfter(text, headerEnd, noFooterBefore);
  if (footer < 0) {
    const end = bodyRunEnd(text, headerEnd);

    return { text: redactedLines(text, headerEnd, end, redacted), end, searchedTo };
  }
  const footerEnd = markerEnd(text, footer + END.length);

  return { text: redactedLines(text, headerEnd, footer, redacted) + text.slice(footer, footerEnd), end: footerEnd, searchedTo };
}

/** The index after `[kind ]PRIVATE KEY[ BLOCK]-----` starting at `kindStart`, or -1 when that is not what stands there. */
function markerEnd(text: string, kindStart: number): number {
  // Looked for within the kind's bound, never to the end of the text: a header that opens nothing must
  // cost the same however much text follows it.
  const window = text.slice(kindStart, kindStart + KIND_LIMIT + PRIVATE_KEY.length);
  const key = window.indexOf(PRIVATE_KEY);
  if (key < 0 || !isKind(window.slice(0, key))) {
    return -1;
  }

  return closingDashesEnd(text, afterOptionalSuffix(text, kindStart + key + PRIVATE_KEY.length));
}

function afterOptionalSuffix(text: string, at: number): number {
  return text.startsWith(BLOCK_SUFFIX, at) ? at + BLOCK_SUFFIX.length : at;
}

function closingDashesEnd(text: string, at: number): number {
  return text.startsWith(DASHES, at) ? at + DASHES.length : -1;
}

/** ASCII capitals, digits and spaces only — bounded by the slice it is handed, so no engine can loop over it. */
function isKind(kind: string): boolean {
  return /^[A-Z0-9 ]*$/.test(kind);
}

/**
 * The first footer starting within `BODY_LIMIT` of `from`, and the furthest index now known to have no
 * footer before it — so the search across many headers stays linear.
 */
function footerAfter(text: string, from: number, noFooterBefore: number): Footer {
  const limit = Math.min(from + BODY_LIMIT, text.length);
  let candidate = text.indexOf(END, Math.max(from, noFooterBefore));
  while (candidate >= 0 && candidate <= limit) {
    if (markerEnd(text, candidate + END.length) >= 0) {
      return { footer: candidate, searchedTo: candidate };
    }
    candidate = text.indexOf(END, candidate + 1);
  }

  return { footer: -1, searchedTo: noFooterBefore_(candidate, text.length) };
}

/** Where the search stopped finding footers: at the next `-----END ` past the bound, or at the end of the text. */
function noFooterBefore_(candidate: number, length: number): number {
  return candidate < 0 ? length : candidate;
}

/** Where the run of key-looking text after a footerless header ends, within `BODY_LIMIT`. */
function bodyRunEnd(text: string, from: number): number {
  const limit = Math.min(text.length, from + BODY_LIMIT);
  let at = from;
  while (at < limit && isBodyCharacter(text[at])) {
    at += 1;
  }

  return at;
}

/** Base64, the whitespace between lines, the backslash of an escape, and what an encrypted body's own header lines are made of. */
function isBodyCharacter(character: string): boolean {
  return /^[A-Za-z0-9+/= \t\r\n\\:,-]$/.test(character);
}

/** Every line of the block: its indentation kept, the rest one placeholder, every CR and LF as it was. */
function redactedLines(text: string, from: number, to: number, redacted: string): string {
  let out = '';
  let at = from;
  while (at < to) {
    const end = lineEnd(text, at, to);
    const indent = indentEnd(text, at, end);
    out += text.slice(at, indent) + (indent < end ? redacted : '');
    const breaks = breaksEnd(text, end, to);
    out += text.slice(end, breaks);
    at = breaks;
  }

  return out;
}

function lineEnd(text: string, from: number, to: number): number {
  let at = from;
  while (at < to && !isBreak(text[at])) {
    at += 1;
  }

  return at;
}

function indentEnd(text: string, from: number, to: number): number {
  let at = from;
  while (at < to && (text[at] === ' ' || text[at] === '\t')) {
    at += 1;
  }

  return at;
}

function breaksEnd(text: string, from: number, to: number): number {
  let at = from;
  while (at < to && isBreak(text[at])) {
    at += 1;
  }

  return at;
}

function isBreak(character: string): boolean {
  return character === '\r' || character === '\n';
}
