import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spendSoFar, spendLabel } from '../chatSpend';

/**
 * What a conversation has cost, as the tab shows it.
 *
 * <p>A chat turn carries the whole conversation, so question five is billed for one through four as
 * well — the code says so out loud where the carry is built. That is the number a person would use
 * to decide between asking again and starting fresh, and until the ledger landed it was recorded
 * nowhere. This is the other half: the ledger writes it down, and the tab says it while the decision
 * is being made.</p>
 *
 * <p><b>An estimate is marked as one.</b> The three vendors do not report comparable numbers —
 * `claude` counts cache reads, `antigravity` omits cache entirely, `codex` reports a cumulative
 * maximum — so for two of the three a cost worked out from tokens is not what anybody was charged.
 * The tilde is this product's existing convention for that and it is kept here.</p>
 */

test('a conversation with nothing spent says nothing', () => {
  assert.strictEqual(spendLabel(spendSoFar([])), '');
});

test('what the vendor charged is shown as money, without a tilde', () => {
  const spend = spendSoFar([
    { costUsd: 0.0123, estimated: false },
    { costUsd: 0.0077, estimated: false },
  ]);

  assert.strictEqual(spend.usd, 0.02);
  assert.strictEqual(spend.estimated, false);
  assert.strictEqual(spendLabel(spend), '$0.0200');
});

test('a cost worked out from tokens wears the tilde', () => {
  const spend = spendSoFar([{ costUsd: 0.42, estimated: true }]);

  assert.strictEqual(spend.estimated, true);
  assert.strictEqual(spendLabel(spend), '~$0.4200');
});

test('one estimated turn makes the whole total an estimate', () => {
  // A sum of one measured price and one guess is a guess. Showing it as a bill would be the most
  // confident number on the page and the least true.
  const spend = spendSoFar([
    { costUsd: 0.01, estimated: false },
    { costUsd: 0.01, estimated: true },
  ]);

  assert.strictEqual(spend.estimated, true);
  assert.match(spendLabel(spend), /^~\$/);
});

test('a turn whose cost nobody reported is counted as nothing, and SAID to be', () => {
  // `codex` reports a cumulative maximum, which the ledger refuses to difference — so its turns
  // arrive with no cost at all. A total that silently skipped them would read as complete.
  const spend = spendSoFar([
    { costUsd: 0.01, estimated: false },
    { costUsd: null, estimated: false },
  ]);

  assert.strictEqual(spend.usd, 0.01);
  assert.strictEqual(spend.unpriced, 1);
  assert.match(spendLabel(spend), /\+ 1 turn nobody priced/);
});

test('a total is counted in four decimals, because cents would read a real cost as free', () => {
  const spend = spendSoFar([{ costUsd: 0.00004, estimated: false }, { costUsd: 0.00004, estimated: false }]);

  assert.strictEqual(spendLabel(spend), '$0.0001');
});

test('a nonsense number is not money', () => {
  for (const costUsd of [Number.NaN, Number.POSITIVE_INFINITY, -1]) {
    const spend = spendSoFar([{ costUsd, estimated: false }]);

    assert.strictEqual(spend.usd, 0, `${String(costUsd)} was counted as money`);
    assert.strictEqual(spend.unpriced, 1, `${String(costUsd)} was not counted as unpriced either`);
  }
});
