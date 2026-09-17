import { escapeHtml } from './escapeHtml';
import { Grouped } from './notificationsRead';
import { PAGE_SIZE, asInstant, compareRows } from './pageTables';
import { PAGE_STYLE } from './notificationsPageStyle';
import { OPENS_SORTED_BY, TABS, firstTab, tabStrip, tableHtml } from './notificationsRows';

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
 *
 * <h2>The page tells the host it was shown; the host does not assume</h2>
 *
 * <p>Nothing is marked read until the page says <code>shown</code>, carrying the generation it was
 * drawn for and whether a filter narrows it. Three reasons, and the third is the one that made it
 * worth the message: the host cannot otherwise know the webview rendered at all; a <b>filtered</b>
 * view must not claim three thousand records were read when three are on screen; and a message
 * posted to a webview that has not finished loading can be dropped, so the acknowledgement failure
 * notice needs a moment it is known to be listening. On today's page a freshly drawn document never
 * has a filter set, so that flag is a GUARD rather than a live condition — it becomes a live one
 * the day a draw preserves the filter, which is exactly when a guard added later would be
 * missing.</p>
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
  /** Which draw this is. The page hands it back, so a `shown` from a stale page acknowledges
   *  nothing — the snapshot it belongs to is no longer the one on screen. */
  readonly generation: number;
  /** Something to tell the person that outlived the draw it happened in. */
  readonly notice?: string;
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
 *
 * <p>The button beside it says what it covers. "Mark everything read" on a page that shows the
 * newest three thousand of forty thousand reads as "mark these read" unless it is told otherwise,
 * and it is the older ones — the ones no page-turn will ever reach — that it exists for. (codex and
 * gemini, the S5 code round, from three roles.)</p>
 */
function acknowledgement(): string {
  return `<p class="ack"><span id="ack-note">${escapeHtml(ACKNOWLEDGING)}</span>
  <button type="button" id="mark-all">Mark everything read</button>
  <span class="quiet">— everything in both ledgers, including the older records this page does not show.</span></p>`;
}

/**
 * The two sentences the acknowledgement line can say, in ONE place.
 *
 * <p>The markup renders one and the script swaps between them, so a literal in each was a literal
 * that could drift — and the sentence is a promise about what is being written to disk.</p>
 *
 * <p>It says LOADED, and it says what loaded means: the page renders every row it was given and
 * pages through them, so "the ones on later pages and in other tabs" are covered too. A reviewer
 * read that as a defect — records marked read while only 200 of 3000 are on screen — and it is a
 * deliberate contract rather than an oversight: acknowledging only the visible page would make the
 * count unclearable, because reaching the rest is fifteen page-turns in each of seven tabs. The
 * operator already ruled on the same question when *Mark everything read* was added. What a
 * contract like that owes a person is to SAY so, in the place where it happens.
 * (codex, the second S5 code round.)</p>
 */
export const ACKNOWLEDGING = 'Opening this page marks every loaded record read'
  + ' — including the ones on later pages and in other tabs.';
export const NOT_ACKNOWLEDGING = 'A filter is on, so nothing is being marked read.';

/** A line for whatever the host needs to say after the page was drawn. Empty most of the time. */
function noticeLine(state: PageState): string {
  const said = state.notice ?? '';

  return `<p class="notice" id="notice" role="status" aria-live="polite"${said === '' ? ' hidden' : ''}>`
    + `${escapeHtml(said)}</p>`;
}

/** The shell every page here shares, so the failure page and the real one cannot drift apart. */
function shell(nonce: string, body: string, script: string): string {
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
${script === '' ? '' : `<script nonce="${nonce}">
${script}
</script>`}
</body>
</html>`;
}

/**
 * What is on screen WHILE the ledgers are being read.
 *
 * <p>The data directory may be a NAS, and a webview with nothing in it is indistinguishable from a
 * webview that failed. It says which directory it is reading, because that is the one fact that
 * makes a slow open diagnosable. (codex, the S5 code round.)</p>
 */
export function waitingPageHtml(dataDir: string, nonce: string): string {
  return shell(
    nonce,
    `<p class="scope">Reading <code>${escapeHtml(dataDir)}</code>…</p>`
    + '<p class="quiet">If this directory is on a network share, the first read can take a moment.</p>',
    '',
  );
}

/** The page, or the reason there is no page. */
export function notificationsPageHtml(state: PageState, nonce: string): string {
  if (state.unreadable !== undefined) {
    // No script: none of the elements it binds to exist here, and a page whose script throws on
    // load is a page whose next defect is invisible in the console it already filled.
    return shell(
      nonce,
      `<p class="failed">The notifications could not be read: ${escapeHtml(state.unreadable)}</p>`
      + `<p class="quiet">Nothing was marked read. Reading <code>${escapeHtml(state.dataDir)}</code>.</p>`,
      '',
    );
  }
  const open = firstTab(state.rows);
  const body = `${scope(state)}${noticeLine(state)}${acknowledgement()}`
    + `${filters(state.rows)}${tabStrip(state.rows, open)}${tabsBody(state.rows, open)}`
    + `<p class="pager"><button type="button" id="prev">Previous</button>
  <span id="where"></span>
  <button type="button" id="next">Next</button></p>`;

  return shell(nonce, body, pageScript(state.generation));
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
function pageScript(generation: number): string {
  return `
  var PAGE_SIZE = ${PAGE_SIZE};
  var GENERATION = ${generation};
  var compareRows = ${compareRows.toString()};
  var asInstant = ${asInstant.toString()};
  var page = 1;
  var sortKey = '${OPENS_SORTED_BY.key}';
  var sortDir = '${OPENS_SORTED_BY.dir}';
  var openTab = document.querySelector('[role="tab"][aria-selected="true"]');
  openTab = openTab === null ? '' : openTab.dataset.tab;
  // Acquired ONCE: acquireVsCodeApi throws the second time it is called in a webview, so acquiring
  // it inside a handler would work until somebody pressed the button twice.
  var api = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
  var markAll = document.getElementById('mark-all');

  function sectionOf(tab) {
    return document.querySelector('[data-section="' + tab + '"]');
  }

  function rowsOf(tab) {
    var section = sectionOf(tab);

    return section === null ? [] : Array.prototype.slice.call(section.querySelectorAll('tbody tr'));
  }

  function say(text) {
    var line = document.getElementById('notice');
    if (line === null) { return; }
    line.textContent = text;
    line.hidden = text === '';
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
      ? ${JSON.stringify(NOT_ACKNOWLEDGING)}
      : ${JSON.stringify(ACKNOWLEDGING)};
  }

  // The host acknowledges nothing until this arrives. It carries the generation so a message from a
  // page that has since been replaced cannot acknowledge the snapshot that replaced it, and the
  // filter state so that a narrowed view never claims the whole window was read.
  function tellShown() {
    if (api === null) { return; }
    api.postMessage({ type: 'shown', generation: GENERATION, filtered: filtering() });
  }

  function onFilter() {
    page = 1;
    draw();
    // Only when the filter has gone: re-offering an unfiltered view is what lets a page that opened
    // filtered ever be acknowledged. The host ignores a generation it has already written down.
    if (!filtering()) { tellShown(); }
  }

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

  if (markAll !== null) {
    markAll.addEventListener('click', function () {
      if (api === null) { return; }
      // Disabled and SAYING so: the work is two file reads and two appends against a directory that
      // may be a share, and a button that looks idle while it runs is a button pressed three times.
      markAll.disabled = true;
      markAll.textContent = 'Marking everything read…';
      say('Marking everything in both ledgers read…');
      api.postMessage({ type: 'markAll' });
    });
  }

  window.addEventListener('message', function (event) {
    var message = event.data;
    if (message === null || typeof message !== 'object' || message.type !== 'notice') { return; }
    say(String(message.said));
    // Terminal, either way. A success redraws the page from the host and this document goes away;
    // a failure lands here, and the button must be pressable again.
    if (markAll !== null) {
      markAll.disabled = false;
      markAll.textContent = 'Mark everything read';
    }
  });

  draw();
  tellShown();
`;
}
