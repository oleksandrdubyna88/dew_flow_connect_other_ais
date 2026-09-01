import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULTS, envBlock, settingsFrom } from '../settingsShape';
import { DEFAULT_VENDORS, normaliseId, Vendor, vendorsEnv, vendorsFrom } from '../vendors';

/** A reader over a plain object, as VS Code's configuration behaves for our purposes. */
const reader = (values: Record<string, unknown>) => (section: string) => values[section];

test('defaults match the master plan configuration table', () => {
  assert.equal(DEFAULTS.rounds['PlanCritique'], 3);
  assert.equal(DEFAULTS.rounds['Architecture'], 2, 'a code role gets two passes by default');
  assert.equal(DEFAULTS.thresholds['PlanCritique'], 2);
  assert.equal(DEFAULTS.thresholds['Architecture'], 3, 'a diff carries more than a plan does');
  assert.equal(DEFAULTS.onExhausted, 'human');
  assert.equal(DEFAULTS.maxConcurrency, 3);
  assert.equal(DEFAULTS.maxPerProvider, 2);
  assert.equal(DEFAULTS.reviewerTimeoutMinutes, 10);
  assert.equal(DEFAULTS.escalationMinutes, 30);
  assert.equal(DEFAULTS.credsKey, '');
});

test('the shipped reviewers are the two whose CLIs authenticate themselves', () => {
  assert.deepEqual(
    DEFAULT_VENDORS.map((v) => v.id),
    ['codex', 'antigravity'],
  );
  assert.ok(DEFAULT_VENDORS.every((v) => v.enabled && v.baseUrl === ''));
});

test('an empty configuration reads as the defaults', () => {
  const settings = settingsFrom(reader({}));
  assert.equal(settings.rounds['PlanCritique'], DEFAULTS.rounds['PlanCritique']);
  assert.equal(settings.onExhausted, DEFAULTS.onExhausted);
  assert.deepEqual(vendorsFrom(undefined), [...DEFAULT_VENDORS]);
});

test('invalid values fall back rather than reaching the server', () => {
  const settings = settingsFrom(
    reader({ rounds: { PlanCritique: 0 }, thresholds: { PlanCritique: -1 }, onExhausted: 'panic', maxConcurrency: 'three' }),
  );
  assert.equal(settings.rounds['PlanCritique'], DEFAULTS.rounds['PlanCritique'], '0 rounds would gate nothing');
  assert.equal(settings.thresholds['PlanCritique'], DEFAULTS.thresholds['PlanCritique']);
  assert.equal(settings.onExhausted, DEFAULTS.onExhausted);
  assert.equal(settings.maxConcurrency, DEFAULTS.maxConcurrency);
});

test('a threshold of zero is legitimate and survives', () => {
});

test('a stored vendor list that names nothing runnable is an accident, not a configuration', () => {
  assert.deepEqual(vendorsFrom([{ runtime: 'codex' }]), [...DEFAULT_VENDORS], 'no id, nothing to run');
  assert.deepEqual(vendorsFrom('codex,gemini'), [...DEFAULT_VENDORS], 'not a list at all');
  assert.deepEqual(vendorsFrom([]), [...DEFAULT_VENDORS]);
});

test('a vendor is read with its runtime, model, endpoint and switch', () => {
  const vendors = vendorsFrom([
    { id: 'Mistral', runtime: 'codex', model: 'mistral-large', enabled: false, baseUrl: 'https://api.mistral.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ]);
  assert.deepEqual(vendors, [
    { id: 'mistral', runtime: 'codex', model: 'mistral-large', enabled: false, baseUrl: 'https://api.mistral.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ]);
});

test('an unknown runtime falls back to codex, which is the one that takes a base URL', () => {
  assert.equal(vendorsFrom([{ id: 'x', runtime: 'llama.cpp' }])[0]?.runtime, 'codex');
});

test('two rows with one name would fight over the same key, so the second is dropped', () => {
  const vendors = vendorsFrom([
    { id: 'codex', runtime: 'codex', model: 'a' },
    { id: 'codex', runtime: 'gemini', model: 'b' },
  ]);
  assert.equal(vendors.length, 1);
  assert.equal(vendors[0]?.model, 'a');
});

test('a typed name becomes something usable as an id, an env suffix and a vault key', () => {
  assert.equal(normaliseId('  Mistral AI  '), 'mistral-ai');
  assert.equal(normaliseId('GPT/4!'), 'gpt-4');
  assert.equal(normaliseId('   '), '');
});

test('a pristine configuration produces NO env at all', () => {
  assert.deepEqual(envBlock(settingsFrom(reader({})), DEFAULT_VENDORS), {});
});

test('only what differs from the defaults reaches the env block', () => {
  const settings = settingsFrom(
    reader({ rounds: { ...DEFAULTS.rounds, PlanCritique: 5 }, onExhausted: 'escalate', credsKey: 'cfg-key' }),
  );
  assert.deepEqual(envBlock(settings, DEFAULT_VENDORS), {
    COAI_ROUNDS_PLANCRITIQUE: '5',
    COAI_ON_EXHAUSTED: 'escalate',
    COAI_CREDS_KEY: 'cfg-key',
  });
});

test('a changed reviewer list travels as JSON, which a comma list could not carry', () => {
  const vendors: Vendor[] = [
    { id: 'gemini', runtime: 'gemini', model: 'gemini-pro-latest', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
    { id: 'mistral', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.mistral.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ];
  const env = envBlock(settingsFrom(reader({})), vendors);
  const parsed = JSON.parse(env['COAI_VENDORS'] ?? '[]') as { id: string; runtime: string; baseUrl: string }[];
  assert.deepEqual(
    parsed.map((v) => v.id),
    ['gemini', 'mistral'],
  );
  assert.equal(parsed[1]?.baseUrl, 'https://api.mistral.ai/v1');
});

test('a switched-off reviewer is not sent at all', () => {
  const parsed = JSON.parse(
    vendorsEnv([
      { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      { id: 'gemini', runtime: 'gemini', model: '', enabled: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
    ]),
  ) as { id: string }[];
  assert.deepEqual(
    parsed.map((v) => v.id),
    ['codex'],
  );
});


test('good enough is a real choice, not a string the reader drops', () => {
  // The fourth thing to do when the rounds run out: read the findings, apply what is true and
  // useful, and move on. `continue` proceeds and touches nothing, which is how a gate becomes
  // decoration — so an unknown-value reader silently turning this back into 'human' would be the
  // whole feature quietly missing.
  assert.equal(settingsFrom(reader({ onExhausted: 'good_enough' })).onExhausted, 'good_enough');
  assert.equal(settingsFrom(reader({ onExhausted: 'panic' })).onExhausted, DEFAULTS.onExhausted);
});

test('the choice travels to the server under the name the server parses', () => {
  const env = envBlock({ ...DEFAULTS, onExhausted: 'good_enough' });
  assert.equal(env['COAI_ON_EXHAUSTED'], 'good_enough');
});

test('the manifest’s own default vendor list matches the one the code ships', () => {
  // The manifest still defaulted to codex + the RETIRED gemini, and carried none of the fields
  // added since — so a fresh install read its reviewers from a list the product had moved past.
  // Two copies of one default is one copy too many; this is the test that keeps them equal.
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: { properties: Record<string, { default?: unknown }> } };
  };
  const shipped = manifest.contributes.configuration.properties['coai.vendors']!.default;

  assert.deepEqual(shipped, DEFAULT_VENDORS.map((v) => ({ ...v })));
});
