import assert from 'node:assert/strict';
import { test } from 'node:test';
import { vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';

/**
 * A CLI row's effort crosses to coai-mcp only when the installed binary lists `cliEffort` in `--features`
 * (todo/PLAN_one_model_catalog.md, epic 2, story 2). What it means is the runtime's and coai-mcp decides it: claude's
 * level as `--effort`, a codex row's kept and not sent while codex is unmeasured. An api row's effort keeps its own
 * road (`apiSettingsOnTheWire`).
 */

const ROWS = vendorsFrom([
  { id: 'claude', runtime: 'claude', model: '', enabled: true, effort: 'high' },
  { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true, effort: 'medium' },
  { id: 'claude-2', runtime: 'claude', model: '', enabled: true },
]);

const crossed = (features: readonly string[]): readonly unknown[] =>
  (JSON.parse(vendorsEnv(ROWS, '', () => undefined, features)) as Record<string, unknown>[]).map((row) => row['effort']);

test('a binary that lists cliEffort is sent each CLI row\'s effort; one that does not is sent none', () => {
  assert.deepEqual(crossed(['cliEffort']), ['high', 'medium', undefined]);
  assert.deepEqual(crossed([]), [undefined, undefined, undefined]);
});
