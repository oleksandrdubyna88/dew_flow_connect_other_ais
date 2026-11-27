import { agoWords } from './gaugeWords';
import { compareRows } from './pageTables';
import { viewOf, viewState } from './remoteAccountsGroups';
import { INSTALL_JQ } from './remoteAccountsRows';
import { PLAIN_TEXT, TEXT_CONTROLS_CSS, textControlsHtml, textControlsScript, textControlsStyle } from './textControls';
import type { TextSettings } from './textControls';
import { RESET_PASSED, minutesLeft, resetDate, resetText } from './usageWords';
import { VENDOR_PALETTE } from './vendorColour';
import { jsonForScript } from './webviewHtml';

/**
 * The accounts tab's page (E2.S3): ONE html text with its CSP and nonce, and a script that builds every node from the model it is sent, by
 * `textContent` and `setAttribute` of a value — never by markup, so no text of a VM can become an element or a handler.
 *
 * <p><b>The word rules are not copied.</b> `minutesLeft`, `resetText` and `agoWords` are embedded by their own source text, and so are the groupings
 * (`viewOf`, `viewState`) and `compareRows`, BY ASSIGNMENT (`var viewOf = <source>`): a minifier renames a declaration, and the page then calls a name
 * that is not there. The bundled page is run by `remoteAccountsPage.test.ts` for that reason.</p>
 *
 * <p><b>Times stay true.</b> The model carries absolute instants; the page writes every age and every *resets in* itself, and writes them again every
 * 15 seconds from its own clock, so an age grows with no message from the host.</p>
 *
 * <p>The page posts only closed names (`remoteAccountsMessages.ts`). The *Group by* switch, the *Sort by* choice and the headers post nothing: they
 * are the page's own state, kept in the webview's state as `{ v, groupBy, sort }` and nothing else.</p>
 */

export const ACCOUNTS_TITLE = 'AI accounts and usage';

/** The words the page says that are not in the model. Closed, and none of them is a value of a reply. */
const WORDS = {
  passed: RESET_PASSED,
  waiting: 'Waiting for the extension…',
  toolNames: { 'claude-code': 'Claude Code', codex: 'Codex', agy: 'Antigravity' },
};

/** The twelve classes of an email's colour, `em-1` to `em-12`: the palette's own `var()` values, in the palette's order. The page only ever sets a class of this closed list. */
const EMAIL_CSS = VENDOR_PALETTE.map((colour, at) => `.em-${at + 1} { color: ${colour}; }`).join(' ');

const STYLE = `
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 16px 20px; __TEXT__ }
  header { display: flex; align-items: baseline; gap: 12px; }
${TEXT_CONTROLS_CSS}
  h1 { font-size: 1.3em; margin: 0 0 12px; }
  h2 { font-size: 1.05em; margin: 16px 0 6px; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin: 8px 0 12px; }
  .toolbar label { display: inline-flex; gap: 6px; align-items: center; font-size: .92em; }
  .toolbar select { background: var(--vscode-input-background); color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px; padding: 3px 6px; font: inherit; }
  button { background: var(--vscode-button-secondaryBackground, transparent); color: var(--vscode-button-secondaryForeground, inherit); border: 1px solid var(--vscode-button-border, var(--vscode-panel-border)); border-radius: 2px; padding: 3px 9px; font: inherit; cursor: pointer; }
  button:hover { background: var(--vscode-button-secondaryHoverBackground, var(--vscode-list-hoverBackground)); }
  #summary { margin-left: auto; opacity: .8; font-size: .92em; }
  .box { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 10px 12px; margin: 0 0 12px; }
  .block { border-top: 1px solid var(--vscode-panel-border); padding: 8px 0 10px; }
  .heading { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; margin-bottom: 4px; }
  .heading .host { font-size: 1.05em; }
  .heading .acts { margin-left: auto; display: inline-flex; gap: 6px; }
  .muted, .sub, .older { opacity: .75; font-size: .92em; }
  .bad { color: var(--vscode-errorForeground); }
  table.acct { border-collapse: collapse; width: 100%; margin: 2px 0 4px; table-layout: fixed; }
  table.acct th, table.acct td { text-align: left; vertical-align: top; padding: 4px 10px 4px 0; overflow-wrap: anywhere; }
  .pct-g { color: #22c55e; } .pct-o { color: #f97316; } .pct-r { color: #ef4444; }
  .rs-g { color: #22c55e; } .rs-o { color: #f97316; } .rs-r { color: #ef4444; }
  .tn-claude-code { color: #d97757; } .tn-codex { color: #38bdf8; } .tn-agy { color: #a78bfa; }
  td.tb-claude-code { border-left: 3px solid #d97757; } td.tb-codex { border-left: 3px solid #38bdf8; } td.tb-agy { border-left: 3px solid #a78bfa; }
  table.acct td.tb-claude-code, table.acct td.tb-codex, table.acct td.tb-agy { padding-left: 0.6em; }
  ${EMAIL_CSS}
  body.vscode-light .pct-g, body.vscode-light .rs-g { color: #15803d; } body.vscode-light .pct-o, body.vscode-light .rs-o { color: #c2410c; } body.vscode-light .pct-r, body.vscode-light .rs-r { color: #b91c1c; }
  body.vscode-light .tn-claude-code { color: #b4532f; } body.vscode-light .tn-codex { color: #0369a1; } body.vscode-light .tn-agy { color: #6d28d9; }
  body.vscode-light td.tb-claude-code { border-left-color: #b4532f; } body.vscode-light td.tb-codex { border-left-color: #0369a1; } body.vscode-light td.tb-agy { border-left-color: #6d28d9; }
  table.acct th { font-weight: 600; font-size: .92em; opacity: .85; }
  th button.sortbtn { border: 0; background: transparent; padding: 0; font-weight: inherit; color: inherit; }
  th[aria-sort="ascending"] button.sortbtn::after { content: " ↑"; }
  th[aria-sort="descending"] button.sortbtn::after { content: " ↓"; }
  .acct td .label { display: none; }
  .failed { color: var(--vscode-errorForeground); margin-top: 8px; }
  .usage { display: flex; flex-wrap: wrap; gap: 4px 24px; margin: 2px 0 6px; }
  .none-row { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; padding: 3px 0; }
  @media (max-width: 560px) {
    table.acct, table.acct tbody, table.acct tr, table.acct td { display: block; width: auto; }
    table.acct thead, table.acct colgroup { display: none; }
    table.acct tr { padding: 4px 0; border-bottom: 1px solid var(--vscode-panel-border); }
    .acct td .label { display: inline-block; min-width: 9em; opacity: .7; }
    .toolbar { gap: 6px 10px; }
    #summary { margin-left: 0; width: 100%; }
  }`;

const SORT_KEYS: readonly (readonly [string, string])[] = [
  ['name', 'Name'], ['account', 'Account'], ['left5h', '% left, 5-hour'], ['reset5h', 'Reset, 5-hour'], ['leftWeek', '% left, weekly'], ['resetWeek', 'Reset, weekly'], ['age', 'Read (newest first)'],
];

const options = (items: readonly (readonly [string, string])[]): string => items.map(([value, label]) => `<option value="${value}">${label}</option>`).join('');

/**
 * The page's script. Plain text, no `${` of its own: the values it needs are interpolated once, as JSON, at the top. It builds with `createElement`
 * and sets `textContent`; it never assigns markup.
 */
const SCRIPT = `
(function () {
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
  ${textControlsScript()}
  var INSTALL = ${jsonForScript(INSTALL_JQ)};
  var WORDS = ${jsonForScript(WORDS)};
  // Assigned, never declared: the extension ships BUNDLED and minified, and a minifier renames a declaration.
  var compareRows = ${compareRows.toString()};
  var viewOf = ${viewOf.toString()};
  var viewState = ${viewState.toString()};
  var minutesLeft = ${minutesLeft.toString()};
  var resetText = ${resetText.toString()};
  var resetDate = ${resetDate.toString()};
  var agoWords = ${agoWords.toString()};

  var model = null;
  var state = viewState(vscode.getState());
  var cache = {};
  var timed = {};
  var current = 'page';

  function now() { return Date.now(); }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function clock(ms) { var d = new Date(ms); return pad(d.getHours()) + ':' + pad(d.getMinutes()); }
  function stamp(ms) {
    var d = new Date(ms);
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }
  function post(type, extra) {
    var message = extra || {};
    message.type = type;
    vscode.postMessage(message);
  }

  // ───── nodes: every text is a textContent, every attribute a value ─────
  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = text; }
    return node;
  }
  function put(parent) {
    for (var i = 1; i < arguments.length; i += 1) {
      if (arguments[i]) { parent.appendChild(arguments[i]); }
    }
    return parent;
  }
  function button(label, onClick) {
    var node = el('button', null, label);
    node.setAttribute('type', 'button');
    node.addEventListener('click', onClick);
    return node;
  }
  /** A text that the 15-second tick writes again: fn(nowMs) gives it. It belongs to the unit being built. */
  /** What a live text says: a string, or parts [text, class] where the class is one of the own closed names (a colour), never a value of a VM. */
  function paint(node, out) {
    if (typeof out === 'string') { node.textContent = out; return; }
    node.textContent = '';
    out.forEach(function (part) { node.appendChild(el('span', part[1] || null, part[0])); });
  }
  function keyOf(out) { return typeof out === 'string' ? out : JSON.stringify(out); }
  function live(node, fn) {
    var first = fn(now());
    paint(node, first);
    if (!timed[current]) { timed[current] = []; }
    timed[current].push({ node: node, fn: fn, key: keyOf(first) });
    return node;
  }
  function tick() {
    var at = now();
    Object.keys(timed).forEach(function (key) {
      timed[key].forEach(function (one) {
        var out = one.fn(at);
        var key = keyOf(out);
        if (one.key !== key) { one.key = key; paint(one.node, out); }
      });
    });
  }
  /** How long a call took, in words: milliseconds under a second, else seconds with one decimal. */
  function tookWords(ms) {
    return ms < 1000 ? ms + ' ms' : (ms / 1000).toFixed(1) + ' s';
  }
  /** An age with its exact local time on hover (and, when it is known, how long the read took), then the clock time of the instant. The clock time never moves. */
  function ago(parent, at, took) {
    var age = live(el('span', 'age'), function (t) { return agoWords(t - at); });
    age.setAttribute('title', stamp(at) + (typeof took === 'number' ? ' · the read took ' + tookWords(took) : ''));
    put(parent, age, el('span', null, ' (' + clock(at) + ')'));
    return parent;
  }
  function when(parent, prefix, at, took) {
    if (prefix) { parent.appendChild(el('span', null, prefix)); }
    return ago(parent, at, took);
  }

  /** The colour of a percent LEFT, by the number only: over 60 green, 10 to 60 orange, under 10 red; a dash has none. */
  function pctClass(left) { return left === null ? '' : left > 60 ? 'pct-g' : left < 10 ? 'pct-r' : 'pct-o'; }
  /** The colour of the minutes left on the 5-hour reset: under 10 green, 10 to 30 orange, over 30 red. */
  function resetClass(minutes) { return minutes < 10 ? 'rs-g' : minutes <= 30 ? 'rs-o' : 'rs-r'; }
  function leftParts(left) { return left === null ? [['—', '']] : [[left + '%', pctClass(left)], [' left', '']]; }
  /** A reset: under 24 hours the countdown (coloured on the 5-hour window only), from 24 hours on the local date and time, plain. */
  function resetParts(resetsAt, t, five) {
    var minutes = minutesLeft(resetsAt, t);
    if (minutes === null) { return [['resets in —', '']]; }
    if (resetsAt - t >= 86400000) { return [['resets ' + resetDate(resetsAt), '']]; }
    return [['resets in ', ''], [resetText(minutes), five ? resetClass(minutes) : '']];
  }

  /** A window cell: the percent LEFT or a dash, and under it when it resets. A Codex or Antigravity window whose reset has passed says so; an Antigravity window with no reset is full and says so. */
  function cellNode(cell, tool, five) {
    var box = el('div', 'cell');
    var passed = function (t) { return tool !== 'claude-code' && cell.resetsAt !== null && t >= cell.resetsAt; };
    box.appendChild(live(el('div', 'left'), function (t) { return passed(t) ? '—' : leftParts(cell.left); }));
    if (cell.resetsAt !== null) {
      var reset = live(el('div', 'reset'), function (t) { return passed(t) ? WORDS.passed : resetParts(cell.resetsAt, t, five); });
      reset.setAttribute('title', stamp(cell.resetsAt));
      box.appendChild(reset);
    } else if (cell.left !== null) {
      box.appendChild(el('div', 'reset', tool === 'agy' ? 'no reset' : 'resets in —'));
    }
    return box;
  }
  function namedLine(one) {
    var line = live(el('div', 'sub'), function (t) {
      var head = [[one.name + ' ', '']].concat(leftParts(one.left));
      return one.resetsAt === null ? head : head.concat([[' · ', '']], resetParts(one.resetsAt, t, false));
    });
    if (one.resetsAt !== null) { line.setAttribute('title', stamp(one.resetsAt)); }
    return line;
  }

  /** ONE width set for every table of a grouping, so the blocks line up. A width attribute, not a style: the CSP allows no inline style. */
  function cols(widths) {
    var group = el('colgroup');
    widths.forEach(function (width) { var col = el('col'); col.setAttribute('width', width + '%'); group.appendChild(col); });
    return group;
  }
  var W_SERVER = [14, 38, 24, 24];
  var W_ACCOUNT = [22, 78];
  var W_CODEX = [22, 28, 16, 16, 18];
  var W_USER = [14, 18, 23, 23, 22];
  function td(label, className) {
    var cell = el('td', className);
    cell.appendChild(el('span', 'label', label));
    return cell;
  }

  // ───── sorting ─────
  function sortOf(col) {
    var on = col.key === state.sort.key && (!col.tool || col.tool === state.sort.tool);
    return on ? (state.sort.dir === 'asc' ? 'ascending' : 'descending') : 'none';
  }
  function headRow(cols) {
    var row = el('tr');
    cols.forEach(function (col) {
      var th = el('th');
      th.setAttribute('scope', 'col');
      if (col.tool) { th.className = 'tn-' + col.tool; }
      th.setAttribute('aria-sort', sortOf(col));
      if (col.key) {
        th.appendChild(button(col.label, function () { sortBy(col.key, col.tool); })).className = 'sortbtn';
      } else {
        th.textContent = col.label;
      }
      row.appendChild(th);
    });
    return put(el('thead'), row);
  }
  function sortBy(key, tool) {
    var same = state.sort.key === key && (!tool || tool === state.sort.tool);
    state = { v: 1, groupBy: state.groupBy, sort: { key: key, dir: same && state.sort.dir === 'asc' ? 'desc' : 'asc', tool: tool || state.sort.tool } };
    changed();
  }
  function changed() {
    vscode.setState(state);
    syncControls();
    drawView();
  }
  function syncControls() {
    document.getElementById('group').value = state.groupBy;
    document.getElementById('sortkey').value = state.sort.key;
    document.getElementById('sorttool').value = state.sort.tool;
    document.getElementById('sorttool').hidden = state.groupBy !== 'user';
    var dir = document.getElementById('sortdir');
    dir.textContent = state.sort.dir === 'asc' ? '↑ ascending' : '↓ descending';
    dir.setAttribute('aria-label', state.sort.dir === 'asc' ? 'Sorted ascending: press for descending' : 'Sorted descending: press for ascending');
  }

  // ───── pieces shared by the groupings ─────
  function refuseButtons(place, canRefuse, canReadAgain) {
    var box = el('span', 'acts');
    if (canRefuse) { box.appendChild(button('Not this host', function () { post('refuse', { host: place }); })); }
    if (canReadAgain) { box.appendChild(button('Read this host again', function () { post('read-again', { host: place }); })); }
    return box;
  }
  function installLine(place, manager) {
    var command = manager === null ? undefined : INSTALL[manager];
    var line = el('div', 'muted');
    if (command === undefined) { return line; }
    line.appendChild(el('span', null, 'Run there: ' + command + (manager === 'apk' ? ' (to be run as root)' : '') + ' '));
    line.appendChild(button('Copy install command', function () { post('copy-install', { place: place }); }));
    return line;
  }
  /** What a place's heading says apart from its name: its read, its failure, or why it is not read. */
  function status(head, took) {
    var box = el('span', 'status');
    if (head.kind === 'read') { return when(box, 'read ', head.at, took); }
    if (head.kind === 'failed') {
      box.appendChild(el('span', 'bad', head.said + ' '));
      return ago(box, head.at, took);
    }
    box.appendChild(el('span', head.kind === 'jq-missing' ? 'bad' : 'muted', head.said));
    if (head.kind === 'jq-missing' && head.at !== null) { when(box, ' · read ', head.at, took); }
    return box;
  }
  /** The colour class of an email: only one of the twelve closed names, whatever the model holds. */
  function toneClass(tone) { return typeof tone === 'string' && /^em-([1-9]|1[0-2])$/.test(tone) ? tone : null; }
  /** An account text with its email in the email's own colour: the model's text starts with the email. The class is the extension's closed name, never a value of a reply. */
  function accountText(node, account) {
    var tone = toneClass(account.tone);
    if (tone === null || account.email === '' || account.text.indexOf(account.email) !== 0) {
      node.textContent = account.text;
      return node;
    }
    put(node, el('span', tone, account.email), account.text.length > account.email.length ? el('span', null, account.text.slice(account.email.length)) : null);
    return node;
  }
  /** A second line that is there even when it has nothing to say, so the rows of a block are as tall as one another. */
  var PAD = String.fromCharCode(160);
  function secondLine(text) { return el('div', 'muted', text === null || text === '' ? PAD : text); }
  function accountCell(row) {
    var cell = td('Account');
    cell.appendChild(accountText(el('div'), row.account));
    cell.appendChild(secondLine(row.account.plan === null ? null : 'plan ' + row.account.plan));
    if (row.sharedAcross !== null) { cell.appendChild(el('div', 'muted', 'shared across ' + row.sharedAcross + ' hosts')); }
    return cell;
  }
  /** A row of Antigravity stands for every group of models it has: one row for each, the group's windows in place of the first group's. Another tool's row is its own. */
  function expand(row) {
    if (!row.groups || row.groups.length === 0) { return [row]; }
    return row.groups.map(function (group) {
      return Object.assign({}, row, { five: group.five, week: group.week, group: group.name });
    });
  }
  /** A place name as shown: cut like the model's shown field, while the raw name is what a button posts. */
  function cut(name) { return name.length > 200 ? name.slice(0, 199) + '…' : name; }
  function windowCells(row) {
    if (row.span !== null) {
      var both = td('Usage');
      both.setAttribute('colspan', '2');
      both.appendChild(el('div', 'muted', row.span));
      return [both];
    }
    var five = td('5-hour window');
    five.appendChild(cellNode(row.five, row.tool, true));
    var week = td('Weekly window');
    week.appendChild(cellNode(row.week, row.tool, false));
    row.models.forEach(function (one) { week.appendChild(namedLine(one)); });
    row.others.forEach(function (one) { week.appendChild(namedLine(one)); });
    return [five, week];
  }

  // ───── Server ─────
  /** The tool's name and, under it, its version (and an Antigravity group's name); a tool with nothing to say there (Codex) keeps the line, empty. */
  function toolCell(row) {
    var cell = td('Tool', 'tb-' + row.tool);
    cell.appendChild(el('strong', 'tn-' + row.tool, WORDS.toolNames[row.tool]));
    var second = [row.version, row.group].filter(function (part) { return typeof part === 'string' && part !== ''; });
    cell.appendChild(secondLine(second.join(' · ')));
    return cell;
  }
  function serverRows(row) {
    return expand(row).map(function (one) {
      var tr = el('tr');
      put(tr, toolCell(one), accountCell(one));
      windowCells(one).forEach(function (cell) { tr.appendChild(cell); });
      return tr;
    });
  }
  /** The scope host wsl's own part: the count, the login-session line (always), a line for each distro and the notes. Every text is a textContent. */
  function wslPart(part) {
    var box = el('div', 'wslpart');
    put(box, el('div', 'muted', part.count), el('div', 'muted', part.login));
    part.distros.forEach(function (one) {
      var line = el('div', 'distro');
      put(line, el('strong', null, one.name), el('span', 'muted', ' ' + one.said));
      box.appendChild(line);
    });
    part.notes.forEach(function (note) { box.appendChild(el('div', 'muted', note)); });
    return box;
  }
  function serverUnit(block) {
    var node = el('section', 'block');
    var heading = el('div', 'heading');
    put(heading, el('strong', 'host', block.wsl === null ? block.shown : block.wsl.heading), status(block.head, block.tookMs), refuseButtons(block.host, block.canRefuse, block.canReadAgain));
    node.appendChild(heading);
    if (block.wsl !== null) { node.appendChild(wslPart(block.wsl)); }
    if (block.head.kind === 'jq-missing') { node.appendChild(installLine(block.host, block.head.manager)); }
    if (block.olderAt !== null) { node.appendChild(when(el('div', 'older'), 'The table below is of the read of ', block.olderAt)); }
    if (block.line !== null) { node.appendChild(el('div', 'muted', block.line)); }
    if (block.rows.length > 0) {
      var body = el('tbody');
      block.rows.forEach(function (row) { serverRows(row).forEach(function (tr) { body.appendChild(tr); }); });
      node.appendChild(put(el('table', 'acct'), cols(W_SERVER), headRow([
        { label: 'Tool', key: 'name' }, { label: 'Account', key: 'account' }, { label: '5-hour window', key: 'left5h' }, { label: 'Weekly window', key: 'leftWeek' },
      ]), body));
    }
    return node;
  }

  // ───── Claude account ─────
  function readCell(place, failure, readAt, took) {
    var cell = td('Read');
    if (failure !== null) {
      cell.appendChild(el('span', 'bad', failure.said + ' '));
      ago(cell, failure.at, took);
      when(cell, ' Older read ', readAt);
    } else {
      ago(cell, readAt, took);
    }
    return cell;
  }
  function accountUnit(account) {
    var node = el('section', 'block');
    var heading = el('div', 'heading');
    var title = el('strong', 'host tn-claude-code');
    var tone = toneClass(account.tone);
    if (tone === null || account.email === '' || account.text.indexOf(account.email) !== 0) {
      title.textContent = account.text;
    } else {
      put(title, el('span', tone, account.email), account.text.length > account.email.length ? el('span', null, account.text.slice(account.email.length)) : null);
    }
    put(heading, title);
    if (account.usage.plan !== null) { heading.appendChild(el('span', 'muted', 'plan ' + account.usage.plan)); }
    node.appendChild(heading);
    var usage = el('div', 'usage');
    if (account.usage.span !== null) {
      usage.appendChild(el('div', 'muted', account.usage.span));
    } else {
      var five = el('div'); put(five, el('span', 'label muted', '5-hour '), cellNode(account.usage.five, 'claude-code', true));
      var week = el('div'); put(week, el('span', 'label muted', 'Weekly '), cellNode(account.usage.week, 'claude-code', false));
      account.usage.models.forEach(function (one) { week.appendChild(namedLine(one)); });
      put(usage, five, week);
    }
    node.appendChild(usage);
    var body = el('tbody');
    account.places.forEach(function (one) {
      var tr = el('tr');
      put(tr, put(td('Place', 'tb-claude-code'), el('strong', null, one.shown)), readCell(one.place, one.failure, one.readAt, one.tookMs));
      body.appendChild(tr);
    });
    node.appendChild(put(el('table', 'acct'), cols(W_ACCOUNT), headRow([{ label: 'Place', key: 'name' }, { label: 'Read', key: 'age' }]), body));
    return node;
  }
  function lineRows(line) {
    return expand(line.row).map(function (one) {
      var tr = el('tr');
      put(tr, put(td('Place', 'tb-' + one.tool), el('strong', null, line.shown)), accountCell(one));
      windowCells(one).forEach(function (cell) { tr.appendChild(cell); });
      tr.appendChild(readCell(line.place, line.failure, one.readAt));
      return tr;
    });
  }
  function sectionUnit(title, tool, lines) {
    var node = el('section', 'block');
    node.appendChild(el('h2', 'tn-' + tool, title));
    var body = el('tbody');
    lines.forEach(function (line) { lineRows(line).forEach(function (tr) { body.appendChild(tr); }); });
    var columns = [{ label: 'Place', key: 'name' }, { label: 'Account', key: 'account' }, { label: '5-hour window', key: 'left5h' }, { label: 'Weekly window', key: 'leftWeek' }, { label: 'Read', key: 'age' }];
    node.appendChild(put(el('table', 'acct'), cols(W_CODEX), headRow(columns), body));
    return node;
  }
  function noneUnit(items) {
    var node = el('section', 'block');
    node.appendChild(el('h2', null, 'No Claude account'));
    items.forEach(function (one) {
      var row = el('div', 'none-row');
      put(row, el('strong', null, one.shown), el('span', 'muted', one.said));
      if (one.action === 'read-again') { row.appendChild(refuseButtons(one.place, false, true)); }
      if (one.action === 'copy-install') { row.appendChild(button('Copy install command', function () { post('copy-install', { place: one.place }); })); }
      node.appendChild(row);
    });
    return node;
  }

  // ───── User ─────
  function userCell(row, label, tool) {
    var cell = td(label, 'tb-' + tool);
    if (row === null) {
      cell.appendChild(el('div', 'muted dash', '—'));
      return cell;
    }
    cell.appendChild(accountText(el('div'), row.account));
    if (row.span !== null) {
      cell.appendChild(el('div', 'muted', row.span));
      return cell;
    }
    expand(row).forEach(function (one) {
      if (one.group) { cell.appendChild(el('div', 'sub', one.group)); }
      var line = el('div', 'usage');
      put(line, cellNode(one.five, row.tool, true), cellNode(one.week, row.tool, false));
      cell.appendChild(line);
    });
    row.models.forEach(function (one) { cell.appendChild(namedLine(one)); });
    row.others.forEach(function (one) { cell.appendChild(namedLine(one)); });
    return cell;
  }
  function userRow(row) {
    var tr = el('tr');
    var place = td('Place');
    put(place, el('strong', null, row.shown), refuseButtons(row.place, row.canRefuse, row.canReadAgain));
    var read = td('Read');
    read.appendChild(status(row.head, row.tookMs));
    if (row.failure !== null || row.olderAt !== null) {
      if (row.olderAt !== null) { when(read, ' Older read ', row.olderAt); }
    }
    if (row.head.kind === 'jq-missing') { read.appendChild(installLine(row.place, row.head.manager)); }
    put(tr, place, read);
    if (row.line !== null) {
      var none = td('Tools', null);
      none.setAttribute('colspan', '3');
      none.appendChild(el('div', 'muted', row.line));
      tr.appendChild(none);
    } else {
      put(tr, userCell(row.cells['claude-code'], 'Claude Code', 'claude-code'), userCell(row.cells.codex, 'Codex', 'codex'), userCell(row.cells.agy, 'Antigravity', 'agy'));
    }
    return tr;
  }

  // ───── the page ─────
  function unitsOf(view) {
    var units = [];
    if (view.groupBy === 'server') {
      view.blocks.forEach(function (block) { units.push({ key: 'server:' + block.host, data: block, build: function () { return serverUnit(block); } }); });
    } else if (view.groupBy === 'account') {
      view.accounts.forEach(function (account) {
        var key = 'account:' + (account.key === null ? 'place:' + account.places[0].place : account.key);
        units.push({ key: key, label: 'account:#' + units.length, data: account, build: function () { return accountUnit(account); } });
      });
      if (view.codex.length > 0) { units.push({ key: 'section:codex', data: view.codex, build: function () { return sectionUnit('Codex', 'codex', view.codex); } }); }
      if (view.agy.length > 0) { units.push({ key: 'section:agy', data: view.agy, build: function () { return sectionUnit('Antigravity', 'agy', view.agy); } }); }
      if (view.none.length > 0) { units.push({ key: 'section:none', data: view.none, build: function () { return noneUnit(view.none); } }); }
    } else {
      view.rows.forEach(function (row) { units.push({ key: 'user:' + row.place, data: row, build: function () { return userRow(row); } }); });
    }
    return units;
  }
  function userTable(rows) {
    var body = el('tbody');
    rows.forEach(function (row) { body.appendChild(row); });
    return put(el('table', 'acct'), cols(W_USER), headRow([
      { label: 'Place', key: 'name' }, { label: 'Read', key: 'age' },
      { label: 'Claude Code', key: 'left5h', tool: 'claude-code' }, { label: 'Codex', key: 'left5h', tool: 'codex' }, { label: 'Antigravity', key: 'left5h', tool: 'agy' },
    ]), body);
  }
  function drawView() {
    var host = document.getElementById('view');
    host.textContent = '';
    if (model === null) { host.appendChild(el('p', 'muted', WORDS.waiting)); return; }
    if (model.permission.kind === 'none') { return; }
    if (model.empty !== null) { host.appendChild(el('p', 'muted', model.empty)); return; }
    var units = unitsOf(viewOf(model, state, compareRows));
    var next = {};
    var nodes = units.map(function (unit) {
      var sig = JSON.stringify([unit.data, state]);
      var old = cache[unit.key];
      if (old && old.sig === sig) {
        next[unit.key] = old;
        return old.node;
      }
      current = unit.key;
      timed[unit.key] = [];
      var node = unit.build();
      node.setAttribute('data-unit', unit.label || unit.key);
      next[unit.key] = { sig: sig, node: node };
      return node;
    });
    Object.keys(cache).forEach(function (key) { if (!next[key]) { delete timed[key]; } });
    cache = next;
    if (state.groupBy === 'user') {
      host.appendChild(userTable(nodes));
    } else {
      nodes.forEach(function (node) { host.appendChild(node); });
    }
  }
  function drawToolbar() {
    var summary = document.getElementById('summary');
    summary.textContent = '';
    current = 'toolbar';
    timed.toolbar = [];
    var round = model.toolbar.round;
    if (round.kind === 'ended') {
      when(summary, 'last round ', round.at);
      summary.appendChild(el('span', null, ' · ' + round.read + ' of ' + round.asked + ' read'));
    } else if (round.kind === 'running') {
      summary.appendChild(el('span', null, 'reading ' + round.host + '… ' + round.index + ' of ' + round.total));
    } else {
      summary.appendChild(el('span', null, 'no round has run yet'));
    }
    document.getElementById('stop').textContent = model.toolbar.stopped ? 'Start the round' : 'Stop the round';
  }
  function drawPermission() {
    var top = document.getElementById('permission');
    var last = document.getElementById('permission-line');
    top.textContent = '';
    last.textContent = '';
    var permission = model.permission;
    if (permission.kind === 'given') {
      put(last, el('span', 'muted', 'Reading is allowed since ' + (permission.at === null ? permission.utc : stamp(permission.at)) + '. '), button('Withdraw the permission', function () { post('withdraw'); }));
      return;
    }
    var box = el('div', 'box');
    box.appendChild(el('p', null, permission.said));
    if (permission.kind === 'none') { box.appendChild(button('Allow reading…', function () { post('allow'); })); }
    if (permission.kind === 'waiting') { box.appendChild(button('Save now', function () { post('save-now'); })); }
    top.appendChild(box);
  }
  function drawAll() {
    drawToolbar();
    drawPermission();
    drawView();
  }

  function onBlock(data) {
    if (model === null || !data.block || typeof data.block.host !== 'string') { return; }
    var blocks = model.blocks.filter(function (one) { return one.host !== data.block.host; });
    blocks.push(data.block);
    blocks.sort(function (a, b) { return a.host < b.host ? -1 : a.host > b.host ? 1 : 0; });
    model = { v: model.v, toolbar: data.toolbar || model.toolbar, permission: model.permission, blocks: blocks, empty: null };
    drawAll();
  }

  document.getElementById('group').addEventListener('change', function (event) {
    state = viewState({ v: 1, groupBy: event.target.value, sort: state.sort });
    changed();
  });
  document.getElementById('sortkey').addEventListener('change', function (event) {
    state = viewState({ v: 1, groupBy: state.groupBy, sort: { key: event.target.value, dir: state.sort.dir, tool: state.sort.tool } });
    changed();
  });
  document.getElementById('sorttool').addEventListener('change', function (event) {
    state = viewState({ v: 1, groupBy: state.groupBy, sort: { key: state.sort.key, dir: state.sort.dir, tool: event.target.value } });
    changed();
  });
  document.getElementById('sortdir').addEventListener('click', function () {
    state = { v: 1, groupBy: state.groupBy, sort: { key: state.sort.key, dir: state.sort.dir === 'asc' ? 'desc' : 'asc', tool: state.sort.tool } };
    changed();
  });
  document.getElementById('refresh').addEventListener('click', function () { post('refresh'); });
  document.getElementById('stop').addEventListener('click', function () { post(model !== null && model.toolbar.stopped ? 'start' : 'stop'); });
  window.addEventListener('message', function (event) {
    var data = event.data;
    if (!data || typeof data !== 'object') { return; }
    if (data.type === 'model' && data.model) {
      model = data.model;
      drawAll();
    } else if (data.type === 'block') {
      onBlock(data);
    }
  });
  setInterval(tick, 15000);
  syncControls();
  drawView();
  post('ready');
})();
`;

/** The accounts tab's page. It holds no model: the host answers the page's `ready` with the whole model, and then with a `block` for each place that landed. */
export function accountsHtml(nonce: string, text: TextSettings = PLAIN_TEXT): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${ACCOUNTS_TITLE}</title>
<style nonce="${nonce}">${STYLE.replace('__TEXT__', textControlsStyle(text.size, text.tone))}
</style>
</head>
<body>
<header><h1>${ACCOUNTS_TITLE}</h1>${textControlsHtml(text.size, text.tone)}</header>
<div id="failed" class="failed" hidden></div>
<div class="toolbar">
  <label>Group by <select id="group">${options([['server', 'Server'], ['account', 'Claude account'], ['user', 'User']])}</select></label>
  <label>Sort by <select id="sortkey">${options(SORT_KEYS)}</select><select id="sorttool" hidden>${options([['claude-code', 'Claude Code'], ['codex', 'Codex'], ['agy', 'Antigravity']])}</select><button type="button" id="sortdir">↑ ascending</button></label>
  <button type="button" id="refresh">Refresh now</button>
  <button type="button" id="stop">Stop the round</button>
  <span id="summary"></span>
</div>
<div id="permission"></div>
<div id="view"></div>
<div id="permission-line" class="muted"></div>
<script nonce="${nonce}">${SCRIPT}</script>
</body>
</html>`;
}
