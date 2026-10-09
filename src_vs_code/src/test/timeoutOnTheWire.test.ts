import assert from 'node:assert/strict';
import { test } from 'node:test';
import { vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';

/**
 * A CLI row's own timeout crosses to coai-mcp only when the installed binary lists `timeoutMinutes` in `--features`
 * (research/PLAN_one_model_catalog.md, epic 2, story 2) — an older binary skips the member and the row would silently run
 * on the round's timeout.
 */

const ROWS = vendorsFrom([
  { id: 'claude', runtime: 'claude', model: '', enabled: true, timeoutMinutes: 25 },
  { id: 'qwen', runtime: 'api', model: 'qwen-max', baseUrl: 'https://q.example/v1', enabled: true, timeoutMinutes: 25 },
]);

const crossed = (features: readonly string[]): readonly unknown[] =>
  (JSON.parse(vendorsEnv(ROWS, '', () => undefined, features)) as Record<string, unknown>[]).map((row) => row['timeoutMinutes']);

test('a binary that lists timeoutMinutes is sent a CLI row\'s minutes; one that does not is sent none', () => {
  assert.deepEqual(crossed(['timeoutMinutes']), [25, undefined], 'an api row has no timeoutMinutes — it keeps reviewMinutes');
  assert.deepEqual(crossed([]), [undefined, undefined]);
});
