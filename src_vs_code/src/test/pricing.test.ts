import assert from 'node:assert/strict';
import { test } from 'node:test';
import { estimated, money, priceOf, totalsByVendor, UsageEntry } from '../usage';
import { Vendor } from '../vendors';

/**
 * Money, for vendors that do not report any.
 *
 * <p>Only Claude prices its own runs. Codex and Antigravity report tokens and nothing else, so
 * every row in the spending section read `—` and every round read `no cost reported`. That is
 * TRUE, and it is also useless: the question a person has is what this cost them.</p>
 *
 * <p>So the rates come from the person, never from a table we ship. A shipped price list would be
 * wrong for anyone on a flat subscription, wrong the first time a vendor changes a price, and
 * wrong silently in both cases. What is computed from a rate the person entered is marked as an
 * ESTIMATE, because a number derived from tokens is not the same fact as a number the vendor
 * billed.</p>
 */

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '',
  pricePerMillionIn: 0, pricePerMillionOut: 0, ...over,
});

const entry = (over: Partial<UsageEntry> = {}): UsageEntry => ({
  utc: new Date().toISOString(), provider: 'codex', model: 'm', role: 'Architecture',
  stage: 'CodeReview', seconds: 60, tokensIn: 1_000_000, tokensOut: 100_000, costUsd: null,
  outcome: 'ok', ...over,
});

test('a vendor with rates gets an estimate from its tokens', () => {
  const rates = [vendor({ pricePerMillionIn: 1.25, pricePerMillionOut: 10 })];
  const [row] = totalsByVendor([entry()], rates);

  // 1M in at 1.25 plus 0.1M out at 10 = 1.25 + 1.00
  assert.equal(row!.estimatedUsd, 2.25);
  assert.equal(row!.costUsd, null, 'the vendor still reported nothing; the estimate is ours');
});

test('a vendor that prices its own runs is not second-guessed', () => {
  const rates = [vendor({ id: 'claude', pricePerMillionIn: 3, pricePerMillionOut: 15 })];
  const [row] = totalsByVendor([entry({ provider: 'claude', costUsd: 0.42 })], rates);

  assert.equal(row!.costUsd, 0.42);
  assert.equal(row!.estimatedUsd, null, 'a reported cost is the fact; an estimate beside it is noise');
});

test('no rates means no number, not a zero', () => {
  const [row] = totalsByVendor([entry()], [vendor()]);

  assert.equal(row!.estimatedUsd, null);
  assert.equal(row!.costUsd, null);
});

test('an estimate is marked as one, so it is never read as a bill', () => {
  assert.equal(money(2.25), '$2.25');
  assert.equal(estimated(2.25), '~$2.25');
  assert.match(estimated(0.0004), /^~\$0\.0004$/, 'cents matter: a round is fractions of a dollar');
});

test('the rate for a vendor is found by its id, whatever the ledger calls the model', () => {
  const rates = [vendor({ id: 'antigravity', pricePerMillionIn: 2, pricePerMillionOut: 4 })];

  assert.deepEqual(priceOf('antigravity', rates), { in: 2, out: 4 });
  assert.equal(priceOf('somebody-else', rates), undefined);
});

test('the total says how much of it is estimated rather than billed', () => {
  const rates = [
    vendor({ id: 'codex', pricePerMillionIn: 1, pricePerMillionOut: 2 }),
    vendor({ id: 'claude', pricePerMillionIn: 3, pricePerMillionOut: 15 }),
  ];
  const rows = totalsByVendor(
    [entry({ provider: 'codex' }), entry({ provider: 'claude', costUsd: 5 })],
    rates,
  );

  const reported = rows.reduce((t, r) => t + (r.costUsd ?? 0), 0);
  const guessed = rows.reduce((t, r) => t + (r.estimatedUsd ?? 0), 0);
  assert.equal(reported, 5);
  assert.equal(guessed, 1.2, '1M in at 1 plus 0.1M out at 2');
});
