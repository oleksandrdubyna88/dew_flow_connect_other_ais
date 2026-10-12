import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { GaugeRatio } from '../gaugeWords';
import { planPricesFrom, readPlanPrice, savePlanPrice, shareLine } from '../planPrice';

/**
 * A subscription's share (story 6.2 of todo/PLAN_every_round_is_counted.md): a monthly plan price typed ONCE per vendor
 * account — a finite number ≥ 0, anything else refused at the card with the reason and never saved, 0 meaning "no price"
 * — turns the weekly gauge into money: monthly price × weekly-% per round ÷ 100 ÷ weeks in the month. Shown only beside a
 * figure that is shown, labelled as a share, never summed with a list-price estimate.
 */

const OCTOBER = Date.parse('2026-10-12T12:00:00Z');

const weekly = (roundsPerPercent: number | null, over: Partial<GaugeRatio> = {}): GaugeRatio => ({
  vendor: 'acme', limitId: 'acme', windowMins: 10080, roundsPerPercent, intervals: 3, unavailable: roundsPerPercent === null ? 'percent-did-not-rise' : '',
  missingConsumer: '', ...over,
});

test('a finite number of dollars, zero or more, is a price — 0 is no price, not free', () => {
  assert.deepEqual(readPlanPrice('20'), { kind: 'price', usd: 20 });
  assert.deepEqual(readPlanPrice(' 19.99 '), { kind: 'price', usd: 19.99 });
  assert.deepEqual(readPlanPrice('0'), { kind: 'none' });
});

test('anything else is refused with the reason', () => {
  for (const typed of ['NaN', 'abc', '', '   ', 'Infinity', '1e400', '$20', '20 USD', '0x14']) {
    const read = readPlanPrice(typed);
    assert.equal(read.kind, 'refused', `"${typed}" was taken as a price`);
    assert.match(read.kind === 'refused' ? read.reason : '', /number|dollars/, `"${typed}" was refused without saying what is wanted`);
  }
  const negative = readPlanPrice('-5');
  assert.equal(negative.kind, 'refused');
  assert.match(negative.kind === 'refused' ? negative.reason : '', /negative/);
});

test('a save keeps every other account\'s price, and 0 takes this one away', () => {
  const before = { 'ts/codex': 200, 'ts/claude': 100 };

  assert.deepEqual(savePlanPrice(before, 'ts/codex|250'), { kind: 'saved', prices: { 'ts/codex': 250, 'ts/claude': 100 } });
  assert.deepEqual(savePlanPrice(before, 'ts/codex|0'), { kind: 'saved', prices: { 'ts/claude': 100 } });
  assert.deepEqual(before, { 'ts/codex': 200, 'ts/claude': 100 }, 'the prices held were changed in place');
});

test('a refused price is never saved — the card is told what was typed and why', () => {
  const saved = savePlanPrice({ 'ts/codex': 200 }, 'ts/codex|-1');

  assert.equal(saved.kind, 'refused');
  assert.deepEqual(saved.kind === 'refused' ? { key: saved.refusal.key, typed: saved.refusal.typed } : {}, { key: 'ts/codex', typed: '-1' });
  assert.equal(savePlanPrice({}, 'no-separator').kind, 'refused', 'a press that names no account saves nothing');
});

test('the stored prices are read back finite and positive only', () => {
  assert.deepEqual(planPricesFrom({ 'ts/codex': 200, 'ts/a': 0, 'ts/b': -3, 'ts/c': 'x', 'ts/d': Number.NaN, 'ts/e': null }), { 'ts/codex': 200 });
  assert.deepEqual(planPricesFrom('nonsense'), {});
  assert.deepEqual(planPricesFrom([200]), {});
});

test('the share: monthly price × weekly-% per round ÷ 100 ÷ weeks in the month', () => {
  // 4 rounds per 1 % is 0.25 % of the week per round; October has 31 / 7 weeks: 200 × 0.25 / 100 / 4.43 ≈ $0.11.
  assert.equal(shareLine(200, weekly(4), OCTOBER), '≈ $0.11 of your plan per round — a share of the $200/month plan, not a bill');
});

test('no share without a shown figure, a weekly window and a price', () => {
  assert.equal(shareLine(200, weekly(null), OCTOBER), '', 'the share is hidden with the figure');
  assert.equal(shareLine(200, weekly(null, { unavailable: 'consumer-not-counted', missingConsumer: 'direct-codex-use' }), OCTOBER), '');
  assert.equal(shareLine(undefined, weekly(4), OCTOBER), '', 'no price typed');
  assert.equal(shareLine(200, weekly(4, { windowMins: 300 }), OCTOBER), '', 'a five-hour window is not the weekly share');
  assert.equal(shareLine(200, undefined, OCTOBER), '', 'no gauge reading, no share');
});
