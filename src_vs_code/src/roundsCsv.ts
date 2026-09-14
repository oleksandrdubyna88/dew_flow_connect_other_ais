import type { FoundState } from './roundsDbRead';

/**
 * A round, as a line of a file somebody keeps.
 *
 * <p>The rounds log is a screen and only a screen: what a review found, and what this repository's
 * author decided about each of it, can be looked at and taken nowhere. The markdown export that
 * predated the page was deleted when the page replaced it. This is the way back out.</p>
 *
 * <p><b>Pure, and it imports only a TYPE.</b> `roundsLog.ts` is bundled into the webview, and
 * anything it reaches pulls `node:` into a page that cannot have it — which is why `roundsDbRead`
 * lives apart from `roundsDb`, and the same rule holds here in the other direction: this module may
 * import from the page module, and the page module must never import this one. The `import type` is
 * erased at compile time, so nothing of this file reaches the page bundle either way.</p>
 */

/** RFC 4180 says CRLF, and a spreadsheet on Windows is the reader this is for. */
const LINE = '\r\n';

/**
 * Excel reads a UTF-8 file as the system codepage unless it is told otherwise, and the only way to
 * tell it is a byte-order mark. Without one, a repository path or a finding written in Cyrillic
 * opens as mojibake — which is the commonest content this file will carry.
 */
const BOM = '﻿';

/**
 * What a spreadsheet EXECUTES if a cell begins with it.
 *
 * <p>`=`, `+`, `-` and `@` start a formula in Excel, LibreOffice and Sheets. The content here is
 * written by other vendors' models and by whoever named a branch, so none of it is trusted.</p>
 *
 * <p><b>Leading whitespace is skipped before the test, and that is the half that was missing.</b>
 * Excel trims a cell before deciding whether it is a formula, so `" =cmd|' /c calc'!A1"` IS one and
 * a guard reading only the first character sees a space and lets it through. Two vendors raised it
 * on the plan round. A leading newline does the same thing — `"\n=HYPERLINK(…)"` — which is why the
 * skipped class covers the control characters as well as spaces.</p>
 */
const FORMULA = /^[\s\u0000-\u001f\u00a0\u200b-\u200d\u2060\ufeff]*[=+\-@|%]/;

/** A cell BEGINNING with a control character is quoted as text whatever follows it. */
const LEADING_CONTROL = /^[\u0000-\u001f]/;

/**
 * One cell, escaped once and for all.
 *
 * <p>Every value goes through here — not just the prose ones. The code round over the plan made
 * exactly that point: a repository called `=HYPERLINK(…)` or a branch called `@release` executes
 * as readily as a model-written title, and `file`, `role` and the vendor list are model-controlled
 * too. One writer means no column can be forgotten, which is the only version of this that stays
 * true when a column is added.</p>
 *
 * <p><b>An absent measurement is an empty cell, never a zero.</b> `null` means nobody recorded it,
 * and `0` is a number somebody measured; writing the first as the second invents data. The page
 * renders them differently for the same reason.</p>
 */
export function cell(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (typeof value === 'number') {
    // NaN and Infinity are not measurements. They reach here only from a torn session file, and an
    // empty cell is the honest rendering of one.
    return Number.isFinite(value) ? String(value) : '';
  }
  if (typeof value === 'boolean') {
    return value ? 'true' : 'false';
  }
  // Anything that is not a primitive is not a cell. A row arrives over the webview bridge and is
  // believed only as far as its key, so a field that turned out to be an object or an array would
  // otherwise reach the file as `[object Object]` — neither the data nor an honest blank. Two
  // vendors raised this on the plan round, with `{key:"r1", durationMs:{}}` as the example.
  if (typeof value !== 'string') {
    return '';
  }

  const text = value;
  // The apostrophe is what every spreadsheet reads as "this is text": it is consumed on import and
  // does not appear in the cell, so the value is preserved rather than mangled.
  const safe = FORMULA.test(text) || LEADING_CONTROL.test(text) ? `'${text}` : text;

  return /["\r\n,;]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/** One row of cells, already escaped. */
export function line(values: readonly unknown[]): string {
  return values.map(cell).join(',');
}

/**
 * The columns a round contributes, in the order they are written.
 *
 * <p>Named here rather than inline so the header and the row cannot drift: a column added to one
 * and not the other is a file whose headings stop describing its contents, and that is exactly the
 * kind of defect a test written against a literal header would not see.</p>
 */
export const FINDING_COLUMNS = [
  'finding_ordinal', 'severity', 'category', 'file', 'line', 'title', 'why', 'fix', 'role',
  'is_gating', 'vendors', 'decision', 'reason', 're_raised',
] as const;

export const ROUND_COLUMNS = [
  // The session is the half of a round's identity the table does not show, and without it two
  // rounds numbered 1 on one branch are indistinguishable in a file somebody keeps for a year.
  // (Plan round, local.)
  'session_id',
  // Whether this round's findings could be READ, which is not the same as what they said. `loaded`
  // is an answer; `not recorded` is a round the database never heard of; `failed` is a read that
  // did not come back — and the finding columns are blank for the last two, so a reader who sorts
  // on this column can tell a clean round from an unknown one. It is a ROUND-level fact and lives
  // here rather than in `decision`, which has a vocabulary of its own. (Plan round, codex + gemini.)
  'findings_read',
  'started_utc', 'started_local_exporter', 'kind', 'repository', 'repository_path', 'branch', 'stage',
  'round', 'subject', 'status', 'accepted', 'rejected', 'verdict', 'gating', 'findings_count',
  'analysis_seconds', 'decide_seconds', 'tokens_in', 'tokens_out', 'cost_in_usd', 'cost_out_usd',
  'cost_total_usd', 'cost_is_estimate', 'cost_partial', 'reviewers_answered', 'reviewers',
] as const;

/**
 * When a round started, on the clock of the machine that EXPORTED it — and it says so.
 *
 * <p>Beside the UTC instant, never instead of it: the stored value is the instant and is written
 * exactly as stored, and this is the convenience at the edge the family's UTC rule allows.</p>
 *
 * <p><b>It carries its offset, and the column is named for whose clock it is.</b> The plan called
 * this "the reader's local time", which is not something a static file can hold. The exporting
 * machine is the EXTENSION HOST — under Remote SSH, WSL, a dev container or Codespaces a different
 * computer from the one the person is sitting at, and commonly a UTC one — and a file opened a year
 * later in another country is read in a third zone again. So the value states the offset it was
 * written with and the header says `started_local_exporter`, rather than `started_local`, which
 * would have been a claim about a reader nobody can see. (Plan round, gemini and codex.)</p>
 */
function exporterLocalTime(iso: string): string {
  if (iso.length === 0) {
    return '';
  }
  const at = new Date(iso);
  // A stamp that will not parse is passed over rather than rendered as "Invalid Date": the UTC
  // column still carries whatever the server actually wrote.
  if (Number.isNaN(at.getTime())) {
    return '';
  }
  const minutes = -at.getTimezoneOffset();
  const pad = (value: number): string => String(Math.floor(Math.abs(value))).padStart(2, '0');

  return `${at.toLocaleString()} (UTC${minutes < 0 ? '-' : '+'}${pad(minutes / 60)}:${pad(minutes % 60)})`;
}

/**
 * A round as it ARRIVES — over the webview bridge, believed only as far as its key.
 *
 * <p>Deliberately not `LogRow`. The host used to take the page's rows and cast them
 * `as unknown as readonly LogRow[]`, which is the cast this repository forbids in fixtures, doing
 * the same damage in production: it told the compiler to stop checking a value that came off a
 * bridge. A field that turned out to be missing or the wrong shape then reached `.join` and threw,
 * failing an export over one bad row. (Code round, gemini, three findings.)</p>
 */
export interface ExportableRow {
  readonly [field: string]: unknown;
}

/** How a round's findings came back, as the file words it. */
export const READ_LOADED = 'loaded';
export const READ_ABSENT = 'not recorded';
export const READ_FAILED = 'failed';

/** One field of a nested object that may be anything at all. */
function readKey(holder: unknown, field: string): string {
  if (typeof holder !== 'object' || holder === null) {
    return '';
  }
  const value = (holder as Record<string, unknown>)[field];

  return typeof value === 'string' ? value : '';
}

/** The reviewer lines, when that is what they are. */
function joined(value: unknown): string {
  return Array.isArray(value) ? value.filter((one) => typeof one === 'string').join('; ') : '';
}

/** A decision count, from a `decided` that may be anything at all. */
function decidedCount(decided: unknown, which: 'accepted' | 'rejected'): number | null {
  if (typeof decided !== 'object' || decided === null) {
    return null;
  }
  const value = (decided as Record<string, unknown>)[which];

  return typeof value === 'number' ? countOf(value) : null;
}

/** What one round contributes to a line, in `ROUND_COLUMNS` order. */
export function roundCells(row: ExportableRow, state: string = READ_LOADED): readonly unknown[] {
  return [
    readKey(row.dbKey, 'sessionId'),
    state,
    row.startedUtc,
    exporterLocalTime(typeof row.startedUtc === 'string' ? row.startedUtc : ''),
    row.kind,
    row.repoName,
    row.repoPath,
    row.branch,
    row.stage,
    row.number,
    row.subject,
    row.status,
    // -1 is the server saying nobody has decided yet, which is not a count. It becomes an empty
    // cell for the same reason a null measurement does.
    decidedCount(row.decided, 'accepted'),
    decidedCount(row.decided, 'rejected'),
    row.verdict,
    row.gating,
    row.findings,
    row.seconds,
    row.decideSeconds,
    row.tokensIn,
    row.tokensOut,
    row.costInUsd,
    row.costOutUsd,
    row.costTotalUsd,
    // The two qualifiers the page shows as `~` and `+`. They are not decoration: the first says the
    // figure was worked out from a public price list rather than billed, and the second that one
    // reviewer's model had no listed price so the total is a FLOOR. A file carrying the number
    // without them turns a hedged figure into a claim. Written as `true`/`false` rather than 1/0,
    // because a spreadsheet filters a word and sums a digit, and these are not quantities.
    row.costIsEstimate,
    row.costPartial,
    row.answered,
    // The per-reviewer lines, uncoloured — the half of the seam `reviewerRows` was split for, kept
    // in `rounds.ts` since the markdown export it was written for was deleted.
    joined(row.reviewers),
  ];
}

/** -1 is "nobody has decided", which is a state and not a number. */
function countOf(value: number | undefined): number | null {
  return value === undefined || value < 0 ? null : value;
}

/**
 * The word the PAGE uses for a decision, so the file and the screen say the same thing.
 *
 * <p>`took` / `declined` / `open`, from `resolution` — the same mapping `foundHtml` renders in the
 * expanded row. A file that said `accept` where the screen said `took` would be a second vocabulary
 * for one fact.</p>
 */
export function decisionWord(resolution: unknown): string {
  if (resolution === 'accept') {
    return 'took';
  }

  return resolution === 'reject' ? 'declined' : 'open';
}

/** What one finding contributes to a line, in `FINDING_COLUMNS` order. */
export function findingCells(finding: unknown): readonly unknown[] {
  // A finding is a bridge value like everything else here. `null`, a string or a number would
  // otherwise throw on the first property access and take the whole export down; and an object with
  // no fields would emit `decision = open`, which reads as a real unresolved finding nobody raised.
  // Both were named on the code round. A thing that is not a record is a row of blanks.
  if (typeof finding !== 'object' || finding === null || Array.isArray(finding)) {
    return FINDING_COLUMNS.map(() => null);
  }
  const it = finding as ExportableRow;

  return [
    it.ordinal,
    it.severity,
    it.category,
    it.file,
    it.line,
    it.title,
    it.why,
    it.fix,
    it.role,
    it.isGating,
    // Comma-joined by the database, which is why it goes through the one cell writer like everything
    // else rather than being spliced into the line as though it were already several cells.
    it.providers,
    decisionWord(it.resolution),
    it.reason,
    it.reRaised,
  ];
}

/** The blanks a round with no finding to write still fills, so every line is the same width. */
const NO_FINDING: readonly unknown[] = FINDING_COLUMNS.map(() => null);

/**
 * A round and what came back when its findings were asked for.
 *
 * <p><b>The state travels WITH the findings, and that is the whole point.</b> An empty list means
 * two entirely different things — this round found nothing, and nobody could read what it found —
 * and the page keeps them apart with `loaded | absent | failed` for exactly this reason. A `Map`
 * whose missing key had to serve for both was the first shape this had, and the code round over the
 * plan refused it: a timed-out read would have written a round of empty finding cells and the export
 * would have reported success.</p>
 */
export interface ExportRound {
  readonly row: ExportableRow;
  readonly found: { readonly state: FoundState; readonly findings: readonly ExportableRow[] };
}

/**
 * The state, believed only if it is one of the three.
 *
 * <p>An unknown word is FAILED, never "not failed". The state crosses a module boundary as data,
 * and a version mismatch or a typo — `load` for `loaded` — must not become a round that looks
 * clean. Fail closed, keep the round's identity, and say so in the file. (Plan round, codex.)</p>
 *
 * <p>A `loaded` state whose findings are not an ARRAY is failed too: that is a shape nobody
 * intended, and rendering it as a round that found nothing is the same lie by a different route.
 * (Plan round, local.)</p>
 */
export function readStateOf(found: ExportRound['found'] | undefined): FoundState {
  if (found === undefined || found === null) {
    return 'failed';
  }
  if (found.state === 'absent') {
    return 'absent';
  }

  return found.state === 'loaded' && Array.isArray(found.findings) ? 'loaded' : 'failed';
}

/** What the `findings_read` column says for each state. */
function wordFor(state: FoundState): string {
  if (state === 'loaded') {
    return READ_LOADED;
  }

  return state === 'absent' ? READ_ABSENT : READ_FAILED;
}

/** What a written file could not read, so the caller can say so. */
export interface CsvWritten {
  readonly text: string;
  /** The keys of rounds whose findings did not come back. Empty when everything was read. */
  readonly unread: readonly string[];
}

/**
 * The whole file: a header, then one line per FINDING with its round's columns repeated.
 *
 * <p>A round that genuinely found nothing still gets one line — otherwise a clean round would vanish
 * from a file that is supposed to be the log — with its finding columns empty and `findings_read`
 * saying `loaded`. A round the database has never heard of says `not recorded`, and one whose read
 * did not come back says `failed`.</p>
 *
 * <p><b>A failed read is never written as a clean round, and it no longer takes the file down with
 * it.</b> The first version refused to build anything at all if any round failed — which is honest
 * but, before selection controls exist, leaves somebody unable to export ANY of a log that contains
 * one unreadable round. The plan round said so from two directions. So the file is written, the
 * failed rounds are in it with blank finding columns and `findings_read = failed`, and the caller is
 * told which they were. Nothing is silently clean and nothing is silently missing.</p>
 *
 * <p>Nothing is written only when NOTHING could be read: a file of nothing but failures has no
 * content to justify a save dialog.</p>
 */
export function csvOf(rounds: readonly ExportRound[]): CsvWritten | CsvRefusal {
  const states = rounds.map((one) => readStateOf(one.found));
  const unread = rounds
    .filter((_, at) => states[at] === 'failed')
    .map((one) => String(one.row.key ?? '(unnamed round)'));
  if (rounds.length > 0 && unread.length === rounds.length) {
    return { refused: unread };
  }

  const header = line([...ROUND_COLUMNS, ...FINDING_COLUMNS]);
  const body = rounds.flatMap((round, at) => linesFor(round, states[at]!));

  // `concat`, not a spread into an array literal: a big export is tens of thousands of lines and
  // spreading that many elements as ARGUMENTS can exceed the call-stack limit. (Code round, gemini.)
  return { text: BOM + [header].concat(body).join(LINE) + LINE, unread };
}

/** Which rounds could not be read at all, when that is every one of them. */
export interface CsvRefusal {
  readonly refused: readonly string[];
}

/** One round: a line per finding, or one line saying why it has none. */
function linesFor(round: ExportRound, state: FoundState): string[] {
  const mine = roundCells(round.row, wordFor(state));
  // The finding columns stay BLANK for anything but a real answer. `not recorded` and `failed` are
  // round-level facts and live in `findings_read`; putting either into `decision` would give that
  // column a value outside its own vocabulary and make a downstream count of decided findings
  // register one that does not exist. (Plan round, codex and gemini.)
  if (state !== 'loaded' || round.found.findings.length === 0) {
    return [line([...mine, ...NO_FINDING])];
  }

  return round.found.findings.map((finding) => line([...mine, ...findingCells(finding)]));
}
