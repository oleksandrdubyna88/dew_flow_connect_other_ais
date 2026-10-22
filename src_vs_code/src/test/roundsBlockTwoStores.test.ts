import assert from 'node:assert/strict';
import { test } from 'node:test';

import { parseRoundCounts, RoundCountsRead } from '../roundCounts';
import { RoundsSnapshot } from '../roundCountsCache';
import { cardRoundsLine, roundsBlock, RoundsBlockInput, StoreFacts, storeSentence } from '../roundsBlock';
import { cellsOf } from './roundsBlockCells';
import { ANSWER_JSON, counted, hoursAgo, MERGED_JSON, NOW, snapshot, THIS_STORE, usageLine, WSL_STORE } from './roundCountsFixtures';

/**
 * The Rounds block and the Models card over TWO stores (E6.S4 of todo/PLAN_the_round_counter_tail_E6.md): the sentence names
 * each store with its part and its age, a store that could not be counted is *not included* with the reason and never a 0, a
 * quiet one is 0 *as of* its last write, a count cell's tooltip lists each store's share, and the sub-rows are labelled as
 * STORES (coai cannot tell whether two stores sign in to one vendor account).
 */

const STORE: StoreFacts = { side: '', directory: 'C:\\coai', others: [] };

type Shape = {
  windows: { fromUtc: string; rows: { parts: { store: string }[] }[] }[];
  stores: Record<string, unknown>[];
  storesNotIncluded: Record<string, unknown>[];
};

/** The merged fixture, edited, and parsed through the REAL parser. */
function merged(edit: (answer: Shape) => void = () => undefined): RoundCountsRead {
  const answer = JSON.parse(MERGED_JSON) as Shape;
  edit(answer);

  return counted(JSON.stringify(answer));
}

function block(read: RoundCountsRead, over: Partial<RoundsBlockInput> = {}, snap: Partial<RoundsSnapshot> = {}): string {
  return roundsBlock({
    snapshot: { ...snapshot(read, [usageLine()]), ...snap }, chat: [], rateOf: () => undefined, ranOn: () => '', store: STORE, ...over,
  });
}

/** The store sentence and the notes under it — everything before the table. */
function headOf(html: string): string {
  return html.slice(html.indexOf('<h3'), html.indexOf('<div class="table-scroll">'));
}

const quietWsl = (answer: Shape): void => {
  answer.stores[1] = { ...answer.stores[1], quiet: true, asOf: hoursAgo(216) };
  for (const window of answer.windows) {
    window.rows[0]!.parts = window.rows[0]!.parts.filter((part) => part.store !== WSL_STORE);
  }
};

test('TwoStoresAreCountedTogether_EachNamedWithItsPart — the sentence, the count cells\' tooltips and the sub-rows', () => {
  const html = block(merged());

  assert.match(headOf(html), /Counted from two stores — Windows \(this machine\) and WSL \(Ubuntu, as of 3 min ago\)\./u);
  assert.doesNotMatch(headOf(html), /this side's store only/u);
  const row = cellsOf(html, '<b>codex</b>');
  assert.match(row['7 d']!, /^<span title="Windows \(this machine\): 2 · WSL \(Ubuntu\): 1">3<\/span>$/u, 'the count cell does not list each store\'s part');
  const wsl = cellsOf(html, 'store · WSL (Ubuntu)');
  assert.equal(wsl['7 d'], '1');
  assert.equal(wsl['By stage (7 d)'], 'plan 0 · code 1 · document 0 · feature 0');
  assert.equal(cellsOf(html, 'store · Windows (this machine)')['5 h'], '2');
  assert.match(html, /<span class="indent">store · WSL \(Ubuntu\)<\/span>/u, 'a sub-row is labelled as a STORE');
  assert.doesNotMatch(html, /account/iu, 'a store was called an account: coai cannot tell whether two stores share one');
});

test('AQuietStore_IsNoughtAsOfItsWrite — 0 rounds as of 9 d ago, never not included', () => {
  const html = block(merged(quietWsl));

  assert.match(headOf(html), /WSL \(Ubuntu, 0 rounds as of 9 d ago\)/u);
  assert.doesNotMatch(headOf(html), /WSL[^.]*not included/u, 'a store quiet for a week was drawn as not counted');
  assert.equal(cellsOf(html, 'store · WSL (Ubuntu)')['7 d'], '0', 'the quiet store has a row of its own, and its part is nought');
});

test('AMissingStore_IsNotIncludedWithItsReason_NeverZero — missing, unreadable, newer schema, did not answer, and a word from the future', () => {
  const html = block(merged((answer) => {
    answer.storesNotIncluded.push({ path: '/mnt/c/x/round-counts-snapshot.json', reason: 'unreadable', detail: 'not a round-counts snapshot' });
    answer.storesNotIncluded.push({ path: 'F:\\y\\round-counts-snapshot.json', reason: 'timeout', detail: 'did not answer in time' });
  }));
  const head = headOf(html);

  assert.match(head, /D:\\coai-nas holds another installation's store; its rounds are not included: no snapshot yet — update coai-mcp on that side; the snapshot is written after the next round there\./u);
  assert.match(head, /\\\\wsl\.localhost\\Debian\\home\\me\\\.local\\share\\coai-mcp holds another installation's store; its rounds are not included: its snapshot was written by a newer coai-mcp \(schema 2 — this coai-mcp reads schema 1; update coai-mcp on this side\)\./u);
  assert.match(head, /\/mnt\/c\/x holds another installation's store; its rounds are not included: its snapshot could not be read \(not a round-counts snapshot\)\./u);
  assert.match(head, /F:\\y holds another installation's store; its rounds are not included: it did not answer in time\./u);
  assert.match(head, /E:\\x holds another installation's store; its rounds are not included: a-word-from-the-future \(kept as text\)\./u);
  assert.equal(html.match(/<tr class="store">/gu)?.length, 2, 'a store that was not counted was given a row of zeros');
});

test('AStoreAheadOfThisClock_IsSaidSo — never clamped to now', () => {
  const html = block(merged((answer) => { answer.stores[1] = { ...answer.stores[1], asOf: new Date(NOW + (40 * 60_000)).toISOString() }; }));

  assert.match(headOf(html), /WSL \(Ubuntu, as of 40 min ahead of this clock\)/u);
});

test('AWindowThatBeginsAfterAStoresWrite_SaysAsOf — the 5 h cell\'s tooltip, not the 24 h one\'s', () => {
  const html = block(merged((answer) => { answer.stores[1] = { ...answer.stores[1], asOf: hoursAgo(6) }; }));
  const row = cellsOf(html, '<b>codex</b>');

  assert.match(row['5 h']!, /WSL \(Ubuntu\): 1 \(as of 6 h ago\)/u, 'the 5 h window began after the WSL write, and the tooltip does not say so');
  assert.doesNotMatch(row['24 h']!, /as of/u, 'the 24 h window contains the WSL write: nothing to qualify');
});

test('TheCardLine_SumsBothStores — the merged rows already hold both', () => {
  const line = cardRoundsLine(snapshot(merged()), 'codex');

  assert.equal(line, '7 days: 3 rounds · 0 consult turns · 5 h: 3');
});

test('AnAnswerWithoutStores_KeepsTodaysSentence — byte for byte', () => {
  const html = block(counted(ANSWER_JSON));
  const others = block(counted(ANSWER_JSON), { store: { ...STORE, others: ['\\\\wsl.localhost\\Ubuntu\\x'] } });

  assert.ok(html.includes(`<p class="hint">${storeSentence(STORE)}</p>`));
  assert.equal(storeSentence(STORE), 'Counted from this side\'s store only — this machine, C:\\coai.');
  assert.match(others, /wsl\.localhost\\Ubuntu\\x holds another installation's store; its rounds are not included\./u);
  assert.doesNotMatch(html, /<tr class="store">/u, 'a single-store answer grew store rows');
});

test('a merged answer of this side alone is today\'s sentence — one store has nothing to be counted together with', () => {
  const html = block(merged((answer) => { answer.stores = [answer.stores[0]!]; answer.storesNotIncluded = []; }));

  assert.ok(headOf(html).includes(storeSentence(STORE)));
  assert.doesNotMatch(html, /<tr class="store">/u);
});

test('a merged call that timed out and fell back says every snapshot store did not answer — and does not repeat them as existing files', () => {
  const read: RoundCountsRead = { state: 'counted', answer: { ...parseRoundCounts(ANSWER_JSON)!, storesNotIncluded: [
    { path: 'D:\\coai-win\\round-counts-snapshot.json', reason: 'timeout', detail: 'did not answer in time' },
  ] } };
  const head = headOf(block(read, { store: { ...STORE, others: ['D:\\coai-win'] } }));

  assert.match(head, /D:\\coai-win holds another installation's store; its rounds are not included: it did not answer in time\./u);
  assert.equal(head.match(/coai-win/gu)?.length, 1, 'the same directory was named twice');
});

test('a store whose share is not answering is said so, with no count', () => {
  const sentence = storeSentence({ ...STORE, notAnswering: ['\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp'] });

  assert.match(sentence, /wsl\.localhost.* is not answering; its rounds are not included\./u);
});

test('ConflictingCopies_AreSaid_InEitherOrder — the owning store is named, whichever snapshot came first', () => {
  const ownedByWsl = block(merged((answer) => { answer.stores[1] = { ...answer.stores[1], conflicts: 2 }; }));
  const ownedByThisSide = block(merged((answer) => { answer.stores[0] = { ...answer.stores[0], conflicts: 1 }; }));

  assert.match(headOf(ownedByWsl), /2 rounds appear in two stores with different contents — counted from WSL \(Ubuntu\)/u);
  assert.match(headOf(ownedByThisSide), /1 round appears in two stores with different contents — counted from Windows \(this machine\)/u);
  assert.doesNotMatch(headOf(block(merged())), /different contents/u, 'no conflict, no sentence');
});

test('the conflicts a store reports are parsed — and an answer from before the field reads 0', () => {
  const read = merged((answer) => { answer.stores[1] = { ...answer.stores[1], conflicts: 3 }; });
  const old = merged((answer) => { delete answer.stores[1]!['conflicts']; });

  assert.equal(read.state === 'counted' ? read.answer.stores?.[1]?.conflicts : -1, 3);
  assert.equal(old.state === 'counted' ? old.answer.stores?.[1]?.conflicts : -1, 0);
});

test('TheFootnote_SaysWhoseLedgerTheMoneyIs — this side\'s and the other store\'s, and which one could not be read', () => {
  const ok = block(merged(), {}, { otherLedgers: [{ store: WSL_STORE, directory: '/x', failure: '' }] });
  const failed = block(merged(), {}, { otherLedgers: [{ store: WSL_STORE, directory: '/x', failure: 'EISDIR: illegal operation on a directory' }] });
  const alone = block(merged());

  assert.match(ok, /Tokens and money are the last 7 days, read from this side's usage ledger and WSL: Ubuntu's\./u);
  assert.match(failed, /read from this side's usage ledger; WSL: Ubuntu's could not be read: EISDIR: illegal operation on a directory\./u);
  assert.match(alone, /Tokens and money are the last 7 days, read from this side's usage ledger\. Consultations/u, 'the footnote changed with no other ledger read');
});

test('a store sub-row never carries money — activity is summed in the vendor row, a store only says how many rounds it held', () => {
  const html = block(merged(), {}, { ledger: [{ ...usageLine({ costUsd: 1 }), store: THIS_STORE }] });
  const wsl = cellsOf(html, 'store · WSL (Ubuntu)');

  assert.equal(wsl['Money (7 d)'], '');
  assert.equal(wsl['Tokens (7 d)'], '');
});
