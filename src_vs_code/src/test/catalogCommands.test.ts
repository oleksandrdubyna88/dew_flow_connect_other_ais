import assert from 'node:assert/strict';
import { test } from 'node:test';
import { duplicated, removedRow, toggledUse } from '../catalogCommands';
import { MAX_ROWS } from '../catalogRules';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';

/**
 * The Models tab's own edits (todo/PLAN_one_model_catalog.md E3.2): a use ticked or unticked on a row — Bugz moving to
 * the one row ticked (D7) — and a row duplicated with its own id. Pure: the host saves what these answer.
 */

const row = (id: string, runtime: Vendor['runtime'], extra: Partial<Vendor> = {}): Vendor => ({
  ...DEFAULT_VENDORS[0]!, id, runtime, model: `${id}-model`, enabled: true, plan: true, code: true, ...extra,
});

test('a use is ticked and unticked in the catalog\'s own order', () => {
  const rows = [row('codex', 'codex', { uses: ['security'] })];

  assert.deepEqual(toggledUse(rows, 'codex', 'consultant', '').rows[0]!.uses, ['security', 'consultant']);
  assert.deepEqual(toggledUse(rows, 'codex', 'security', '').rows[0]!.uses, [], 'unticked');
});

test('a use the runtime cannot take is refused by name, and nothing changes', () => {
  const rows = [row('codex', 'codex')];
  const change = toggledUse(rows, 'codex', 'bugz', '');

  assert.match(change.refused, /Bugz ranks with a model on this machine/);
  assert.equal(change.rows, rows);
});

test('Bugz has ONE model: ticking it moves it off every other row and names the ranking model (D7)', () => {
  const rows = [row('local', 'local', { uses: ['bugz'] }), row('local-2', 'local')];
  const change = toggledUse(rows, 'local-2', 'bugz', 'local/local-model');

  assert.deepEqual(change.rows.map((one) => one.uses ?? []), [[], ['bugz']]);
  assert.equal(change.bugzModel, 'local-2/local-2-model');
  assert.match(change.said, /moved from local/);
});

test('unticking the Bugz row leaves Bugz with no model, rather than a model nobody ticked', () => {
  const rows = [row('local', 'local', { uses: ['bugz'] })];

  assert.equal(toggledUse(rows, 'local', 'bugz', 'local/local-model').bugzModel, '');
});

test('a duplicate is the same row with its own id, right after the source, named a copy', () => {
  const rows = [row('qwen', 'api', { vaultKeyName: 'QWEN', systemPrompt: 'terse', name: 'Qwen' }), row('codex', 'codex')];
  const change = duplicated(rows, 'qwen');

  assert.deepEqual(change.rows.map((one) => one.id), ['qwen', 'qwen-2', 'codex']);
  assert.equal(change.rows[1]!.vaultKeyName, 'QWEN', 'the key name is copied — two rows may share one key');
  assert.equal(change.rows[1]!.systemPrompt, 'terse');
  assert.equal(change.rows[1]!.name, 'Qwen (copy)');
});

test('a duplicate of a duplicate takes the next free id from the same base', () => {
  const rows = [row('qwen', 'api'), row('qwen-2', 'api')];

  assert.equal(duplicated(rows, 'qwen-2').rows[2]!.id, 'qwen-3');
});

test('a removed row is gone; the last model switched on for a review stage is refused, and nothing changes', () => {
  const rows = [row('codex', 'codex'), row('claude', 'claude', { plan: false })];

  assert.deepEqual(removedRow(rows, 'claude').rows.map((one) => one.id), ['codex']);
  const last = removedRow(rows, 'codex');
  assert.match(last.refused, /codex is the only model switched on for plan review/);
  assert.equal(last.rows, rows);
});

test('a duplicate past the cap is refused, and nothing changes', () => {
  const rows = Array.from({ length: MAX_ROWS }, (_, at) => row(`r${at}`, 'codex'));
  const change = duplicated(rows, 'r0');

  assert.match(change.refused, /at most 64/);
  assert.equal(change.rows, rows);
});
