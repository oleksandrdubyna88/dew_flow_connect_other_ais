import { Escalation } from './escalations';
import { roundKey, usageRegion } from './panelView';
import { ModelPrice } from './modelPrices';
import { UsageEntry, Window, WINDOWS } from './usage';
import { Vendor } from './vendors';
import { MAX_PLAUSIBLE_SECONDS, reviewerLines, reviewerRows, RoundRecord, SessionFile, stageName } from './rounds';
import { vendorColour } from './vendorColour';
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
  /** `YYYY-MM-DD`, inclusive, against the UTC day the round started (or finished, when it never recorded a start). */
  readonly from?: string;
  readonly to?: string;
}

/**
 * What one vendor's model costs per million tokens, or nothing when no list prices it.
 *
 * <p>By PROVIDER rather than by model id: a round's reviewer rows name the vendor, and which model
 * that vendor is set to is the panel's configuration, not the round's.</p>
 */
export type ProviderPrice = (provider: string) => { inPerMillion: number; outPerMillion: number } | undefined;

/** Every round of every session, newest first. */
export function rowsFrom(
  sessions: readonly SessionFile[],
  nowMs: number = Date.now(),
  priceOf: ProviderPrice = () => undefined,
): LogRow[] {
  return sessions
    .flatMap((session) => session.rounds.map((round) => rowFrom(session, round, nowMs, priceOf)))
    .sort((a, b) => (b.startedUtc || b.completedUtc).localeCompare(a.startedUtc || a.completedUtc));
}

function rowFrom(session: SessionFile, round: RoundRecord, nowMs: number, priceOf: ProviderPrice): LogRow {
  const cost = costOf(round, priceOf);
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
  };
}

/**
 * What a round cost, per REVIEWER.
 *
 * <p>A round's own token totals are the sum over vendors whose prices differ by an order of
 * magnitude, so one multiplication over the round total would be a number with no meaning. Each
 * reviewer is priced at its own vendor's rate and the results are added; a reviewer whose model no
 * list prices contributes nothing and sets `costPartial`, because a total that quietly leaves
 * somebody out is worse than one that says it is a floor.</p>
 *
 * <p>A vendor that BILLED the round wins over anything worked out from a price list — the two are
 * not the same kind of number, and `costIsEstimate` says which one this is.</p>
 */
function costOf(round: RoundRecord, priceOf: ProviderPrice): Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'> {
  const billed = round.costUsd ?? null;
  let inUsd = 0;
  let outUsd = 0;
  let priced = 0;
  let unpriced = 0;
  for (const state of round.reviewerStates ?? []) {
    const price = priceOf(state.provider);
    if (price === undefined) {
      unpriced += 1;
      continue;
    }
    priced += 1;
    inUsd += ((state.tokensIn ?? 0) / 1_000_000) * price.inPerMillion;
    outUsd += ((state.tokensOut ?? 0) / 1_000_000) * price.outPerMillion;
  }
  const nothingPriced = priced === 0;

  return {
    costInUsd: nothingPriced ? null : round4(inUsd),
    costOutUsd: nothingPriced ? null : round4(outUsd),
    costTotalUsd: billed ?? (nothingPriced ? null : round4(inUsd + outUsd)),
    costIsEstimate: billed === null && !nothingPriced,
    costPartial: unpriced > 0 && !nothingPriced,
  };
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
  // The date range. ISO dates compare as strings, so the day is the first ten characters and the
  // bounds are inclusive by plain comparison. A round with no date cannot be shown to be inside a
  // bounded range, so a bound drops it; no bound keeps it.
  const day = (row.startedUtc || row.completedUtc).slice(0, 10);
  if ((filters.from || filters.to) && day.length === 0) {
    return false;
  }
  if (filters.from && day < filters.from) {
    return false;
  }
  if (filters.to && day > filters.to) {
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
export function roundsLogHtml(rows: readonly LogRow[], questions: readonly Escalation[], nonce: string, usageHtml = ''): string {
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
</style>
</head>
<body>
<h1>Review rounds</h1>
<div id="failed" class="failed" hidden></div>
<div id="questions">${questionsHtml(questions)}</div>
<div class="tabs"><button type="button" class="tab on" data-tab="rounds">Rounds</button><button type="button" class="tab" data-tab="usage">What each AI has used</button></div>
<section id="tab-rounds">
<div class="toolbar">
      <input id="search" type="search" placeholder="Search subject, branch, repository, reviewers…" autocomplete="off">
      <label>From <input id="from" type="date"></label>
      <label>To <input id="to" type="date"></label>
      <button type="button" class="secondary" id="today">Today</button>
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
<div class="hint">Cost is <b>in / out / total</b> — <code>~</code> means worked out from a public price list rather than billed, <code>+</code> means one reviewer's model had no listed price so the total is a floor. Click a column to sort, a row to see its reviewers. The table advances by itself while a round runs; your sort, filters and search stay.</div>
</section>
<section id="tab-usage" hidden><div id="usage-body">${usageHtml}</div></section>
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

  var state = { sortKey: 'startedUtc', dir: 'desc', filters: {}, search: '', expanded: {} };
  function localDay(d) {
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
  }

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
  function money(c) {
    return c === null || c === undefined ? '' : '$' + c.toFixed(c < 1 ? 3 : 2);
  }
  // Three numbers, in the order the header names them: what the round READ, what it WROTE, and the
  // sum. A tilde marks a total worked out from a public price list rather than one a vendor billed;
  // a plus marks a total that left an unpriced reviewer out, so it is a floor.
  // What the three numbers mean, spelled out for the tooltip — the column is narrow and the marks
  // are one character each.
  function costTitle(r) {
    if (r.costTotalUsd === null || r.costTotalUsd === undefined) { return 'No price is listed for these models, so this round has no cost figure.'; }
    var how = r.costIsEstimate
      ? 'Worked out from a public price list, not billed.'
      : 'Reported by the vendor.';
    var part = r.costPartial ? ' One reviewer had no listed price, so the total is a floor.' : '';
    return 'Input ' + money(r.costInUsd) + ' + output ' + money(r.costOutUsd) + ' = ' + money(r.costTotalUsd) + '. ' + how + part;
  }
  function cost3(r) {
    if (r.costTotalUsd === null || r.costTotalUsd === undefined) { return ''; }
    var mark = (r.costIsEstimate ? '~' : '') ;
    var tail = r.costPartial ? '+' : '';
    if (r.costInUsd === null || r.costInUsd === undefined) { return mark + money(r.costTotalUsd) + tail; }
    return mark + money(r.costInUsd) + ' / ' + money(r.costOutUsd) + ' / ' + money(r.costTotalUsd) + tail;
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
    return lines;
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
        + '<td class="num cost" title="' + esc(costTitle(r)) + '">' + cost3(r) + '</td>'
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
    state.filters.from = fromInput.value;
    state.filters.to = toInput.value;
    render();
  }
  fromInput.addEventListener('change', readDates);
  toInput.addEventListener('change', readDates);
  document.getElementById('today').addEventListener('click', function () {
    var today = localDay(new Date());
    fromInput.value = today;
    toInput.value = today;
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
    render();
  } catch (e) {
    failed(String(e && e.message ? e.message : e));
  }
})();
</script>
</body>
</html>`;
}
