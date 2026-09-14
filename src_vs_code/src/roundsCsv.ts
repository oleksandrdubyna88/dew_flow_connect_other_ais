
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
export function roundCells(row: ExportableRow): readonly unknown[] {
  return [
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
export function findingCells(finding: ExportableRow): readonly unknown[] {
  return [
    finding.ordinal,
    finding.severity,
    finding.category,
    finding.file,
    finding.line,
    finding.title,
    finding.why,
    finding.fix,
    finding.role,
    finding.isGating,
    // Comma-joined by the database, which is why it goes through the one cell writer like everything
    // else rather than being spliced into the line as though it were already several cells.
    finding.providers,
    decisionWord(finding.resolution),
    finding.reason,
    finding.reRaised,
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
  readonly found: { readonly state: string; readonly findings: readonly ExportableRow[] };
}

/** Which rounds could not be read, by the key the page knows them by. */
export interface CsvRefusal {
  readonly refused: readonly string[];
}

/**
 * The whole file: a header, then one line per FINDING with its round's columns repeated.
 *
 * <p>A round that genuinely found nothing still gets one line — otherwise a clean round would vanish
 * from a file that is supposed to be the log — with its finding columns empty. A round the database
 * has never heard of gets the same line, and its `decision` column reads `not recorded` rather than
 * `open`, because "nobody wrote this down" and "nobody has decided yet" are different facts.</p>
 *
 * <p><b>It REFUSES rather than guessing.</b> If any round's findings could not be read, no file is
 * built at all and the caller is given the keys that failed. Writing those rounds as blank lines
 * would be the lie this codebase has already paid for once.</p>
 */
export function csvOf(rounds: readonly ExportRound[]): string | CsvRefusal {
  const refused = rounds
    .filter((one) => one.found.state === 'failed')
    .map((one) => String(one.row.key ?? '(unnamed round)'));
  if (refused.length > 0) {
    return { refused };
  }

  const header = line([...ROUND_COLUMNS, ...FINDING_COLUMNS]);

  return BOM + [header, ...rounds.flatMap(linesFor)].join(LINE) + LINE;
}

/** One round: a line per finding, or one line saying it has none. */
function linesFor(round: ExportRound): string[] {
  const mine = roundCells(round.row);
  if (round.found.state === 'absent') {
    // Never recorded, which is not the same as "found nothing". The round's own columns are still
    // true and are still written.
    return [line([...mine, ...NO_FINDING.map((_, at) => (FINDING_COLUMNS[at] === 'decision' ? 'not recorded' : null))])];
  }
  if (round.found.findings.length === 0) {
    return [line([...mine, ...NO_FINDING])];
  }

  return round.found.findings.map((finding) => line([...mine, ...findingCells(finding)]));
}
