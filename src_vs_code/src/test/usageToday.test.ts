import assert from 'node:assert/strict';
import { test } from 'node:test';
import { UsageEntry, within } from '../usage';

/**
 * "Today" is since midnight, not the last twenty-four hours.
 *
 * <p>Ruled by the operator on 2026-09-05: <i>"по умолчанию показывать сегодня от 00 до сейчас"</i>.
 * The previous rule — a rolling day, so that 23:50 and 00:10 share a chart — was an argument the
 * page's own reader did not accept: "what did today cost" is a question about the calendar day.
 * Week, month and year stay rolling.</p>
 */

function at(utc: string): UsageEntry {
  return { utc, provider: 'codex', role: 'PlanCritique', tokensIn: 1, tokensOut: 1, costUsd: null, seconds: 1, outcome: 'ok' } as UsageEntry;
}

test('today starts at local midnight', () => {
  const now = new Date(2026, 8, 5, 9, 0, 0); // 09:00 local on the 5th
  const beforeMidnight = at(new Date(2026, 8, 4, 23, 50, 0).toISOString());
  const afterMidnight = at(new Date(2026, 8, 5, 0, 10, 0).toISOString());

  const kept = within([beforeMidnight, afterMidnight], 'day', now);

  assert.deepEqual(kept.map((e) => e.utc), [afterMidnight.utc], '23:50 yesterday is yesterday');
});

test('the week is still rolling, so a quiet Monday morning shows last week\'s work', () => {
  const now = new Date(2026, 8, 7, 8, 0, 0); // Monday 08:00
  const lastFriday = at(new Date(2026, 8, 4, 15, 0, 0).toISOString());

  assert.equal(within([lastFriday], 'week', now).length, 1);
});
