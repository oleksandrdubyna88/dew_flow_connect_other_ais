import assert from 'node:assert/strict';
import { test } from 'node:test';
import { groupOf, grouped } from '../addModelGroups';

/** "Add a model" grouped by where a model runs (todo/PLAN_one_model_catalog.md E3.2). */

test('a runtime is in the group of where it runs', () => {
  assert.deepEqual(['codex', 'claude', 'antigravity', 'api', 'local', 'remote'].map(groupOf), ['cli', 'cli', 'cli', 'api', 'gpu', 'remote']);
});

test('the items come in group order, each group under its separator, an empty group with none', () => {
  const items = [
    { name: 'qwen', group: 'api' as const },
    { name: 'codex', group: 'cli' as const },
    { name: 'claude', group: 'cli' as const },
  ];

  assert.deepEqual(grouped(items).map((one) => ('separator' in one ? `— ${one.separator}` : one.name)),
    ['— A CLI on this machine', 'codex', 'claude', '— An API key', 'qwen']);
});
