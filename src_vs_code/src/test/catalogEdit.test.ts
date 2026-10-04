import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CatalogState, editThroughCatalog } from '../catalogEdit';
import { CatalogLayer, migrateLayer } from '../catalogMigration';
import { consultantRecordUpdate } from '../consultantWrite';
import { qconsultSettingsFrom } from '../qconsultSettings';
import { envBlock, settingsFrom } from '../settingsShape';
import { vendorsFrom } from '../vendors';

/**
 * The old Settings page writes through the catalog (PLAN_one_model_catalog.md E1.4).
 *
 * <p>The old page still edits a consultant or a question row as a DEFINITION — it resolves the reference,
 * changes one field, and writes the whole entry back. Left alone, the migration would then fork a new row for
 * every edit and orphan the one before. So an edit of an entry that refers to a catalog row nothing else uses
 * rewrites THAT row, and the entry stays a reference; a reference the edit removes takes its row with it.</p>
 */

const REVIEWERS = [{ id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true }];
const GLM = { vendor: 'glm', runtime: 'codex', model: 'glm-5.3', baseUrl: 'https://glm.example/v1', executablePath: '' };

/** A layer after the migration: what every test starts from. */
function migrated(layer: CatalogLayer): CatalogState {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate');
  const next: Record<string, unknown> = { ...layer };
  for (const write of outcome.kind === 'migrate' ? outcome.writes : []) {
    next[write.key] = write.value;
  }

  return { vendors: next['vendors'], consultants: next['consultants'], qconsultRows: next['qconsultRows'] };
}

function env(state: CatalogState): Record<string, string> {
  const read = (key: string): unknown => (state as Record<string, unknown>)[key];

  return envBlock(settingsFrom(read), vendorsFrom(read('vendors')));
}

/** Apply one fold the way the panel does: the rows first, then the key. */
function saved(state: CatalogState, key: 'consultants' | 'qconsultRows', value: unknown): CatalogState {
  const fold = editThroughCatalog(key, value, state);

  return { ...state, ...(fold.vendors === undefined ? {} : { vendors: fold.vendors }), [key]: fold.value };
}

test('editing a migrated consultant\'s model rewrites its row and keeps the reference', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  const edited = consultantRecordUpdate(state.consultants as Record<string, unknown>, 'codex', 'consultModel', 'glm-6', vendorsFrom(state.vendors));
  const after = saved(state, 'consultants', edited);
  const rows = vendorsFrom(after.vendors);

  assert.deepEqual((after.consultants as Record<string, unknown>)['codex'], { vendor: 'consult-codex' });
  assert.equal(rows.filter((row) => row.uses !== undefined).length, 1, 'no fork, no orphan');
  assert.equal(rows.find((row) => row.id === 'consult-codex')?.model, 'glm-6');
  assert.match(env(after)['COAI_CONSULTANTS'] ?? '', /"vendor":"glm","model":"glm-6"/u, 'and coai-mcp reads the edit');
});

test('picking another vendor for a migrated consultant rewrites the same row', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  const edited = consultantRecordUpdate(state.consultants as Record<string, unknown>, 'codex', 'consultVendor', 'claude', vendorsFrom(state.vendors));
  const after = saved(state, 'consultants', edited);
  const row = vendorsFrom(after.vendors).find((one) => one.id === 'consult-codex');

  assert.equal(row?.runtime, 'claude');
  assert.equal(row?.baseUrl, '');
  assert.equal(row?.vaultKeyName, 'claude');
  assert.match(env(after)['COAI_CONSULTANTS'] ?? '', /"vendor":"claude"/u);
});

test('a reference the edit removes takes its catalog row with it', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  const after = saved(state, 'consultants', {});

  assert.equal(vendorsFrom(after.vendors).some((row) => row.id === 'consult-codex'), false);
});

test('a row two entries share is never rewritten by an edit of one of them', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM, gemini: GLM } });
  const edited = consultantRecordUpdate(state.consultants as Record<string, unknown>, 'codex', 'consultModel', 'glm-6', vendorsFrom(state.vendors));
  const after = saved(state, 'consultants', edited);

  assert.equal(vendorsFrom(after.vendors).find((row) => row.id === 'consult-codex')?.model, 'glm-5.3', 'gemini still has its model');
  assert.equal((after.consultants as Record<string, Record<string, string>>)['codex']?.['runtime'], 'codex', 'the edit stays a definition, for the migration to give its own row');
});

test('a row a Security lane run names is never rewritten or removed', () => {
  const state = { ...migrated({ vendors: REVIEWERS, consultants: { codex: GLM } }), securityLane: { runs: [{ vendor: 'consult-codex', prompt: 'p' }] } };
  const after = saved(state, 'consultants', {});

  assert.equal(vendorsFrom(after.vendors).some((row) => row.id === 'consult-codex'), true);
});

test('the question tab writing every row back resolved changes no row it did not edit', () => {
  const row = { id: 'q1', vendor: 'qwen', runtime: 'codex', model: 'qwen-max', prompt: 'p', enabled: true, key: '' };
  const other = { id: 'q2', vendor: 'kimi', runtime: 'codex', model: 'k2', prompt: 'p', enabled: true, key: '' };
  const state = migrated({ vendors: REVIEWERS, qconsultRows: [row, other] });
  // What the tab writes: the resolved rows, with one model edited.
  const resolved = qconsultSettingsFrom((key) => (state as Record<string, unknown>)[key]).rows;
  const edited = resolved.map((one) => (one.id === 'q1' ? { ...one, model: 'qwen-3' } : one));
  const after = saved(state, 'qconsultRows', edited);
  const rows = after.qconsultRows as Record<string, unknown>[];

  assert.deepEqual(rows.map((one) => one['vendor']), ['ask-q1', 'ask-q2'], 'both stay references');
  assert.equal(vendorsFrom(after.vendors).find((one) => one.id === 'ask-q1')?.model, 'qwen-3');
  assert.equal(vendorsFrom(after.vendors).find((one) => one.id === 'ask-q2')?.model, 'k2');
  assert.match(env(after)['COAI_QCONSULT_ROWS'] ?? '', /"model":"qwen-3"/u);
});

test('removing a question row takes its catalog row with it', () => {
  const row = { id: 'q1', vendor: 'qwen', runtime: 'codex', model: 'qwen-max', prompt: 'p', enabled: true, key: '' };
  const state = migrated({ vendors: REVIEWERS, qconsultRows: [row] });
  const after = saved(state, 'qconsultRows', []);

  assert.equal(vendorsFrom(after.vendors).some((one) => one.id === 'ask-q1'), false);
});

test('an edit of an entry that never referred to the catalog passes through untouched', () => {
  const state: CatalogState = { vendors: REVIEWERS, consultants: {} };
  const fold = editThroughCatalog('consultants', { codex: GLM }, state);

  assert.deepEqual(fold, { value: { codex: GLM } });
});

test('a rewritten row is the definition\'s launch, whole: a dialect the definition never carried is not kept', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  // A person hand-gave the consultant's row a dialect; the old page's definition has no field for one.
  const withDialect = { ...state, vendors: (state.vendors as Record<string, unknown>[]).map((raw) => (raw['id'] === 'consult-codex' ? { ...raw, dialect: 'anthropic' } : raw)) };
  const edited = consultantRecordUpdate(withDialect.consultants as Record<string, unknown>, 'codex', 'consultModel', 'glm-6', vendorsFrom(withDialect.vendors));
  const row = vendorsFrom(saved(withDialect, 'consultants', edited).vendors).find((one) => one.id === 'consult-codex');

  assert.equal(row?.model, 'glm-6');
  assert.equal(row?.dialect, undefined, 'the launch is compared and written as ONE set of fields (catalogLaunch.ts), dialect included');
});
