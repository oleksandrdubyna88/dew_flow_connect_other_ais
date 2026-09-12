import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatDoorRecord } from '../chatDoors';
import { ChatPriceOf, ChatSpendRow, chatSpend } from '../chatSpendRows';
import { ChatTurnRecord } from '../chatUsage';

/**
 * What the chat cost, as arithmetic — the half of the spending page a squint cannot check.
 *
 * <p>The clock is REAL, like the bundled page's fixtures and for the same reason: the day window is
 * built from the LOCAL calendar day (`within` says so and the operator ruled it), so a fixture
 * pinned to a literal UTC date is a test that is green on the afternoon it was written and red every
 * morning after. Local noon is the one hour no offset on earth can move out of its own day, and what
 * these fixtures STORE is `toISOString()` — UTC, as the rule requires.</p>
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

function rowsOf(
  turns: readonly ChatTurnRecord[],
  doors: readonly ChatDoorRecord[],
  priceOf?: ChatPriceOf,
  window: 'day' | 'year' = 'day',
): readonly ChatSpendRow[] {
  return chatSpend(turns, doors, window, NOW, priceOf).rows;
}

test('two models of one vendor are two rows, and one model of two vendors is two', () => {
  // A chat switches model mid-conversation and a rate belongs to a model, so the vendor alone
  // cannot key a row that shows a price.
  const rows = rowsOf(
    [
      turn({}),
      turn({ model: 'gemini-3.7-flash' }),
      turn({ provider: 'codex', model: 'gemini-3.8-flash' }),
    ],
    [],
  );

  assert.deepStrictEqual(
    rows.map((row) => `${row.provider}/${row.model}`).sort(),
    ['antigravity/gemini-3.7-flash', 'antigravity/gemini-3.8-flash', 'codex/gemini-3.8-flash'],
  );
});

test('Asked counts take and add; Opened counts every door', () => {
  const rows = rowsOf(
    [turn({})],
    [
      door({ door: 'take' }),
      door({ door: 'add' }),
      door({ door: 'key' }),
      door({ door: 'default' }),
      door({ door: 'choose' }),
    ],
  );

  assert.strictEqual(rows[0]?.asked, 2, 'Asked is take and add, and it counted something else');
  assert.strictEqual(rows[0]?.opened, 5, 'Opened is every door, and one of them went missing');
});

test('the window bounds the tokens and both counts, and does NOT bound the all-time money', () => {
  // The one column that ignores the buttons, asked for in those words: what this pair has cost
  // altogether is the number behind "carry on with this conversation, or start a fresh one".
  const rows = rowsOf(
    [turn({}), turn({ utc: daysAgo(40) })],
    [door({}), door({ utc: daysAgo(40) })],
    PRICED,
  );

  assert.strictEqual(rows.length, 1);
  assert.strictEqual(rows[0]?.tokensIn, 1_000_000, 'a turn from outside the window was counted in it');
  assert.strictEqual(rows[0]?.opened, 1, 'a door from outside the window was counted in it');
  // One turn in the window is $1 in + $10 out; both turns together are twice that.
  assert.strictEqual(rows[0]?.estimatedUsd, 11);
  assert.strictEqual(rows[0]?.allTimeEstimatedUsd, 22, 'the all-time column followed the window');
});

test('a bill and an estimate are never summed into one number', () => {
  const rows = rowsOf(
    [turn({ costUsd: 0.5 }), turn({ provider: 'codex', model: 'gpt-5.6', costUsd: null })],
    [],
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

test('a row that is part billed and part not keeps BOTH numbers', () => {
  // The hole two reviewers found from different directions: one billed turn used to make every
  // unbilled turn beside it free. A vendor that reports a cost on some calls and not others is an
  // ordinary state, and a row that reads $0.50 for $11.50 of work is the worst kind of wrong number.
  const rows = rowsOf([turn({ costUsd: 0.5 }), turn({ costUsd: null })], [], PRICED);

  assert.strictEqual(rows.length, 1);
  assert.strictEqual(rows[0]?.costUsd, 0.5, 'the bill was lost');
  assert.strictEqual(rows[0]?.estimatedUsd, 11, 'the turn nobody billed was recorded as free');
  assert.strictEqual(rows[0]?.unpriced, 0, 'a turn with a rate was counted as unpriceable');
  // And the same over the whole ledger, which is the column nobody can cross-check by eye.
  assert.deepStrictEqual([rows[0]?.allTimeUsd, rows[0]?.allTimeEstimatedUsd], [0.5, 11]);
});

test('a cost nobody could have charged is not a cost', () => {
  // A negative bill is not a credit and NaN is not a price - both mean "nobody said", which is the
  // rule the running total in a chat tab already applies.
  const rows = rowsOf([turn({ costUsd: -3 }), turn({ costUsd: Number.NaN })], [], PRICED);

  assert.strictEqual(rows[0]?.costUsd, null, 'a negative or unreadable bill was added up as money');
  assert.strictEqual(rows[0]?.estimatedUsd, 22, 'both turns should have been priced from the rate');
});

test('a record whose instant cannot be read belongs to no window', () => {
  // It would otherwise reach a comparison as NaN, which is false both ways round - so the record
  // would be counted or not depending on which side of the comparison it landed.
  const rows = rowsOf([turn({ utc: 'the day before yesterday' })], [door({ utc: 'soon' })], PRICED);

  assert.deepStrictEqual(rows, [], 'a record with no readable instant was placed in the window anyway');
});

test('a door that resolved nothing lands in its own row rather than in somebody else', () => {
  // A window with no vendor configured is a real state. Inventing a vendor for it would put
  // invocations under a row that never ran.
  const rows = rowsOf([turn({})], [door({ provider: '', model: '', door: 'add' })]);
  const nowhere = rows.find((row) => row.provider === '');

  assert.strictEqual(nowhere?.opened, 1, 'an invocation with no vendor was dropped or misfiled');
  assert.strictEqual(rows.find((row) => row.provider === 'antigravity')?.opened, 0,
    'an invocation with no vendor was counted against a vendor');
});

test('a pair that was only ever reached for still has a row', () => {
  // Opened and never answered is the thing worth seeing: a tab was opened and nothing was asked.
  const rows = rowsOf([], [door({})]);

  assert.strictEqual(rows.length, 1);
  assert.deepStrictEqual([rows[0]?.turns, rows[0]?.opened], [0, 1]);
});

test('a pair with nothing in the window is not a row of dashes', () => {
  // The same rule the reviewer cards above follow: a window with nothing in it says so. The all-time
  // column is context for a pair that IS on the page - keeping a row alive because its all-time
  // money is known would have made the row appear or not depending on whether its model had a rate.
  assert.deepStrictEqual(rowsOf([turn({ utc: daysAgo(40) })], []), []);
  assert.deepStrictEqual(rowsOf([turn({ utc: daysAgo(40) })], [], PRICED), []);

  const year = rowsOf([turn({ utc: daysAgo(40) })], [], PRICED, 'year');

  assert.strictEqual(year[0]?.allTimeEstimatedUsd, 11, 'the wider window did not find the turn at all');
});

test('the section total counts the ledger, not the rows that survived the filter', () => {
  const { totals } = chatSpend(
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

test('the whole ledger is read once, not once per row', () => {
  // The finding four reviewers reached independently. A year of history against fifty pairs used to
  // be fifty full sweeps, on the extension host's own thread, on every repaint. Counted rather than
  // timed: a clock measures the machine, a count measures the algorithm.
  let reads = 0;
  const many: ChatTurnRecord[] = [];
  for (let index = 0; index < 400; index += 1) {
    many.push(turn({ model: `model-${index % 40}`, utc: todayAt(12) }));
  }
  const counting: ChatPriceOf = (model) => {
    reads += 1;

    return model === 'gemini-3.8-flash' ? { inPerMillion: 1, outPerMillion: 10 } : undefined;
  };

  const { rows } = chatSpend(many, [], 'day', NOW, counting);

  assert.strictEqual(rows.length, 40, 'the fixture does not have the pairs this is measuring');
  assert.strictEqual(reads, 40, 'the price was looked up per record rather than per row');
});
