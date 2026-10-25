import assert from 'node:assert/strict';
import { test } from 'node:test';

import { RoundCountsRead } from '../roundCounts';
import { StoredLine } from '../roundCountsCache';
import { cardRoundsLine, roundsBlock, RoundsBlockInput } from '../roundsBlock';
import { cellsOf, headersOf, rowWidths } from './roundsBlockCells';
import { counted, KEY_A, KEY_B, MERGED_JSON, snapshot, THIS_STORE, usageLine } from './roundCountsFixtures';

/**
 * The Rounds block's launches-per-round cell (E6.S3): 7 d in the cell, the other windows and the fact in its tooltip; a
 * Team-server row's launches run elsewhere and say so; a ledger that did not read is unavailable, never 0.
 */

const KEY_C = '5d6c7b8a9f0e1d2c3b4a59687766554f';
const STORE = { side: '', directory: 'C:\\coai', others: [] };

const row = (keys: readonly string[], roundsWithoutKey = 0): object => ({
  provider: 'codex', rounds: { plan: 0, code: keys.length + roundsWithoutKey, document: 0, feature: 0 }, reviewers: keys.length, models: [],
  roundKeys: keys, roundsWithoutKey,
});

/** The merged fixture with each window's codex row replaced: 5 h has A; 24 h has A and B; 7 d has A, B, C and one keyless round. */
function windowsRead(): RoundCountsRead {
  const answer = JSON.parse(MERGED_JSON) as { windows: { name: string; rows: object[] }[] };
  const byWindow: Record<string, object> = { '5h': row([KEY_A]), '24h': row([KEY_A, KEY_B]), '7d': row([KEY_A, KEY_B, KEY_C], 1) };
  for (const window of answer.windows) {
    window.rows = [byWindow[window.name]!];
  }

  return counted(JSON.stringify(answer));
}

const launch = (round: string, over: Partial<StoredLine> = {}): StoredLine => ({ ...usageLine({ costUsd: 1, attempt: 'first', cli: 'codex', round, ...over }), store: THIS_STORE });

/** A launched twice, B once, C never. */
const LINES = [launch(KEY_A), launch(KEY_A, { attempt: 'retry' }), launch(KEY_B)];

function input(read: RoundCountsRead, ledger: readonly StoredLine[], over: Partial<RoundsBlockInput> = {}, failure = ''): RoundsBlockInput {
  return { snapshot: snapshot(read, ledger, failure), chat: [], rateOf: () => undefined, ranOn: () => '', store: STORE, ...over };
}

test('TheCell_ShowsSevenDays_AndTheTooltipTheOtherWindowsAndTheFact', () => {
  const html = roundsBlock(input(windowsRead(), LINES));
  const cell = cellsOf(html, '<b>codex</b>')['Launches per round'] ?? '';

  assert.match(cell, />1\.5<\/span>$/u, '3 launches over the 2 rounds of the 7 d window that have a launch line (the third keyed round has none) is 1.5');
  assert.match(cell, /5 h: 2\.0 \(2 launches over 1 round\)/u);
  assert.match(cell, /24 h: 1\.5 \(3 launches over 2 rounds\)/u);
  assert.match(cell, /7 d: 1\.5 \(3 launches over 2 rounds\)/u);
  assert.match(cell, /launches per round/u);
  assert.match(cell, /1 round carries no launch count/u);
  assert.match(cell, /1 round written before coai-mcp 0\.60\.2 carries no round key/u);
});

test('the header sits beside Reviewers per round, and every row keeps as many cells as the header', () => {
  const html = roundsBlock(input(windowsRead(), [...LINES, launch(KEY_A, { kind: 'consult', attempt: 'first', model: 'gpt-6-astra' }), usageLine({ kind: 'chat' })]));
  const headers = headersOf(html);

  assert.equal(headers.indexOf('Launches per round'), headers.indexOf('Reviewers per round') + 1);
  assert.ok(rowWidths(html).length >= 3, 'the table drew no model and kind lines to check');
  assert.deepEqual([...new Set(rowWidths(html))], [headers.length], 'a model or kind line has a different number of cells than the header');
});

test('ARemoteRow_SaysItsLaunchesRunElsewhere — a Team-server row\'s launch lines are cli remote, so a number would always be 0', () => {
  const cell = cellsOf(roundsBlock(input(windowsRead(), LINES, { ranOn: () => 'work' })), '<b>codex</b>')['Launches per round'] ?? '';

  assert.match(cell, /^<span title="launches run on work, not recorded here">—<\/span>$/u);
});

test('a ledger that did not read is unavailable in the cell — never a 0 launches', () => {
  const cell = cellsOf(roundsBlock(input(windowsRead(), LINES, {}, 'EACCES')), '<b>codex</b>')['Launches per round'];

  assert.equal(cell, 'unavailable');
});

test('a count that is unavailable is count unavailable in the cell', () => {
  const html = roundsBlock(input({ state: 'unavailable', why: 'x' }, LINES));

  assert.equal(cellsOf(html, '<b>codex</b>')['Launches per round'], 'count unavailable');
});

test('an answer from a binary older than the keys has no keyed round: a dash, not a division', () => {
  const answer = JSON.parse(MERGED_JSON) as { windows: { rows: Record<string, unknown>[] }[] };
  for (const window of answer.windows) {
    delete window.rows[0]!['roundKeys'];
    delete window.rows[0]!['keyStore'];
    delete window.rows[0]!['roundsWithoutKey'];
  }

  const cell = cellsOf(roundsBlock(input(counted(JSON.stringify(answer)), LINES)), '<b>codex</b>')['Launches per round'] ?? '';

  assert.match(cell, /^<span title="[^"]*">—<\/span>$/u);
});

test('TheCardLine_IsUnchanged — the Models card line is the rounds and the consult turns, byte for byte, over a merged answer', () => {
  assert.equal(cardRoundsLine(snapshot(counted(MERGED_JSON), [usageLine({ kind: 'consult' })]), 'codex'), '7 days: 3 rounds · 1 consult turns · 5 h: 3');
});
