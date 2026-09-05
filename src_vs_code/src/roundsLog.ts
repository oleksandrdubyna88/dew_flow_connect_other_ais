import { Escalation } from './escalations';
import { roundKey, usageRegion } from './panelView';
import { ModelPrice } from './modelPrices';
import { UsageEntry, Window, WINDOWS } from './usage';
import { Vendor } from './vendors';
import { MAX_PLAUSIBLE_SECONDS, reviewerLines, reviewerRows, RoundRecord, SessionFile, stageName } from './rounds';
import { vendorColour } from './vendorColour';
import { BlindSpot, DbFinding, DbLog, EMPTY_LOG, findingsByRound, roundKeyOf } from './roundsDb';
import { escapeHtml, jsonForScript } from './webviewHtml';

/**
 * The rounds log: every round of every session, as rows a table can sort, filter and search.
 *
 * <p>Asked for on 2026-09-05 over a screenshot of `rounds.md`: fifty-three lines of markdown
 * tables, one per session, each row one unwrapped line running off the right edge, no way to sort
 * by time across sessions, no way to find "every round on branch X". A log is a table. The same
 * ruling left the sidebar with only what is running (`activeRounds.test.ts`); this is where the
 * rest went.</p>
 *
 * <p><b>The predicates are the page's.</b> {@link compareRows} and {@link rowMatches} reference
 * nothing outside their own parameters, and their source is embedded into the webview script
 * verbatim — so the function the tests exercise is the function the table sorts with. The
 * alternative was a TypeScript version here and a hand-copied JavaScript one in the page, which is
 * two implementations of one rule and the usual way one of them ends up wrong.</p>
 *
 * <p>`vscode`-free, like every other page module.</p>
 */

/** One round, flattened with its session, every column derived once. */
export interface LogRow {
  readonly key: string;
  /** ISO, or empty when the server that wrote the round did not record it. Never the epoch. */
  readonly startedUtc: string;
  readonly completedUtc: string;
  readonly repoPath: string;
  /** The folder's own name — for a column; the path is what the filter matches on. */
  readonly repoName: string;
  readonly branch: string;
  /** As the sidebar words it: "plan review", "code review". */
  readonly stage: string;
  readonly number: number;
  readonly subject: string;
  readonly status: 'running' | 'done' | 'interrupted';
  readonly verdict: string;
  readonly gating: number;
  /** The sum over its reviewers, or null when the file recorded none — absent is not zero. */
  readonly findings: number | null;
  /** Completed minus started; so far, for a running round; null for one that died. */
  readonly seconds: number | null;
  readonly tokensIn: number | null;
  readonly tokensOut: number | null;
  readonly costUsd: number | null;
  /** What the round's reviewers READ, in dollars — null when nothing could be priced. */
  readonly costInUsd: number | null;
  /** What they WROTE. */
  readonly costOutUsd: number | null;
  /** The two added up, or what a vendor actually billed when it says so. */
  readonly costTotalUsd: number | null;
  /** True when the number is derived from a public price list rather than billed. */
  readonly costIsEstimate: boolean;
  /** True when at least one reviewer's model had no listed price, so the total is a floor. */
  readonly costPartial: boolean;
  /** How many answered, in the server's own words ("7 of 9 reviewers answered"). */
  readonly answered: string;
  readonly vendors: readonly string[];
  /** One line per reviewer — the same lines the sidebar shows, from the same builder. */
  readonly reviewers: readonly string[];
  /** Parallel to `reviewers`: the vendor word's colour, so the page needs no palette of its own. */
  readonly reviewerColours: readonly string[];
  /**
   * What this round FOUND, from the rounds database — empty when the server predates it.
   *
   * <p>The session files record that a reviewer produced four findings and not what they were, so
   * the Findings column was a number with nothing behind it. These are the sentences. Named apart
   * from `findings`, which is that count and stays what it is.</p>
   */
  readonly found: readonly DbFinding[];
}

/** The columns a header click can sort by. */
export type SortKey =
  | 'startedUtc' | 'repoName' | 'branch' | 'stage' | 'number' | 'subject' | 'status' | 'verdict'
  | 'gating' | 'findings' | 'seconds' | 'tokensIn' | 'tokensOut' | 'costTotalUsd' | 'answered';

/** The facets a select can narrow by. Empty or absent means "any". */
export interface LogFilters {
  readonly repoPath?: string;
  readonly branch?: string;
  readonly stage?: string;
  readonly status?: string;
  readonly verdict?: string;
  readonly vendor?: string;
  /**
   * Inclusive bounds on WHEN the round started (or finished, when it never recorded a start).
   *
   * <p>Either an instant (`2026-09-05T14:30:00.000Z`, what the page sends once a time is picked) or a
   * bare day (`2026-09-05`). A bare `to` means the END of that day, not its midnight — otherwise
   * "to today" would exclude everything that happened today, which is the whole of today.</p>
   */
  readonly from?: string;
  readonly to?: string;
}

/**
 * What a MODEL costs per million tokens, or nothing when no list prices it.
 *
 * <p>By the model id the ledger recorded, never by the vendor's current setting: a round priced
 * from what codex happens to be set to today changes its cost the moment somebody switches models,
 * which the gate raised twice over on 2026-09-05.</p>
 */
export type PriceOfModel = (model: string) => { inPerMillion: number; outPerMillion: number } | undefined;

/** Every round of every session, newest first. */
export function rowsFrom(
  sessions: readonly SessionFile[],
  nowMs: number = Date.now(),
  priceOf: PriceOfModel = () => undefined,
  usage: readonly UsageEntry[] = [],
  log: DbLog = EMPTY_LOG,
): LogRow[] {
  const byRound = findingsByRound(log);

  return sessions
    .flatMap((session) => session.rounds.map((round) => rowFrom(session, round, nowMs, priceOf, usage, byRound)))
    .sort((a, b) => (b.startedUtc || b.completedUtc).localeCompare(a.startedUtc || a.completedUtc));
}

function rowFrom(
  session: SessionFile,
  round: RoundRecord,
  nowMs: number,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  byRound: Map<string, readonly DbFinding[]> = new Map(),
): LogRow {
  const cost = costOf(round, priceOf, usage, nowMs);
  const status = round.status === 'running' ? 'running' : round.status === 'interrupted' ? 'interrupted' : 'done';
  const states = round.reviewerStates ?? [];
  const rows = reviewerRows(round);

  return {
    key: roundKey({ ...round, branch: session.state.branch }),
    startedUtc: round.startedUtc ?? '',
    completedUtc: round.completedUtc,
    repoPath: session.state.repoPath,
    repoName: repoNameOf(session.state.repoPath),
    branch: session.state.branch,
    stage: stageName(round.stage),
    number: round.number,
    subject: round.subject ?? '',
    status,
    verdict: round.verdict,
    gating: round.gatingCount,
    findings: states.length === 0 ? null : states.reduce((sum, s) => sum + s.findings, 0),
    seconds: secondsOf(round, status, nowMs),
    tokensIn: round.tokensIn ?? null,
    tokensOut: round.tokensOut ?? null,
    costUsd: round.costUsd ?? null,
    ...cost,
    answered: round.reviewers,
    vendors: [...new Set(states.map((s) => s.provider))],
    reviewers: reviewerLines(round),
    reviewerColours: rows.map((r) => vendorColour(r.provider)),
    found: byRound.get(roundKeyOf(
      session.state.sessionId, session.state.repoPath, session.state.branch, round.stage, round.number)) ?? [],
  };
}

/**
 * What a round cost, from the USAGE LEDGER.
 *
 * <p>Not from the round's reviewer states: those record `{provider, role, status, findings, note,
 * seconds}` and no tokens at all, which is why the first version of this column was empty on every
 * row in the installed extension. And not from the round's own totals either — they are a sum over
 * vendors whose prices differ by an order of magnitude, so one multiplication over them is a number
 * with no meaning.</p>
 *
 * <p>The ledger has exactly what is needed: one line per reviewer run, with the tokens it used and
 * the MODEL that answered. Each line is priced at its own model's rate, so a round keeps the cost
 * it had when it ran even after somebody points the vendor at a different model.</p>
 *
 * <p>A line with no listed price, or with no tokens recorded, contributes nothing and sets
 * `costPartial`: a total that quietly leaves somebody out is worse than one that says it is a
 * floor. So does a total whose tokens do not add up to what the round itself recorded — two rounds
 * of one stage running at once can each match the other's lines by time alone, and that is the
 * honest way to say the figure may not be only this round's.</p>
 *
 * <p>A vendor that BILLED the round wins over anything worked out from a price list — the two are
 * not the same kind of number, and `costIsEstimate` says which one this is.</p>
 */
function costOf(
  round: RoundRecord,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  nowMs: number,
): Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'> {
  const billed = round.costUsd ?? null;
  let inUsd = 0;
  let outUsd = 0;
  let priced = 0;
  let unpriced = 0;
  let tokensIn = 0;
  let tokensOut = 0;
  for (const line of linesOf(round, usage, nowMs)) {
    const price = priceOf(line.model);
    if (price === undefined || line.tokensIn === undefined || line.tokensOut === undefined) {
      unpriced += 1;
      continue;
    }
    priced += 1;
    tokensIn += line.tokensIn;
    tokensOut += line.tokensOut;
    inUsd += (line.tokensIn / 1_000_000) * price.inPerMillion;
    outUsd += (line.tokensOut / 1_000_000) * price.outPerMillion;
  }
  const nothingPriced = priced === 0;

  return {
    costInUsd: nothingPriced ? null : round4(inUsd),
    costOutUsd: nothingPriced ? null : round4(outUsd),
    costTotalUsd: billed ?? (nothingPriced ? null : round4(inUsd + outUsd)),
    costIsEstimate: billed === null && !nothingPriced,
    costPartial: !nothingPriced && (unpriced > 0 || drifted(round, tokensIn + tokensOut)),
  };
}

/**
 * The ledger lines that belong to this round: written while it ran, and of its own stage.
 *
 * <p>By time and stage because that is all there is to match on — a ledger line names no round.
 * (The local database will carry the round's id on every line and end the guessing; until then this
 * is exact for one round at a time and marked partial when the tokens disagree.)</p>
 */
function linesOf(round: RoundRecord, usage: readonly UsageEntry[], nowMs: number): readonly UsageEntry[] {
  const from = round.startedUtc ?? '';
  if (from.length === 0 || usage.length === 0) {
    return [];
  }
  const to = round.completedUtc.length > 0 ? round.completedUtc : new Date(nowMs).toISOString();

  return usage.filter(
    (line) => line.utc >= from && line.utc <= to && line.stage.toLowerCase() === round.stage.toLowerCase());
}

/** Whether the priced lines add up to what the round said it used, within a fiftieth. */
function drifted(round: RoundRecord, counted: number): boolean {
  const recorded = (round.tokensIn ?? 0) + (round.tokensOut ?? 0);

  return recorded > 0 && Math.abs(counted - recorded) > recorded / 50;
}

/** Cents-and-a-bit, so a sum of many small numbers does not drift into float noise. */
function round4(value: number): number {
  return Math.round(value * 10_000) / 10_000;
}

function repoNameOf(repoPath: string): string {
  const parts = repoPath.replace(/\\/g, '/').replace(/\/+$/, '').split('/');
  return parts[parts.length - 1] ?? repoPath;
}

/**
 * How long a round took, or has taken — and no number at all for one that died.
 *
 * <p>An interrupted round was never written a completion time; measuring it against `now` would
 * report how long ago it started, which is what the sidebar did for a day and called `361m 40s`.</p>
 */
function secondsOf(round: RoundRecord, status: LogRow['status'], nowMs: number): number | null {
  const started = Date.parse(round.startedUtc ?? '');
  if (Number.isNaN(started)) {
    return null;
  }
  if (status === 'running') {
    return Math.max(0, Math.round((nowMs - started) / 1000));
  }
  if (status === 'interrupted') {
    return null;
  }
  const completed = Date.parse(round.completedUtc);
  if (Number.isNaN(completed)) {
    return null;
  }
  const seconds = Math.round((completed - started) / 1000);
  // .NET's default date is year ONE, and a server that never recorded a start wrote exactly that.
  // Subtracting it from a real completion time produced "1065396701m 44s" once; the sidebar's
  // `elapsed` refuses the same way, with the same cap.
  return seconds < 0 || seconds > MAX_PLAUSIBLE_SECONDS ? null : seconds;
}

// ---------------------------------------------------------------------------------------------
// The two functions below run in the PAGE. They must reference nothing outside their parameters
// — no import, no module constant, no helper — and use no template literal, because their source
// is embedded into a script that itself lives inside one. `roundsLog.test.ts` asserts both.
// ---------------------------------------------------------------------------------------------

/**
 * Orders two rows by one column. A blank sorts after every real value in BOTH directions: a
 * missing number is not a small one, and "oldest first" must not begin with rounds that have no
 * date at all.
 */
export function compareRows(a: LogRow, b: LogRow, key: SortKey, dir: 'asc' | 'desc'): number {
  const x = (a as unknown as Record<string, unknown>)[key];
  const y = (b as unknown as Record<string, unknown>)[key];
  const xBlank = x === null || x === undefined || x === '';
  const yBlank = y === null || y === undefined || y === '';
  if (xBlank && yBlank) {
    return 0;
  }
  if (xBlank) {
    return 1;
  }
  if (yBlank) {
    return -1;
  }
  const sign = dir === 'asc' ? 1 : -1;
  if (typeof x === 'number' && typeof y === 'number') {
    return (x - y) * sign;
  }
  return String(x).localeCompare(String(y)) * sign;
}

/**
 * A wall-clock value from a `datetime-local` input, as the instant the filter compares against.
 *
 * <p>What the input holds is WALL CLOCK and what a round records is UTC, so comparing the two as
 * strings is wrong by the reader's offset. `endOfMinute` includes the minute the bound names, which
 * is what a minute-granularity picker means by it — without it "to 23:59" ends at 23:59:00.000 and
 * the last minute of today falls outside the range the page opens on.</p>
 */
export function asInstant(localValue: string, endOfMinute: boolean): string {
  if (!localValue) {
    return '';
  }
  var at = new Date(localValue).getTime();

  return isNaN(at) ? '' : new Date(at + (endOfMinute ? 59999 : 0)).toISOString();
}

/**
 * What a figure in a money column says, or an em dash where there is no figure.
 *
 * <p>Embedded into the page by its source text, like every function below it — so it references
 * nothing outside itself. {@link cost3} and {@link costTitle} take it as an ARGUMENT rather than
 * calling it by name: a function embedded by its text lands in a scope where only its own name was
 * re-declared, so a call to a module-level binding arrives under whatever the minifier called it
 * (0.29.12 shipped exactly that, and the page died with "R is not defined"). An argument cannot be
 * renamed out from under it, and there is one copy of the formatter rather than three.</p>
 */
export function money(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—';
  }

  return '$' + value.toFixed(value < 1 ? 3 : 2);
}

/**
 * The three figures the column is named for: what the round READ, what it WROTE, and the sum.
 *
 * <p>Always three, always with the slashes, an em dash standing in for anything unknown — a vendor
 * that bills one total and reports no split reads `— / — / $0.42` rather than a lone number the
 * header promises to be three. And a round nothing could price is a dash, never an empty cell: an
 * empty cell says "nothing to see", which is a different claim from "nobody knows".</p>
 *
 * <p>A tilde marks a total worked out from a public price list rather than one a vendor billed; a
 * plus marks a total that had to leave a reviewer out, so it is a floor.</p>
 */
export function cost3(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return '—';
  }

  return (row.costIsEstimate ? '~' : '')
    + figure(row.costInUsd) + ' / ' + figure(row.costOutUsd) + ' / ' + figure(row.costTotalUsd)
    + (row.costPartial ? '+' : '');
}

/** The same thing in words, for the cell's tooltip — the column is narrow and the marks are one character. */
export function costTitle(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return 'No price is listed for these models, so this round has no cost figure.';
  }
  var how = row.costIsEstimate
    ? 'Worked out from a public price list, not billed.'
    : 'Reported by the vendor.';
  var part = row.costPartial
    ? ' Some of it could not be priced, so the total is a floor.'
    : '';

  return 'Input ' + figure(row.costInUsd) + ' + output ' + figure(row.costOutUsd)
    + ' = ' + figure(row.costTotalUsd) + '. ' + how + part;
}

/** How a figure is written. Passed IN, never reached for — see the remark on {@link money}. */
export type Money = (value: number | null | undefined) => string;

/** Just the cost fields, because these three run in the page against plain row objects. */
export type Costed = Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'>;

/** Whether a row survives the selects and the search box. A blank search is no search. */
export function rowMatches(row: LogRow, filters: LogFilters, search: string): boolean {
  if (filters.repoPath && row.repoPath !== filters.repoPath) {
    return false;
  }
  if (filters.branch && row.branch !== filters.branch) {
    return false;
  }
  if (filters.stage && row.stage !== filters.stage) {
    return false;
  }
  if (filters.status && row.status !== filters.status) {
    return false;
  }
  if (filters.verdict && row.verdict !== filters.verdict) {
    return false;
  }
  if (filters.vendor && row.vendors.indexOf(filters.vendor) < 0) {
    return false;
  }
  // The date range. ISO-8601 instants of the same shape compare correctly as plain strings, so the
  // bounds are inclusive by comparison. A bare day as the UPPER bound means the end of that day: an
  // upper bound of 2026-09-05 against 2026-09-05T14:30Z would otherwise exclude the whole of today,
  // which is exactly the range somebody picking today wants. A round with no date at all cannot be
  // shown to be inside a bounded range, so a bound drops it; no bound keeps it.
  const at = row.startedUtc || row.completedUtc;
  if ((filters.from || filters.to) && at.length === 0) {
    return false;
  }
  if (filters.from && at < filters.from) {
    return false;
  }
  if (filters.to && at > (filters.to.indexOf('T') < 0 ? filters.to + 'T23:59:59.999Z' : filters.to)) {
    return false;
  }
  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return true;
  }
  const haystack = [row.subject, row.branch, row.repoPath, row.repoName].concat(row.reviewers).join(' ').toLowerCase();
  return haystack.indexOf(needle) >= 0;
}

// ---------------------------------------------------------------------------------------------
// The page.
// ---------------------------------------------------------------------------------------------

/** The open questions, as the block at the top of the page — the same block the tick re-posts. */
export function questionsHtml(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const items = questions.map((q) => {
    const findings = q.openFindings
      .map((f) => `<li><code>${escapeHtml(f.severity)}</code> ${escapeHtml(f.category)} `
        + `${f.file ? `<code>${escapeHtml(f.file)}:${f.line ?? ''}</code> ` : ''}— ${escapeHtml(f.title)}</li>`)
      .join('');
    return `<div class="question">
  <div class="where">${escapeHtml(q.branch)} · ${escapeHtml(q.repoPath)} · asked ${escapeHtml(q.askedUtc)}</div>
  <p>${escapeHtml(q.question)}</p>
  ${findings.length > 0 ? `<div class="gating">Still gating:</div><ul>${findings}</ul>` : '<div class="gating">No findings attached.</div>'}
  <button type="button" data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
</div>`;
  });

  return `<h2>Open questions — a review is waiting on you</h2>\n${items.join('\n')}`;
}

/**
 * The spending tab's body: the window buttons and the per-vendor region the sidebar used to carry.
 *
 * <p>Moved here from the sidebar on 2026-09-05 — "перенеси отдельной табой в Review rounds и убери из
 * дерева" — with Today as the default window, since midnight. The region itself is the function the
 * sidebar rendered, so there is one renderer for a vendor's row.</p>
 */
export function usageTabHtml(
  usage: readonly UsageEntry[],
  window: Window,
  vendors: readonly Vendor[],
  prices: Readonly<Record<string, ModelPrice>>,
): string {
  const buttons = WINDOWS
    .map((w) => `<button type="button" class="tab${w.id === window ? ' on' : ''}" data-command="usageWindow" data-id="${w.id}">${escapeHtml(w.label)}</button>`)
    .join('');

  return `<div class="windows">${buttons}</div>` + '\n' + `<div class="usage-rows">${usageRegion(usage, window, vendors, prices)}</div>`;
}

const COLUMNS: ReadonlyArray<{ key: SortKey; label: string; numeric?: boolean }> = [
  { key: 'startedUtc', label: 'When' },
  { key: 'repoName', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'number', label: 'Round', numeric: true },
  { key: 'subject', label: 'What' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'gating', label: 'Gating', numeric: true },
  { key: 'findings', label: 'Findings', numeric: true },
  { key: 'seconds', label: 'Took', numeric: true },
  { key: 'tokensIn', label: 'Tokens in', numeric: true },
  { key: 'tokensOut', label: 'Tokens out', numeric: true },
  { key: 'costTotalUsd', label: 'Cost', numeric: true },
  { key: 'answered', label: 'Reviewers' },
];

type Facet = 'repoPath' | 'branch' | 'stage' | 'status' | 'verdict' | 'vendor';

const FACETS: ReadonlyArray<{ key: Facet; label: string }> = [
  { key: 'repoPath', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'vendor', label: 'Vendor' },
];

/** A select's options for one facet, from the rows themselves — a filter offers only what exists. */
function facetOptions(rows: readonly LogRow[], key: Facet): string {
  const values = key === 'vendor'
    ? [...new Set(rows.flatMap((r) => r.vendors))]
    : [...new Set(rows.map((r) => r[key]))];
  return values
    .filter((v) => v.length > 0)
    .sort((a, b) => a.localeCompare(b))
    .map((v) => `<option value="${escapeHtml(v)}">${escapeHtml(key === 'repoPath' ? repoNameOf(v) : v)}</option>`)
    .join('');
}

/**
 * The whole page: questions, a toolbar of filters and a search box, and the table.
 *
 * <p>The table BODY is rendered by the page from the rows as JSON — sorting, filtering and
 * searching are page state, and a live push of new rows re-renders the body alone, so the sort,
 * the filters, the search text, the scroll position and the expanded rows all survive it. The
 * questions block is rendered here and re-posted as HTML by the same function, so there is one
 * renderer for it.</p>
 */
/**
 * The two questions this data exists to answer, as a region of the page.
 *
 * <p>Operator, 2026-09-05: <i>"я хочу сначала идентифицировать [белые пятна], а потом уже или рагом
 * или математикой дать инструмент, который закроет эти пятна"</i>. Identifying them is this.</p>
 *
 * <p><b>What it accepted</b> is the blind-spot corpus: a finding the caller took is by definition
 * something it had not seen and then agreed was worth having. Shown as accepted OVER total, because
 * a category that produces fifty findings and gets two taken says something quite different from
 * one that produces two and gets both — the second is the blind spot; the first is noise.</p>
 *
 * <p><b>What it argued with and got again</b> is the shorter and sharper list: the caller rejected
 * it with a reason, the rejection still stood, and a reviewer raised it anyway.</p>
 */
export function blindSpotsHtml(log: DbLog): string {
  if (log.blindSpots.length === 0 && log.defended.length === 0) {
    return '<div class="empty">Nothing decided yet. This fills in as gates are closed —'
      + ' every accepted finding is something the AI had not seen and then agreed was worth having.</div>';
  }

  return '<div class="spots">'
    + spotTable('By category', log.blindSpots.filter((s) => s.kind === 'category'))
    + spotTable('By reviewer role', log.blindSpots.filter((s) => s.kind === 'role'))
    + spotTable('By vendor', log.blindSpots.filter((s) => s.kind === 'providers'))
    + '</div>'
    + defendedHtml(log.defended);
}

function spotTable(title: string, spots: readonly BlindSpot[]): string {
  if (spots.length === 0) {
    return '';
  }
  const rows = [...spots]
    .sort((a, b) => b.accepted - a.accepted || b.total - a.total)
    .map((s) => `<tr><td>${escapeHtml(s.name)}</td><td class="num">${s.accepted}</td>`
      + `<td class="num">${s.total}</td><td class="num">${share(s)}</td></tr>`)
    .join('');

  return `<div><h2>${escapeHtml(title)}</h2><table><thead><tr><th></th>`
    + '<th class="num">taken</th><th class="num">of</th><th class="num">%</th>'
    + `</tr></thead><tbody>${rows}</tbody></table></div>`;
}

function share(spot: BlindSpot): string {
  return spot.total === 0 ? '—' : `${Math.round((100 * spot.accepted) / spot.total)}`;
}

/** What the server caps that list at; it sends one more so the page can say there are more. */
const DEFENDED_CAP = 200;

function defendedHtml(defended: readonly DbFinding[]): string {
  if (defended.length === 0) {
    return '';
  }
  const items = defended
    .slice(0, DEFENDED_CAP)
    .map((f) => `<div class="finding declined"><span class="sev">${escapeHtml(f.severity)}</span> `
      + `<span class="where">${escapeHtml(f.file ? `${f.file}:${f.line}` : 'no file')}</span> `
      + `<b>${escapeHtml(f.title)}</b><div class="why">${escapeHtml(f.why)}</div>`
      + `<div class="verdict">${escapeHtml(f.providers)} / ${escapeHtml(f.role)}`
      + `${f.reason ? ` &middot; the standing reason: ${escapeHtml(f.reason)}` : ''}</div></div>`)
    .join('');

  // The server fetches one more than it will show, so "exactly two hundred" and "more than that"
  // are different answers. A list that was cut and looks whole is worse than no list.
  const cut = defended.length > DEFENDED_CAP
    ? ` The most recent ${DEFENDED_CAP} of them; there are more.`
    : '';

  return '<h2>Rejected, and raised again anyway</h2>'
    + '<div class="hint">A disagreement the caller is defending — the rejection still stood when'
    + ` another reviewer made the same case.${cut}</div>`
    + `<div class="findings">${items}</div>`;
}

export function roundsLogHtml(
  rows: readonly LogRow[],
  questions: readonly Escalation[],
  nonce: string,
  usageHtml = '',
  spotsHtml = '',
): string {
  const headers = COLUMNS
    .map((c) => `<th data-sort="${c.key}"${c.numeric ? ' class="num"' : ''}>${c.label}</th>`)
    .join('');
  const filters = FACETS
    .map((f) => `<label>${f.label} <select data-filter="${f.key}"><option value="">any</option>${facetOptions(rows, f.key)}</select></label>`)
    .join('\n      ');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ConnectOtherAIs — review rounds</title>
<style>
  body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 16px 20px; }
  h1 { font-size: 1.3em; margin: 0 0 12px; }
  h2 { font-size: 1.1em; margin: 16px 0 8px; }
  .question { border: 1px solid var(--vscode-inputValidation-warningBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .question .where { opacity: .75; font-size: .92em; margin-bottom: 4px; }
  .question .gating { margin-top: 6px; opacity: .85; }
  .question ul { margin: 4px 0 8px 18px; padding: 0; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin: 12px 0; }
  .toolbar label { display: inline-flex; gap: 6px; align-items: center; font-size: .92em; }
  .toolbar input, .toolbar select { background: var(--vscode-input-background); color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px; padding: 3px 6px; font: inherit; }
  .toolbar input { min-width: 220px; }
  #count { opacity: .75; margin-left: auto; font-size: .92em; }
  button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 2px; padding: 4px 10px; font: inherit; cursor: pointer; }
  button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  .wrap { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; font-size: .95em; }
  th, td { text-align: left; padding: 5px 8px; border-bottom: 1px solid var(--vscode-panel-border); white-space: nowrap; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--vscode-editor-background); cursor: pointer; user-select: none; }
  th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
  td.cost { white-space: nowrap; }
  th[data-sort].asc::after { content: " ▲"; opacity: .7; }
  th[data-sort].desc::after { content: " ▼"; opacity: .7; }
  td.what { white-space: normal; min-width: 260px; max-width: 560px; }
  /* Long cells are cut with an ellipsis rather than pushing the table sideways; the full text is
     the cell's own title, so hovering reads it. */
  td.who-answered { max-width: 320px; overflow: hidden; text-overflow: ellipsis; }
  td[title] { cursor: help; }
  tr[data-key] { cursor: pointer; }
  tr[data-key]:hover td { background: var(--vscode-list-hoverBackground); }
  tr.detail td { white-space: normal; background: var(--vscode-editorWidget-background); padding: 6px 8px 8px 28px; cursor: default; }
  .reviewer { font-size: .95em; margin: 1px 0; }
  .who { font-weight: 600; }
  .badge { display: inline-block; padding: 0 6px; border-radius: 8px; font-size: .88em; }
  .badge.running { background: var(--vscode-charts-blue); color: var(--vscode-editor-background); }
  .badge.interrupted { background: var(--vscode-charts-orange); color: var(--vscode-editor-background); }
  .badge.done { background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); }
  .empty { opacity: .75; padding: 24px 0; }
  .failed { border: 1px solid var(--vscode-inputValidation-errorBorder, #c33); background: var(--vscode-inputValidation-errorBackground, transparent); padding: 8px 12px; margin: 0 0 12px; white-space: pre-wrap; }
  .tabs { display: flex; gap: 6px; margin: 4px 0 10px; border-bottom: 1px solid var(--vscode-panel-border); }
  .tabs .tab, .windows .tab { background: transparent; color: var(--vscode-foreground); border: none; border-bottom: 2px solid transparent; border-radius: 0; padding: 6px 10px; opacity: .75; }
  .tabs .tab.on, .windows .tab.on { opacity: 1; border-bottom-color: var(--vscode-focusBorder); }
  .windows { display: flex; gap: 6px; margin: 0 0 10px; }
  .spend { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 8px 12px; margin: 0 0 8px; max-width: 640px; }
  .spend .head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .spend .name { font-weight: 600; }
  .spend .cost { margin-left: auto; }
  .spend .bar { height: 6px; background: var(--vscode-progressBar-background, var(--vscode-panel-border)); border-radius: 3px; margin: 6px 0; overflow: hidden; }
  .spend .bar span { display: block; height: 100%; background: var(--vscode-charts-blue); }
  .spend .figures { font-size: .95em; }
  .spend .hint, .total { margin-top: 2px; }
  .warn { color: var(--vscode-charts-yellow); }
  .link { background: none; border: none; color: var(--vscode-textLink-foreground); padding: 0 4px; }
  .hint { opacity: .65; font-size: .9em; margin-top: 10px; }
  .findings { margin-top: 8px; }
  .finding { border-left: 3px solid var(--vscode-panel-border); padding: 2px 0 6px 10px; margin: 6px 0; }
  .finding.took { border-left-color: var(--vscode-charts-green, var(--vscode-charts-blue)); }
  .finding.declined { border-left-color: var(--vscode-charts-orange); }
  .finding .sev { font-weight: 600; }
  .finding .where { opacity: .75; font-family: var(--vscode-editor-font-family); font-size: .92em; }
  .finding .why { opacity: .85; margin: 2px 0 0; }
  .finding .verdict { opacity: .75; font-size: .92em; margin-top: 3px; }
  .finding .again { color: var(--vscode-charts-orange); font-size: .9em; }
  .spots { display: flex; flex-wrap: wrap; gap: 18px; }
  .spots table { width: auto; min-width: 260px; }
</style>
</head>
<body>
<h1>Review rounds</h1>
<div id="failed" class="failed" hidden></div>
<div id="questions">${questionsHtml(questions)}</div>
<div class="tabs"><button type="button" class="tab on" data-tab="rounds">Rounds</button><button type="button" class="tab" data-tab="usage">What each AI has used</button><button type="button" class="tab" data-tab="spots">What it keeps missing</button></div>
<section id="tab-rounds">
<div class="toolbar">
      <input id="search" type="search" placeholder="Search subject, branch, repository, reviewers…" autocomplete="off">
      <label>From <input id="from" type="datetime-local" step="60"></label>
      <label>To <input id="to" type="datetime-local" step="60"></label>
      <button type="button" class="secondary" id="today">Today</button>
      <button type="button" class="secondary" id="alldates">All dates</button>
      ${filters}
      <button type="button" class="secondary" id="clear">Clear</button>
      <span id="count"></span>
</div>
<div class="wrap">
<table id="log">
  <thead><tr>${headers}</tr></thead>
  <tbody id="rows"></tbody>
</table>
</div>
<div id="empty" class="empty"${rows.length === 0 ? '' : ' hidden'}>No rounds yet. A session appears once an AI calls <code>open</code> for a repository and branch.</div>
<div class="hint">Showing <b>today</b> — <b>All dates</b> clears the range, and the pickers take a time as well as a day. Cost is <b>in / out / total</b> — <code>~</code> means worked out from a public price list rather than billed, <code>+</code> means one reviewer's model had no listed price so the total is a floor. Click a column to sort, a row to see its reviewers. The table advances by itself while a round runs; your sort, filters and search stay.</div>
</section>
<section id="tab-usage" hidden><div id="usage-body">${usageHtml}</div></section>
<section id="tab-spots" hidden><div id="spots-body">${spotsHtml}</div></section>
<script nonce="${nonce}">
(function () {
  // A page that fails must say so on the page. The first release of this page came up as a header
  // row over nothing inside VS Code's webview, while the same HTML rendered every row in node and
  // in headless Chromium; whatever it was, it said nothing. Now it says what and where.
  function failed(message) {
    var box = document.getElementById('failed');
    if (box) {
      box.hidden = false;
      box.textContent = 'This page hit an error and stopped: ' + message + '. Reload the window (Developer: Reload Window); if it comes back, copy this text into an issue.';
    }
  }
  window.onerror = function (message, source, line, column) {
    failed(String(message) + ' (line ' + line + ':' + column + ')');
  };
  var vscode = acquireVsCodeApi();
  var ROWS = ${jsonForScript(rows)};
  // Assigned, never declared: the extension ships BUNDLED and minified, and a minifier renames a
  // function that is not a top-level export — so the declaration this embedded read
  // "function m(a, b, c, d)" in the VSIX while the page called rowMatches(). It worked from the
  // unbundled out/ in node and in headless Chromium, and failed only in the installed extension,
  // which is why the page now reports its own errors (0.29.10 found this one in a minute).
  var compareRows = ${compareRows.toString()};
  var rowMatches = ${rowMatches.toString()};
  var money = ${money.toString()};
  var cost3 = ${cost3.toString()};
  var costTitle = ${costTitle.toString()};

  var state = { sortKey: 'startedUtc', dir: 'desc', filters: {}, search: '', expanded: {} };
  function localDay(d) {
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
  }
  var asInstant = ${asInstant.toString()};

  function esc(value) {
    return String(value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
  function when(iso) {
    if (!iso) { return ''; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return esc(iso); }
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  function num(n) {
    if (n === null || n === undefined) { return ''; }
    if (n >= 1000000) { return (n / 1000000).toFixed(1) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k'; }
    return String(n);
  }
  function took(s) {
    if (s === null || s === undefined) { return ''; }
    if (s < 60) { return s + ' s'; }
    var m = Math.floor(s / 60);
    return m < 60 ? m + 'm ' + (s % 60) + 's' : Math.floor(m / 60) + 'h ' + (m % 60) + 'm';
  }

  function badge(status) {
    return '<span class="badge ' + esc(status) + '">' + esc(status) + '</span>';
  }
  function detail(row) {
    if (row.reviewers.length === 0) {
      return '<div class="reviewer">This round recorded no reviewer detail — it was written by an older server.</div>';
    }
    var lines = '';
    for (var i = 0; i < row.reviewers.length; i++) {
      var line = row.reviewers[i];
      var slash = line.indexOf('/');
      var who = slash < 0 ? line : line.slice(0, slash);
      var rest = slash < 0 ? '' : line.slice(slash);
      lines += '<div class="reviewer"><span class="who" style="color:' + esc(row.reviewerColours[i] || 'inherit') + '">' + esc(who) + '</span>' + esc(rest) + '</div>';
    }
    return lines + foundHtml(row);
  }
  // What the round FOUND, under the reviewers that found it. Severity first because that is how a
  // person triages, then where, then what it said and what was decided about it - an accepted
  // finding is something this repository's author had not seen, which is the whole point of keeping
  // them.
  function foundHtml(row) {
    if (!row.found || row.found.length === 0) {
      return '';
    }
    var out = '<div class="findings">';
    for (var i = 0; i < row.found.length; i++) {
      var f = row.found[i];
      var mark = f.resolution === 'accept' ? 'took' : f.resolution === 'reject' ? 'declined' : 'open';
      out += '<div class="finding ' + esc(mark) + '">'
        + '<span class="sev">' + esc(f.severity) + '</span> '
        + '<span class="where">' + esc(f.file ? f.file + ':' + f.line : 'no file') + '</span> '
        + '<b>' + esc(f.title) + '</b>'
        + (f.reRaised ? ' <span class="again">raised again</span>' : '')
        + '<div class="why">' + esc(f.why) + '</div>'
        + (f.fix ? '<div class="why"><i>fix:</i> ' + esc(f.fix) + '</div>' : '')
        + '<div class="verdict">' + esc(f.providers) + ' / ' + esc(f.role) + ' &middot; <b>' + esc(mark) + '</b>'
        + (f.reason ? ': ' + esc(f.reason) : '') + '</div>'
        + '</div>';
    }
    return out + '</div>';
  }
  function render() {
    var shown = ROWS.filter(function (r) { return rowMatches(r, state.filters, state.search); });
    shown.sort(function (a, b) { return compareRows(a, b, state.sortKey, state.dir); });
    var html = '';
    for (var i = 0; i < shown.length; i++) {
      var r = shown[i];
      html += '<tr data-key="' + esc(r.key) + '">'
        + '<td>' + when(r.startedUtc || r.completedUtc) + '</td>'
        + '<td title="' + esc(r.repoPath) + '">' + esc(r.repoName) + '</td>'
        + '<td title="' + esc(r.branch) + '">' + esc(r.branch) + '</td>'
        + '<td>' + esc(r.stage) + '</td>'
        + '<td class="num">' + r.number + '</td>'
        + '<td class="what" title="' + esc(r.subject) + '">' + esc(r.subject) + '</td>'
        + '<td>' + badge(r.status) + '</td>'
        + '<td>' + esc(r.verdict) + '</td>'
        + '<td class="num">' + r.gating + '</td>'
        + '<td class="num">' + num(r.findings) + '</td>'
        + '<td class="num">' + took(r.seconds) + '</td>'
        + '<td class="num">' + num(r.tokensIn) + '</td>'
        + '<td class="num">' + num(r.tokensOut) + '</td>'
        + '<td class="num cost" title="' + esc(costTitle(r, money)) + '">' + cost3(r, money) + '</td>'
        + '<td class="who-answered" title="' + esc(r.answered) + '">' + esc(r.answered) + '</td>'
        + '</tr>';
      if (state.expanded[r.key]) {
        html += '<tr class="detail"><td colspan="15">' + detail(r) + '</td></tr>';
      }
    }
    document.getElementById('rows').innerHTML = html;
    document.getElementById('empty').hidden = ROWS.length > 0;
    document.getElementById('count').textContent = shown.length === ROWS.length
      ? ROWS.length + ' round' + (ROWS.length === 1 ? '' : 's')
      : shown.length + ' of ' + ROWS.length + ' rounds';
    var ths = document.querySelectorAll('th[data-sort]');
    for (var t = 0; t < ths.length; t++) {
      ths[t].className = ths[t].className.replace(/\\b(asc|desc)\\b/g, '').trim();
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className += ' ' + state.dir; }
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className = ths[t].className.trim(); }
    }
  }

  document.addEventListener('click', function (event) {
    var target = event.target;
    var tab = target.closest('[data-tab]');
    if (tab) {
      var which = tab.getAttribute('data-tab');
      var tabs = document.querySelectorAll('[data-tab]');
      for (var i = 0; i < tabs.length; i++) {
        tabs[i].className = tabs[i].getAttribute('data-tab') === which ? 'tab on' : 'tab';
      }
      document.getElementById('tab-rounds').hidden = which !== 'rounds';
      document.getElementById('tab-usage').hidden = which !== 'usage';
      document.getElementById('tab-spots').hidden = which !== 'spots';
      return;
    }
    var button = target.closest('[data-command]');
    if (button) {
      vscode.postMessage({ type: 'command', command: button.getAttribute('data-command'), id: button.getAttribute('data-id') });
      return;
    }
    var th = target.closest('th[data-sort]');
    if (th) {
      var key = th.getAttribute('data-sort');
      if (state.sortKey === key) { state.dir = state.dir === 'asc' ? 'desc' : 'asc'; }
      else { state.sortKey = key; state.dir = key === 'startedUtc' ? 'desc' : 'asc'; }
      render();
      return;
    }
    var tr = target.closest('tr[data-key]');
    if (tr) {
      var k = tr.getAttribute('data-key');
      if (state.expanded[k]) { delete state.expanded[k]; } else { state.expanded[k] = true; }
      render();
    }
  });
  var selects = document.querySelectorAll('[data-filter]');
  for (var s = 0; s < selects.length; s++) {
    selects[s].addEventListener('change', function (event) {
      state.filters[event.target.getAttribute('data-filter')] = event.target.value;
      render();
    });
  }
  document.getElementById('search').addEventListener('input', function (event) {
    state.search = event.target.value;
    render();
  });
  var fromInput = document.getElementById('from');
  var toInput = document.getElementById('to');
  function readDates() {
    state.filters.from = asInstant(fromInput.value, false);
    // The upper bound INCLUDES the minute it names: the picker has minute granularity, so "to 23:59"
    // that stopped at 23:59:00.000 dropped the last minute of the day the page opens on.
    state.filters.to = asInstant(toInput.value, true);
    render();
  }
  // Today, from its first minute to its last — the range the page opens on, because the question
  // somebody has when they open it is almost always "what happened today".
  function setToday() {
    var now = new Date();
    fromInput.value = localDay(now) + 'T00:00';
    toInput.value = localDay(now) + 'T23:59';
    readDates();
  }
  fromInput.addEventListener('change', readDates);
  toInput.addEventListener('change', readDates);
  document.getElementById('today').addEventListener('click', setToday);
  document.getElementById('alldates').addEventListener('click', function () {
    fromInput.value = '';
    toInput.value = '';
    readDates();
  });
  document.getElementById('clear').addEventListener('click', function () {
    state.filters = {};
    state.search = '';
    document.getElementById('search').value = '';
    fromInput.value = '';
    toInput.value = '';
    for (var c = 0; c < selects.length; c++) { selects[c].value = ''; }
    render();
  });
  window.addEventListener('message', function (event) {
    var message = event.data;
    if (!message) { return; }
    if (message.type === 'spots' && typeof message.html === 'string') {
      document.getElementById('spots-body').innerHTML = message.html;
      return;
    }
    if (message.type === 'usage' && typeof message.html === 'string') {
      document.getElementById('usage-body').innerHTML = message.html;
      return;
    }
    if (message.type !== 'rows') { return; }
    ROWS = message.rows || [];
    if (typeof message.questions === 'string') {
      document.getElementById('questions').innerHTML = message.questions;
    }
    try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
  });
  try {
    // Today by default. Everything older is one click away on "All dates"; the hint says so.
    setToday();
  } catch (e) {
    failed(String(e && e.message ? e.message : e));
  }
})();
</script>
</body>
</html>`;
}
