import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CoaiSettings, DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { serverSettingsJson } from '../serverSettingsFile';

/**
 * No setting may be silently dropped on its way to the server.
 *
 * <p>The env block writes only what DIFFERS from the defaults, which keeps a pristine config
 * empty and readable — and makes forgetting one field invisible: the panel saves, the file has no
 * key, the server uses its own default, and nothing anywhere says so. This walks every field.</p>
 */
const CHANGED: { readonly [K in keyof CoaiSettings]: CoaiSettings[K] } = {
  maxRounds: 5,
  gateThreshold: 1,
  onExhausted: 'escalate',
  maxConcurrency: 7,
  maxPerProvider: 4,
  reviewerTimeoutMinutes: 12,
  credsKey: 'coai-key',
  language: 'ru',
  translator: { provider: 'claude', model: 'haiku' },
  escalationMinutes: 45,
  promptsPerRound: { SecurityReliability: ['sec-attack'] },
  rotatePrompts: true,
};

test('every setting, changed on its own, reaches the server file', () => {
  for (const field of Object.keys(DEFAULTS) as (keyof CoaiSettings)[]) {
    const settings = { ...DEFAULTS, [field]: CHANGED[field] } as CoaiSettings;
    const env = envBlock(settings, DEFAULT_VENDORS);
    const pristine = envBlock(DEFAULTS, DEFAULT_VENDORS);

    assert.notDeepEqual(
      env,
      pristine,
      `changing ${field} produced an identical env block — the setting would never reach the server`,
    );
  }
});

test('a pristine configuration writes nothing, so the block stays readable', () => {
  assert.deepEqual(envBlock(DEFAULTS, DEFAULT_VENDORS), {});
});

test('the file the panel writes is the shape the server parses', () => {
  const written = JSON.parse(serverSettingsJson({ ...DEFAULTS, ...CHANGED }, DEFAULT_VENDORS)) as Record<string, string>;

  // Keys are env names and values are STRINGS, because the server has one parser for the file and
  // the environment; a second encoding would be a second thing to keep in step.
  for (const [key, value] of Object.entries(written)) {
    assert.match(key, /^COAI_[A-Z_]+$/, `${key} is not an env-shaped key`);
    assert.equal(typeof value, 'string', `${key} must be a string, the env carries no other type`);
  }
  assert.equal(written['COAI_ROTATE_PROMPTS'], 'true');
  assert.deepEqual(JSON.parse(written['COAI_PROMPTS_PER_ROUND']!), { SecurityReliability: ['sec-attack'] });
  assert.equal(written['COAI_LANGUAGE'], 'ru');
});

test('a vendor added in the panel travels with its runtime and model', () => {
  const written = JSON.parse(
    serverSettingsJson(DEFAULTS, [{ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, baseUrl: '' }]),
  ) as Record<string, string>;

  const vendors = JSON.parse(written['COAI_VENDORS']!) as { id: string; runtime: string; model: string }[];
  assert.equal(vendors[0]!.id, 'antigravity');
  assert.equal(vendors[0]!.runtime, 'antigravity', 'a runtime that does not travel runs the wrong vendor');
  assert.equal(vendors[0]!.model, 'gemini-3.7-flash-high');
});

test('a stored vendor keeps its runtime through the parser, not only through the type', () => {
  // This is the path that actually runs: VS Code hands back whatever JSON is stored, and
  // `vendorsFrom` decides what it means. A runtime the parser does not recognise falls back to
  // codex — which silently runs the wrong vendor's model under the right vendor's name.
  const stored = [{ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, baseUrl: '' }];

  const parsed = vendorsFrom(stored);

  assert.equal(parsed[0]!.runtime, 'antigravity', 'the parser dropped a runtime this build supports');
  assert.equal(parsed[0]!.model, 'gemini-3.7-flash-high');
});

test('a runtime nobody knows still falls back to codex, which is the one that takes a base URL', () => {
  assert.equal(vendorsFrom([{ id: 'x', runtime: 'llama.cpp', baseUrl: 'https://x/v1' }])[0]!.runtime, 'codex');
});
