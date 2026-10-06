import assert from 'node:assert/strict';
import { test } from 'node:test';
import { migrateLayer, MIGRATED, restoreLayer, RESTORED, type CatalogLayer, type LayerWrite } from '../catalogMigration';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md, wired into the epic 1 migration: the chat presets move in the same run, in
 * the same safe order (backup, rows, the record, the chat's own keys, the marker), per layer — and never twice.
 */

const preset = (id: string, extra: Record<string, unknown> = {}): Record<string, unknown> => ({ id, name: `Name of ${id}`, runtime: 'claude', model: 'opus', ...extra });

function writes(layer: CatalogLayer): readonly LayerWrite[] {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate', `the run did not migrate: ${JSON.stringify(outcome)}`);

  return outcome.kind === 'migrate' ? outcome.writes : [];
}

function written(all: readonly LayerWrite[], key: LayerWrite['key']): unknown {
  return all.find((one) => one.key === key)?.value;
}

test('the user layer moves its presets: rows, the record, the main model, in the safe order', () => {
  const all = writes({ chatPresets: [preset('a'), preset('b', { main: true, model: 'sonnet' })], chatModel: 'a', chatModelName: 'haiku' });

  assert.deepEqual(all.map((one) => one.key), ['migratedFrom', 'vendors', 'chatPresetsMoved', 'chatModel', 'chatModelName', 'catalogMigration']);
  const rows = written(all, 'vendors') as readonly Record<string, unknown>[];
  assert.deepEqual(rows.slice(-2).map((row) => row['id']), ['chat-a', 'chat-b']);
  assert.equal(written(all, 'chatModel'), 'chat-b');
  assert.equal(written(all, 'chatModelName'), 'sonnet', 'a stale model name would open another model on the main row');
  const backup = written(all, 'migratedFrom') as { keys: readonly string[]; values: Record<string, unknown> };
  assert.ok(['chatPresetsMoved', 'chatModel', 'chatModelName'].every((key) => backup.keys.includes(key)), `not backed up: ${backup.keys.join(', ')}`);
  assert.deepEqual([backup.values['chatModel'], backup.values['chatModelName']], ['a', 'haiku']);
  assert.equal(written(all, 'catalogMigration'), MIGRATED);
});

test('with no main preset the chat model is remapped to its row and the model name left alone', () => {
  const all = writes({ chatPresets: [preset('a'), preset('b')], chatModel: 'b', chatModelName: 'opus' });

  assert.equal(written(all, 'chatModel'), 'chat-b');
  assert.ok(!all.some((one) => one.key === 'chatModelName'));
});

test('a second run changes nothing — the record holds every preset', () => {
  const first = writes({ chatPresets: [preset('a')] });
  const again = migrateLayer({
    chatPresets: [preset('a')],
    vendors: written(first, 'vendors'),
    chatPresetsMoved: written(first, 'chatPresetsMoved'),
    marker: MIGRATED,
    backup: written(first, 'migratedFrom'),
  });

  assert.equal(again.kind, 'unchanged');
});

test('a side that keeps no rows of its own is left alone; one that does gets the user layer\'s row ids', () => {
  assert.equal(migrateLayer({ side: true, chatPresets: [preset('a')], sharedVendors: DEFAULT_VENDORS }).kind, 'unchanged');

  const userRecord = [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }];
  const taken = [...DEFAULT_VENDORS.map((row) => ({ ...row })), { id: 'chat-a', runtime: 'codex', model: '', enabled: true, plan: false, code: true }];
  const side = writes({ side: true, chatPresets: [preset('a')], vendors: DEFAULT_VENDORS.map((row) => ({ ...row })), userChatRecord: userRecord });
  assert.equal((written(side, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['id'], 'chat-a');
  assert.ok(!side.some((one) => one.key === 'chatModel'), 'the chat model is the user layer\'s, written once there');

  const clash = writes({ side: true, chatPresets: [preset('a')], vendors: taken, userChatRecord: userRecord });
  assert.equal((written(clash, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['id'], 'chat-a-2', 'a taken id is never reused');
});

test('a restored layer moves nothing, and a restore puts the chat\'s keys back before the rows', () => {
  assert.equal(migrateLayer({ chatPresets: [preset('a')], marker: RESTORED }).kind, 'restored');

  const all = writes({ chatPresets: [preset('a', { main: true })], chatModel: 'a' });
  const restore = restoreLayer({ backup: written(all, 'migratedFrom') });
  assert.equal(restore.kind, 'restore');
  const keys = restore.kind === 'restore' ? restore.writes.map((one) => one.key) : [];
  assert.ok(keys.indexOf('chatModel') < keys.indexOf('vendors') && keys.indexOf('chatPresetsMoved') < keys.indexOf('vendors'), keys.join(', '));
  const values = restore.kind === 'restore' ? restore.writes : [];
  assert.equal(values.find((one) => one.key === 'chatModel')?.value, 'a');
  assert.equal(values.find((one) => one.key === 'chatPresetsMoved')?.value, undefined, 'the record did not exist before the move');
});

test('presets past the 64-row cap refuse the run, said', () => {
  const many = Array.from({ length: 70 }, (_, index) => preset(`p-${index}`));

  assert.equal(migrateLayer({ chatPresets: many }).kind, 'refused');
});

test('a chat row an older build wrote back without its uses gets its chat tick back', () => {
  const lost = { id: 'chat-a', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false, baseUrl: '', executablePath: '', vaultKeyName: 'a' };
  const all = writes({
    chatPresets: [preset('a')],
    vendors: [...DEFAULT_VENDORS.map((row) => ({ ...row })), lost],
    chatPresetsMoved: [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }],
    marker: MIGRATED,
    backup: { keys: ['vendors', 'consultants', 'qconsultRows'], values: {} },
  });

  assert.deepEqual((written(all, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['uses'], ['chat']);
});
