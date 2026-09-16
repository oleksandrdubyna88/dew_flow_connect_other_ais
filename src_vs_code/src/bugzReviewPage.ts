/**
 * The review page: every collected pair, and what a person decided about it.
 *
 * <p>Markup and script as text, like every other page here, so the script can be RUN by a test
 * rather than read — `.agents/PROJECT.md` refuses a new behavioural assertion over page source, and
 * story 4 earned that ruling the hard way when a picker matched every regex written about it while
 * being wired to nothing.</p>
 *
 * <p><b>The tick-boxes follow `roundsLog.ts` rather than inventing a pattern.</b> That file already
 * has a `.pick` column and a `pickall` box, and PROJECT.md records what they cost: a tick-box branch
 * needs an early `return` or the control also opens the row it sits in, and no source assertion can
 * see a missing one. The plan's first draft called this the first multi-select in the codebase and a
 * plan reviewer corrected it.</p>
 */

/** One pair as the server hands it over. */
export interface ReviewPair {
  readonly findingId: number;
  readonly symbolName: string;
  readonly language: string;
  readonly skeletonBefore: string;
  readonly skeletonAfter: string;
  /** -1 nobody has looked, 0 dropped, 1 kept. */
  readonly keep: number;
  readonly severity: string;
  readonly category: string;
  readonly title: string;
}

export const UNDECIDED = -1;
export const DROPPED = 0;
export const KEPT = 1;

/** What a person has decided, as a word. */
export function decision(keep: number): string {
  if (keep === KEPT) {
    return 'kept';
  }

  return keep === DROPPED ? 'dropped' : 'undecided';
}

/**
 * How many are still waiting on somebody.
 *
 * <p>The number a person comes back to the page for. A review of two hundred pairs happens over
 * days, which is the whole reason `keep` is a column and not a flag in the page.</p>
 */
export const undecided = (pairs: readonly ReviewPair[]): number =>
  pairs.filter((p) => p.keep === UNDECIDED).length;

const escape = (text: string): string =>
  text.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;').replaceAll('"', '&quot;');

function row(pair: ReviewPair): string {
  return `<tr class="pair" data-row="${pair.findingId}">
  <td class="pick"><input type="checkbox" data-pick="${pair.findingId}"></td>
  <td class="what">
    <div class="sym">${escape(pair.symbolName)}</div>
    <div class="said">${escape(pair.severity)} · ${escape(pair.category)} — ${escape(pair.title)}</div>
    <div class="state ${decision(pair.keep)}">${decision(pair.keep)}</div>
  </td>
  <td class="code"><pre>${escape(pair.skeletonBefore)}</pre></td>
  <td class="code"><pre>${escape(pair.skeletonAfter)}</pre></td>
</tr>`;
}


/**
 * The three things this page can be showing, as one decision rather than a nested ternary.
 *
 * <p>They are genuinely three: a read that FAILED, a corpus that is empty, and rows. The first two
 * send a person to different places — press Collect, or find out why the server would not answer —
 * and collapsing them is the defect four reviewers found in the first version.</p>
 */
function body(pairs: readonly ReviewPair[], rows: string, trouble: string): string {
  if (trouble.length > 0) {
    return `<p class="empty" id="trouble">The pairs could not be read: ${escape(trouble)}</p>`;
  }

  if (pairs.length === 0) {
    return '<p class="empty" id="nothing">Nothing has been collected yet.'
      + ' Press Collect in the Bugz section of the panel.</p>';
  }

  return `<table>
<thead><tr>
  <th class="pick"><input type="checkbox" id="pickall" title="Select every pair"></th>
  <th>Method</th><th>Before</th><th>After</th>
</tr></thead>
<tbody>
${rows}
</tbody>
</table>`;
}

/**
 * The page, whole.
 *
 * <p>`trouble` is what the server said when it could not answer, and it is NOT the same page as an
 * empty corpus: "nothing has been collected yet" sends a person to press Collect, while a read that
 * timed out sends them somewhere else entirely. Four reviewers of the code round found the first
 * version saying the former for both.</p>
 */
export function reviewPageHtml(
  pairs: readonly ReviewPair[], nonce: string, trouble = ''): string {
  const rows = pairs.map(row).join('\n');
  const waiting = undecided(pairs);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: var(--vscode-editor-background);
    margin: 0; padding: 12px 16px;
  }
  h1 { font-size: 15px; margin: 0 0 4px; }
  .hint { opacity: .7; font-size: 12px; margin: 0 0 12px; }
  .bar { display: flex; gap: 8px; align-items: center; margin: 0 0 10px; flex-wrap: wrap; }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 12px; cursor: pointer;
    font-family: inherit; font-size: inherit;
  }
  button:disabled { opacity: .5; cursor: default; }
  button.quiet { background: none; color: var(--vscode-textLink-foreground); text-decoration: underline; }
  table { width: 100%; border-collapse: collapse; }
  th { text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .06em;
       opacity: .7; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  td { vertical-align: top; padding: 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  /* The tick column is as narrow as a box and must not take the click target of the row with it. */
  th.pick, td.pick { width: 1%; padding-right: 0; }
  th.pick input, td.pick input { cursor: pointer; margin: 0; }
  td.what { width: 22%; }
  .sym { font-weight: 600; }
  .said { opacity: .7; font-size: 11px; margin-top: 2px; }
  .state { font-size: 11px; margin-top: 4px; text-transform: uppercase; letter-spacing: .05em; }
  .state.kept { color: var(--vscode-charts-green); }
  .state.dropped { color: var(--vscode-charts-red); }
  .state.undecided { opacity: .5; }
  td.code { width: 39%; }
  pre {
    margin: 0; font-family: var(--vscode-editor-font-family); font-size: 11px;
    white-space: pre-wrap; word-break: break-word; opacity: .9;
  }
  .empty { opacity: .7; padding: 24px 0; }
</style>
</head>
<body>
<h1>Review bugs</h1>
<p class="hint" id="waiting">${waiting} of ${pairs.length} still waiting on you.</p>
<div class="bar">
  <button type="button" id="keep" disabled>Keep selected</button>
  <button type="button" id="drop" disabled>Drop selected</button>
  <button type="button" class="quiet" id="clear">Clear selection</button>
  <span class="hint" id="picked"></span>
</div>
${body(pairs, rows, trouble)}
<script nonce="${nonce}">
(function () {
  var vscode = acquireVsCodeApi();
  var selected = {};

  function count() { return Object.keys(selected).length; }

  function paint() {
    var n = count();
    document.getElementById('keep').disabled = n === 0;
    document.getElementById('drop').disabled = n === 0;
    document.getElementById('picked').textContent = n === 0 ? '' : n + ' selected';
    var boxes = document.querySelectorAll('[data-pick]');
    for (var i = 0; i < boxes.length; i++) {
      boxes[i].checked = selected[boxes[i].getAttribute('data-pick')] === true;
    }
    // The select-all box too. The browser ticks it natively on click while the handler decides
    // from the COUNT, so the two drift apart the moment anything else changes the selection: after
    // a decision clears it the box stayed ticked over nothing, and unticking one row left it ticked
    // over a partial selection. It then showed the opposite of what the next click would do.
    var all = document.getElementById('pickall');
    if (all) { all.checked = boxes.length > 0 && n === boxes.length; }
  }

  function decide(keep) {
    var ids = Object.keys(selected).map(Number);
    if (ids.length === 0) { return; }
    // The page does NOT paint the new state itself. The decision is written by the extension and
    // the page is redrawn from what the database then says — a page that congratulated itself and
    // was wrong is exactly the failure this whole column exists to prevent.
    vscode.postMessage({ type: 'decide', keep: keep, ids: ids });
    selected = {};
    paint();
  }

  document.addEventListener('click', function (event) {
    var target = event.target;
    // ABOVE anything that acts on the row, and it RETURNS: a tick-box that falls through would
    // also trigger the row's own behaviour. roundsLog.ts has the same two lines for the same
    // reason, and PROJECT.md records what it cost to learn.
    var picking = target.closest ? target.closest('[data-pick]') : null;
    if (picking) {
      var id = picking.getAttribute('data-pick');
      if (selected[id]) { delete selected[id]; } else { selected[id] = true; }
      paint();
      return;
    }
    if (target.id === 'pickall') {
      var boxes = document.querySelectorAll('[data-pick]');
      var everyOne = boxes.length > 0 && count() === boxes.length;
      selected = {};
      if (!everyOne) {
        for (var i = 0; i < boxes.length; i++) { selected[boxes[i].getAttribute('data-pick')] = true; }
      }
      paint();
      return;
    }
    if (target.id === 'keep') { decide(1); return; }
    if (target.id === 'drop') { decide(0); return; }
    if (target.id === 'clear') { selected = {}; paint(); return; }
  });

  paint();
  vscode.postMessage({ type: 'ready' });
}());
</script>
</body>
</html>`;
}
