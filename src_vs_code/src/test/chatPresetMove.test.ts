import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatMove, movedRecordFrom, type MovedPreset } from '../chatPresetMove';
import type { ModelPreset } from '../chatPresets';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md, the move's pure core: each chat model preset becomes a catalog row of its
 * own, recorded by its fingerprint — the record, never the row's key name, is what a later run skips by (the design
 * review of 2026-10-06).
 */

const preset = (id: string, extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id, name: `Name of ${id}`, runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '', ...extra,
});

const BASE = DEFAULT_VENDORS.map((row) => ({ ...row })) as readonly Record<string, unknown>[];

test('a preset becomes a chat row of its own, with every field and its vault key name', () => {
  const moved = chatMove({ presets: [preset('preset-lw3-1', { startingPrompt: 'You are an architect.', executablePath: 'C:\\claude.exe' })], rows: BASE, record: [] });

  const row = moved.rows.at(-1)!;
  assert.deepEqual(row, {
    id: 'chat-preset-lw3-1', name: 'Name of preset-lw3-1', runtime: 'claude', model: 'opus', enabled: true,
    plan: false, code: false, document: false, baseUrl: '', executablePath: 'C:\\claude.exe', uses: ['chat'],
    chatStartingPrompt: 'You are an architect.', vaultKeyName: 'preset-lw3-1',
  });
  assert.deepEqual(moved.record, [{ presetId: 'preset-lw3-1', runtime: 'claude', model: 'opus', name: 'Name of preset-lw3-1', rowId: 'chat-preset-lw3-1' }]);
  assert.equal(moved.changed, true);
});

test('a second run changes nothing, and a deleted row stays deleted', () => {
  const first = chatMove({ presets: [preset('p-1')], rows: BASE, record: [] });
  const again = chatMove({ presets: [preset('p-1')], rows: first.rows, record: first.record });
  assert.equal(again.changed, false);

  const deleted = chatMove({ presets: [preset('p-1')], rows: BASE, record: first.record });
  assert.equal(deleted.changed, false, 'the person removed the row on Models and the move put it back');
});

test('a run interrupted after its rows were written adopts the row it wrote, and never makes a second one', () => {
  // The rows and the record are two settings writes. A window closed between them left the row and no record, and the next
  // run moved the preset again into `chat-p-1-2`. (CodeRabbit's architecture note on PR #688.)
  const first = chatMove({ presets: [preset('p-1')], rows: BASE, record: [] });
  const resumed = chatMove({ presets: [preset('p-1')], rows: first.rows, record: [] });

  assert.deepEqual(resumed.rows, first.rows, 'the interrupted run\'s row was written a second time');
  assert.deepEqual(resumed.record, first.record);
  assert.equal(resumed.changed, true, 'the record the interrupted run never wrote is still owed');
});

test('a row of the same id that is not this preset\'s is never adopted — it is somebody else\'s model', () => {
  const theirs = { ...BASE[0], id: 'chat-p-1', name: 'Mine', model: 'sonnet', uses: ['chat'] };
  const moved = chatMove({ presets: [preset('p-1')], rows: [...BASE, theirs], record: [] });

  assert.deepEqual(moved.rows.map((row) => row['id']).slice(-2), ['chat-p-1', 'chat-p-1-2']);
  assert.equal(moved.record[0]!.rowId, 'chat-p-1-2');
});

test('a row like this preset\'s but launched elsewhere — another executable or endpoint — is never adopted', () => {
  // Matching runtime, model and name is not the same model: another CLI or endpoint answers (the risk consultation, R3).
  const first = chatMove({ presets: [preset('p-1')], rows: BASE, record: [] });
  const elsewhere = first.rows.map((row) => (row['id'] === 'chat-p-1' ? { ...row, executablePath: 'C:\\other\\claude.exe' } : row));

  const moved = chatMove({ presets: [preset('p-1')], rows: elsewhere, record: [] });

  assert.equal(moved.record[0]!.rowId, 'chat-p-1-2');
});

test('after a collision, a retry adopts the row the interrupted run wrote — never a third one', () => {
  // The unrelated `chat-p-1` pushed the move to `chat-p-1-2`; the window closed before the record; the retry looked only
  // at `chat-p-1` and wrote `chat-p-1-3` (the risk consultation, R3).
  const theirs = { ...BASE[0], id: 'chat-p-1', name: 'Mine', model: 'sonnet', uses: ['chat'] };
  const first = chatMove({ presets: [preset('p-1')], rows: [...BASE, theirs], record: [] });
  const resumed = chatMove({ presets: [preset('p-1')], rows: first.rows, record: [] });

  assert.deepEqual(resumed.rows, first.rows, 'the interrupted run\'s row was written again');
  assert.equal(resumed.record[0]!.rowId, 'chat-p-1-2');
});

test('a recorded id with a different fingerprint is a different preset — a positional id that shifted', () => {
  const first = chatMove({ presets: [preset('preset-2', { name: 'Fast', model: 'haiku' })], rows: BASE, record: [] });
  const shifted = chatMove({ presets: [preset('preset-2', { name: 'Deep', model: 'opus' })], rows: first.rows, record: first.record });

  assert.equal(shifted.changed, true);
  assert.deepEqual(shifted.rows.map((row) => row['id']).slice(-2), ['chat-preset-2', 'chat-preset-2-2']);
  assert.equal(shifted.rows.at(-1)!['name'], 'Deep');
});

test('a mixed-case or odd id keeps no key name it cannot hold, and an id with nothing left still gets a row', () => {
  const moved = chatMove({ presets: [preset('GLM Fast'), preset('***')], rows: BASE, record: [] });
  const [glm, empty] = moved.rows.slice(-2);

  assert.equal(glm!['id'], 'chat-glm-fast');
  assert.ok(!('vaultKeyName' in glm!), 'a key name that is not a clean id was written');
  assert.equal(empty!['id'], 'chat-model');
  assert.equal(chatMove({ presets: [preset('GLM Fast')], rows: moved.rows, record: moved.record }).changed, false, 'a mixed-case id moved again');
});

test('a Team-server preset keeps the vendor its server knows, written out', () => {
  const moved = chatMove({ presets: [preset('acme-codex', { runtime: 'remote', teamServerId: 'acme', model: 'gpt-5' })], rows: BASE, record: [] });
  const row = moved.rows.at(-1)!;

  assert.equal(row['teamServerId'], 'acme');
  assert.equal(row['remoteVendor'], 'codex', 'the server vendor came from the id prefix that chat- breaks');
});

test('the main preset names the row and the model the chat opens on', () => {
  const moved = chatMove({ presets: [preset('a'), preset('b', { main: true, model: 'sonnet' })], rows: BASE, record: [] });

  assert.deepEqual(moved.main, { rowId: 'chat-b', model: 'sonnet' });
  assert.equal(chatMove({ presets: [preset('a')], rows: BASE, record: [] }).main, undefined);
});

test('an imported preset moves alone', () => {
  const first = chatMove({ presets: [preset('a')], rows: BASE, record: [] });
  const imported = chatMove({ presets: [preset('a'), preset('b')], rows: first.rows, record: first.record });

  assert.deepEqual(imported.record.map((one) => one.presetId), ['a', 'b']);
  assert.equal(imported.rows.length, first.rows.length + 1);
});

test('the record is read as written, and anything else in it is left out', () => {
  const kept: MovedPreset = { presetId: 'a', runtime: 'claude', model: 'opus', name: 'A', rowId: 'chat-a' };

  assert.deepEqual(movedRecordFrom([kept, { presetId: 1 }, 'x', null]), [kept]);
  assert.deepEqual(movedRecordFrom(undefined), []);
});

test('the catalog reads a moved row back as a chat model, with its name, starting text and server', () => {
  const moved = chatMove({
    presets: [preset('p-1', { startingPrompt: 'You review APIs.' }), preset('acme-codex', { runtime: 'remote', teamServerId: 'acme' })],
    rows: BASE,
    record: [],
  });
  const [mine, remote] = vendorsFrom([...moved.rows]).slice(-2);

  assert.deepEqual([mine?.id, mine?.uses, mine?.name, mine?.chatStartingPrompt, mine?.vaultKeyName], ['chat-p-1', ['chat'], 'Name of p-1', 'You review APIs.', 'p-1']);
  assert.deepEqual([remote?.runtime, remote?.teamServerId, remote?.remoteVendor], ['remote', 'acme', 'codex']);
});
