import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { accountsHtml } from '../remoteAccountsPage';
import { tableModel } from '../remoteAccountsTable';
import type { TableModel } from '../remoteAccountsTable';
import { VENDOR_PALETTE } from '../vendorColour';
import { stylesheet, winning } from './cssRules';
import { runAccountsPage } from './accountsPageHarness';
import type { AccountsPage } from './accountsPageHarness';
import {
  AGY_MAIL, ANN, COX, NOW, agyOf, agyUsage, claude, codexOf, inputOf, memoryOf, reading, seen, usageOf,
} from './accountsTableFixtures';

/**
 * The look the owner asked for on 2026-11-27, RUN: the text of a tool row stands off its coloured border, every tool row of a block has the same lines,
 * every email has ONE colour from the palette in every tool and place, and Antigravity shows its two groups of models with their windows.
 */

const HTML = accountsHtml('n0nce-look');
const GROUPS = ['server', 'account', 'user'] as const;

const page = (model: TableModel): AccountsPage => {
  const run = runAccountsPage(HTML, { now: NOW });
  run.message({ type: 'model', model });
  assert.equal(run.failure(), '', 'the page stopped on an error');

  return run;
};

function choose(run: AccountsPage, value: string): void {
  const select = run.document.getElementById('group');
  assert.ok(select !== null);
  select.value = value;
  run.document.dispatch('change', select);
}

/** The three tools at one place, each with its own account, or all with one: `same` makes the account of every tool `ANN`. */
const threeTools = (same: boolean): TableModel => tableModel(inputOf({
  hosts: ['a', 'b'],
  memory: memoryOf(
    ['a', seen(reading({ claude: claude({ usage: usageOf(10, 20, 30) }), codex: codexOf(undefined, same ? ANN : COX), agy: agyOf(agyUsage(), same ? ANN : AGY_MAIL) }), 2)],
    ['b', seen(reading({ claude: claude({ email: 'bob@example.test', usage: usageOf(10, 20, 30) }), codex: codexOf(undefined, same ? ANN : COX) }), 3)],
  ),
}));

const toneOf = (run: AccountsPage, email: string): readonly string[] =>
  run.document.querySelectorAll('span').filter((one) => one.textContent === email && /^em-\d+$/u.test(one.className)).map((one) => one.className);

// ───────────────── 1. the text stands off the border ─────────────────

test('R3-L1: the cell that carries the tb-* border has a padding-left of 0.6em that wins over the table\'s own padding (table.acct td) — the text is not on the border', () => {
  const sheet = stylesheet(HTML.replace(/<style nonce="[^"]*">/u, '<style>'));
  const table = { tag: 'table', classes: ['acct'], attrs: {} };
  for (const tool of ['claude-code', 'codex', 'agy']) {
    const got = winning(sheet, { tag: 'td', classes: [`tb-${tool}`], attrs: {} }, [table], ['padding-left', 'padding']);
    assert.deepEqual(got.unreadable, [], `${tool}: a rule that cannot be read`);
    assert.equal(got.value, '0.6em', `${tool}: the padding-left that wins is ${got.value}`);
    const border = winning(sheet, { tag: 'td', classes: [`tb-${tool}`], attrs: {} }, [table], ['border-left']);
    assert.match(border.value ?? '', /^3px solid #/u, `${tool}: the border`);
  }
  const plain = winning(sheet, { tag: 'td', classes: [], attrs: {} }, [table], ['padding-left', 'padding']);
  assert.match(plain.value ?? '', /^4px 10px 4px 0$/u, 'a cell with no border keeps the table\'s padding');
});

// ───────────────── 2. every tool row has the same lines ─────────────────

test('R3-L2: in every grouping the bordered tool cells have two lines — the name and a second line, Codex\'s an empty placeholder — and the account cells of a block have two as well', () => {
  const run = page(threeTools(false));
  for (const groupBy of GROUPS) {
    choose(run, groupBy);
    const tools = run.document.querySelectorAll('td').filter((cell) => cell.children.some((one) => one.tag === 'strong' && /^tn-/u.test(one.className)) && /\btb-/u.test(cell.className));
    if (groupBy === 'server') {
      // place a: Claude, Codex and two Antigravity group rows; place b: Claude and Codex
      assert.equal(tools.length, 6, 'the bordered tool cells of the Server grouping');
      const lines = tools.map((cell) => cell.children.filter((one) => one.tag === 'strong' || one.tag === 'div').length);
      assert.ok(lines.every((count) => count === 2), `a tool cell has not two lines: ${lines.join(',')}`);
      const seconds = tools.map((cell) => cell.children.find((one) => one.tag === 'div')?.textContent ?? '');
      assert.equal(seconds.filter((text) => text === '\u00a0').length, 2, `the two Codex rows keep an empty second line: ${JSON.stringify(seconds)}`);
      assert.ok(seconds.some((text) => text.includes('1.5.12') && text.includes('Gemini')) && seconds.some((text) => text.includes('Claude and GPT')), JSON.stringify(seconds));
      const accounts = run.document.querySelectorAll('td').filter((cell) => cell.children.some((one) => one.className === 'label' && one.textContent === 'Account'));
      assert.equal(accounts.length, 6);
      assert.ok(accounts.every((cell) => cell.children.filter((one) => one.tag === 'div').length >= 2), 'an account cell has less than two lines');
    } else {
      assert.ok(run.document.querySelectorAll('td').some((cell) => /\btb-codex\b/u.test(cell.className)), `${groupBy}: no bordered cell`);
    }
  }
});

// ───────────────── 3. an email has one colour ─────────────────

test('R3-L3: the same email is the same em-N class in Claude, Codex and Antigravity and in every grouping; two emails are two classes; the class is one of the twelve', () => {
  const same = page(threeTools(true));
  for (const groupBy of GROUPS) {
    choose(same, groupBy);
    const classes = toneOf(same, ANN);
    assert.ok(classes.length >= 3, `${groupBy}: the email is coloured ${classes.length} times: ${classes.join(',')}`);
    assert.equal(new Set(classes).size, 1, `${groupBy}: one email, several colours: ${classes.join(',')}`);
    assert.match(classes[0] as string, /^em-([1-9]|1[0-2])$/u);
  }
  const other = page(threeTools(false));
  choose(other, 'server');
  const tones = new Set(['ann@example.test', COX, AGY_MAIL, 'bob@example.test'].map((email) => toneOf(other, email)[0]));
  assert.equal(tones.size, 4, `four emails, ${tones.size} colours`);
  assert.ok([...tones].every((one) => one !== undefined && /^em-\d+$/u.test(one)));
});

test('R3-L3: the model makes the class — over the sorted and de-duplicated emails of all rows, up to twelve distinct, the same email the same class, and never from the text of a reply', () => {
  const emails = Array.from({ length: 12 }, (_, n) => `user${String(n).padStart(2, '0')}@example.test`);
  const memory = memoryOf(...emails.map((email, n): readonly [string, ReturnType<typeof seen>] => [`h${n}`, seen(reading({ claude: claude({ email, usage: { state: 'skipped' } }), codex: codexOf(undefined, emails[(n + 1) % 12]) }), 1)]));
  const model = tableModel(inputOf({ hosts: emails.map((_, n) => `h${n}`), memory }));
  const byEmail = new Map<string, Set<string | null>>();
  for (const block of model.blocks) {
    for (const row of block.rows) {
      byEmail.set(row.account.email, new Set([...(byEmail.get(row.account.email) ?? []), row.account.tone]));
    }
  }
  assert.equal(byEmail.size, 12);
  assert.ok([...byEmail.values()].every((tones) => tones.size === 1), 'an email has two classes');
  const all = [...byEmail.values()].map((tones) => [...tones][0]);
  assert.equal(new Set(all).size, 12, 'twelve emails are not twelve classes');
  assert.deepEqual([...all].sort(), VENDOR_PALETTE.map((_, n) => `em-${n + 1}`).sort());
  // an email whose text looks like a class, and a row that has no email: the class is the palette's, or null
  const odd = tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ claude: claude({ email: 'em-3 x@example.test', usage: { state: 'skipped' } }), codex: codexOf(undefined, '') }), 1)]) }));
  const rows = odd.blocks[0]?.rows ?? [];
  assert.match(rows[0]?.account.tone ?? '', /^em-([1-9]|1[0-2])$/u);
  assert.equal(rows[1]?.account.tone, null, 'a row with no known email has no class');
  assert.equal(rows[1]?.account.email, '');
});

test('R3-L3: the page sets only one of its twelve closed classes, whatever the model holds, and its stylesheet has the twelve rules with the palette\'s own var() values', () => {
  const model = threeTools(true);
  const hostile: TableModel = JSON.parse(JSON.stringify(model).replaceAll('"tone":"em-', '"tone":"x\\" onclick=\\"1 em-')) as TableModel;
  const run = page(hostile);
  for (const groupBy of GROUPS) {
    choose(run, groupBy);
    for (const node of run.document.root.descendants()) {
      assert.ok(![...node.attributes.keys()].some((name) => name.startsWith('on')), `${node.tag} has a handler`);
      assert.ok(!node.className.includes('onclick') && !node.className.includes('"'), `a class holds model text: ${node.className}`);
    }
    assert.deepEqual(toneOf(run, ANN), [], `${groupBy}: a class that is not one of the twelve was set`);
  }
  const sheet = stylesheet(HTML.replace(/<style nonce="[^"]*">/u, '<style>'));
  VENDOR_PALETTE.forEach((colour, n) => {
    const rule = sheet.find((one) => one.selector === `.em-${n + 1}`);
    assert.ok(rule !== undefined, `no rule for .em-${n + 1}`);
    assert.equal(rule?.body.replace(/\s+/gu, ' ').trim(), `color: ${colour};`);
  });
});

// ───────────────── 4. Antigravity: the groups ─────────────────

test('R3-L4: Antigravity is drawn with its groups — a row for each in Server and in the section of Claude account, a block for each in User; a full window says no reset', () => {
  const run = page(threeTools(false));
  choose(run, 'server');
  const text = run.text();
  assert.ok(text.includes('Gemini') && text.includes('Claude and GPT'), 'the groups are not named');
  assert.ok(text.includes('92%') && text.includes('60%') && text.includes('100%'), 'a percent left of the quota is not on the page');
  assert.ok(text.includes('no reset'), 'a full window with no reset does not say so');
  assert.ok(!text.includes('agy states no usage') && !text.includes('its account is not read'), 'a sentence of the old tab is still on the page');
  assert.ok(!text.includes('from a Codex session') && !text.includes('account name not shown by Codex'), 'a sentence of the old Codex is still on the page');
  choose(run, 'account');
  const section = run.document.querySelectorAll('section').find((one) => one.querySelector('h2')?.textContent === 'Antigravity');
  assert.ok(section !== undefined, 'no Antigravity section');
  assert.equal(section.querySelectorAll('th').length, 5, 'the section has the five columns of Codex\'s');
  assert.equal(section.querySelectorAll('tr').length - 1, 2, 'one row for each group');
  choose(run, 'user');
  assert.ok(run.text().includes('Claude and GPT'), 'the User grouping does not name the second group');
});
