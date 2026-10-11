import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ModelPrice } from '../modelPrices';
import { spend } from '../panelView';
import { parseUsage, totalsByVendor, UsageEntry } from '../usage';
import { Vendor } from '../vendors';

/**
 * The spending tab prices each ledger LINE (story 2.5 of todo/PLAN_every_round_is_counted.md): by the model that
 * answered it, its cached input at the cached rate, a long-context tier per request, a reported cost never re-estimated,
 * and a total that could not price every line marked as a floor. The arithmetic is `TokenPrice.CostOf`'s
 * (`src_mcp/core/Findings/TokenPrice.cs`) — one rule on both halves.
 */

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '',
  pricePerMillionIn: 0, pricePerMillionOut: 0, ...over,
});

const line = (over: Partial<UsageEntry> = {}): UsageEntry => ({
  utc: '2026-10-09T10:00:00Z', provider: 'codex', model: 'gpt-6-luna', role: 'Architecture', stage: 'CodeReview',
  seconds: 30, tokensIn: 1_000_000, tokensOut: 0, costUsd: null, outcome: 'ok', ...over,
});

const listed = (table: Readonly<Record<string, ModelPrice>>) => (model: string): ModelPrice | undefined => table[model];

const near = (actual: number | null, expected: number, why: string): void => {
  assert.ok(actual !== null && Math.abs(actual - expected) < 1e-9, `${why}: expected ${expected}, got ${actual}`);
};

test('TheParser_KeepsCachedReasoningAndKind — a new line keeps its cached, reasoning, kind and usage note; an old line reads as a review with none', () => {
  const [fresh, old] = parseUsage([
    '{"utc":"2026-10-09T10:00:00Z","provider":"codex","model":"gpt-6-astra","tokensIn":29138,"tokensOut":261,'
    + '"tokensCached":14080,"tokensReasoning":62,"kind":"consult","usageNote":"usage not captured","outcome":"ok"}',
    '{"utc":"2026-09-01T10:00:00Z","provider":"codex","tokensIn":5,"tokensOut":1}',
  ].join('\n'));

  assert.equal(fresh?.tokensCached, 14080, 'the cached subset was dropped on the way in');
  assert.equal(fresh?.tokensReasoning, 62, 'the reasoning share was dropped on the way in');
  assert.equal(fresh?.kind, 'consult', 'the kind was dropped, so a consultation reads as a review');
  assert.equal(fresh?.usageNote, 'usage not captured');
  assert.equal(old?.tokensCached, 0, 'an old line has no cached count — zero, never NaN');
  assert.equal(old?.tokensReasoning, 0);
  assert.equal(old?.kind, 'review', 'everything written before `kind` existed was a review');
});

test('APastLine_IsPricedAtItsOwnModel — a row that changed model prices its earlier lines at the model that answered them', () => {
  // The Gemini row moved to flash-medium on 2026-10-02: every earlier flash-low line was priced as flash-medium.
  const prices = listed({
    'gemini-3.7-flash-low': { inPerMillion: 0.1, outPerMillion: 0.4, source: 'openrouter' },
    'gemini-3.7-flash-medium': { inPerMillion: 1, outPerMillion: 4, source: 'openrouter' },
  });
  const [row] = totalsByVendor(
    [
      line({ provider: 'gemini', model: 'gemini-3.7-flash-low' }),
      line({ provider: 'gemini', model: 'gemini-3.7-flash-medium' }),
    ],
    [vendor({ id: 'gemini', runtime: 'antigravity', model: 'gemini-3.7-flash-medium' })],
    prices,
  );

  near(row?.estimatedUsd ?? null, 1.1, 'one million at 0.1 plus one million at 1 — the row\'s CURRENT model priced both');
});

test('APastLine_IsPricedAtItsOwnModel — the row\'s TYPED rate still wins for its own lines, whatever model answered', () => {
  const [row] = totalsByVendor(
    [line({ model: 'gpt-6-astra' })],
    [vendor({ model: 'gpt-6-luna', pricePerMillionIn: 0.5, pricePerMillionOut: 1 })],
    listed({ 'gpt-6-astra': { inPerMillion: 9, outPerMillion: 9, source: 'openrouter' } }),
  );

  near(row?.estimatedUsd ?? null, 0.5, 'a typed rate is a fact about this account and must win over a list price');
});

test('CachedInput_IsPricedAtTheCachedRate — codex\'s cached tokens are a subset of its input and cost the cached rate', () => {
  const [row] = totalsByVendor(
    [line({ tokensIn: 1_000_000, tokensCached: 800_000, tokensOut: 0 })],
    [vendor({ model: 'gpt-6-luna' })],
    listed({ 'gpt-6-luna': { inPerMillion: 2, outPerMillion: 8, cachedPerMillion: 0.2, source: 'openrouter' } }),
  );

  // (1M − 0.8M) at 2 + 0.8M at 0.2 = 0.40 + 0.16
  near(row?.estimatedUsd ?? null, 0.56, 'every input token was priced at the full input rate');
});

test('CachedInput_IsPricedAtTheCachedRate — a list without a cached rate prices cached input at the input rate', () => {
  const [row] = totalsByVendor(
    [line({ tokensIn: 1_000_000, tokensCached: 800_000 })],
    [vendor({ model: 'gpt-6-luna' })],
    listed({ 'gpt-6-luna': { inPerMillion: 2, outPerMillion: 8, source: 'openrouter' } }),
  );

  near(row?.estimatedUsd ?? null, 2, 'a rate nobody stated is not zero');
});

test('ALongContextTier_AppliesPerLine — the tier is a per-REQUEST threshold, so it prices the one line that crossed it', () => {
  const tiered: ModelPrice = {
    inPerMillion: 3, outPerMillion: 15, source: 'litellm',
    tier: { fromTokens: 200_000, inPerMillion: 6, outPerMillion: 30, cachedPerMillion: 1 },
  };
  const [row] = totalsByVendor(
    [
      line({ provider: 'grok', model: 'grok-4.7', tokensIn: 150_000 }),
      line({ provider: 'grok', model: 'grok-4.7', tokensIn: 150_000 }),
      line({ provider: 'grok', model: 'grok-4.7', tokensIn: 300_000 }),
    ],
    [vendor({ id: 'grok', runtime: 'api', model: 'grok-4.7' })],
    listed({ 'grok-4.7': tiered }),
  );

  // 2 × 150K at 3 = 0.90, 300K at the tier's 6 = 1.80 — never 600K at 6 (a sum crosses every tier) nor all at 3.
  near(row?.estimatedUsd ?? null, 2.7, 'the long-context tier was not applied per line');
});

test('AReportedCost_IsNeverReEstimated — a billed line stays the bill; only the unbilled line beside it is estimated', () => {
  const [row] = totalsByVendor(
    [line({ provider: 'claude', model: 'claude-opus-5', costUsd: 0.42 }), line({ provider: 'claude', model: 'claude-opus-5' })],
    [vendor({ id: 'claude', runtime: 'claude', model: 'claude-opus-5' })],
    listed({ 'claude-opus-5': { inPerMillion: 3, outPerMillion: 15, source: 'openrouter' } }),
  );

  assert.equal(row?.costUsd, 0.42, 'the vendor\'s own figure is the fact');
  near(row?.estimatedUsd ?? null, 3, 'the billed line was re-estimated, or the unbilled one was dropped');
  assert.equal(spend(row!), '$0.4200 + ~$3.00', 'the bill and the estimate are shown apart, never summed into either');
});

test('AnUnpricedLine_MakesTheTotalAFloor — a model no list prices, or a line whose usage was not captured, marks the money ≥', () => {
  const prices = listed({ 'gpt-6-luna': { inPerMillion: 2, outPerMillion: 8, source: 'openrouter' } });
  const rates = [vendor({ model: 'gpt-6-luna' })];
  const [unpriced] = totalsByVendor([line(), line({ model: 'a-model-nobody-lists' })], rates, prices);
  const [uncaptured] = totalsByVendor([line(), line({ tokensIn: 0, usageNote: 'usage not captured', outcome: 'timeout' })], rates, prices);
  const [whole] = totalsByVendor([line()], rates, prices);
  const [nothing] = totalsByVendor([line({ model: 'a-model-nobody-lists' })], rates, prices);

  assert.equal(unpriced?.floor, true, 'an unpriced line was counted as free');
  near(unpriced?.estimatedUsd ?? null, 2, 'the priced part is still shown');
  assert.equal(spend(unpriced!), '≥ ~$2.00');
  assert.equal(uncaptured?.floor, true, 'a line whose usage was not captured was counted as free');
  assert.equal(spend(uncaptured!), '≥ ~$2.00');
  assert.equal(whole?.floor, false, 'a fully priced row is not a floor — the positive case the two above need');
  assert.equal(spend(whole!), '~$2.00');
  assert.equal(spend(nothing!), '—', 'nothing priced is a dash, never ≥ $0');
});
