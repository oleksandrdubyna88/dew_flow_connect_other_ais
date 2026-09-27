import assert from 'node:assert/strict';
import { test } from 'node:test';
import { liteLlmTable, openRouterTable, priceFor, priceKey, routeOf } from '../modelPrices';

/**
 * A model's price, looked up instead of typed.
 *
 * <p>Every shape below is a real row from a real answer, fetched on 2026-09-01 while writing this:
 * OpenRouter listed 419 models and LiteLLM 3408 entries, and between them they carry every model
 * this build offers.</p>
 */

const OPENROUTER = {
  data: [
    { id: 'google/gemini-3.7-flash', pricing: { prompt: '0.00000075', completion: '0.00000375' } },
    { id: 'google/gemini-3.7-flash:batch', pricing: { prompt: '0.0000001875', completion: '0.0000009375' } },
    { id: 'anthropic/claude-opus-4.6', pricing: { prompt: '0.000005', completion: '0.000025' } },
    { id: 'anthropic/claude-sonnet-4.6', pricing: { prompt: '0.000003', completion: '0.000015' } },
    { id: 'google/gemini-3.1-pro-preview', pricing: { prompt: '0.000002', completion: '0.000012' } },
    { id: 'openai/gpt-oss-120b', pricing: { prompt: '0.00000004', completion: '0.00000017' } },
    { id: 'openai/gpt-5.6-luna', pricing: { prompt: '0.0000002', completion: '0.0000012' } },
    { id: 'some/free-model', pricing: { prompt: '0', completion: '0' } },
  ],
};

const LITELLM = {
  'azure/gpt-5.6-sol': { input_cost_per_token: 5e-6, output_cost_per_token: 3e-5 },
  'azure/eu/gpt-5.6-sol': { input_cost_per_token: 5.5e-6, output_cost_per_token: 3.3e-5 },
  'azure/gpt-5.4': { input_cost_per_token: 2.5e-6, output_cost_per_token: 1.5e-5 },
  'gemini/gemini-3.7-flash': { input_cost_per_token: 7.5e-7, output_cost_per_token: 3.75e-6 },
  'some-context-only-entry': { max_input_tokens: 128000 },
};

test('a reasoning effort is not a different model', () => {
  // `-high` and `-low` are one model at two settings, priced identically, and no list has the
  // suffix. Failing to strip it is a lookup that always misses.
  assert.equal(priceKey('gemini-3.7-flash-high'), 'gemini-3.7-flash');
  assert.equal(priceKey('gemini-3.7-flash-low'), 'gemini-3.7-flash');
  assert.equal(priceKey('gpt-oss-120b-medium'), 'gpt-oss-120b');
});

test('a dashed version is the same model as a dotted one', () => {
  // This panel's ids came from a CLI's naming; the price lists use Anthropic's.
  assert.equal(priceKey('claude-opus-4-6-thinking'), 'claude-opus-4.6');
  assert.equal(priceKey('claude-sonnet-4-6'), 'claude-sonnet-4.6');
});

test('a vendor prefix is not part of the name', () => {
  assert.equal(priceKey('google/gemini-3.7-flash'), 'gemini-3.7-flash');
  assert.equal(priceKey('anthropic/claude-opus-4.6'), 'claude-opus-4.6');
});

test('the panel’s own model ids find their price', () => {
  const or = openRouterTable(OPENROUTER);
  const lite = liteLlmTable(LITELLM);

  assert.deepEqual(priceFor('gemini-3.7-flash-high', or, lite),
    { inPerMillion: 0.75, outPerMillion: 3.75, source: 'openrouter' });
  assert.deepEqual(priceFor('claude-opus-4-6-thinking', or, lite),
    { inPerMillion: 5, outPerMillion: 25, source: 'openrouter' });
  assert.deepEqual(priceFor('gpt-oss-120b-medium', or, lite),
    { inPerMillion: 0.04, outPerMillion: 0.17, source: 'openrouter' });
});

test('LiteLLM answers for the models OpenRouter does not list', () => {
  const or = openRouterTable(OPENROUTER);
  const lite = liteLlmTable(LITELLM);

  // `gpt-5.6-sol` is a codex model with no OpenRouter row; the price file has it under two
  // deployments at different regional rates.
  assert.deepEqual(priceFor('gpt-5.6-sol', or, lite),
    { inPerMillion: 5, outPerMillion: 30, source: 'litellm' });
});

test('a batch or free variant never becomes the price of the model', () => {
  // `:batch` is the same model at half rate. Taking it because it sorted first would quietly halve
  // every number on the page.
  const or = openRouterTable(OPENROUTER);

  assert.equal(or['gemini-3.7-flash']?.inPerMillion, 0.75);
  assert.equal(or['free-model'], undefined, 'an unpriced row says nothing about what a model costs');
});

test('a model neither list knows keeps its dash', () => {
  const or = openRouterTable(OPENROUTER);
  const lite = liteLlmTable(LITELLM);

  assert.equal(priceFor('some-model-nobody-publishes', or, lite), undefined);
});

test('an entry with no price at all is not a zero price', () => {
  // LiteLLM's file is mostly context windows; a row without costs must not become "free".
  const lite = liteLlmTable(LITELLM);

  assert.equal(lite['some-context-only-entry'], undefined);
});

test('junk in, nothing out', () => {
  for (const junk of [null, undefined, 42, 'text', {}, { data: 'no' }]) {
    assert.deepEqual(openRouterTable(junk), {});
  }
  for (const junk of [null, undefined, 42]) {
    assert.deepEqual(liteLlmTable(junk), {});
  }
});

// ---------------------------------------------------------------------------------------------
// S3.7 and §9.12 (PLAN_feature_review.md): the cached rate, the route, the tier, and `-max`.
// The rows below are the real answers of 2026-09-26 for our routes, trimmed to the fields read.
// ---------------------------------------------------------------------------------------------

const OPENROUTER_API = {
  data: [
    // OpenRouter's RESALE price for grok-4.7 — not what xAI bills the key.
    { id: 'x-ai/grok-4.7', pricing: { prompt: '0.0000016', completion: '0.0000048', input_cache_read: '0.0000004' } },
  ],
};

const LITELLM_API = {
  'xai/grok-4.7': {
    input_cost_per_token: 0.000002, output_cost_per_token: 0.000006, cache_read_input_token_cost: 5e-7,
    input_cost_per_token_above_200k_tokens: 0.000004, output_cost_per_token_above_200k_tokens: 0.000012,
    cache_read_input_token_cost_above_200k_tokens: 0.000001,
  },
  'dashscope/qwen3.8-max': { input_cost_per_token: 0.000002, output_cost_per_token: 0.000006, cache_read_input_token_cost: 2.5e-7 },
  'dashscope/deepseek-v4-pro': { input_cost_per_token: 0.0000024, output_cost_per_token: 0.0000048, cache_read_input_token_cost: 2e-7 },
  'zai/glm-5.3': { input_cost_per_token: 0.0000014, output_cost_per_token: 0.0000044, cache_read_input_token_cost: 2.6e-7 },
};

test('-max is part of a model name, not an effort to strip (§9.12)', () => {
  // `qwen3.8-max` became `qwen3.8`, which no list prices, so a model both lists carry showed a dash.
  assert.equal(priceKey('qwen3.8-max'), 'qwen3.8-max');
  assert.equal(priceKey('gemini-3.7-flash-high'), 'gemini-3.7-flash', 'a real effort is still stripped');
});

test('qwen3.8-max finds its price, cached rate included', () => {
  const price = priceFor('qwen3.8-max', openRouterTable({ data: [] }), liteLlmTable(LITELLM_API), 'dashscope');

  assert.equal(price?.inPerMillion, 2);
  assert.equal(price?.cachedPerMillion, 0.25);
  assert.equal(price?.outPerMillion, 6);
});

test('a grok row on the xAI endpoint gets xAI’s price, not OpenRouter’s resale price', () => {
  const or = openRouterTable(OPENROUTER_API);
  const lite = liteLlmTable(LITELLM_API);

  const routed = priceFor('grok-4.7', or, lite, routeOf('https://api.x.ai/v1'));
  assert.deepEqual(routed, {
    inPerMillion: 2, outPerMillion: 6, cachedPerMillion: 0.5, source: 'litellm',
    tier: { fromTokens: 200_000, inPerMillion: 4, outPerMillion: 12, cachedPerMillion: 1 },
  });

  // With no route the order is what it always was — OpenRouter first — and its cached rate is read too.
  assert.deepEqual(priceFor('grok-4.7', or, lite), { inPerMillion: 1.6, outPerMillion: 4.8, cachedPerMillion: 0.4, source: 'openrouter' });
});

test('an endpoint names its provider, and an unknown one names none', () => {
  assert.equal(routeOf('https://api.x.ai/v1'), 'xai');
  assert.equal(routeOf('https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1'), 'dashscope');
  assert.equal(routeOf('https://dashscope-intl.aliyuncs.com/compatible-mode/v1'), 'dashscope');
  assert.equal(routeOf('https://api.z.ai/api/paas/v4'), 'zai');
  assert.equal(routeOf('https://openrouter.ai/api/v1'), '');
  assert.equal(routeOf('not a url'), '');
  assert.equal(routeOf(''), '');
});

test('a routed model the provider does not list falls back rather than inventing one', () => {
  // glm-5.3 has no dashscope row (measured): the provider-agnostic entry answers, and it is zai's.
  const price = priceFor('glm-5.3', openRouterTable({ data: [] }), liteLlmTable(LITELLM_API), 'dashscope');

  assert.equal(price?.inPerMillion, 1.4);
  assert.equal(price?.cachedPerMillion, 0.26);
});
