import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { readAccounts } from '../remoteAccountsReply';
import { accountsHtml } from '../remoteAccountsPage';
import { tableModel } from '../remoteAccountsTable';
import type { TableModel } from '../remoteAccountsTable';
import { GOOD_LINES, NOW as PARSER_NOW, frame, ok, readOf } from './accountsFrames';
import { runAccountsPage } from './accountsPageHarness';
import type { AccountsPage } from './accountsPageHarness';
import { NOW, agyOf, claude, codexOf, inputOf, memoryOf, reading, seen } from './accountsTableFixtures';

/**
 * The owner's decision of 2026-11-27: a vendor program (Claude Code, Codex, Antigravity) that runs as root from a path another user can write is READ, and its row says so
 * in one muted line under the account. A TOOL there is still refused (the script's runs: `remoteAccountsRuns.test.ts`, `remoteAccountsUid0.test.ts`). And a row that
 * is refused, or only says why its tool was not read, says it ONCE.
 */

const WARN = '/home/a/.local/bin';
const withWarning = (vendor: 'claude' | 'codex' | 'agy', value: string): string => frame(...GOOD_LINES.flatMap((line) => (line === `COAI-VENDOR ${vendor} found` ? [line, `:unsafe-path ${value}`] : [line])));

test('R3-W1: the parser reads :unsafe-path under each vendor as the path it shows; a dash or a path in the shape of a credential is null; none is no field at all', () => {
  const plain = readOf(readAccounts(ok(frame(...GOOD_LINES)), 'no-claude-usage', PARSER_NOW));
  for (const side of [plain.claude, plain.codex, plain.agy]) {
    assert.ok(side.state === 'found' && !('unsafePath' in side), 'a warning where there is none');
  }
  for (const vendor of ['claude', 'codex', 'agy'] as const) {
    const got = readOf(readAccounts(ok(withWarning(vendor, WARN)), 'no-claude-usage', PARSER_NOW));
    const side = got[vendor];
    assert.ok(side.state === 'found' && side.unsafePath === WARN, `${vendor}: ${JSON.stringify(side)}`);
    for (const other of (['claude', 'codex', 'agy'] as const).filter((one) => one !== vendor)) {
      assert.ok(!('unsafePath' in got[other]), `${vendor}: the warning leaked to ${other}`);
    }
    const dash = readOf(readAccounts(ok(withWarning(vendor, '-')), 'no-claude-usage', PARSER_NOW))[vendor];
    assert.ok(dash.state === 'found' && dash.unsafePath === null, `${vendor}: a dash`);
    const shaped = readOf(readAccounts(ok(withWarning(vendor, `/${'M4RK'.repeat(10)}`)), 'no-claude-usage', PARSER_NOW))[vendor];
    assert.ok(shaped.state === 'found' && shaped.unsafePath === null, `${vendor}: a path in the shape of a credential makes no row unreadable and is not shown`);
  }
});

test('R3-W1: a path outside the closed form, a second line, and the key under no vendor are an unreadable answer', () => {
  for (const bad of ['relative/path', '/a b', '/a;id', '/a$(x)', '', `/${'a/'.repeat(120)}`]) {
    assert.equal(readAccounts(ok(withWarning('claude', bad)), 'no-claude-usage', PARSER_NOW).kind, 'unreadable', JSON.stringify(bad));
  }
  const twice = frame(...GOOD_LINES.flatMap((line) => (line === 'COAI-VENDOR codex found' ? [line, ':unsafe-path /a', ':unsafe-path /b'] : [line])));
  assert.deepEqual(readAccounts(ok(twice), 'no-claude-usage', PARSER_NOW), { kind: 'unreadable', why: 'key' });
  const under = frame(...GOOD_LINES.flatMap((line) => (line === 'COAI-USAGE codex read' ? [line, ':unsafe-path /a'] : [line])));
  assert.deepEqual(readAccounts(ok(under), 'no-claude-usage', PARSER_NOW), { kind: 'unreadable', why: 'key' });
});

const model = (path: string | null | undefined): TableModel => tableModel(inputOf({
  hosts: ['a'],
  memory: memoryOf(['a', seen(reading({
    claude: { ...claude({ usage: { state: 'skipped' } }), ...(path === undefined ? {} : { unsafePath: path }) },
    codex: { ...codexOf(), ...(path === undefined ? {} : { unsafePath: path }) },
    agy: { ...agyOf(), ...(path === undefined ? {} : { unsafePath: path }) },
  }), 1)]),
}));

test('R3-W2: the row of a vendor that runs from a path others can write carries one warning of the model\'s own words and the path through forList; no path, no warning, a path of none says so', () => {
  assert.deepEqual(model(undefined).blocks[0]?.rows.map((row) => row.warning), [null, null, null]);
  assert.deepEqual(model(WARN).blocks[0]?.rows.map((row) => row.warning), Array(3).fill(`runs as root although ${WARN} is writable by another user`));
  assert.deepEqual(model(null).blocks[0]?.rows.map((row) => row.warning), Array(3).fill('runs as root although a folder of its path is writable by another user'));
  const hostile = model('/x/$(id)/<img>').blocks[0]?.rows[0]?.warning ?? '';
  assert.ok(hostile.includes('$ (id)') && !hostile.includes('$(id)'), hostile);
  assert.ok(model(`/${'p'.repeat(500)}`).blocks[0]?.rows.every((row) => (row.warning ?? '').length < 280));
});

const page = (m: TableModel): AccountsPage => {
  const run = runAccountsPage(accountsHtml('n0nce-warn'), { now: NOW });
  run.message({ type: 'model', model: m });
  assert.equal(run.failure(), '', 'the page stopped on an error');

  return run;
};

function choose(run: AccountsPage, value: string): void {
  const select = run.document.getElementById('group');
  assert.ok(select !== null);
  select.value = value;
  run.document.dispatch('change', select);
}

test('R3-W2: the warning is one muted line, drawn as text under the account in every grouping, and nowhere when there is none', () => {
  const sentence = `runs as root although ${WARN} is writable by another user`;
  const count = (run: AccountsPage): number => run.text().split(sentence).length - 1;
  const run = page(model(WARN));
  choose(run, 'server');
  assert.equal(count(run), 3, 'Server: one for each tool');
  const lines = run.document.querySelectorAll('div').filter((one) => one.textContent === sentence);
  assert.ok(lines.length === 3 && lines.every((one) => one.className === 'muted warn'), lines.map((one) => one.className).join(','));
  choose(run, 'account');
  assert.equal(count(run), 3, 'Claude account: the place of the account, the Codex row and the Antigravity row');
  choose(run, 'user');
  assert.equal(count(run), 3, 'User: one in each tool cell');
  const none = page(model(undefined));
  for (const groupBy of ['server', 'account', 'user']) {
    choose(none, groupBy);
    assert.ok(!none.text().includes('writable by another user'), groupBy);
  }
  const hostile = page(tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', seen(reading({ claude: { ...claude({ usage: { state: 'skipped' } }), unsafePath: '/<img src=x onerror=1>' } }), 1)]) })));
  assert.equal(hostile.document.root.descendants().filter((one) => one.tag === 'img').length, 0);
});

test('R3-W3: a row that only says why its tool was not read says it ONCE — the sentence stands in the cells of the windows, and the account cell holds a dash — in every grouping', () => {
  const m = tableModel(inputOf({
    hosts: ['a', 'b'],
    memory: memoryOf(
      ['a', seen(reading({ claude: { state: 'signed-out' }, codex: { state: 'unsafe', path: '/opt/x' }, agy: { state: 'unreadable', why: 'credential-shape' } }), 1)],
      ['b', seen(reading({ claude: claude({ usage: { state: 'skipped' } }) }), 1)],
    ),
  }));
  const rows = m.blocks[0]?.rows ?? [];
  assert.ok(rows.length === 3 && rows.every((row) => row.account.text === '—' && row.span !== null && row.span !== row.account.text), JSON.stringify(rows.map((row) => [row.account.text, row.span])));
  const run = page(m);
  for (const groupBy of ['server', 'account', 'user']) {
    choose(run, groupBy);
    for (const sentence of ['signed out', '/opt/x is writable by another user', 'not shown: a value in the answer looked like a credential']) {
      const n = run.text().split(sentence).length - 1;
      assert.ok(n >= 1, `${groupBy}: "${sentence}" is on the page ${n} times`);
    }
  }
  choose(run, 'server');
  assert.equal(run.text().split('/opt/x is writable by another user').length - 1, 1, 'Server: the refused path sentence stands once');
  assert.equal(run.text().split('signed out').length - 1, 1, 'Server: signed out stands once');
});
