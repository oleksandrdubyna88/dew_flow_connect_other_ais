import assert from 'node:assert/strict';
import { test } from 'node:test';

import { askedOrRefused, asksAnEndpoint, EndpointListing, listingFor, listingOf, RowEndpoint } from '../endpointModels';
import { modelsFor, modelsProvenance } from '../models';

/**
 * A reviewer on SOMEBODY ELSE'S endpoint — the Codex CLI pointed at OpenRouter, a hosted `api` row — is
 * offered that endpoint's models, never the Codex CLI's cache.
 *
 * <p>Reported from a screenshot on 2026-10-01: a `codex` row with base URL `https://openrouter.ai/api/v1`
 * listed "10 models the Codex CLI has cached for this machine" — OpenAI's own slugs, none of which
 * OpenRouter accepts — because `modelsFor` chose the list by runtime alone. The `api` runtime already had
 * the right decision; this pins it as ONE decision for every endpoint row
 * (research/PLAN_custom_endpoint_model_list.md).</p>
 */

const CODEX_CACHE = [{ id: 'gpt-6-luna', label: 'GPT-6 Luna' }, { id: 'gpt-6-astra', label: 'GPT-6 Astra' }];
const OPENROUTER = 'https://openrouter.ai/api/v1';

function listing(overrides: Partial<EndpointListing> = {}): EndpointListing {
  return {
    baseUrl: OPENROUTER,
    keyName: 'openrouter',
    ids: ['deepseek/deepseek-chat', 'openai/gpt-5'],
    reason: '',
    askedUtc: '2026-10-01T14:05:09.000Z',
    ...overrides,
  };
}

function endpoint(overrides: Partial<RowEndpoint> = {}): RowEndpoint {
  return { baseUrl: OPENROUTER, keyName: 'openrouter', ...overrides };
}

const ids = (models: readonly { id: string }[]): string[] => models.map((m) => m.id);

test('a codex row on another endpoint is never offered the Codex CLI cache', () => {
  assert.deepEqual(ids(modelsFor('codex', CODEX_CACHE, '', undefined, [], [], undefined, '', endpoint())), [],
    'OpenAI slugs are not names OpenRouter accepts');
  assert.deepEqual(ids(modelsFor('codex', CODEX_CACHE, 'deepseek/deepseek-chat', undefined, [], [], undefined, '', endpoint())),
    ['deepseek/deepseek-chat'], 'the saved model is kept, and is the whole list until the endpoint is asked');
});

test('a codex row with no endpoint of its own still gets the Codex CLI cache', () => {
  assert.deepEqual(ids(modelsFor('codex', CODEX_CACHE, '', undefined, [], [], undefined, '', endpoint({ baseUrl: '' }))),
    ['gpt-6-luna', 'gpt-6-astra']);
  assert.deepEqual(ids(modelsFor('codex', CODEX_CACHE, '')), ['gpt-6-luna', 'gpt-6-astra'], 'and so does a caller that passes none');
});

test('what the endpoint listed becomes the list', () => {
  const offered = modelsFor('codex', CODEX_CACHE, 'openai/gpt-5', undefined, [], [], undefined, '', endpoint({ listed: listing() }));

  assert.deepEqual(ids(offered), ['deepseek/deepseek-chat', 'openai/gpt-5']);
});

test('a saved model the endpoint did not list is kept, and marked as such', () => {
  const offered = modelsFor('codex', CODEX_CACHE, 'mistral/large', undefined, [], [], undefined, '', endpoint({ listed: listing() }));

  assert.deepEqual(ids(offered), ['mistral/large', 'deepseek/deepseek-chat', 'openai/gpt-5'],
    'dropping it would switch the reviewer to another model in silence');
  assert.match(offered[0]?.label ?? '', /not in what openrouter\.ai listed/u);
});

test('an api row and a codex row on an endpoint are one decision', () => {
  for (const runtime of ['api', 'codex'] as const) {
    assert.deepEqual(ids(modelsFor(runtime, CODEX_CACHE, 'x/y', undefined, [], [], undefined, '', endpoint({ listed: listing() }))),
      ['x/y', 'deepseek/deepseek-chat', 'openai/gpt-5'], runtime);
  }
  assert.ok(asksAnEndpoint('api', ''), 'an api row asks an endpoint even before it has one');
  assert.ok(asksAnEndpoint('codex', OPENROUTER));
  assert.ok(!asksAnEndpoint('codex', '   '), 'whitespace is no endpoint');
  assert.ok(!asksAnEndpoint('gemini', OPENROUTER), 'only the runtimes that speak to an endpoint');
});

test('an answer is only used while the row still has the base URL AND the key it was asked with', () => {
  assert.equal(listingFor(endpoint({ listed: listing() }))?.ids.length, 2);
  assert.equal(listingFor(endpoint({ baseUrl: 'https://api.deepseek.com/v1', listed: listing() })), undefined,
    'another endpoint was not asked');
  assert.equal(listingFor(endpoint({ keyName: 'openrouter-2', listed: listing() })), undefined,
    'another key may call other models');
  assert.deepEqual(ids(modelsFor('codex', CODEX_CACHE, '', undefined, [], [], undefined, '', endpoint({ keyName: 'other', listed: listing() }))),
    [], 'a stale answer offers nothing rather than another key’s models');
});

test('the caption says where the list came from, in each of its states', () => {
  const never = modelsProvenance('codex', CODEX_CACHE, undefined, [], undefined, undefined, false, endpoint());
  assert.match(never, /openrouter\.ai is not asked until you press ≡/u);
  assert.doesNotMatch(never, /Codex CLI has cached/u, 'the caption is about the endpoint, not the CLI');

  const asking = modelsProvenance('codex', CODEX_CACHE, undefined, [], undefined, undefined, false, endpoint({ asking: true }));
  assert.match(asking, /asking openrouter\.ai/u);

  const answered = modelsProvenance('codex', CODEX_CACHE, undefined, [], undefined, undefined, false, endpoint({ listed: listing() }));
  assert.match(answered, /2 models openrouter\.ai listed for the key under 'openrouter', asked 14:05 UTC/u);

  const refused = modelsProvenance('codex', CODEX_CACHE, undefined, [], undefined, undefined, false,
    endpoint({ listed: listing({ ids: [], reason: 'the vault holds no key under openrouter' }) }));
  assert.match(refused, /openrouter\.ai did not list its models: the vault holds no key under openrouter/u);
  assert.match(refused, /≡/u, 'and says how to ask again');
});

test('an ask is kept with what it was asked with — a refusal too, so the card can say why', () => {
  const at = new Date('2026-10-01T14:05:09.000Z');

  assert.deepEqual(listingOf({ baseUrl: OPENROUTER, keyName: 'openrouter' }, { ids: ['a/b'], reason: '' }, at),
    { baseUrl: OPENROUTER, keyName: 'openrouter', ids: ['a/b'], reason: '', askedUtc: '2026-10-01T14:05:09.000Z' });
  const refused = listingOf({ baseUrl: OPENROUTER, keyName: 'openrouter' }, { ids: [], reason: 'exit 78' }, at);
  assert.equal(refused.reason, 'exit 78');
  assert.match(modelsProvenance('codex', CODEX_CACHE, undefined, [], undefined, undefined, false, endpoint({ listed: refused })),
    /did not list its models: exit 78/u, 'a kept refusal is said, not shown as "not asked"');
});

test('a plain codex row keeps its own caption', () => {
  assert.match(modelsProvenance('codex', CODEX_CACHE), /2 models the Codex CLI has cached/u);
});

test('a probe that THROWS is kept as a refusal with its reason, never as "not asked"', async () => {
  // Code round (codex and gemini): `modelsForKey` answers a reason rather than throwing, but a spawn that
  // fails before it, or a progress notification that rejects, used to fly out of the command — the card
  // went back to "not asked" and nobody was told why.
  const kept = await askedOrRefused(() => Promise.reject(new Error('spawn coai-mcp ENOENT')));

  assert.deepEqual(kept, { ids: [], reason: 'spawn coai-mcp ENOENT' });
  assert.deepEqual(await askedOrRefused(() => Promise.reject('not an Error')), { ids: [], reason: 'not an Error' });
  assert.deepEqual(await askedOrRefused(() => Promise.resolve({ ids: ['a/b'], reason: '' })), { ids: ['a/b'], reason: '' },
    'an answer passes through untouched');
});
