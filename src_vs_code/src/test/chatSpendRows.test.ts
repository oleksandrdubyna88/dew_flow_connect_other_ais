import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatDoorRecord } from '../chatDoors';
import { ChatPriceOf, chatSpendRows, chatSpendTotals } from '../chatSpendRows';
import { ChatTurnRecord } from '../chatUsage';

/**
 * What the chat cost, as arithmetic — the half of the spending page a squint cannot check.
 *
 * <p>The clock is REAL, like the bundled page's fixtures and for the same reason: the day window is
 * built from `new Date()`, so a fixture pinned to a literal date is a test that is green on the
 * afternoon it was written and red every morning after. Local noon, which no offset can move out of
 * its own day.</p>
 */

function todayAt(hours: number): string {
  const day = new Date();
  day.setHours(hours, 0, 0, 0);

  return day.toISOString();
}

function daysAgo(days: number): string {
  const day = new Date();
  day.setDate(day.getDate() - days);
  day.setHours(12, 0, 0, 0);

  return day.toISOString();
}

const NOW = new Date();

function turn(one: Partial<ChatTurnRecord>): ChatTurnRecord {
  return {
    utc: todayAt(12),
    provider: 'antigravity',
    model: 'gemini-3.8-flash',
    tokensIn: 1_000_000,
    tokensOut: 1_000_000,
    costUsd: null,
    seconds: 3,
    outcome: 'answered',
    conversation: 'c1',
    title: 'a tab',
    ...one,
  };
}

function door(one: Partial<ChatDoorRecord>): ChatDoorRecord {
  return { utc: todayAt(12), door: 'take', provider: 'antigravity', model: 'gemini-3.8-flash', ...one };
}

/** A dollar in, ten out, per million — for one model and nothing else. */
const PRICED: ChatPriceOf = (model) =>
  model === 'gemini-3.8-flash' ? { inPerMillion: 1, outPerMillion: 10 } : undefined;

test('two models of one vendor are two rows, and one model of two vendors is two', () => {
  // A chat switches model mid-conversation and a rate belongs to a model, so the vendor alone
  // cannot key a row that shows a price.
  const rows = chatSpendRows(
    [
      turn({}),
      turn({ model: 'gemini-3.7-flash' }),
      turn({ provider: 'codex', model: 'gemini-3.8-flash' }),
    ],
    [],
    'day',
    NOW,
  );

  assert.deepStrictEqual(
    rows.map((row) => `${row.provider}/${row.model}`).sort(),
    ['antigravity/gemini-3.7-flash', 'antigravity/gemini-3.8-flash', 'codex/gemini-3.8-flash'],
  );
});

test('Asked counts take and add; Opened counts every door', () => {
  const rows = chatSpendRows(
    [turn({})],
    [
      door({ door: 'take' }),
      door({ door: 'add' }),
      door({ door: 'key' }),
      door({ door: 'default' }),
      door({ door: 'choose' }),
    ],
    'day',
    NOW,
  );

  assert.strictEqual(rows[0]?.asked, 2, 'Asked is take and add, and it counted something else');
  assert.strictEqual(rows[0]?.opened, 5, 'Opened is every door, and one of them went missing');
});

test('the window bounds the tokens and both counts, and does NOT bound the all-time money', () => {
  // The one column that ignores the buttons, asked for in those words: what this pair has cost
  // altogether is the number behind "carry on with this conversation, or start a fresh one".
  const rows = chatSpendRows(
    [turn({}), turn({ utc: daysAgo(40) })],
    [door({}), door({ utc: daysAgo(40) })],
    'day',
    NOW,
    PRICED,
  );

  assert.strictEqual(rows.length, 1);
  assert.strictEqual(rows[0]?.tokensIn, 1_000_000, 'a turn from outside the window was counted in it');
  assert.strictEqual(rows[0]?.opened, 1, 'a door from outside the window was counted in it');
  // One turn in the window is $1 in + $10 out; both turns together are twice that.
  assert.strictEqual(rows[0]?.estimatedUsd, 11);
  assert.strictEqual(rows[0]?.allTimeUsd, 22, 'the all-time column followed the window');
});

test('a bill and an estimate are never summed into one number', () => {
  const rows = chatSpendRows(
    [turn({ costUsd: 0.5 }), turn({ provider: 'codex', model: 'gpt-5.6', costUsd: null })],
    [],
    'day',
    NOW,
    PRICED,
  );
  const billed = rows.find((row) => row.provider === 'antigravity');
  const guessed = rows.find((row) => row.provider === 'codex');

  assert.deepStrictEqual([billed?.costUsd, billed?.estimatedUsd], [0.5, null], 'a bill was marked as a guess');
  // codex's model has no rate at all here: no money, and the turn is COUNTED rather than dropped.
  assert.deepStrictEqual([guessed?.costUsd, guessed?.estimatedUsd], [null, null]);
  assert.strictEqual(guessed?.unpriced, 1, 'a turn nobody can price was quietly read as free');
  assert.deepStrictEqual([guessed?.inPerMillion, guessed?.outPerMillion], [null, null],
    'a model with no rate showed a price of zero, which is a different claim');
});

test('a door that resolved nothing lands in its own row rather than in somebody else', () => {
  // A window with no vendor configured is a real state. Inventing a vendor for it would put
  // invocations under a row that never ran.
  const rows = chatSpendRows([turn({})], [door({ provider: '', model: '', door: 'add' })], 'day', NOW);
  const nowhere = rows.find((row) => row.provider === '');

  assert.strictEqual(nowhere?.opened, 1, 'an invocation with no vendor was dropped or misfiled');
  assert.strictEqual(rows.find((row) => row.provider === 'antigravity')?.opened, 0,
    'an invocation with no vendor was counted against a vendor');
});

test('a pair that was only ever reached for still has a row', () => {
  // Opened and never answered is the thing worth seeing: a tab was opened and nothing was asked.
  const rows = chatSpendRows([], [door({})], 'day', NOW);

  assert.strictEqual(rows.length, 1);
  assert.deepStrictEqual([rows[0]?.turns, rows[0]?.opened], [0, 1]);
});

test('a pair with nothing in the window is not a row of dashes', () => {
  // The same rule the reviewer cards above follow: a window with nothing in it says so. The all-time
  // column is context for a pair that IS on the page - keeping a row alive because its all-time
  // money is known would have made the row appear or not depending on whether its model had a rate.
  assert.deepStrictEqual(chatSpendRows([turn({ utc: daysAgo(40) })], [], 'day', NOW), []);
  assert.deepStrictEqual(chatSpendRows([turn({ utc: daysAgo(40) })], [], 'day', NOW, PRICED), []);

  const month = chatSpendRows([turn({ utc: daysAgo(40) })], [], 'year', NOW, PRICED);

  assert.strictEqual(month[0]?.allTimeUsd, 11, 'the wider window did not find the turn at all');
});

test('the section total counts the ledger, not the rows', () => {
  // A door whose pair never answered is dropped by no filter here, but the totals must not DEPEND on
  // that: they are counted from the records, so the two numbers agree with the ledger rather than
  // with each other.
  const totals = chatSpendTotals(
    [turn({}), turn({ provider: 'codex', model: 'gpt-5.6' })],
    [door({}), door({ door: 'add', provider: '', model: '' }), door({ door: 'key' })],
    'day',
    NOW,
    PRICED,
  );

  assert.strictEqual(totals.tokens, 4_000_000);
  assert.strictEqual(totals.asked, 2, 'take plus add, across every row');
  assert.strictEqual(totals.opened, 3);
  assert.strictEqual(totals.estimatedUsd, 11, 'only the priced model contributes money');
  assert.strictEqual(totals.costUsd, null, 'nobody billed anything, and the total claimed somebody had');
  assert.strictEqual(totals.unpriced, 1, 'the turn nobody can price went missing from the total');
});
