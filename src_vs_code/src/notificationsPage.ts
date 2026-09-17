import { escapeHtml } from './escapeHtml';
import { Grouped } from './notificationsRead';
import { PAGE_SIZE, asInstant, compareRows } from './pageTables';
import { PAGE_STYLE } from './notificationsPageStyle';
import { TABS, firstTab, tabStrip, tableHtml } from './notificationsRows';

/**
 * The notifications page: every message this product showed a person, and what to do about it.
 *
 * <p>Markup and script as TEXT, like every page here, so that a test can RUN the script against a
 * DOM shim rather than read it. An operator ruling stands behind that: a source-text assertion once
 * passed a model picker that matched every regex written about it while being wired to nothing.</p>
 *
 * <p>The two page functions this file embeds — `compareRows` and `asInstant` — come from
 * `pageTables.ts` and are pasted in BY SOURCE, bound by assignment rather than declared, because a
 * minifier renames a declaration and the page then calls a name that is not there. The same
 * mechanism the rounds log uses, and its test asserts the exact string.</p>
 */

/** What the page is handed. */
export interface PageState {
  readonly rows: readonly Grouped[];
  /** Which directory these came out of. Two exist on this machine; "empty" and "the other one"
   *  are different problems and a page that does not say which is being read cannot tell them. */
  readonly dataDir: string;
  /** Whether records older than the loaded window are still in the file. */
  readonly older: boolean;
  /** How many records were loaded, so the page can say what it is NOT showing. */
  readonly loaded: number;
  /** Set when the ledgers could not be read at all — never rendered as an empty table. */
  readonly unreadable?: string;
}

/** Every distinct source in the rows, for the facet. Derived, never a list somebody maintains. */
export function sourcesOf(rows: readonly Grouped[]): readonly string[] {
  return [...new Set(rows.map((row) => row.source))].sort();
}

function filters(rows: readonly Grouped[]): string {
  const options = sourcesOf(rows)
    .map((source) => `<option value="${escapeHtml(source)}">${escapeHtml(source)}</option>`)
    .join('');

  return `<div class="filters">
  <label>Search <input type="search" id="find" placeholder="any word on the row"></label>
  <label>Source <select id="source"><option value="">any</option>${options}</select></label>
  <label>From <input type="datetime-local" id="from"></label>
  <label>To <input type="datetime-local" id="to"></label>
  <button type="button" id="clear">Clear</button>
  <span id="range-note" class="warn" hidden></span>
</div>`;
}

/** What the page says about what it is NOT showing, which is as important as what it is. */
function scope(state: PageState): string {
  const said = [`Reading <code>${escapeHtml(state.dataDir)}</code>.`];
  said.push(`${state.loaded} record(s) loaded.`);
  if (state.older) {
    said.push('Older records are in the file and are not shown.');
  }

  return `<p class="scope">${said.join(' ')}</p>`;
}

/**
 * The acknowledgement line.
 *
 * <p><b>A filtered view acknowledges nothing, and says so.</b> Three rows on screen must not mark
 * three thousand records read — that is the same "claim they read what they did not see" the
 * interval watermark exists to prevent, arriving through the filter door. Operator, 2026-09-17.</p>
 */
function acknowledgement(): string {
  return `<p class="ack"><span id="ack-note">Opening this page marks the loaded records read.</span>
  <button type="button" id="mark-all">Mark everything read</button></p>`;
}

/** The page, or the reason there is no page. */
export function notificationsPageHtml(state: PageState, nonce: string): string {
  const open = firstTab(state.rows);
  const body = state.unreadable !== undefined
    ? `<p class="failed">The notifications could not be read: ${escapeHtml(state.unreadable)}</p>`
    : `${scope(state)}${acknowledgement()}${filters(state.rows)}${tabStrip(state.rows, open)}`
      + tabsBody(state.rows, open);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Notifications</title>
<style>${PAGE_STYLE}</style>
</head>
<body>
<h1>Notifications</h1>
${body}
<p class="pager"><button type="button" id="prev">Previous</button>
  <span id="where"></span>
  <button type="button" id="next">Next</button></p>
<script nonce="${nonce}">
${pageScript()}
</script>
</body>
</html>`;
}

/**
 * One section per tab, all of them rendered and all but the open one hidden.
 *
 * <p>All of them, so that switching tabs needs no round trip to the extension and the handler can
 * DERIVE which sections exist from the markup. A page that fetched a tab on demand would be a
 * second code path for the same rows, and the empty ones are three words each.</p>
 */
function tabsBody(rows: readonly Grouped[], open: string): string {
  return TABS.map((tab) => {
    const section = tableHtml(rows, tab);

    return tab === open ? section : section.replace('<section ', '<section hidden ');
  }).join('');
}

/**
 * The page's own script.
 *
 * <p>Filter → sort → slice, in that order, and every change returns to page 1. The order is not a
 * preference: sorting before filtering orders rows that are about to be thrown away, and slicing
 * before either shows page four of a set the person is no longer looking at.</p>
 */
function pageScript(): string {
  return `
  var PAGE_SIZE = ${PAGE_SIZE};
  var compareRows = ${compareRows.toString()};
  var asInstant = ${asInstant.toString()};
  var page = 1;
  var sortKey = 'when';
  var sortDir = 'desc';
  var openTab = document.querySelector('[role="tab"][aria-selected="true"]');
  openTab = openTab === null ? '' : openTab.dataset.tab;

  function sectionOf(tab) {
    return document.querySelector('[data-section="' + tab + '"]');
  }

  function rowsOf(tab) {
    var section = sectionOf(tab);

    return section === null ? [] : Array.prototype.slice.call(section.querySelectorAll('tbody tr'));
  }

  function bounds() {
    var from = asInstant(document.getElementById('from').value, false);
    var to = asInstant(document.getElementById('to').value, true);
    var note = document.getElementById('range-note');
    var backwards = from !== '' && to !== '' && from > to;
    note.hidden = !backwards;
    note.textContent = backwards ? 'The end of the range is before its start, so nothing is filtered by it.' : '';

    return backwards ? { from: '', to: '' } : { from: from, to: to };
  }

  function matches(row, find, source, range) {
    if (find !== '' && row.dataset.find.indexOf(find) < 0) { return false; }
    if (source !== '' && row.dataset.source !== source) { return false; }
    if (range.from !== '' && row.dataset.when < range.from) { return false; }
    if (range.to !== '' && row.dataset.when > range.to) { return false; }

    return true;
  }

  function filtering() {
    return document.getElementById('find').value.trim() !== ''
      || document.getElementById('source').value !== ''
      || document.getElementById('from').value !== ''
      || document.getElementById('to').value !== '';
  }

  function keyOf(row, key) {
    var index = 0;
    var head = document.querySelectorAll('[data-section]:not([hidden]) th');
    for (var i = 0; i < head.length; i += 1) { if (head[i].dataset.key === key) { index = i; } }
    var cell = row.children[index];
    var raw = cell === undefined ? '' : cell.dataset.sort;
    var asNumber = Number(raw);

    return raw !== '' && !isNaN(asNumber) ? asNumber : raw;
  }

  function draw() {
    var find = document.getElementById('find').value.trim().toLowerCase();
    var source = document.getElementById('source').value;
    var range = bounds();
    var rows = rowsOf(openTab);
    var kept = rows.filter(function (row) { return matches(row, find, source, range); });
    kept.sort(function (a, b) {
      return compareRows({ k: keyOf(a, sortKey) }, { k: keyOf(b, sortKey) }, 'k', sortDir);
    });
    var pages = Math.max(1, Math.ceil(kept.length / PAGE_SIZE));
    if (page > pages) { page = pages; }
    var from = (page - 1) * PAGE_SIZE;
    rows.forEach(function (row) { row.hidden = true; });
    kept.slice(from, from + PAGE_SIZE).forEach(function (row, i) {
      row.hidden = false;
      row.style.order = String(i);
    });
    document.getElementById('where').textContent = kept.length === 0
      ? 'No notifications match these filters.'
      : 'Page ' + page + ' of ' + pages + ' — ' + kept.length + ' row(s)';
    document.getElementById('prev').disabled = page <= 1;
    document.getElementById('next').disabled = page >= pages;
    document.getElementById('ack-note').textContent = filtering()
      ? 'A filter is on, so nothing is being marked read.'
      : 'Opening this page marks the loaded records read.';
  }

  function onFilter() { page = 1; draw(); }

  ['find', 'source', 'from', 'to'].forEach(function (id) {
    document.getElementById(id).addEventListener('input', onFilter);
    document.getElementById(id).addEventListener('change', onFilter);
  });
  document.getElementById('clear').addEventListener('click', function () {
    ['find', 'source', 'from', 'to'].forEach(function (id) { document.getElementById(id).value = ''; });
    onFilter();
  });
  document.getElementById('prev').addEventListener('click', function () { page -= 1; draw(); });
  document.getElementById('next').addEventListener('click', function () { page += 1; draw(); });

  Array.prototype.forEach.call(document.querySelectorAll('[role="tab"]'), function (tab) {
    tab.addEventListener('click', function () {
      openTab = tab.dataset.tab;
      Array.prototype.forEach.call(document.querySelectorAll('[role="tab"]'), function (other) {
        other.setAttribute('aria-selected', other === tab ? 'true' : 'false');
      });
      // DERIVED from the markup, never a literal list: every section the page rendered is hidden
      // and the chosen one shown, so a tab added tomorrow needs no change here.
      Array.prototype.forEach.call(document.querySelectorAll('[data-section]'), function (section) {
        section.hidden = section.dataset.section !== openTab;
      });
      page = 1;
      draw();
    });
  });

  Array.prototype.forEach.call(document.querySelectorAll('th[data-key]'), function (th) {
    th.querySelector('.sort').addEventListener('click', function () {
      var key = th.dataset.key;
      sortDir = sortKey === key && sortDir === 'desc' ? 'asc' : 'desc';
      sortKey = key;
      Array.prototype.forEach.call(document.querySelectorAll('th[data-key]'), function (other) {
        other.setAttribute('aria-sort', other === th ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none');
      });
      page = 1;
      draw();
    });
  });

  var markAll = document.getElementById('mark-all');
  if (markAll !== null) {
    // Acquired ONCE: acquireVsCodeApi throws the second time it is called in a webview, so
    // calling it inside the handler would work until somebody pressed the button twice.
    var api = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
    markAll.addEventListener('click', function () {
      if (api !== null) { api.postMessage({ type: 'markAll' }); }
    });
  }

  draw();
`;
}
