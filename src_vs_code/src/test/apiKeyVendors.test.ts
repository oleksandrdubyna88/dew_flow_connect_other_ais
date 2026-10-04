import assert from 'node:assert/strict';
import { test } from 'node:test';

import { API_KEY_PRESETS, presetEndpoint, probeModels, vaultKeyItems, vaultKeyRow } from '../apiKeyVendors';
import { parseProviderNotes } from '../providers';
import { Vendor, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';

/**
 * S3.6 of PLAN_feature_review.md: the vault's keys are vendors in "Add a reviewer".
 *
 * <p>The operator's words: list every provider that is available, plus one entry per key name in the
 * vault's config entry, shown with a leading `!` because it is an API key — `!grok`, `!qwen`. The
 * server reports the NAMES; the values never leave it, and nothing on this side could hold one.</p>
 */

const KEY_VALUE = 'sk-live-0123456789abcdefghijklmnopqrstuv';

function row(overrides: Partial<Vendor> = {}): Vendor {
  return {
    id: 'grok', runtime: 'api', model: 'grok-4.7', enabled: true, plan: true, code: true,
    baseUrl: 'https://api.x.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
    ...overrides,
  };
}

test('no key value ever crosses: only names are read off the providers answer', () => {
  // What a defective or hostile server might send: a map of names to values, and junk beside a name.
  const notes = parseProviderNotes(JSON.stringify({
    providers: [],
    vaultNote: '2 vendor key(s) loaded',
    vaultRead: true,
    vaultKeyNames: ['Grok', 'qwen', { grok: KEY_VALUE }, KEY_VALUE.replace(/-/gu, ' '), 42, 'qwen'],
  }));

  assert.deepEqual(notes.vaultKeys, ['grok', 'qwen']);
  assert.ok(!JSON.stringify(notes).includes('0123456789abcdef'), 'nothing value-shaped is kept');
});

test('the ! entries appear only for names in the vault, after nothing else, in the vault’s order', () => {
  const items = vaultKeyItems({ vaultKeys: ['grok', 'qwen'], vaultNote: '2 vendor key(s) loaded', vault: 'read' }, true, []);

  assert.deepEqual(items.map((one) => one.label), ['!grok', '!qwen']);
  assert.deepEqual(items.map((one) => one.keyName), ['grok', 'qwen']);
  assert.match(items[0]?.detail ?? '', /api\.x\.ai/u, 'a name with a preset says where it goes');
});

test('a name with no preset says the base URL will be asked for', () => {
  const [item] = vaultKeyItems({ vaultKeys: ['mistral'], vaultNote: '', vault: 'read' }, true, []);

  assert.equal(item?.label, '!mistral');
  assert.match(item?.detail ?? '', /base URL/u);
});

test('a key already used by a row is still listed — a second model on one key', () => {
  const items = vaultKeyItems(
    { vaultKeys: ['qwen'], vaultNote: '', vault: 'read' }, true, [row({ id: 'qwen', vaultKeyName: 'qwen' })]);

  assert.equal(items.length, 1);
  assert.equal(items[0]?.keyName, 'qwen');
  assert.match(items[0]?.description ?? '', /qwen-2/u, 'it says which id the second row takes');
});

test('a vault that cannot be read lists nothing and says why', () => {
  const items = vaultKeyItems(
    { vaultKeys: [], vaultNote: 'creds config refused (exit 1) — the key may be revoked', vault: 'unreadable' }, true, []);

  assert.equal(items.filter((one) => one.keyName.length > 0).length, 0);
  assert.equal(items.length, 1);
  assert.match(items[0]?.detail ?? '', /refused/u);
});

test('a server too old to name the vault’s keys is said so, not shown as an empty vault', () => {
  const unanswered = vaultKeyItems({ vaultKeys: [], vaultNote: '', vault: 'unreadable' }, false, []);
  const old = vaultKeyItems(parseProviderNotes(JSON.stringify({ providers: [], vaultNote: '1 vendor key(s) loaded' })), true, []);

  assert.equal(unanswered.length, 1);
  assert.equal(old.length, 1);
  assert.match(old[0]?.detail ?? '', /update/iu);
});

test('a readable vault with no keys adds nothing to the list', () => {
  assert.deepEqual(vaultKeyItems({ vaultKeys: [], vaultNote: '0 vendor key(s) loaded', vault: 'read' }, true, []), []);
});

test('choosing !qwen writes an api row whose key is qwen, and the key name crosses to the server', () => {
  const chosen = vaultKeyRow('qwen', presetEndpoint('qwen'), 'qwen3.8-max', new Set(['codex']));

  assert.equal(chosen.id, 'qwen');
  assert.equal(chosen.runtime, 'api');
  assert.equal(chosen.vaultKeyName, 'qwen');
  assert.equal(chosen.model, 'qwen3.8-max');
  assert.equal(chosen.baseUrl, 'https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1');

  const [stored] = vendorsFrom([chosen]);
  assert.equal(stored?.vaultKeyName, 'qwen', 'the key name survives being saved and read back');
  const wire = JSON.parse(vendorsEnv([chosen])) as Record<string, unknown>[];
  assert.equal(wire[0]?.['key'], 'qwen');
});

test('a second row on one key takes the next free id and keeps the key’s name', () => {
  const second = vaultKeyRow('qwen', presetEndpoint('qwen'), 'deepseek-v4-pro', new Set(['qwen']));

  assert.equal(second.id, 'qwen-2');
  assert.equal(second.vaultKeyName, 'qwen');
});

test('the presets are the two measured endpoints and nothing else', () => {
  assert.equal(presetEndpoint('grok'), 'https://api.x.ai/v1');
  assert.equal(presetEndpoint('GROK'), 'https://api.x.ai/v1');
  assert.equal(presetEndpoint('mistral'), '');
  assert.deepEqual(Object.keys(API_KEY_PRESETS).sort(), ['grok', 'qwen']);
});

test('the model list comes from the endpoint: the probe’s GET /models ids', () => {
  const answered = probeModels(JSON.stringify({
    vendor: 'qwen', models: { status: 200, ids: ['qwen3.8-max', 'deepseek-v4-pro', 7], error: '' }, requests: [],
  }));

  assert.deepEqual(answered, { ids: ['qwen3.8-max', 'deepseek-v4-pro'], reason: '' });
});

test('an endpoint that refused the list is a reason, never an empty list pretending to be one', () => {
  const refused = probeModels(JSON.stringify({ models: { status: 401, ids: [], error: 'bad key' } }));
  const junk = probeModels('not json');

  assert.deepEqual(refused.ids, []);
  assert.match(refused.reason, /401/u);
  assert.deepEqual(junk.ids, []);
  assert.ok(junk.reason.length > 0);
});
