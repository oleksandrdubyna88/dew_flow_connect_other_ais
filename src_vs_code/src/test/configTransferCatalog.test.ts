import assert from 'node:assert/strict';
import { test } from 'node:test';
import { backupFileName, backupSettings, CONFIG_FORMAT, CONFIG_VERSION, exportedSettings, importedConfig, modelsSentence, staleBackups, type Declared } from '../configTransfer';
import { vendorsFrom } from '../vendors';

/**
 * Export/import format v2 (PLAN_one_model_catalog.md E1.5): a file carries the catalog's rows and references; a v1
 * file — definitions, from a build before the catalog — still imports, and the migration then moves its
 * definitions into the catalog like any other. An import that replaces the models says what it replaces, names
 * every row whose key this machine's vault does not hold, and saves the current setup to a file first.
 */

const declared: Declared = {
  vendors: { default: [], type: 'array' },
  consultants: { default: {}, type: 'object' },
  migratedFrom: { type: 'object' },
  catalogMigration: { type: 'string' },
};

test('a file this build writes is version 2', () => {
  assert.equal(CONFIG_VERSION, 2);
});

test('a version 1 file — written before the catalog — still imports', () => {
  const v1 = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: 1, settings: { consultants: { codex: { vendor: 'glm', runtime: 'codex', model: 'glm-5.3' } } }, prompts: {},
  }), declared);

  assert.equal(v1.ok, true);
  assert.deepEqual(v1.ok ? Object.keys(v1.settings) : [], ['consultants']);
});

test('the backup from before the catalog and its marker never travel, either way', () => {
  const exported = exportedSettings(declared, (key) => ({ migratedFrom: { keys: ['vendors'], values: {} }, catalogMigration: 'migrated' })[key]);
  const imported = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: 2, settings: { migratedFrom: { keys: [] }, catalogMigration: 'restored' }, prompts: {},
  }), declared);

  assert.deepEqual(exported, {});
  assert.ok(imported.ok);
  assert.deepEqual(imported.ok ? imported.settings : {}, {});
  assert.deepEqual(imported.ok ? imported.refused.map((one) => one.name).sort() : [], ['catalogMigration', 'migratedFrom']);
});

const CURRENT = vendorsFrom([{ id: 'codex', runtime: 'codex', model: '' }, { id: 'claude', runtime: 'claude', model: '' }]);

test('replacing the models says how many it replaces with how many', () => {
  const sentence = modelsSentence(CURRENT, [{ id: 'codex', runtime: 'codex', model: '' }], ['grok']);

  assert.match(sentence, /replaces your 2 models with the file's 1/u);
});

test('a row that reads a key this machine\'s vault does not hold is named, with the key', () => {
  const file = [
    { id: 'grok', runtime: 'api', model: 'grok-5', baseUrl: 'https://api.x.ai/v1' },
    { id: 'qwen-2', runtime: 'api', model: 'qwen-max', baseUrl: 'https://q.example', vaultKeyName: 'qwen' },
    { id: 'codex', runtime: 'codex', model: '' },
  ];
  const sentence = modelsSentence(CURRENT, file, ['grok']);

  assert.match(sentence, /qwen-2 \(key qwen\)/u);
  assert.doesNotMatch(sentence, /grok \(key/u, 'a key the vault holds is not named');
  assert.doesNotMatch(sentence, /codex \(key/u, 'a CLI row signs in through its CLI');
});

test('when nobody has asked the vault yet, the sentence says so rather than calling every key missing', () => {
  const sentence = modelsSentence(CURRENT, [{ id: 'grok', runtime: 'api', model: 'grok-5', baseUrl: 'https://api.x.ai/v1' }], undefined);

  assert.match(sentence, /not known/u);
  assert.match(sentence, /grok \(key grok\)/u);
});

test('a file that does not name the models replaces none, and says nothing about them', () => {
  assert.equal(modelsSentence(CURRENT, undefined, []), '');
});

test('the backup before an import is named by its moment and a suffix of its own, and only the newest ten are kept', () => {
  // Two imports in one second must not share a file, or the second backup replaces the first (CodeRabbit, PR #681).
  assert.equal(backupFileName(new Date('2026-10-04T18:30:05.123Z'), 'a1b2c3d4'), 'before-import-2026-10-04T18-30-05-123Z-a1b2c3d4.json');
  assert.notEqual(backupFileName(new Date('2026-10-04T18:30:05.123Z'), 'a1b2c3d4'), backupFileName(new Date('2026-10-04T18:30:05.123Z'), 'e5f6a7b8'));
  const names = Array.from({ length: 12 }, (_, i) => `before-import-2026-10-${String(i + 1).padStart(2, '0')}T00-00-00-000Z-0000000${i % 10}.json`);

  assert.deepEqual(staleBackups([...names, 'notes.txt', 'before-import-x.json']), names.slice(0, 2));
});

test('the backup names the models even at their defaults, so importing it really puts them back', () => {
  // An export leaves out a setting at its default, and an import leaves a setting it does not name alone — so a
  // backup taken while `vendors` was the shipped default would not undo an import that replaced it (CodeRabbit).
  const settings = backupSettings(declared, (key) => (key === 'consultants' ? { codex: { vendor: 'glm', runtime: 'codex', executablePath: '/x' } } : undefined));

  assert.deepEqual(settings['vendors'], [], 'the shipped default, said out loud');
  assert.deepEqual(settings['consultants'], { codex: { vendor: 'glm', runtime: 'codex' } }, 'still without a machine path');
  assert.equal('migratedFrom' in settings, false, 'and still never the catalog backup');
});
