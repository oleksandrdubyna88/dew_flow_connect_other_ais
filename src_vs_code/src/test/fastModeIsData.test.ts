import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { hasFastTier } from '../featureAvailability';
import { FAST_MODE } from '../featureAvailability.generated';
import type { Runtime } from '../models';

/**
 * Which runtime and model has a fast tier is DATA (todo/PLAN_fast_mode.md, decision 2): `shared/feature-availability.json`
 * generated into the extension, read by coai-mcp from the same file — so the card offers a state exactly where the launch
 * sends one. The C# twin is `FastModeIsDataTests`; the cases are the same.
 */

const CASES: readonly (readonly [Runtime, string, boolean])[] = [
  ['codex', 'gpt-6.1-sol', true],
  ['codex', '', true],
  ['claude', 'opus', true],
  ['claude', 'claude-opus-5-5[1m]', true],
  ['claude', 'claude-sonnet-5', false],
  ['claude', '', false],
  ['antigravity', 'gemini-3.7-flash-high', false],
  ['local', 'qwen', false],
  ['api', 'grok-4', false],
];

test('the file says which model has the tier, as coai-mcp reads it', () => {
  for (const [runtime, model, has] of CASES) {
    assert.equal(hasFastTier(runtime, model), has, `${runtime} ${model}`);
  }
});

test('the generated rows are the shared file\'s, one per runtime, each saying why', () => {
  const seed = JSON.parse(readFileSync(join(__dirname, '..', '..', '..', 'shared', 'feature-availability.json'), 'utf8')) as { fastMode: readonly { runtime: string }[] };

  assert.deepEqual(FAST_MODE.map((row) => row.runtime), seed.fastMode.map((row) => row.runtime));
  assert.ok(FAST_MODE.every((row) => row.note.length > 0));
});
