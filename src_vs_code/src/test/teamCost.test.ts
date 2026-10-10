import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ModelPrice } from '../modelPrices';
import { costText, modelCost, totalCost, vendorCost } from '../teamCost';
import { ModelUsage, VendorUsage } from '../teamServerApi';

/**
 * ~$ per model, vendor and person from the server's per-model breakdown (story 3.2, D4): each model at ITS public list
 * price, a vendor-reported cost winning over an estimate, a model nobody lists a dash that makes its total a floor —
 * and never a long-context tier on a sum, never a person's own typed row rate (there is no row here to have one).
 */

const SOL: ModelPrice = { inPerMillion: 2, outPerMillion: 10, source: 'openrouter' };
// A tier from 200K prompt tokens in ONE request: summed totals cross it trivially, and must not be priced by it.
const TIERED: ModelPrice = {
  inPerMillion: 3, outPerMillion: 15, source: 'litellm',
  tier: { fromTokens: 200_000, inPerMillion: 6, outPerMillion: 30, cachedPerMillion: 1 },
};
const price = (model: string): ModelPrice | undefined => ({ 'gpt-5.6-sol': SOL, 'grok-4.7': TIERED })[model];

function model(over: Partial<ModelUsage>): ModelUsage {
  return { model: 'gpt-5.6-sol', runs: 1, failed: 0, tokensIn: 1_000_000, tokensOut: 100_000, ...over };
}

test('a model is priced at its own list price: tokens in and out at their rates', () => {
  const estimate = modelCost(model({}), price);

  assert.equal(estimate.known, true);
  assert.equal(estimate.floor, false);
  assert.ok(Math.abs(estimate.usd - 3) < 1e-9, `2 + 1 dollars, got ${estimate.usd}`);
  assert.equal(costText(estimate), '~$3.00');
});

test('a summed total is never priced at a long-context tier — that threshold is per REQUEST', () => {
  const estimate = modelCost(model({ model: 'grok-4.7', tokensIn: 2_000_000, tokensOut: 0 }), price);

  assert.ok(Math.abs(estimate.usd - 6) < 1e-9, `two million at the base 3/M is 6, got ${estimate.usd} — the 6/M tier was applied to a sum`);
});

test('a vendor-reported cost wins over an estimate, and is shown without the tilde', () => {
  const estimate = modelCost(model({ costUsd: 0.42 }), price);

  assert.equal(estimate.usd, 0.42);
  assert.equal(estimate.estimated, false);
  assert.equal(costText(estimate), '$0.4200');
});

test('a model no list prices is a dash, never $0', () => {
  const estimate = modelCost(model({ model: 'local-thing' }), price);

  assert.equal(estimate.known, false);
  assert.equal(costText(estimate), '—');
});

test('an unknown model ("") is unpriced too, not guessed', () => {
  assert.equal(modelCost(model({ model: '' }), price).known, false);
});

test('a vendor with one unpriced model is a FLOOR: the priced part, marked as at least', () => {
  const row: VendorUsage = {
    vendor: 'codex', runs: 2, failed: 0, tokensIn: 0, tokensOut: 0, seconds: 0,
    models: [model({}), model({ model: 'local-thing' })],
  };
  const estimate = vendorCost(row, price);

  assert.equal(estimate.known, true);
  assert.equal(estimate.floor, true);
  assert.equal(costText(estimate), '≥ ~$3.00');
});

test('a vendor whose server reported a partial cost keeps it as a floor', () => {
  const estimate = modelCost(model({ costUsd: 1.5, costIsFloor: true }), price);

  assert.equal(costText(estimate), '≥ $1.50');
});

test('a vendor with no per-model breakdown has no estimate — the older server cannot say which model ran', () => {
  const row: VendorUsage = { vendor: 'codex', runs: 2, failed: 0, tokensIn: 5, tokensOut: 5, seconds: 0 };

  assert.equal(vendorCost(row, price).known, false);
});

test('a total of estimates and bills is one estimate; of bills alone, a bill; of nothing known, a dash', () => {
  const bill = modelCost(model({ costUsd: 1 }), price);
  const guess = modelCost(model({}), price);
  const none = modelCost(model({ model: 'local-thing' }), price);

  assert.equal(costText(totalCost([bill, guess])), '~$4.00');
  assert.equal(costText(totalCost([bill, bill])), '$2.00');
  assert.equal(costText(totalCost([none, none])), '—');
  assert.equal(costText(totalCost([guess, none])), '≥ ~$3.00');
  assert.equal(costText(totalCost([])), '—');
});
