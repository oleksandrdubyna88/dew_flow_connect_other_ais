import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { agoWords } from '../gaugeWords';
import { INSTALL_JQ } from '../remoteAccountsRows';
import { accountsHtml } from '../remoteAccountsPage';
import { tableModel } from '../remoteAccountsTable';
import type { TableInput, TableModel } from '../remoteAccountsTable';
import { minutesLeft, resetDate, resetText, windowLabel } from '../usageWords';
import { stylesheet } from './cssRules';
import { bundledAccountsModule, pageScriptOf, runAccountsPage, tagCounts } from './accountsPageHarness';
import type { AccountsPage } from './accountsPageHarness';
import {
  NOW, ORG, agyOf, bigInput, claude, codexOf, codexUsage, inputOf, memoryOf, min, reading, seen, usageOf,
} from './accountsTableFixtures';

/**
 * The accounts tab, RUN (E2.S3, E2S3-T1 … T8 and T14 … T19): its html is parsed, its script runs over the tree with a clock the test moves, and the
 * test asserts on what the page drew and posted. Every email, organization and host is made up.
 */

const NONCE = 'n0nce-test';
const HTML = accountsHtml(NONCE);
const BIG: TableModel = tableModel(bigInput());

const page = (model: TableModel | undefined = BIG, state?: unknown): AccountsPage => {
  const run = runAccountsPage(HTML, { now: NOW, ...(state === undefined ? {} : { state }) });
  assert.equal(run.failure(), '', 'the page stopped on an error');
  if (model !== undefined) {
    run.message({ type: 'model', model });
    assert.equal(run.failure(), '', 'the page stopped on an error when it was given the model');
  }

  return run;
};

const GROUPS = ['server', 'account', 'user'] as const;

function choose(run: AccountsPage, id: string, value: string): void {
  const select = run.document.getElementById(id);
  assert.ok(select !== null, `no #${id}`);
  select.value = value;
  run.document.dispatch('change', select);
}

const HOSTILE = '<img src="x" onerror="alert(1)"><b>bold</b>$(account)';

/** The same structure with the text of a VM made of `bad`. */
function textedInput(bad: string): TableInput {
  const read = reading({
    claude: claude({ email: `${bad}@example.test`, org: bad, plan: bad, version: '1.2.3', usage: { state: 'read', unmatched: 0, windows: [{ name: '5h', usedPercent: 10, resetsAtMs: NOW + min(5) }, { name: { model: bad }, usedPercent: 1, resetsAtMs: null }] } }),
    codex: codexOf(),
    agy: agyOf(),
  });

  return inputOf({
    hosts: ['a', 'b', 'c'],
    memory: memoryOf(['a', seen(read, 3)], ['b', { failure: { sentence: `${bad}${'x'.repeat(10_000)}`, atMs: NOW - min(1) }, ...seen(read, 20) }], ['c', seen(reading({ claude: claude({ email: `${bad}@example.test`, usage: { state: 'skipped' } }) }), 2)]),
  });
}

// ───────────────── T1, T18 ─────────────────

test('E2S3-T1: a hostile model is drawn as TEXT — no element the model did not order, no handler, every value the text of a node', () => {
  for (const groupBy of GROUPS) {
    const hostile = page(tableModel(textedInput(HOSTILE)));
    const plain = page(tableModel(textedInput('plain')));
    choose(hostile, 'group', groupBy);
    choose(plain, 'group', groupBy);

    assert.deepEqual(tagCounts(hostile.document.root), tagCounts(plain.document.root), `${groupBy}: the element count by tag is not what the model ordered`);
    assert.equal(hostile.document.root.descendants().filter((one) => one.tag === 'img').length, 0);
    for (const one of hostile.document.root.descendants()) {
      assert.ok(![...one.attributes.keys()].some((name) => name.startsWith('on')), `${one.tag} has a handler attribute`);
    }
    assert.ok(hostile.text().includes('<img src="x" onerror="alert(1)"><b>bold</b>$ (account)'), `${groupBy}: the text of a VM is not on the page as text`);
    assert.ok(hostile.text().includes('$ (account)'), `${groupBy}: $( is not broken`);
  }
});

test('E2S3-T18: the hostile model in each grouping — every value is the text of a node, and the 10 KB text arrives cut', () => {
  const run = page(tableModel(textedInput(HOSTILE)));
  for (const groupBy of GROUPS) {
    choose(run, 'group', groupBy);

    assert.ok(run.text().length < 60_000, `${groupBy}: the page holds ${run.text().length} characters`);
    assert.ok(!run.text().includes('x'.repeat(300)), `${groupBy}: 10 KB of text arrived whole`);
  }
});

// ───────────────── T2 ─────────────────

const MODELS = {
  allowed: BIG,
  none: tableModel(inputOf({ permission: 'none', hosts: ['h'] })),
  waiting: tableModel(inputOf({ permission: 'waiting', hosts: ['h'] })),
  stopped: tableModel(inputOf({ stopped: true, hosts: ['h'] })),
};

test('E2S3-T2: each button posts its one message and nothing else', () => {
  const table: readonly (readonly [keyof typeof MODELS, string, Record<string, unknown>])[] = [
    ['allowed', 'Refresh now', { type: 'refresh' }],
    ['allowed', 'Stop the round', { type: 'stop' }],
    ['stopped', 'Start the round', { type: 'start' }],
    ['none', 'Allow reading…', { type: 'allow' }],
    ['allowed', 'Withdraw the permission', { type: 'withdraw' }],
    ['waiting', 'Save now', { type: 'save-now' }],
  ];
  for (const [which, label, message] of table) {
    const run = page(MODELS[which]);
    const before = run.posted.length;
    run.button(label).click();

    assert.deepEqual(run.posted.slice(before), [message], label);
  }
  const run = page(BIG);
  const before = run.posted.length;
  run.buttons('Not this host')[0]?.click();
  run.button('Read this host again').click();
  run.button('Copy install command').click();

  assert.deepEqual(run.posted.slice(before), [{ type: 'refuse', host: 'host-a' }, { type: 'read-again', host: 'host-c' }, { type: 'copy-install', place: 'host-d' }]);
  assert.deepEqual(run.posted[0], { type: 'ready' }, 'the page says ready, once, first');
  assert.equal(run.posted.filter((one) => one['type'] === 'ready').length, 1);
});

test('E2S3-T2: without the permission the page holds one block — what would be read and Allow reading… — and no host block', () => {
  const run = page(MODELS.none);

  assert.match(run.text(), /With your permission it logs in over ssh/u);
  assert.equal(run.buttons('Allow reading…').length, 1);
  assert.equal(run.document.querySelectorAll('[data-unit]').length, 0, 'no host block is drawn without the permission');
  assert.equal(run.buttons('Withdraw the permission').length, 0);
  const given = page(MODELS.allowed);
  assert.equal(given.buttons('Allow reading…').length, 0);
  assert.match(given.text(), /Reading is allowed since/u);
});

// ───────────────── T3, T5 ─────────────────

test('E2S3-T3: the clock goes on 10 minutes with no message — the age grows, the clock time stays, a reset went down by 10 minutes', () => {
  const model = tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 3)]) }));
  const run = page(model);
  const clockTime = (ms: number): string => `${String(new Date(ms).getHours()).padStart(2, '0')}:${String(new Date(ms).getMinutes()).padStart(2, '0')}`;
  const heading = (): string => run.document.querySelector('.heading')?.textContent ?? '';

  assert.match(heading(), new RegExp(`read 3 min ago \\(${clockTime(NOW - min(3))}\\)`, 'u'));
  assert.match(run.text(), /resets in 2h 10m/u);
  assert.equal(run.posted.length, 1, 'only ready was posted');
  run.advance(min(10));

  assert.match(heading(), new RegExp(`read 13 min ago \\(${clockTime(NOW - min(3))}\\)`, 'u'), 'the age grew and the clock time did not move');
  assert.match(run.text(), /resets in 2h 0m/u, 'a reset went down by 10 minutes');
  assert.equal(run.posted.length, 1, 'the host was not asked anything');
});

test('E2S3-T5: a hover on an age and on a reset holds the exact local date and time', () => {
  const model = tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 3)]) }));
  const run = page(model);
  const stamp = (ms: number): string => {
    const d = new Date(ms);
    const two = (n: number): string => String(n).padStart(2, '0');

    return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())} ${two(d.getHours())}:${two(d.getMinutes())}:${two(d.getSeconds())}`;
  };
  const titles = run.document.querySelectorAll('[title]').map((one) => one.getAttribute('title'));

  assert.ok(titles.includes(stamp(NOW - min(3))), `no hover holds the read's instant: ${JSON.stringify(titles)}`);
  assert.ok(titles.includes(stamp(NOW + min(130))), 'no hover holds the reset\'s instant');
});

// ───────────────── T4, T19 ─────────────────

const unitsOf = (run: AccountsPage): Map<string, unknown> => new Map(run.document.querySelectorAll('[data-unit]').map((one) => [one.getAttribute('data-unit') ?? '', one]));

test('E2S3-T4: a block post changes that host\'s block only — the other blocks keep their node identity', () => {
  const run = page(BIG);
  const before = unitsOf(run);
  const landed = tableModel({ ...bigInput(), memory: memoryOf(['host-d', seen(reading({ claude: claude({ email: 'new@example.test', usage: usageOf(1, 1, 1) }) }), 0)]) });
  const block = landed.blocks.find((one) => one.host === 'host-d');
  run.message({ type: 'block', block, toolbar: landed.toolbar });
  const after = unitsOf(run);

  assert.equal(after.size, before.size);
  for (const [key, node] of before) {
    assert.equal(after.get(key) === node, key !== 'server:host-d', `${key}: ${after.get(key) === node ? 'kept' : 'replaced'}`);
  }
  assert.match(run.text(), /new@example\.test/u);
});

test('E2S3-T19: a block post in Claude account and in User — the grouping and the sort stay, only the blocks and rows holding that place change', () => {
  const landed = tableModel({ ...bigInput(), memory: memoryOf(['host-d', seen(reading({ claude: claude({ email: 'new@example.test', usage: usageOf(1, 1, 1) }) }), 0)]) });
  const block = landed.blocks.find((one) => one.host === 'host-d');
  for (const groupBy of ['account', 'user'] as const) {
    const run = page(BIG);
    choose(run, 'group', groupBy);
    choose(run, 'sortkey', 'age');
    const before = unitsOf(run);
    run.message({ type: 'block', block, toolbar: BIG.toolbar });
    const after = unitsOf(run);
    const state = run.states[run.states.length - 1] as { groupBy: string; sort: { key: string } };

    assert.equal(state.groupBy, groupBy);
    assert.equal(state.sort.key, 'age');
    assert.equal(run.document.getElementById('group')?.value, groupBy);
    let kept = 0;
    for (const [key, node] of before) {
      kept += after.get(key) === node ? 1 : 0;
    }
    assert.ok(kept >= before.size - 2, `${groupBy}: ${kept} of ${before.size} units kept their node — only what holds host-d may change`);
    assert.ok(kept > 0, `${groupBy}: everything was redrawn`);
    assert.ok(run.text().includes('new@example.test') || groupBy === 'user', `${groupBy}: the landed place is not on the page`);
  }
});

// ───────────────── T6 ─────────────────

test('E2S3-T6: the narrow width — the stylesheet stacks the columns and shows the labels; every cell has a label node; Group by and Sort by are in the toolbar', () => {
  const css = HTML.slice(HTML.indexOf('<style'), HTML.indexOf('</style>'));
  assert.match(css, /@media \(max-width: 560px\)/u);
  const rules = stylesheet(HTML.replace(/<style nonce="[^"]*">/u, '<style>'));
  const stacked = rules.filter((rule) => /display:\s*block/u.test(rule.body) && rule.selector.includes('td'));
  assert.ok(stacked.length > 0, 'no rule makes the cells block under 560px');
  assert.ok(rules.some((rule) => rule.selector.includes('.label') && /display:\s*none/u.test(rule.body)), 'the labels are hidden at full width');
  assert.ok(rules.some((rule) => rule.selector.includes('.label') && /display:\s*inline-block/u.test(rule.body)), 'the labels are shown when stacked');

  const run = page(BIG);
  for (const groupBy of GROUPS) {
    choose(run, 'group', groupBy);
    const cells = run.document.querySelectorAll('td');
    assert.ok(cells.length > 0, `${groupBy}: no cells`);
    for (const cell of cells) {
      assert.ok(cell.children.some((one) => one.className === 'label'), `${groupBy}: a cell has no label node`);
    }
    assert.ok(run.document.querySelectorAll('table.acct').every((one) => one.className.includes('acct')));
  }
  const toolbar = run.document.querySelector('.toolbar');
  assert.ok(toolbar?.querySelector('#group') !== null && toolbar?.querySelector('#sortkey') !== null, 'the switch and the sort are in the toolbar');
});

// ───────────────── T7 ─────────────────

const EMBEDDED = ['compareRows', 'viewOf', 'viewState', 'minutesLeft', 'resetText', 'resetDate', 'agoWords'];

test('E2S3-T7: the bundled page defines every embedded function, and its words equal usageWords\' and agoWords\' on their own cases', () => {
  const html = bundledAccountsModule().accountsHtml(NONCE);
  const script = pageScriptOf(html);
  for (const name of EMBEDDED) {
    assert.ok(new RegExp(`var ${name}\\s*=\\s*function`, 'u').test(script), `${name} is not defined in the page the bundle produces`);
  }
  assert.doesNotThrow(() => new Function('document', 'window', 'acquireVsCodeApi', 'setInterval', 'Date', script));
  const minutes = [0, 1, 59, 60, 61, 130, 1439, 1440, 3_800, 10_080];
  for (const [label, source] of [['source', HTML], ['bundle', html]] as const) {
    const memory = memoryOf(...minutes.map((m, index) => [`h${index}`, seen(reading({ claude: claude({ email: `u${index}@example.test`, usage: { state: 'read', unmatched: 0, windows: [{ name: '5h', usedPercent: 30, resetsAtMs: NOW + min(m) + 1_000 }] } }) }), 1 + index * 7)] as const));
    const model = tableModel(inputOf({ hosts: minutes.map((_m, index) => `h${index}`), memory }));
    const run = runAccountsPage(source, { now: NOW });
    run.message({ type: 'model', model });
    const text = run.text();

    for (const [index, m] of minutes.entries()) {
      const want = m >= 1440 ? `resets ${resetDate(NOW + min(m) + 1_000)}` : `resets in ${resetText(minutesLeft(NOW + min(m) + 1_000, NOW))}`;
      assert.ok(text.includes(want), `${label}: "${want}" is not on the page for ${m} minutes`);
      assert.ok(text.includes(`${agoWords(min(1 + index * 7))} (`), `${label}: the age ${agoWords(min(1 + index * 7))} is not on the page`);
    }
    assert.ok(text.includes('70% left'), `${label}: % left is not said as left`);
  }
});

// ───────────────── T8 ─────────────────

test('E2S3-T8: the CSP and the nonce — no source but the nonce, no http, no unsafe-', () => {
  const policy = /<meta http-equiv="Content-Security-Policy" content="([^"]*)"/u.exec(HTML)?.[1] ?? '';

  assert.equal(policy, `default-src 'none'; script-src 'nonce-${NONCE}'; style-src 'nonce-${NONCE}'`);
  assert.ok(!policy.includes('http') && !policy.includes('unsafe-'));
  assert.equal(HTML.split(`nonce="${NONCE}"`).length - 1, 2, 'one script and one style carry the nonce');
  assert.ok(!/<script(?![^>]*nonce=)/u.test(HTML), 'a script with no nonce');
  // The parsed tree, not the text: no element of the page, as drawn over a model, carries an `on…` attribute (any case).
  const handlers = page().document.root.descendants().flatMap((one) => [...one.attributes.keys()].filter((name) => name.toLowerCase().startsWith('on')).map((name) => `${one.tag}[${name}]`));
  assert.deepEqual(handlers, [], 'an inline handler in the markup');
  assert.ok(!pageScriptOf(HTML).includes('innerHTML'), 'the page script assigns no markup');
});

// ───────────────── T14 … T17 ─────────────────

const viewHtml = (run: AccountsPage): string => run.document.getElementById('view')?.innerHTML ?? '';

test('E2S3-T14: each grouping is drawn — real table, thead and th[scope=col], every place of the model; Server again equals the first tree', () => {
  const run = page(BIG);
  const first = viewHtml(run);
  const places = BIG.blocks.map((one) => one.host);
  for (const groupBy of ['account', 'user', 'server'] as const) {
    choose(run, 'group', groupBy);
    const view = viewHtml(run);

    assert.ok(run.document.querySelectorAll('table').length > 0, `${groupBy}: no table`);
    assert.ok(run.document.querySelectorAll('thead').length > 0, `${groupBy}: no thead`);
    assert.ok(run.document.querySelectorAll('th[scope="col"]').length > 0, `${groupBy}: no th scope=col`);
    for (const place of places) {
      assert.ok(view.includes(place), `${groupBy}: the place ${place} is not drawn`);
    }
    if (groupBy === 'server') {
      assert.equal(view, first, 'Server drawn again is not the tree it was drawn as at first');
    } else {
      assert.notEqual(view, first);
    }
  }
});

const headers = (run: AccountsPage): { readonly label: string; readonly sort: string | null }[] =>
  run.document.querySelectorAll('th[scope="col"]').map((one) => ({ label: one.textContent, sort: one.getAttribute('aria-sort') }));

test('E2S3-T15: a header click sorts ascending, a second descending; aria-sort follows and is none on the others; a dash is last both times; Sort by agrees', () => {
  const run = page(tableModel(bigInput()));
  choose(run, 'group', 'user');
  const placeOrder = (): string[] => run.document.querySelectorAll('tr[data-unit]').map((one) => one.getAttribute('data-unit') ?? '');
  const header = run.document.querySelectorAll('th[scope="col"]').find((one) => one.textContent === 'Claude Code');
  assert.ok(header !== undefined);

  header.querySelector('button')?.click();
  const up = placeOrder();
  assert.equal(headers(run).find((one) => one.label === 'Claude Code')?.sort, 'ascending');
  assert.ok(headers(run).filter((one) => one.label !== 'Claude Code').every((one) => one.sort === 'none'));
  assert.equal(run.document.getElementById('sortkey')?.value, 'left5h');
  assert.deepEqual(up.slice(0, 4), ['user:host-a', 'user:host-b', 'user:host-e', 'user:host-g'], 'ascending: 44% left for the three places of one account (by name), then 90%');

  run.document.querySelectorAll('th[scope="col"]').find((one) => one.textContent === 'Claude Code')?.querySelector('button')?.click();
  const down = placeOrder();
  assert.equal(headers(run).find((one) => one.label === 'Claude Code')?.sort, 'descending');
  assert.deepEqual(down.slice(0, 4), ['user:host-g', 'user:host-a', 'user:host-b', 'user:host-e'], 'descending: 90% first, equal ones still by name ascending');
  assert.deepEqual(up.slice(4), down.slice(4), 'the places with a dash are last both times, in the same name order');
  assert.equal(run.document.getElementById('sortdir')?.textContent?.includes('descending'), true);
});

test('E2S3-T16: no message — a switch through the three groupings, each header twice and Sort by changed post nothing', () => {
  const run = page(BIG);
  const before = run.posted.length;
  for (const groupBy of ['account', 'user', 'server', 'account']) {
    choose(run, 'group', groupBy);
    const count = run.document.querySelectorAll('button.sortbtn').length;
    for (let index = 0; index < count; index += 1) {
      run.document.querySelectorAll('button.sortbtn')[index]?.click();
      run.document.querySelectorAll('button.sortbtn')[index]?.click();
    }
  }
  choose(run, 'sortkey', 'resetWeek');
  run.document.getElementById('sortdir')?.click();

  assert.equal(run.posted.length, before, `the page posted ${JSON.stringify(run.posted.slice(before))}`);
});

test('E2S3-T17: the state — setState holds exactly { v, groupBy, sort }, no model value; a page built again on it draws that grouping and sort; a bad state draws Server by name', () => {
  const run = page(BIG);
  choose(run, 'group', 'user');
  run.document.querySelectorAll('th[scope="col"]').find((one) => one.textContent === 'Codex')?.querySelector('button')?.click();
  const last = run.states[run.states.length - 1] as Record<string, unknown>;

  assert.deepEqual(Object.keys(last).sort(), ['groupBy', 'sort', 'v']);
  assert.deepEqual(last, { v: 1, groupBy: 'user', sort: { key: 'left5h', dir: 'asc', tool: 'codex' } });
  for (const stored of run.states) {
    const text = JSON.stringify(stored);
    assert.ok(!text.includes('host-') && !text.includes('example'), 'a value of the model is in the state');
  }
  const again = page(BIG, last);
  assert.equal(again.document.getElementById('group')?.value, 'user');
  assert.equal(again.document.getElementById('sortkey')?.value, 'left5h');
  assert.equal(again.document.getElementById('sorttool')?.value, 'codex');
  assert.equal(headers(again).find((one) => one.label === 'Codex')?.sort, 'ascending');

  const bad = page(BIG, { v: 1, groupBy: 'galaxy', sort: { key: 'size', dir: 'up', tool: 'x' } });
  assert.equal(bad.document.getElementById('group')?.value, 'server');
  assert.equal(headers(bad).find((one) => one.label === 'Tool')?.sort, 'ascending');
  const host = bad.document.querySelectorAll('[data-unit]').map((one) => one.getAttribute('data-unit'));
  assert.deepEqual(host.slice(0, 3), ['server:host-a', 'server:host-b', 'server:host-c']);
});

test('E2S3-T2: the install command of a place is shown from the closed table, never from a reply', () => {
  const run = page(BIG);

  assert.ok(run.text().includes(`Run there: ${INSTALL_JQ['apt-get']}`));
});

test('E2S3-F3: the User grouping draws a Codex window that is neither 5-hour nor weekly, as the Server grouping does', () => {
  const odd = codexUsage({ windows: [{ minutes: 300, usedPercent: 10, resetsAtMs: NOW + min(65) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(6_000) }, { minutes: 1_440, usedPercent: 40, resetsAtMs: NOW + min(90) }] });
  const model = tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ codex: codexOf(odd) }), 2)]) }));
  const label = windowLabel({ minutes: 1_440 });
  const run = page(model);
  assert.ok(viewHtml(run).includes(label), 'the Server grouping does not draw the odd window');
  choose(run, 'group', 'user');
  assert.ok(viewHtml(run).includes(label), 'the User grouping drops the odd window');
  assert.ok(viewHtml(run).includes('60%'), 'its figure is not drawn');
});

test('E2S3-N1: no attribute of the page holds an account of a VM — the units of the Claude account grouping are numbered', () => {
  const run = page(BIG);
  choose(run, 'group', 'account');
  const units = run.document.querySelectorAll('[data-unit]').map((one) => one.getAttribute('data-unit') ?? '');
  assert.ok(units.length > 0, 'the grouping drew no unit');
  assert.ok(units.every((unit) => !unit.includes('@') && !unit.includes(ORG)), `a unit is named by an account: ${units.join(' | ')}`);
});

// ───────────────── the owner's look: columns, tool colours, the mod's colours, the 24-hour rule ─────────────────

const RAW_CSS = /<style[^>]*>([\s\S]*?)<\/style>/u.exec(HTML)?.[1] ?? '';

const claudeAt = (usedPercent: number | null, resetsInMin: number, email = 'u@example.test'): ReturnType<typeof claude> =>
  claude({ email, usage: { state: 'read', unmatched: 0, windows: [{ name: '5h', usedPercent, resetsAtMs: NOW + min(resetsInMin) }, { name: 'week', usedPercent, resetsAtMs: NOW + min(resetsInMin) }] } });

const classesOf = (run: AccountsPage, selector: string): string[] => run.document.querySelectorAll(selector).map((one) => one.className);

test('E2S3-L1: every table of a grouping has the same colgroup widths, so the blocks line up', () => {
  const two = tableModel(inputOf({ hosts: ['a', 'b', 'c'], memory: memoryOf(
    ['a', seen(reading({ claude: claudeAt(10, 100), codex: codexOf(), agy: agyOf() }), 1)],
    ['b', seen(reading({ claude: claudeAt(50, 100, 'v@example.test'), codex: codexOf(), agy: agyOf() }), 2)],
    ['c', seen(reading({ claude: claudeAt(20, 100, 'w@example.test') }), 3)],
  ) }));
  const run = page(two);
  for (const groupBy of GROUPS) {
    choose(run, 'group', groupBy);
    const widths = run.document.querySelectorAll('table').map((table) => table.querySelectorAll('col').map((col) => col.getAttribute('width') ?? '').join(','));
    assert.ok(widths.length >= 2 || groupBy !== 'server', groupBy + ': too few tables to compare');
    assert.ok(widths.every((one) => one.length > 0), groupBy + ': a table has no colgroup');
    // A column with no width of its own gets 0 % in a fixed table: its text then stands one letter to a line (the owner saw it on Antigravity's Read).
    for (const table of run.document.querySelectorAll('table')) {
      const cols = table.querySelectorAll('col').map((col) => Number.parseFloat(col.getAttribute('width') ?? ''));
      const heads = table.querySelectorAll('th').length;
      assert.equal(cols.length, heads, `${groupBy}: ${cols.length} widths for ${heads} columns`);
      assert.equal(cols.reduce((sum, one) => sum + one, 0), 100, `${groupBy}: the widths do not add up to 100 %`);
    }
    if (groupBy === 'server') {
      assert.equal(new Set(widths).size, 1, 'the Server tables do not share one width set: ' + widths.join(' | '));
    }
  }
  const css = RAW_CSS;
  assert.match(css, /table-layout:\s*fixed/u, 'the tables are not fixed');
  assert.match(css, /overflow-wrap:\s*anywhere/u);
});

test('E2S3-L2: each tool has its colour on its name and a border on its row; none comes from a value of a VM', () => {
  const model = tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ claude: claudeAt(30, 100), codex: codexOf(), agy: agyOf() }), 1)]) }));
  const run = page(model);
  const names = classesOf(run, 'strong');
  for (const tool of ['claude-code', 'codex', 'agy']) {
    assert.ok(names.some((one) => one.includes('tn-' + tool)), tool + ' has no coloured name');
    assert.ok(classesOf(run, 'td').some((one) => one.includes('tb-' + tool)), tool + ' has no bordered cell');
  }
  const css = RAW_CSS;
  for (const hex of ['#d97757', '#38bdf8', '#a78bfa']) {
    assert.ok(css.includes(hex), hex + ' is not in the stylesheet');
  }
  assert.ok(css.includes('body.vscode-light'), 'no light-theme colours');
});

test('E2S3-L3: the percent left has the mod\'s colour by its number — 61 green, 60 orange, 10 orange, 9 red — only the number, never a dash', () => {
  const left = (usedPercent: number | null): string[] => {
    const model = tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ claude: claudeAt(usedPercent, 40_000) }), 1)]) }));
    const run = page(model);

    return run.document.querySelectorAll('span').filter((one) => /^pct-/u.test(one.className)).map((one) => one.className + ':' + one.textContent);
  };
  assert.deepEqual(left(39).slice(0, 1), ['pct-g:61%']);
  assert.deepEqual(left(40).slice(0, 1), ['pct-o:60%']);
  assert.deepEqual(left(90).slice(0, 1), ['pct-o:10%']);
  assert.deepEqual(left(91).slice(0, 1), ['pct-r:9%']);
  assert.deepEqual(left(null), [], 'a dash has a colour class');
  assert.ok(RAW_CSS.includes('#22c55e') && RAW_CSS.includes('#f97316') && RAW_CSS.includes('#ef4444'));
});

test('E2S3-L4: the 5-hour countdown is coloured by minutes left — 9 green, 10 and 30 orange, 31 red; the weekly one is plain', () => {
  const colour = (minutes: number): string[] => {
    const model = tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ claude: claudeAt(30, minutes) }), 1)]) }));
    const run = page(model);

    return run.document.querySelectorAll('span').filter((one) => /^rs-/u.test(one.className)).map((one) => one.className + ':' + one.textContent);
  };
  // The model's reset is an instant; the page reads minutes with a floor, so a minute is added for the exact boundary.
  assert.deepEqual(colour(9), ['rs-g:9m']);
  assert.deepEqual(colour(10), ['rs-o:10m']);
  assert.deepEqual(colour(30), ['rs-o:30m']);
  assert.deepEqual(colour(31), ['rs-r:31m']);
  assert.equal(colour(31).length, 1, 'the weekly reset is coloured too');
});

test('E2S3-L5: under 24 hours the reset is a countdown, from 24 hours on it is the local date and time, plain — 23h59m, 24h, 4d12h', () => {
  const said = (minutes: number): string => {
    const model = tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ claude: claudeAt(30, minutes) }), 1)]) }));

    return page(model).text();
  };
  assert.ok(said(23 * 60 + 59).includes('resets in 23h 59m'));
  assert.ok(!said(23 * 60 + 59).includes('resets Nov'));
  const day = said(24 * 60);
  assert.ok(day.includes('resets ' + resetDate(NOW + min(24 * 60))), 'at 24 h: ' + day);
  assert.ok(!day.includes('resets in 1d'));
  assert.ok(said(4 * 1440 + 12 * 60).includes('resets ' + resetDate(NOW + min(4 * 1440 + 12 * 60))));
  assert.match(resetDate(NOW + min(4 * 1440 + 12 * 60)), /^[A-Z][a-z]{2} \d{1,2}, \d{2}:\d{2}$/u);
});
