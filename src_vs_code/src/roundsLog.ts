import { Escalation } from './escalations';
import { roundKey } from './panelView';
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
  | 'gating' | 'findings' | 'seconds' | 'tokensIn' | 'tokensOut' | 'costUsd' | 'answered';

/** The facets a select can narrow by. Empty or absent means "any". */
export interface LogFilters {
  readonly repoPath?: string;
  readonly branch?: string;
  readonly stage?: string;
  readonly status?: string;
  readonly verdict?: string;
  readonly vendor?: string;
}

/** Every round of every session, newest first. */
export function rowsFrom(sessions: readonly SessionFile[], nowMs: number = Date.now()): LogRow[] {
  return sessions
    .flatMap((session) => session.rounds.map((round) => rowFrom(session, round, nowMs)))
    .sort((a, b) => (b.startedUtc || b.completedUtc).localeCompare(a.startedUtc || a.completedUtc));
}

function rowFrom(session: SessionFile, round: RoundRecord, nowMs: number): LogRow {
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
    answered: round.reviewers,
    vendors: [...new Set(states.map((s) => s.provider))],
    reviewers: reviewerLines(round),
    reviewerColours: rows.map((r) => vendorColour(r.provider)),
  };
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
  { key: 'costUsd', label: 'Cost', numeric: true },
  { key: 'answered', label: 'Reviewers' },
];

const FACETS: ReadonlyArray<{ key: keyof LogFilters; label: string }> = [
  { key: 'repoPath', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'vendor', label: 'Vendor' },
];

/** A select's options for one facet, from the rows themselves — a filter offers only what exists. */
function facetOptions(rows: readonly LogRow[], key: keyof LogFilters): string {
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
export function roundsLogHtml(rows: readonly LogRow[], questions: readonly Escalation[], nonce: string): string {
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
  th[data-sort].asc::after { content: " ▲"; opacity: .7; }
  th[data-sort].desc::after { content: " ▼"; opacity: .7; }
  td.what { white-space: normal; min-width: 260px; max-width: 560px; }
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
  .hint { opacity: .65; font-size: .9em; margin-top: 10px; }
</style>
</head>
<body>
<h1>Review rounds</h1>
<div id="questions">${questionsHtml(questions)}</div>
<div class="toolbar">
      <input id="search" type="search" placeholder="Search subject, branch, repository, reviewers…" autocomplete="off">
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
<div class="hint">Click a column to sort, a row to see its reviewers. The table advances by itself while a round runs; your sort, filters and search stay.</div>
<script nonce="${nonce}">
(function () {
  var vscode = acquireVsCodeApi();
  var ROWS = ${jsonForScript(rows)};
  ${compareRows.toString()}
  ${rowMatches.toString()}

  var state = { sortKey: 'startedUtc', dir: 'desc', filters: {}, search: '', expanded: {} };

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
        + '<td>' + esc(r.branch) + '</td>'
        + '<td>' + esc(r.stage) + '</td>'
        + '<td class="num">' + r.number + '</td>'
        + '<td class="what">' + esc(r.subject) + '</td>'
        + '<td>' + badge(r.status) + '</td>'
        + '<td>' + esc(r.verdict) + '</td>'
        + '<td class="num">' + r.gating + '</td>'
        + '<td class="num">' + num(r.findings) + '</td>'
        + '<td class="num">' + took(r.seconds) + '</td>'
        + '<td class="num">' + num(r.tokensIn) + '</td>'
        + '<td class="num">' + num(r.tokensOut) + '</td>'
        + '<td class="num">' + money(r.costUsd) + '</td>'
        + '<td>' + esc(r.answered) + '</td>'
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
  document.getElementById('clear').addEventListener('click', function () {
    state.filters = {};
    state.search = '';
    document.getElementById('search').value = '';
    for (var c = 0; c < selects.length; c++) { selects[c].value = ''; }
    render();
  });
  window.addEventListener('message', function (event) {
    var message = event.data;
    if (!message || message.type !== 'rows') { return; }
    ROWS = message.rows || [];
    if (typeof message.questions === 'string') {
      document.getElementById('questions').innerHTML = message.questions;
    }
    render();
  });
  render();
})();
</script>
</body>
</html>`;
}
