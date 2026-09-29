import { escapeHtml } from './escapeHtml';

/**
 * A question as the sidebar's "A review is waiting on you" card shows it.
 *
 * <p>The server writes a failed round's question as one sentence — the lead, then every reviewer that failed
 * joined by commas, then the question — and the card drew it as one run-on block. The operator asked for it to
 * read better (2026-09-29, `todo/PLAN_the_cadence_has_its_own_section.md`, story 2). The WORDS stay the server's
 * (`ReviewerSummary.Sentence`, which the round log and every gate reply read too); this lays them out: the lead,
 * a line per failed reviewer, the question on its own. Anything that does not have that shape is drawn as it
 * always was.</p>
 */

const ASK = 'Proceed anyway, or fix the findings and review again?';

/** `N of M reviewers answered; failed: <a>, <b>, …` — the part of the sentence that lists the failures. */
const FAILED_ROUND = /^([\s\S]*?)(\d+ of \d+ reviewers answered); failed: ([\s\S]+?)\.?$/;

/** Where one failure ends and the next begins: `, provider/Role: `. */
const NEXT_FAILURE = /, (?=[\w.-]+\/[A-Za-z]+: )/;

/** One failure: `provider/Role: what it said`. */
const ONE_FAILURE = /^([\w.-]+)\/([A-Za-z]+): ([\s\S]*)$/;

/**
 * A vendor's raw error body, `{"type":"error","message":"…"}`, possibly cut short by the server. The message
 * may hold escaped quotes (`\"`), so it is read up to an UNescaped quote or the end, then decoded as the JSON
 * string it is — `\n` a line break, `\u00e9` an é.
 */
const ERROR_BODY = /\{"type":"[^"]*","message":"((?:[^"\\]|\\.)*)"?\}?/;

export function questionHtml(text: string): string {
  const round = text.endsWith(ASK) ? FAILED_ROUND.exec(text.slice(0, -ASK.length).trim()) : null;
  if (round === null) {
    return escapeHtml(text);
  }
  const failures = round[3]!.split(NEXT_FAILURE).map((failure) => ONE_FAILURE.exec(failure));
  // Every entry must be `provider/Role: …`, or the sentence is not the one this lays out.
  if (failures.some((parts) => parts === null)) {
    return escapeHtml(text);
  }

  return `<div class="lead">${escapeHtml(`${round[1]}${round[2]}.`)}</div>`
    + `<ul class="failures">${failures.map((parts) => failureHtml(parts!)).join('')}</ul>`
    + `<div class="ask">${escapeHtml(ASK)}</div>`;
}

/** One failed reviewer's line: who, in which role, and what it said. */
function failureHtml(parts: RegExpExecArray): string {
  return `<li class="failed"><b>${escapeHtml(parts[1]!)}</b> · ${escapeHtml(parts[2]!)} — ${escapeHtml(readable(parts[3]!))}</li>`;
}

/** What a reviewer said, with a vendor's JSON error body replaced by its message. */
function readable(said: string): string {
  return said.replace(ERROR_BODY, (_body, message: string) => jsonString(message));
}

/** A JSON string's body, decoded; a body the server cut mid-escape falls back to dropping the backslashes. */
function jsonString(body: string): string {
  try {
    return JSON.parse(`"${body}"`) as string;
  } catch {
    return body.replace(/\\(.)/g, '$1');
  }
}
