import { HIGHLIGHT_CSS, highlight } from './codeHighlight';
import { pairDiff } from './lineDiff';
import { TONE_CSS, toneControlHtml, toneScript, toneStyle } from './textTone';
import { escapeHtml } from './webviewHtml';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';

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
 *
 * <p><b>Every row is collapsed when the page opens</b> (`PLAN_the_review_page_can_be_read.md`,
 * story 1.1). Two hundred pairs each showing two skeletons is a page nobody scrolls; what a person
 * needs to see at once is the LIST, and the code when they ask for it. Which rows are open is held
 * by the panel and handed back in {@link ReviewView.expanded}, because a redraw replaces the whole
 * document — `rolesPanel.ts` holds its tab for the same reason and says so at length.</p>
 *
 * <p>Pure, and free of `node:` and `vscode` imports, like `zoomControl.ts` and `textTone.ts` beside
 * it: a page module that reaches for either fails the bundle test, and a decision inside one is a
 * decision no unit test can run.</p>
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

/**
 * Everything the page is drawn from, as one argument.
 *
 * <p>`renderHelpHtml`'s shape, for the same reason it has it: this went from three positional
 * parameters to six in one story, and the fourth one along would have been a boolean nobody could
 * read at a call site. The two offsets are the SETTINGS' values, read by the host at paint —
 * the page never computes its own.</p>
 */
export interface ReviewView {
  readonly pairs: readonly ReviewPair[];
  readonly nonce: string;
  /** What the server said when it could not answer. Empty is not the same as an empty corpus. */
  readonly trouble?: string;
  /** The `findingId`s whose code is showing. Keyed by id, never by position — see {@link row}. */
  readonly expanded?: ReadonlySet<number>;
  readonly uiScale?: number;
  readonly textTone?: number;
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

/** An ARIA boolean is a lowercase STRING, which `${true}` also spells but by accident. */
const ariaBoolean = (yes: boolean): string => (yes ? 'true' : 'false');

/**
 * A `findingId` on its way into an attribute or a selector.
 *
 * <p>It is typed `number` and `roundsDbRead.pairOf` refuses a pair whose id is not one — but this
 * module is pure and its caller's promise is not a property of the page. The id is written into
 * `data-row`, `data-toggle`, `data-detail` and an element id, and the page's own script then builds
 * `[data-detail="' + id + '"]` from what it reads back, so a value carrying a quote would break out
 * of both the attribute and the selector. Escaping costs one call and makes the page safe by
 * construction rather than by what the reader upstream happens to check today.</p>
 */
const key = (findingId: number): string => escapeHtml(String(findingId));

/**
 * One pair: the line you always see, and the code you asked for.
 *
 * <p><b>Two `<tr>`s, not one row with hidden cells.</b> A collapsed pair must take the height of a
 * line, and cells sized for skeletons leave a collapsed table as a column of empty space wider than
 * the text beside it. The detail row carries the id it belongs to so a redraw can find it, and
 * `hidden` rather than a class so the browser's own default hides it before any stylesheet loads.</p>
 *
 * <p><b>The key is `findingId` everywhere</b> — `data-row`, `data-pick`, `data-toggle`,
 * `data-detail` and the element id. A position would pass every test written in one order and lose
 * the open row the moment a redraw reorders or removes anything, which is what the three tests named
 * in the plan exist to catch.</p>
 */
function row(pair: ReviewPair, open: boolean): string {
  const id = key(pair.findingId);
  const said = `${escapeHtml(pair.severity)} · ${escapeHtml(pair.category)} — ${escapeHtml(pair.title)}`;

  // The three lines are INSIDE the button, so the whole summary is the target. The first version
  // wrapped only the chevron and the symbol, and left the severity, the title and the state word
  // outside it — pressing any of them did nothing at all, while the stylesheet beside it claimed
  // the whole line was the button. A code reviewer (codex, UX) found it. They are spans rather
  // than divs because a `button` takes phrasing content: a `div` in there is invalid markup that
  // browsers merely tolerate.
  // What differs, computed once per pair and handed to BOTH sides — the two panes must agree about
  // which lines are opposite which, and two independent diffs would not have to.
  const differs = pairDiff(pair.skeletonBefore, pair.skeletonAfter);

  return `<tr class="pair" data-row="${id}">
  <td class="pick"><input type="checkbox" data-pick="${id}"></td>
  <td class="what">
    <button type="button" class="twist" data-toggle="${id}"
            aria-expanded="${ariaBoolean(open)}" aria-controls="detail-${id}">
      <span class="chev" aria-hidden="true">▸</span>
      <span class="lines">
        <span class="sym">${escapeHtml(pair.symbolName)}</span>
        <span class="said">${said}</span>
        <span class="state ${decision(pair.keep)}">${decision(pair.keep)}</span>
      </span>
    </button>
  </td>
</tr>
<tr class="detail" id="detail-${id}" data-detail="${id}"${open ? '' : ' hidden'}>
  <td class="pick"></td>
  <td>
    <div class="sides">
      <div class="side">
        <div class="sideName">Before</div>${highlight(pair.skeletonBefore, pair.language, differs.before)}
      </div>
      <div class="side">
        <div class="sideName">After</div>${highlight(pair.skeletonAfter, pair.language, differs.after)}
      </div>
    </div>
  </td>
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
    return `<p class="empty" id="trouble">The pairs could not be read: ${escapeHtml(trouble)}</p>`;
  }

  if (pairs.length === 0) {
    return '<p class="empty" id="nothing">Nothing has been collected yet.'
      + ' Press Collect in the Bugz section of the panel.</p>';
  }

  return `<table>
<thead><tr>
  <th class="pick"><input type="checkbox" id="pickall" title="Select every pair"></th>
  <th>Method</th>
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
export function reviewPageHtml(view: ReviewView): string {
  const { pairs, nonce, trouble = '', uiScale = 0, textTone = 0 } = view;
  const expanded = view.expanded ?? new Set<number>();
  const rows = pairs.map((pair) => row(pair, expanded.has(pair.findingId))).join('\n');
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
    font-family: var(--vscode-font-family);
    color: var(--vscode-foreground); background: var(--vscode-editor-background);
    margin: 0; padding: 12px 16px;
    /* Both INSIDE the rule. A tone fragment written above it silently drops the whole body rule —
       the trap chatPage.ts documents and a test parses for. */
    ${zoomStyle(uiScale)} ${toneStyle(textTone)}
  }
${ZOOM_CSS}
${TONE_CSS}
${HIGHLIGHT_CSS}
  h1 { font-size: 1.15em; margin: 0 0 4px; }
  .hint { opacity: .7; font-size: .92em; margin: 0 0 12px; }
  .bar { display: flex; gap: 8px; align-items: center; margin: 0 0 10px; flex-wrap: wrap; }
  /* Pushes the view controls to the far end: what a person presses often and what they set once
     should not sit in one undifferentiated row of buttons. */
  .bar .spacer { flex: 1 1 auto; }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 12px; cursor: pointer;
    font-family: inherit; font-size: inherit;
  }
  button:disabled { opacity: .5; cursor: default; }
  button.quiet { background: none; color: var(--vscode-textLink-foreground); text-decoration: underline; }
  table { width: 100%; border-collapse: collapse; }
  th { text-align: left; font-size: .85em; text-transform: uppercase; letter-spacing: .06em;
       opacity: .7; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  td { vertical-align: top; padding: 8px; }
  /* The border belongs to the PAIR, and a pair is two rows: drawn under the summary when it is
     closed and under the detail when it is open, so an expanded pair reads as one block rather
     than as two rows that happen to be adjacent. */
  tr.pair > td { border-bottom: 1px solid var(--vscode-panel-border); }
  tr.pair:has(+ tr.detail:not([hidden])) > td { border-bottom: none; }
  tr.detail > td { border-bottom: 1px solid var(--vscode-panel-border); padding-top: 0; }
  /* Explicit rather than trusting the UA sheet to outrank a display we might add later. */
  tr[hidden] { display: none; }
  /* The tick column is as narrow as a box and must not take the click target of the row with it. */
  th.pick, td.pick { width: 1%; padding-right: 0; }
  th.pick input, td.pick input { cursor: pointer; margin: 0; }
  /* The whole summary line is the button — every one of the three lines is inside it — so the
     target is the line and not a glyph. It keeps the page's own text colour: a button coloured as
     a button would make every method look pressable in the accent colour and drown the state
     words underneath. */
  button.twist {
    background: none; color: inherit; padding: 0; text-align: left; width: 100%;
    display: flex; align-items: flex-start; gap: 6px; font: inherit;
  }
  .lines { display: flex; flex-direction: column; align-items: flex-start; min-width: 0; }
  .chev { display: inline-block; opacity: .6; transition: transform .1s; font-size: .9em;
          line-height: 1.4; }
  button.twist[aria-expanded="true"] .chev { transform: rotate(90deg); }
  .sym { font-weight: 600; }
  .said { opacity: .7; font-size: .85em; margin-top: 2px; }
  .state { font-size: .85em; margin-top: 4px; text-transform: uppercase; letter-spacing: .05em; }
  .state.kept { color: var(--vscode-charts-green); }
  .state.dropped { color: var(--vscode-charts-red); }
  .state.undecided { opacity: .5; }
  /* Without min-width: 0, a long unbroken token makes a flex child refuse to shrink and the table
     grows a horizontal scrollbar instead of wrapping. */
  .sides { display: flex; gap: 16px; align-items: flex-start; }
  .side { flex: 1 1 0; min-width: 0; }
  .sideName { font-size: .8em; text-transform: uppercase; letter-spacing: .06em; opacity: .55;
              margin-bottom: 3px; }
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
  <span class="spacer"></span>
  <button type="button" class="quiet" id="expandAll">Expand all</button>
  <button type="button" class="quiet" id="collapseAll">Collapse all</button>
  ${zoomControlHtml(uiScale)}${toneControlHtml(textTone)}
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

  // Opening a row is painted HERE and the panel is merely told. A redraw runs the server — one
  // process per click is what a round trip would cost — so the page owns the gesture and the panel
  // owns what survives the next redraw. Nothing about the corpus changes either way.
  function showRow(id, open) {
    // By the data attribute rather than by the element id, so opening one row and opening all of
    // them look the same thing up the same way. The element id exists for aria-controls, which
    // needs a real target; a second lookup path would be a second place for the two to disagree.
    var detail = document.querySelector('[data-detail="' + id + '"]');
    if (detail) { detail.hidden = !open; }
    var twist = document.querySelector('[data-toggle="' + id + '"]');
    if (twist) { twist.setAttribute('aria-expanded', open ? 'true' : 'false'); }
  }

  function showAll(open) {
    var details = document.querySelectorAll('[data-detail]');
    var ids = [];
    for (var i = 0; i < details.length; i++) {
      details[i].hidden = !open;
      ids.push(Number(details[i].getAttribute('data-detail')));
    }
    var twists = document.querySelectorAll('[data-toggle]');
    for (var j = 0; j < twists.length; j++) {
      twists[j].setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    vscode.postMessage({ type: 'expandAll', ids: ids, open: open });
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
    // closest() and not an id test, because the press can land on the chevron inside the button.
    // The selector is deliberately the BUTTON and not the row: matching the row would take the
    // whole line, tick-box included, and would read the expanded state off an element that does
    // not carry it — so a second press could never close what the first one opened. The suite
    // goes red on exactly that mutation.
    var twisting = target.closest ? target.closest('[data-toggle]') : null;
    if (twisting) {
      var rowId = twisting.getAttribute('data-toggle');
      var opening = twisting.getAttribute('aria-expanded') !== 'true';
      showRow(rowId, opening);
      vscode.postMessage({ type: 'expand', id: Number(rowId), open: opening });
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
    if (target.id === 'expandAll' || target.id === 'collapseAll') {
      showAll(target.id === 'expandAll');
      return;
    }
    if (target.id === 'keep') { decide(1); return; }
    if (target.id === 'drop') { decide(0); return; }
    if (target.id === 'clear') { selected = {}; paint(); return; }
  });
${zoomScript()}
${toneScript()}

  paint();
  vscode.postMessage({ type: 'ready' });
}());
</script>
</body>
</html>`;
}
