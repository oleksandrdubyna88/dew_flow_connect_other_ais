import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CatalogState, editThroughCatalog } from '../catalogEdit';
import { isRecord, sameLaunch } from '../catalogLaunch';
import { storedForThisSide, type PathFamily } from '../pathFamily';
import { CatalogLayer, migrateLayer } from '../catalogMigration';
import { qconsultSettingsFrom } from '../qconsultSettings';
import { envBlock, settingsFrom } from '../settingsShape';
import { vendorsFrom } from '../vendors';

/**
 * A write of `consultants` or `qconsultRows` goes through the catalog (PLAN_one_model_catalog.md E1.4).
 *
 * <p>The old Settings page edited a consultant as a DEFINITION — it resolved the reference, changed one field, and
 * wrote the whole entry back; the question tab still writes every row back resolved. Left alone, the migration would
 * then fork a new row for every edit and orphan the one before. So an edit of an entry that refers to a catalog row
 * nothing else uses rewrites THAT row, and the entry stays a reference; a reference the edit removes takes its row
 * with it. The consultant definitions below are what the old page wrote (E5.3 removed it): no page writes one now,
 * and the fold is held here until it is decided whether it goes too.</p>
 */

const REVIEWERS = [{ id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true }];
const GLM = { vendor: 'glm', runtime: 'codex', model: 'glm-5.3', baseUrl: 'https://glm.example/v1', executablePath: '' };
/** The migrated GLM consultant with its model edited to glm-6, as the old page stored it: a definition again. */
const GLM_6 = { vendor: 'glm', runtime: 'codex', model: 'glm-6', baseUrl: 'https://glm.example/v1' };

/** The state with one catalog row given more raw fields — through a type guard, where a cast would stop the compiler checking. */
function withRowFields(state: CatalogState, id: string, extra: Record<string, unknown>): CatalogState {
  const rows: readonly unknown[] = Array.isArray(state.vendors) ? state.vendors : [];

  return { ...state, vendors: rows.map((raw) => (isRecord(raw) && raw['id'] === id ? { ...raw, ...extra } : raw)) };
}

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

/** Apply one fold the way the panel does: the rows first, then the key — written by a window of `family`, or this host's. */
function saved(state: CatalogState, key: 'consultants' | 'qconsultRows', value: unknown, family?: PathFamily): CatalogState {
  const fold = family === undefined ? editThroughCatalog(key, value, state) : editThroughCatalog(key, value, state, family);

  return { ...state, ...(fold.vendors === undefined ? {} : { vendors: fold.vendors }), [key]: fold.value };
}

/** The CLI path a WSL window saved on a row (cliPathSave): the legacy value and `mirrored` are its, Windows' own is empty. */
const WSL_CLI = '/home/u/.local/bin/codex';
const SAVED_IN_WSL = { executablePath: WSL_CLI, executablePathBySide: { windows: '', posix: WSL_CLI, mirrored: WSL_CLI } };

/** What each side runs of one catalog row. */
function runs(state: CatalogState, id: string): Readonly<Record<PathFamily, string>> {
  const row = vendorsFrom(state.vendors).find((one) => one.id === id);
  assert.ok(row !== undefined, `no row ${id}`);

  return { windows: storedForThisSide(row, 'windows'), posix: storedForThisSide(row, 'posix') };
}

test('editing a migrated consultant\'s model rewrites its row and keeps the reference', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  // The definition the old page wrote for a model changed from glm-5.3 to glm-6.
  const after = saved(state, 'consultants', { codex: GLM_6 });
  const rows = vendorsFrom(after.vendors);

  assert.deepEqual((after.consultants as Record<string, unknown>)['codex'], { vendor: 'consult-codex' });
  assert.equal(rows.filter((row) => row.uses !== undefined).length, 1, 'no fork, no orphan');
  assert.equal(rows.find((row) => row.id === 'consult-codex')?.model, 'glm-6');
  assert.match(env(after)['COAI_CONSULTANTS'] ?? '', /"vendor":"glm","model":"glm-6"/u, 'and coai-mcp reads the edit');
});

test('rewriting a row in place keeps its per-side CLI paths — a field the definition does not carry is not a field the edit drops', () => {
  // todo/PLAN_paths_per_side.md E3.1: the fold rewrites only the launch fields; `executablePathBySide` rides along.
  const bySide = { windows: 'C:\\tools\\codex.cmd', posix: '/opt/codex', mirrored: 'C:\\tools\\codex.cmd' };
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  const withSides = withRowFields(state, 'consult-codex', { executablePathBySide: bySide });
  const after = saved(withSides, 'consultants', { codex: GLM_6 });
  const row = vendorsFrom(after.vendors).find((one) => one.id === 'consult-codex');

  assert.equal(row?.model, 'glm-6', 'the row was rewritten');
  assert.deepEqual(row?.executablePathBySide, bySide);
});

// todo/PLAN_paths_per_side.md, E3 as built — the code round's own review: a definition comes back RESOLVED for the side
// that wrote it (`storedForThisSide`), so comparing or writing it as the LEGACY value let one side's edit of a switch
// or a prompt take the other side's CLI path away, silently (the member stayed, so no keeper notice either).

test('a question-row edit in a Windows window never changes the CLI path the WSL side runs', () => {
  const q1 = { id: 'q1', vendor: 'qwen', runtime: 'codex', model: 'qwen-max', prompt: 'p', enabled: true, key: '', executablePath: '' };
  const state = withRowFields(migrated({ vendors: REVIEWERS, qconsultRows: [q1] }), 'ask-q1', SAVED_IN_WSL);
  // The Windows tab writes every row back resolved for ITS side — the path it runs is '' — with one switch flipped.
  const resolved = qconsultSettingsFrom((key) => (state as Record<string, unknown>)[key]).rows;
  const edited = resolved.map((one) => ({ ...one, executablePath: '', enabled: false }));
  const after = saved(state, 'qconsultRows', edited, 'windows');

  assert.deepEqual(runs(after, 'ask-q1'), { windows: '', posix: WSL_CLI }, 'a Windows edit of a switch took the WSL side\'s CLI path away');
});

test('a consultant edit in a Windows window never changes the CLI path the WSL side runs', () => {
  const state = withRowFields(migrated({ vendors: REVIEWERS, consultants: { codex: GLM } }), 'consult-codex', SAVED_IN_WSL);
  // The old page's definition, resolved on Windows: the model edited, the path the Windows side runs.
  const after = saved(state, 'consultants', { codex: { ...GLM_6, executablePath: '' } }, 'windows');

  assert.equal(vendorsFrom(after.vendors).find((one) => one.id === 'consult-codex')?.model, 'glm-6', 'the row was rewritten');
  assert.deepEqual(runs(after, 'consult-codex'), { windows: '', posix: WSL_CLI }, 'a Windows edit of the model took the WSL side\'s CLI path away');
});

test('a CLI path a Windows window changes through a definition is Windows\' own — the WSL side keeps what it runs', () => {
  const state = withRowFields(migrated({ vendors: REVIEWERS, consultants: { codex: GLM } }), 'consult-codex', SAVED_IN_WSL);
  const after = saved(state, 'consultants', { codex: { ...GLM, executablePath: 'C:\\tools\\codex.cmd' } }, 'windows');

  assert.deepEqual(runs(after, 'consult-codex'), { windows: 'C:\\tools\\codex.cmd', posix: WSL_CLI });
});

test('a bare CLI name a Windows window gives a definition is Windows\' own too — spelled for neither OS, it still moves nothing on WSL', () => {
  // The case the `mirrored` check cannot rescue: a legacy-only write of a value spelled for neither OS reads as an older
  // extension's save on BOTH sides, so the WSL side would run `codex.cmd` from PATH in place of its own path.
  const state = withRowFields(migrated({ vendors: REVIEWERS, consultants: { codex: GLM } }), 'consult-codex', SAVED_IN_WSL);
  const after = saved(state, 'consultants', { codex: { ...GLM, executablePath: 'codex.cmd' } }, 'windows');

  assert.deepEqual(runs(after, 'consult-codex'), { windows: 'codex.cmd', posix: WSL_CLI }, 'a bare name typed on Windows took the WSL side\'s CLI path away');
});

test('a row is the same launch as a definition resolved on either side — compared on THAT side\'s value, not the legacy one', () => {
  // The migration's matchingRow and the question tab's "did this edit change the row" both ask sameLaunch: a definition
  // written back resolved on Windows must still match its own row, or the migration forks a row that loses WSL's path.
  const row = vendorsFrom([{ id: 'ask-q1', runtime: 'codex', model: 'qwen-max', vaultKeyName: 'qwen', ...SAVED_IN_WSL }])[0];
  assert.ok(row !== undefined);
  const launch = { runtime: 'codex', model: 'qwen-max', baseUrl: '', vault: 'qwen' };

  assert.equal(sameLaunch(row, { ...launch, executablePath: '', family: 'windows' }), true, 'the Windows-resolved definition did not match its own row');
  assert.equal(sameLaunch(row, { ...launch, executablePath: WSL_CLI, family: 'posix' }), true);
  assert.equal(sameLaunch(row, { ...launch, executablePath: WSL_CLI, family: 'windows' }), false, 'Windows runs no WSL path');
});

test('picking another vendor for a migrated consultant rewrites the same row', () => {
  const state = migrated({ vendors: REVIEWERS, consultants: { codex: GLM } });
  // The definition the old page wrote for Claude picked in place of glm: the catalogue's Claude entry.
  const after = saved(state, 'consultants', { codex: { vendor: 'claude', runtime: 'claude', model: 'haiku' } });
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
  const after = saved(state, 'consultants', { codex: GLM_6, gemini: { vendor: 'consult-codex' } });

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
  const withDialect = withRowFields(state, 'consult-codex', { dialect: 'anthropic' });
  const row = vendorsFrom(saved(withDialect, 'consultants', { codex: GLM_6 }).vendors).find((one) => one.id === 'consult-codex');

  assert.equal(row?.model, 'glm-6');
  assert.equal(row?.dialect, undefined, 'the launch is compared and written as ONE set of fields (catalogLaunch.ts), dialect included');
});
