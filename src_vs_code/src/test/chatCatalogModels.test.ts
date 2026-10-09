import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatModelsOf } from '../chatCatalogModels';
import { chatRunSpec, type ModelPreset } from '../chatPresets';
import { chatMove } from '../chatPresetMove';
import { vaultKeyOf } from '../vaultKey';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';

/**
 * E4.6a of research/PLAN_one_model_catalog.md, the read: the chat lists the catalog rows ticked Chat — and, only for a
 * preset the record does not hold (before a layer's first move, after a refused one, in a restored layer), the preset
 * itself, so nothing the chat offered disappears while the move has not happened.
 */

const preset = (id: string, extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id, name: `Name of ${id}`, runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '', ...extra,
});

function movedRows(presets: readonly ModelPreset[]) {
  const moved = chatMove({ presets, rows: DEFAULT_VENDORS.map((row) => ({ ...row })), record: [] });

  return { rows: vendorsFrom([...moved.rows]), record: moved.record };
}

test('before any move the chat lists its presets, exactly as today', () => {
  const presets = [preset('a', { main: true }), preset('b')];

  assert.deepEqual(chatModelsOf(presets, DEFAULT_VENDORS, []), presets);
});

test('after the move the chat lists the rows ticked Chat, under their row ids, with the preset\'s fields', () => {
  const presets = [preset('a', { startingPrompt: 'You are an architect.' })];
  const { rows, record } = movedRows(presets);
  const [only] = chatModelsOf(presets, rows, record);

  assert.deepEqual(only, {
    id: 'chat-a', name: 'Name of a', runtime: 'claude', model: 'opus', main: false, startingPrompt: 'You are an architect.',
    executablePath: '', baseUrl: '', vaultKeyName: 'a',
  });
  assert.equal(chatModelsOf(presets, rows, record).length, 1, 'a moved preset is listed twice');
});

test('a reviewer row ticked Chat is a chat model too; a row not ticked is not', () => {
  const reviewer: Vendor = { ...DEFAULT_VENDORS[0]!, uses: ['chat'] };

  assert.deepEqual(chatModelsOf([], [reviewer, DEFAULT_VENDORS[1]!], []).map((one) => one.id), [reviewer.id]);
});

test('a preset the record lacks is still offered beside the rows — a move refused, or not run yet', () => {
  const { rows, record } = movedRows([preset('a')]);

  assert.deepEqual(chatModelsOf([preset('a'), preset('late')], rows, record).map((one) => one.id), ['chat-a', 'late']);
});

test('a row removed on Models is gone from the chat — its preset does not come back', () => {
  const { record } = movedRows([preset('a')]);

  assert.deepEqual(chatModelsOf([preset('a')], DEFAULT_VENDORS, record), []);
});

test('a moved row keeps the vault key it was filed under', () => {
  const { rows, record } = movedRows([preset('a', { runtime: 'codex', baseUrl: 'https://api.example.test/v1' })]);

  assert.equal(vaultKeyOf(chatRunSpec(chatModelsOf([], rows, record)[0]!)), 'a');
});
