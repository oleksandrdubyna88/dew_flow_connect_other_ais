import assert from 'node:assert/strict';
import { test } from 'node:test';

import { RoundCountsRead } from '../roundCounts';
import { RoundsSnapshot, StoredLine } from '../roundCountsCache';
import { roundsBlock, RoundsBlockInput } from '../roundsBlock';
import { cardResetLine } from '../statedReset';
import { cellsOf } from './roundsBlockCells';
import { counted, hoursAgo, KEY_A, KEY_B, MERGED_JSON, NOW, snapshot, THIS_STORE, usageLine, WSL_STORE } from './roundCountsFixtures';

/**
 * "Stores are not accounts" (section 9 of todo/PLAN_the_round_counter_tail_E6.md), through the REAL renderer: activity is summed
 * across stores, status is not. Keyed money follows the round to the store `keyStore` names (and is never dropped for want of
 * an owner); a foreign line is priced at the LIST rate and marked; a stated reset is per (provider, store); a foreign ledger
 * that did not read leaves this side's money on screen and says which store's is missing.
 */

const STORE = { side: '', directory: 'C:\\coai', others: [] };
const OVERRIDE = { in: 1, out: 0 };
const LIST = { in: 10, out: 0 };

/** A merged answer whose every window holds the one codex row given, over the fixture's two stores. */
function mergedRead(row: object): RoundCountsRead {
  const answer = JSON.parse(MERGED_JSON) as { windows: { rows: object[] }[] };
  for (const window of answer.windows) {
    window.rows = [row];
  }

  return counted(JSON.stringify(answer));
}

const keyedRow = (keys: readonly string[], owners: Record<string, string>): object => ({
  provider: 'codex', rounds: { plan: 0, code: keys.length, document: 0, feature: 0 }, reviewers: keys.length, models: [],
  roundKeys: keys, keyStore: owners, roundsWithoutKey: 0,
});

/** One codex launch line of the 1 M-token kind, priced $1 unless it says otherwise. */
function launchIn(store: string, over: Partial<StoredLine> = {}): StoredLine {
  return { ...usageLine({ costUsd: 1, tokensOut: 0, attempt: 'first', cli: 'codex', ...over }), store };
}

function input(read: RoundCountsRead, ledger: readonly StoredLine[], over: Partial<RoundsSnapshot> = {}, block: Partial<RoundsBlockInput> = {}): RoundsBlockInput {
  return { snapshot: { ...snapshot(read, ledger), ...over }, chat: [], rateOf: () => OVERRIDE, ranOn: () => '', store: STORE, ...block };
}

/**
 * A count cell's figure — over two stores it is wrapped in a span whose tooltip lists each store's part. Tags are removed until none
 * is left (one pass can leave a tag that two nested ones formed), so the figure read is the text alone.
 */
function plain(cell: string | undefined): string {
  let text = cell ?? '';
  for (let previous = ''; previous !== text; ) {
    previous = text;
    text = text.replace(/<[^>]*>/gu, '');
  }
  return text;
}

test('ACopiedStore_IsOneRoundOneLaunchOneDollar — the same key and the same $1 launch in both ledgers is $1, not $2', () => {
  const read = mergedRead(keyedRow([KEY_A], { [KEY_A]: THIS_STORE }));
  const copies = [launchIn(THIS_STORE, { round: KEY_A }), launchIn(WSL_STORE, { round: KEY_A })];

  const cells = cellsOf(roundsBlock(input(read, copies)), '<b>codex</b>');

  assert.equal(plain(cells['7 d']), '1');
  assert.equal(cells['Money (7 d)'], '$1.00', 'the copied store\'s line was counted again: keyed money must follow the round to its owning store');
  assert.match(cells['Launches per round'] ?? '', /7 d: 1\.0 \(1 launch over 1 round\)/u, 'the copy\'s launch doubled the figure');
});

test('TwoRetriesInTheOwningStore_BothCount — ownership never drops a retry inside the store that owns the round', () => {
  const read = mergedRead(keyedRow([KEY_A], { [KEY_A]: THIS_STORE }));
  const retries = [launchIn(THIS_STORE, { round: KEY_A }), launchIn(THIS_STORE, { round: KEY_A, attempt: 'retry' })];

  assert.equal(cellsOf(roundsBlock(input(read, retries)), '<b>codex</b>')['Money (7 d)'], '$2.00');
});

test('ASpendWhoseRoundStartedBeforeTheWindow_IsKept — a key with no keyStore entry is counted from the store it was read in', () => {
  const read = mergedRead({ provider: 'gemini', rounds: { plan: 1, code: 0, document: 0, feature: 0 }, reviewers: 1, models: [], roundKeys: [KEY_B], keyStore: { [KEY_B]: THIS_STORE }, roundsWithoutKey: 0 });
  const early = launchIn(WSL_STORE, { round: KEY_A, utc: new Date(NOW - (7 * 24 * 3_600_000) + 60_000).toISOString() });

  const cells = cellsOf(roundsBlock(input(read, [early])), '<b>codex</b>');

  assert.equal(plain(cells['7 d']), '0', 'the round started before the window: it is not counted');
  assert.equal(cells['Money (7 d)'], '$1.00', 'the spend whose round began before the window was dropped for want of an owner');
});

test('AForeignLine_IsNotPricedAtThisWindowsOverride — list rate, marked list-price estimate', () => {
  const read = mergedRead(keyedRow([], {}));
  const foreignLine = launchIn(WSL_STORE, { costUsd: null, tokensIn: 1_000_000 });

  const html = roundsBlock(input(read, [foreignLine], {}, { listRateOf: () => LIST }));
  const money = cellsOf(html, '<b>codex</b>')['Money (7 d)'];

  assert.match(money ?? '', /~\$10\.00/u, 'a foreign line was priced at the local override instead of the list rate');
  assert.match(money ?? '', /list-price estimate/u);
});

test('a foreign line no list carries is cost unknown — a dash, never ~$0 and never the override\'s price', () => {
  const read = mergedRead(keyedRow([], {}));
  const unlisted = launchIn(WSL_STORE, { costUsd: null, tokensIn: 1_000_000, model: 'a-model-no-list-has' });

  const money = cellsOf(roundsBlock(input(read, [unlisted], {}, { listRateOf: () => undefined })), '<b>codex</b>')['Money (7 d)'];

  assert.match(money ?? '', /^—/u);
  assert.match(money ?? '', /cost unknown/u);
});

test('a foreign line that reports its own cost is a bill, not an estimate — the list is not consulted', () => {
  const read = mergedRead(keyedRow([], {}));

  const money = cellsOf(roundsBlock(input(read, [launchIn(WSL_STORE, { costUsd: 3 })], {}, { listRateOf: () => LIST })), '<b>codex</b>')['Money (7 d)'];

  assert.equal(money, '$3.00');
});

test('a LOCAL line without a reported cost is still priced at the override, and is not marked a list-price estimate', () => {
  const read = mergedRead(keyedRow([], {}));
  const local = launchIn(THIS_STORE, { costUsd: null, tokensIn: 1_000_000 });

  const money = cellsOf(roundsBlock(input(read, [local], {}, { listRateOf: () => LIST })), '<b>codex</b>')['Money (7 d)'];

  assert.match(money ?? '', /~\$1\.00/u);
  assert.doesNotMatch(money ?? '', /list-price/u);
});

test('OneStoresRefusal_IsNotHiddenByAnothersSuccess — the WSL store\'s quota-out line is shown though Windows answered later', () => {
  const read = mergedRead(keyedRow([], {}));
  const refused = { ...launchIn(WSL_STORE, { utc: hoursAgo(3), outcome: 'rate limited' }), resetsUtc: new Date(NOW + (89 * 3_600_000)).toISOString() };
  const answered = launchIn(THIS_STORE, { utc: hoursAgo(1) });
  const snap = snapshot(read, [refused, answered]);

  const line = cardResetLine(snap, 'codex');
  const html = roundsBlock(input(read, [refused, answered]));

  assert.match(line, /WSL: Ubuntu.*quota out — resets in 89 h/u, 'the other store\'s refusal was hidden by this side\'s later success');
  assert.match(html, /quota out — resets in 89 h/u);
  assert.doesNotMatch(line, /this machine/u, 'this machine answered: it has no refusal to show');
});

test('one store only: the stated reset is today\'s sentence, with no store named', () => {
  const read = mergedRead(keyedRow([], {}));
  const refused = { ...launchIn(THIS_STORE, { utc: hoursAgo(3), outcome: 'rate limited' }), resetsUtc: new Date(NOW + (89 * 3_600_000)).toISOString() };

  assert.match(cardResetLine(snapshot(read, [refused]), 'codex'), /^quota out — resets in 89 h/u);
});

test('AForeignLedgerError_KeepsThisSidesMoney_AndSaysItIsPartial — the block shows $1 and which store\'s money is missing', () => {
  const read = mergedRead(keyedRow([], {}));
  const otherLedgers = [{ store: WSL_STORE, directory: '/home/me/.local/share/coai-mcp', failure: 'EISDIR: illegal operation on a directory' }];

  const html = roundsBlock(input(read, [launchIn(THIS_STORE)], { otherLedgers }));

  assert.equal(cellsOf(html, '<b>codex</b>')['Money (7 d)'], '$1.00', 'this side\'s money was hidden by the other store\'s failure');
  assert.match(html, /Money from WSL: Ubuntu not included: EISDIR/u);
  assert.doesNotMatch(html, /Tokens and money: unavailable/u, 'a foreign failure was drawn as this side\'s ledger failing');
});

test('a foreign ledger that read says nothing is missing', () => {
  const read = mergedRead(keyedRow([], {}));
  const otherLedgers = [{ store: WSL_STORE, directory: '/x', failure: '' }];

  assert.doesNotMatch(roundsBlock(input(read, [launchIn(THIS_STORE)], { otherLedgers })), /Money from .* not included/u);
});
