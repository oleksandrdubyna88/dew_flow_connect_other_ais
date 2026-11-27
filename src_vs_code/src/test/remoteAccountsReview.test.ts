import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { accountsHtml } from '../remoteAccountsPage';
import { agyUsageOf } from '../remoteAccountsUsage';
import { tableModel } from '../remoteAccountsTable';
import type { TableModel } from '../remoteAccountsTable';
import { EMAIL_COLOURS, emailSlots } from '../vendorColour';
import { runAccountsPage } from './accountsPageHarness';
import type { AccountsPage } from './accountsPageHarness';
import { ANN, NOW, agyOf, claude, codexOf, hour, inputOf, memoryOf, reading, seen, usageOf } from './accountsTableFixtures';

/**
 * The findings of the review of PR #1066 that are about what the tab SAYS and DRAWS: the reason of an Antigravity block, Codex's failures, an agy reset that is
 * out of reach, the palette of the emails, and the lines of a row in every grouping.
 */

test('R3-F6: an agy reset is kept only between one hour ago and eight days ahead, like Claude\'s; a dash and a far one are no reset, and the percent stays', () => {
  const at = (ms: number): string => String(Math.floor(ms / 1000));
  const windows = (...resets: readonly string[]) => (agyUsageOf(resets.map((reset, n) => `gemini ${n === 0 ? '5h' : 'weekly'} 50 ${reset}`), NOW) as { windows: readonly { leftPercent: number | null; resetsAtMs: number | null }[] }).windows;
  const got = windows(at(NOW + hour(3)), at(NOW + 7 * 24 * 3_600_000));
  assert.deepEqual(got.map((one) => one.resetsAtMs), [Math.floor((NOW + hour(3)) / 1000) * 1000, Math.floor((NOW + 7 * 24 * 3_600_000) / 1000) * 1000]);
  const out = windows(at(NOW - hour(2)), at(NOW + 9 * 24 * 3_600_000));
  assert.deepEqual(out.map((one) => one.resetsAtMs), [null, null], 'a reset two hours ago and one nine days ahead');
  assert.deepEqual(out.map((one) => one.leftPercent), [50, 50]);
  assert.deepEqual(windows('-', '99999999999').map((one) => one.resetsAtMs), [null, null]);
  assert.equal(windows(at(NOW - 30 * 60_000))[0]?.resetsAtMs, Math.floor((NOW - 30 * 60_000) / 1000) * 1000, 'half an hour ago is still shown');
});

const rowsOf = (agy: ReturnType<typeof agyOf>, block?: { version: string; why: 'model-call' | 'format' | 'timeout' | 'error' }): TableModel['blocks'][number]['rows'] =>
  tableModel(inputOf({ hosts: ['a'], memory: memoryOf(['a', { ...seen(reading({ agy }), 1), ...(block === undefined ? {} : { agyBlock: block }) }]) })).blocks[0]?.rows ?? [];

test('R3-F8: the row says the reason of the block — a skipped reply says the reason the block holds, a failed one its own; a timeout and an error end with Refresh now', () => {
  const skipped = { ...agyOf(), usage: { state: 'skipped' } } as ReturnType<typeof agyOf>;
  const said = (why: 'model-call' | 'format' | 'timeout' | 'error'): string => rowsOf(skipped, { version: '1.5.12', why })[0]?.span ?? '';
  assert.match(said('model-call'), /^Quota not read: the quota command of Antigravity 1\.5\.12 asked the model, so CoAI stopped asking this host$/u);
  assert.match(said('format'), /^Quota not read: Antigravity 1\.5\.12 printed no quota this version understands, so CoAI stopped asking this host$/u);
  assert.match(said('timeout'), /^Quota not read: Antigravity 1\.5\.12 did not answer within 4 s, so CoAI stopped asking this host \(Refresh now asks again\)$/u);
  assert.match(said('error'), /^Quota not read: the quota command of Antigravity 1\.5\.12 ended with an error, so CoAI stopped asking this host \(Refresh now asks again\)$/u);
  for (const why of ['model-call', 'format', 'timeout', 'error'] as const) {
    const failed = { ...agyOf(), usage: { state: 'failed', why } } as ReturnType<typeof agyOf>;
    assert.equal(rowsOf(failed)[0]?.span, said(why), `a failed reply and a skipped one with the same reason say the same: ${why}`);
  }
  assert.equal(rowsOf({ ...agyOf(), usage: { state: 'failed', why: 'no-jq' } } as ReturnType<typeof agyOf>)[0]?.span, 'Quota not read: jq is missing on this host');
  assert.match(rowsOf(skipped)[0]?.span ?? '', /asked the model/u, 'a skipped reply with no block in the memory keeps the safe sentence');
});

test('R3-F4: Codex\'s failures each have their own sentence — none, an error, a timeout, and a program that was not found on the script\'s PATH', () => {
  const said = (why: 'none' | 'error' | 'timeout' | 'not-started'): string | null | undefined => tableModel(inputOf({
    hosts: ['a'], memory: memoryOf(['a', seen(reading({ codex: codexOf({ state: 'failed', why }) }), 1)]),
  })).blocks[0]?.rows[0]?.span;
  assert.equal(said('none'), 'Codex stated no rate limits');
  assert.equal(said('error'), 'Codex did not answer (no app-server in this version, or it did not start)');
  assert.equal(said('timeout'), 'Codex did not answer within 4 s');
  assert.equal(said('not-started'), 'Codex\'s program was not found on the PATH the script uses, so it was not asked');
});

test('R3-F9c: the emails take the palette from its own list — no slot held back for a vendor, sorted, de-duplicated, the same email the same slot, the hues far from the tool colours first', () => {
  const slot = emailSlots(['codex', 'claude', 'agy', 'Claude ', 'zed@example.test']);
  // the sorted, de-duplicated list is agy, claude, codex, zed@example.test: the vendor names hold no slot of their own
  assert.deepEqual(['agy', 'claude', 'codex', 'zed@example.test'].map(slot), [0, 1, 2, 3]);
  assert.equal(slot('CLAUDE'), 1, 'case and space make no second email');
  assert.equal(slot('other'), undefined);
  assert.equal(slot(''), undefined);
  const many = Array.from({ length: 14 }, (_, n) => `e${String(n).padStart(2, '0')}@example.test`);
  const place = emailSlots(many);
  assert.deepEqual(many.slice(0, 12).map(place), [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 'twelve emails, twelve slots');
  assert.deepEqual(many.slice(12).map(place), [0, 1], 'the thirteenth repeats the first');
  assert.equal(new Set(EMAIL_COLOURS).size, 12);
  assert.ok(EMAIL_COLOURS[0]?.includes('#5CC46F') && EMAIL_COLOURS[1]?.includes('#B9C742'), 'green and lime come first: they are far from orange, sky blue and violet');
});

const THREE: TableModel = tableModel(inputOf({
  hosts: ['a', 'b', 'c'],
  memory: memoryOf(
    ['a', seen(reading({ claude: claude({ usage: usageOf(10, 20, 30) }), codex: codexOf(), agy: agyOf() }), 1)],
    ['b', seen(reading({ claude: claude({ usage: usageOf(10, 20, 30) }), codex: { state: 'unreadable', why: 'credential-shape' }, agy: { state: 'unreadable', why: 'credential-shape' } }), 2)],
    ['c', seen(reading({ claude: { state: 'signed-out' }, codex: codexOf({ state: 'failed', why: 'none' }), agy: { ...agyOf(), usage: { state: 'skipped' } } as ReturnType<typeof agyOf> }), 3)],
  ),
}));

const page = (): AccountsPage => {
  const run = runAccountsPage(accountsHtml('n0nce-review'), { now: NOW });
  run.message({ type: 'model', model: THREE });
  assert.equal(run.failure(), '', 'the page stopped on an error');

  return run;
};

test('R3-F9a: in every grouping the tool cells of a block have two lines, an account cell has ONE second line (the plan and "shared across" together), and a sentence row keeps the lines of the others', () => {
  const run = page();
  const select = run.document.getElementById('group');
  assert.ok(select !== null);
  for (const groupBy of ['server', 'account', 'user']) {
    select.value = groupBy;
    run.document.dispatch('change', select);
    const bordered = run.document.querySelectorAll('td').filter((cell) => /\btb-/u.test(cell.className) && cell.children.some((one) => one.tag === 'strong' && /^tn-/u.test(one.className)));
    for (const cell of bordered) {
      assert.equal(cell.children.filter((one) => one.tag === 'strong' || one.tag === 'div').length, 2, `${groupBy}: a tool cell has not two lines`);
    }
    const accounts = run.document.querySelectorAll('td').filter((cell) => cell.children.some((one) => one.className === 'label' && one.textContent === 'Account'));
    for (const cell of accounts) {
      const lines = cell.children.filter((one) => one.tag === 'div');
      assert.equal(lines.length, 2, `${groupBy}: an account cell has ${lines.length} lines: ${cell.textContent}`);
    }
    assert.ok(!run.text().includes('shared across') || accounts.some((cell) => /plan .* · shared across|^.*shared across/u.test(cell.textContent)), `${groupBy}: shared across is not on the second line`);
  }
  // the sentence rows of place c and b hold the account as a dash and their sentence once, in the cells of the windows
  select.value = 'server';
  run.document.dispatch('change', select);
  assert.equal(run.text().split('signed out').length - 1, 1);
  const css = /<style[^>]*>([\s\S]*?)<\/style>/u.exec(accountsHtml('n'))?.[1] ?? '';
  assert.match(css, /table\.acct tbody td \{ height: 3em; \}/u, 'the cells have a least height');
  assert.match(css, /@media \(max-width: 560px\)[\s\S]*table\.acct tbody td \{ height: auto; \}/u, 'and none where they are stacked');
});

test('R3-F9b: the heading of a Claude account is drawn by the same function as the account cells — one email span, in the class of its colour', () => {
  const run = page();
  const select = run.document.getElementById('group');
  assert.ok(select !== null);
  select.value = 'account';
  run.document.dispatch('change', select);
  const heading = run.document.querySelectorAll('strong').find((one) => /\bhost\b/u.test(one.className) && one.textContent.startsWith(ANN));
  assert.ok(heading !== undefined, 'no heading of the account');
  const spans = heading.children.filter((one) => one.tag === 'span');
  assert.equal(spans.filter((one) => one.textContent === ANN).length, 1, 'the email is drawn twice');
  assert.match(spans[0]?.className ?? '', /^em-\d+$/u);
});
