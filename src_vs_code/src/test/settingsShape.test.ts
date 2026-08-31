import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CoaiSettings, DEFAULTS, envBlock, settingsFrom } from '../settingsShape';

/** A reader over a plain object, as VS Code's configuration behaves for our purposes. */
const reader = (values: Record<string, unknown>) => (section: string) => values[section];

test('defaults match the master plan configuration table', () => {
  assert.deepEqual(DEFAULTS.providers, ['codex', 'gemini']);
  assert.equal(DEFAULTS.maxRounds, 3);
  assert.equal(DEFAULTS.gateThreshold, 2);
  assert.equal(DEFAULTS.onExhausted, 'human');
  assert.equal(DEFAULTS.maxConcurrency, 3);
  assert.equal(DEFAULTS.maxPerProvider, 2);
  assert.equal(DEFAULTS.reviewerTimeoutMinutes, 10);
  assert.equal(DEFAULTS.credsKey, '');
});

test('an empty configuration reads as the defaults', () => {
  const settings = settingsFrom(reader({}));
  assert.deepEqual(settings.providers, DEFAULTS.providers);
  assert.equal(settings.maxRounds, DEFAULTS.maxRounds);
  assert.equal(settings.onExhausted, DEFAULTS.onExhausted);
});

test('invalid values fall back rather than reaching the server', () => {
  const settings = settingsFrom(
    reader({
      providers: ['codex', 'mistral'],
      maxRounds: 0,
      gateThreshold: -1,
      onExhausted: 'panic',
      maxConcurrency: 'three',
    }),
  );
  assert.deepEqual(settings.providers, ['codex'], 'an unknown vendor is dropped, the known one kept');
  assert.equal(settings.maxRounds, DEFAULTS.maxRounds, '0 rounds would gate nothing');
  assert.equal(settings.gateThreshold, DEFAULTS.gateThreshold);
  assert.equal(settings.onExhausted, DEFAULTS.onExhausted);
  assert.equal(settings.maxConcurrency, DEFAULTS.maxConcurrency);
});

test('a threshold of zero is legitimate and survives', () => {
  assert.equal(settingsFrom(reader({ gateThreshold: 0 })).gateThreshold, 0);
});

test('an all-invalid provider list keeps the defaults rather than disabling review', () => {
  assert.deepEqual(settingsFrom(reader({ providers: ['mistral'] })).providers, DEFAULTS.providers);
});

test('a pristine configuration produces NO env at all', () => {
  assert.deepEqual(envBlock(settingsFrom(reader({}))), {});
});

test('only what differs from the defaults reaches the env block', () => {
  const settings = settingsFrom(
    reader({ maxRounds: 5, onExhausted: 'escalate', 'model.codex': 'gpt-5.3-codex', credsKey: 'cfg-key' }),
  );
  assert.deepEqual(envBlock(settings), {
    COAI_MAX_ROUNDS: '5',
    COAI_ON_EXHAUSTED: 'escalate',
    COAI_MODEL_CODEX: 'gpt-5.3-codex',
    COAI_CREDS_KEY: 'cfg-key',
  });
});

test('a model for a disabled provider is not sent', () => {
  const settings: CoaiSettings = settingsFrom(
    reader({ providers: ['gemini'], 'model.deepseek': 'deepseek-chat' }),
  );
  assert.equal(envBlock(settings)['COAI_MODEL_DEEPSEEK'], undefined);
  assert.equal(envBlock(settings)['COAI_PROVIDERS'], 'gemini');
});
