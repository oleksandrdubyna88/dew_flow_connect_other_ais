import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ANTIGRAVITY_MODELS, modelsFor, modelsProvenance, parseAgyModels } from '../models';

/**
 * The antigravity model list is read from the CLI, not remembered from the day this was written.
 *
 * <p>Found by the operator on 2026-09-05: the dropdown offered Gemini 3.7 Flash and nothing newer,
 * while `agy models` on the same machine listed 3.8 first — along with 3.6 and a Pro (Low) the panel
 * had never heard of. The list was a hand-written constant, and the line under it said <i>"what
 * `agy models` lists for this subscription"</i>, which made a snapshot look like an answer.</p>
 *
 * <p>The fixture below is that machine's real output, kept verbatim: a test written from what the
 * parser expects would only prove the parser agrees with itself.</p>
 */

/** `agy models` on a Google AI Pro subscription, 2026-09-05, copied exactly. */
const AGY_OUTPUT = [
  'Fetching available models...',
  'gemini-3.8-flash-high\tGemini 3.8 Flash (High)',
  'gemini-3.8-flash-medium\tGemini 3.8 Flash (Medium)',
  'gemini-3.8-flash-low\tGemini 3.8 Flash (Low)',
  'gemini-3.7-flash-high\tGemini 3.7 Flash (High)',
  'gemini-3.7-flash-medium\tGemini 3.7 Flash (Medium)',
  'gemini-3.7-flash-low\tGemini 3.7 Flash (Low)',
  'gemini-3.6-flash-high\tGemini 3.6 Flash (High)',
  'gemini-3.6-flash-medium\tGemini 3.6 Flash (Medium)',
  'gemini-3.6-flash-low\tGemini 3.6 Flash (Low)',
  'gemini-3.1-pro-high\tGemini 3.1 Pro (High)',
  'gemini-3.1-pro-low\tGemini 3.1 Pro (Low)',
  'claude-sonnet-4-6\tClaude Sonnet 4.6 (Thinking)',
  'claude-opus-4-6-thinking\tClaude Opus 4.6 (Thinking)',
  'gpt-oss-120b-medium\tGPT-OSS 120B (Medium)',
  '',
].join('\n');

test('every model the CLI lists is offered, in its order', () => {
  const models = parseAgyModels(AGY_OUTPUT);

  assert.equal(models.length, 14);
  assert.deepEqual(models[0], { id: 'gemini-3.8-flash-high', label: 'Gemini 3.8 Flash (High)' });
  assert.ok(models.some((m) => m.id === 'gemini-3.6-flash-low'), 'including the ones the constant never had');
  assert.ok(models.some((m) => m.id === 'gemini-3.1-pro-low'));
});

test('what the CLI says before its list is not a model', () => {
  // "Fetching available models..." is one column, and a model called Fetching in the dropdown would
  // be the panel's own doing.
  const models = parseAgyModels(AGY_OUTPUT);

  assert.ok(!models.some((m) => m.id.startsWith('Fetching')), 'the greeting is not a model');
  assert.ok(!models.some((m) => m.label.length === 0), 'and neither is a blank line');
});

test('nothing at all from the CLI is no models, not a broken list', () => {
  assert.deepEqual(parseAgyModels(''), []);
  assert.deepEqual(parseAgyModels('agy: command not found'), []);
  assert.deepEqual(parseAgyModels('\t\n \t \n'), [], 'two empty columns are not a model either');
});

test('the discovered list is what the dropdown offers; the constant is only the fallback', () => {
  const discovered = parseAgyModels(AGY_OUTPUT);

  const offered = modelsFor('antigravity', [], '', undefined, discovered);
  assert.equal(offered[0]?.id, 'gemini-3.8-flash-high', 'the newest the CLI knows leads');

  const fallback = modelsFor('antigravity', [], '', undefined, []);
  assert.deepEqual(fallback.map((m) => m.id), ANTIGRAVITY_MODELS.map((m) => m.id));
});

test('a model already chosen never vanishes from its own dropdown', () => {
  // It is what the vendor is set to; a list that dropped it would silently change the setting.
  const offered = modelsFor('antigravity', [], 'gemini-9.9-flash-high', undefined, parseAgyModels(AGY_OUTPUT));

  assert.equal(offered[0]?.id, 'gemini-9.9-flash-high');
  assert.match(offered[0]?.label ?? '', /yours/);
});

test('the line under the dropdown says where the list actually came from', () => {
  // It used to claim the CLI's answer while offering a snapshot. That is what made a list a model
  // generation behind look like the truth.
  assert.match(modelsProvenance('antigravity', [], undefined, parseAgyModels(AGY_OUTPUT)), /14 models/);
  assert.match(modelsProvenance('antigravity', [], undefined, []), /did not answer/);
});
