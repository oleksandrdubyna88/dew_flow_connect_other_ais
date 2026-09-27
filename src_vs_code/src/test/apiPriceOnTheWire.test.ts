import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ModelPrice } from '../modelPrices';
import { serverSettingsJson } from '../serverSettingsFile';
import { settingsFrom } from '../settingsShape';
import { Vendor, vendorsEnv, vendorsFrom, wirePrice } from '../vendors';

/**
 * S3.7 of PLAN_feature_review.md, the panel's half: an `api` row's price crosses to the server with the
 * row — a price is not a secret — so the server can put a cost on a turn whose response carries none.
 *
 * <p>The price is the one the panel already shows: a rate the person TYPED wins, field by field, over
 * the looked-up one (`modelPrices.ts`, the lookup codex and antigravity use).</p>
 */

const XAI: ModelPrice = {
  inPerMillion: 2, outPerMillion: 6, cachedPerMillion: 0.5, source: 'litellm',
  tier: { fromTokens: 200_000, inPerMillion: 4, outPerMillion: 12, cachedPerMillion: 1 },
};

function api(overrides: Partial<Vendor> = {}): Vendor {
  return {
    id: 'grok', runtime: 'api', model: 'grok-4.7', enabled: true, plan: true, code: true,
    baseUrl: 'https://api.x.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
    ...overrides,
  };
}

function wire(vendors: readonly Vendor[], price: ModelPrice | undefined): Record<string, unknown>[] {
  return JSON.parse(vendorsEnv(vendors, '', () => price)) as Record<string, unknown>[];
}

test('an api row with a looked-up price carries all three rates and the tier', () => {
  assert.deepEqual(wire([api()], XAI)[0]?.['price'], {
    in: 2, cached: 0.5, out: 6, tierFrom: 200_000, tierIn: 4, tierCached: 1, tierOut: 12,
  });
});

test('a typed rate wins over the looked-up one, field by field, and then no tier is guessed', () => {
  // A person who typed their own input rate is on their own terms; doubling it from a list's
  // threshold would be arithmetic on somebody else's price.
  assert.deepEqual(wirePrice(api({ pricePerMillionIn: 1.5, pricePerMillionCached: 0.1 }), XAI),
    { in: 1.5, cached: 0.1, out: 6 });
});

test('a typed price crosses with no lookup at all', () => {
  assert.deepEqual(wirePrice(api({ pricePerMillionIn: 2, pricePerMillionOut: 6 }), undefined), { in: 2, cached: 0, out: 6 });
});

test('no price is no field — never a zero price', () => {
  assert.equal(wirePrice(api(), undefined), undefined);
  assert.equal('price' in (wire([api()], undefined)[0] ?? {}), false);
});

test('only an api row carries a price: a CLI on a subscription is not billed per token', () => {
  const codex = api({ id: 'codex', runtime: 'codex', baseUrl: '', model: '', pricePerMillionIn: 3, pricePerMillionOut: 9 });

  assert.equal('price' in (wire([codex], XAI)[0] ?? {}), false);
});

test('the cached rate is a field of the row that survives being saved', () => {
  const [stored] = vendorsFrom([{ ...api(), pricePerMillionCached: 0.5 }]);

  assert.equal(stored?.pricePerMillionCached, 0.5);
  assert.equal(vendorsFrom([{ ...api(), pricePerMillionCached: -1 }])[0]?.pricePerMillionCached ?? 0, 0,
    'a negative rate would credit the person');
});

test('the settings file the server reads carries the price the lookup gave', () => {
  const settings = settingsFrom(() => undefined);
  const json = JSON.parse(serverSettingsJson(settings, [api()], '', '', () => XAI)) as Record<string, string>;
  const rows = JSON.parse(json['COAI_VENDORS'] ?? '[]') as Record<string, unknown>[];

  assert.deepEqual((rows[0]?.['price'] as Record<string, unknown> | undefined)?.['in'], 2);
});
