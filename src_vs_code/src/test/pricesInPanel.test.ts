import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import { ModelPrice } from '../modelPrices';
import { DEFAULTS } from '../settingsShape';
import { totalsByVendor, UsageEntry } from '../usage';
import { Vendor } from '../vendors';

/**
 * A looked-up price is a DEFAULT, and a typed one is a fact.
 *
 * <p>The rates were empty on every machine this shipped to, so the money column was dashes. Both
 * public lists carry every model here, so the number can be filled in — but it is a LIST price, not
 * a bill: reviews run on a subscription, and what a token would have cost through an API is a
 * different kind of statement from what somebody was charged. So it shows as a PLACEHOLDER, it never
 * replaces a typed rate, and the money it produces keeps the tilde.</p>
 */

const GEMINI: ModelPrice = { inPerMillion: 0.75, outPerMillion: 3.75, source: 'openrouter' };

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high', baseUrl: '',
    executablePath: '', enabled: true, pricePerMillionIn: 0, pricePerMillionOut: 0, ...over,
  };
}

function html(v: Vendor, prices: Record<string, ModelPrice>): string {
  return panelHtml({
    settings: DEFAULTS, vendors: [v], codexModels: [], serverInstalled: false, serverVersion: '',
    latestServerVersion: '', questions: [], sessions: [], openSections: [], usage: [],
    usageWindow: 'week', cliStatus: {}, modelPrices: prices,
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

test('an empty rate field shows the published price as its placeholder', () => {
  const page = html(vendor(), { 'gemini-3.7-flash-high': GEMINI });

  assert.match(page, /id="price-in-antigravity"[^>]*placeholder="0\.75"/s);
  assert.match(page, /id="price-out-antigravity"[^>]*placeholder="3\.75"/s);
});

test('a typed rate stays the value, and the published one stays behind it', () => {
  // The panel must never overwrite what somebody entered: their number is a fact about their
  // account, the list price is a general estimate.
  const page = html(vendor({ pricePerMillionIn: 1.5 }), { 'gemini-3.7-flash-high': GEMINI });

  assert.match(page, /id="price-in-antigravity"[^>]*value="1\.5"/s);
  assert.match(page, /id="price-in-antigravity"[^>]*placeholder="0\.75"/s);
});

test('a model no list knows keeps its dash', () => {
  const page = html(vendor({ model: 'some-private-model' }), {});

  assert.match(page, /id="price-in-antigravity"[^>]*placeholder="—"/s);
});

test('the tooltip says where the number came from, and that it is not a bill', () => {
  const page = html(vendor(), { 'gemini-3.7-flash-high': GEMINI });

  // The apostrophe is not entity-escaped, and inside a double-quoted attribute it does not
  // need to be — asserting the escaped form would be asserting a coincidence.
  assert.match(page, /from OpenRouter's model list/);
  assert.match(page, /not what you were billed/);
});

test('the money uses the published price when nobody typed one', () => {
  const entry = {
    utc: '2026-09-01T10:00:00Z', provider: 'antigravity', model: 'gemini-3.7-flash-high',
    role: 'Architecture', stage: 'CodeReview', seconds: 10, tokensIn: 1_000_000,
    tokensOut: 1_000_000, costUsd: null, outcome: 'ok',
  } as UsageEntry;

  const [row] = totalsByVendor([entry], [vendor()], (id) => (id === 'gemini-3.7-flash-high' ? GEMINI : undefined));

  assert.equal(row?.estimatedUsd, 4.5, 'one million each way at 0.75 + 3.75');
});

test('a typed rate beats the published one in the money too', () => {
  const entry = {
    utc: '2026-09-01T10:00:00Z', provider: 'antigravity', model: 'gemini-3.7-flash-high',
    role: 'Architecture', stage: 'CodeReview', seconds: 10, tokensIn: 1_000_000,
    tokensOut: 0, costUsd: null, outcome: 'ok',
  } as UsageEntry;

  const [row] = totalsByVendor([entry], [vendor({ pricePerMillionIn: 10 })], () => GEMINI);

  assert.equal(row?.estimatedUsd, 10);
});

test('a vendor that reported real money is not second-guessed', () => {
  // An estimate beside a bill is noise, and the bill is the fact.
  const entry = {
    utc: '2026-09-01T10:00:00Z', provider: 'antigravity', model: 'gemini-3.7-flash-high',
    role: 'Architecture', stage: 'CodeReview', seconds: 10, tokensIn: 1_000_000,
    tokensOut: 1_000_000, costUsd: 0.42, outcome: 'ok',
  } as UsageEntry;

  const [row] = totalsByVendor([entry], [vendor()], () => GEMINI);

  assert.equal(row?.costUsd, 0.42);
  assert.equal(row?.estimatedUsd, null);
});
