import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FEATURES } from '../binaryFeatures';
import { ignoredSaid } from '../modelCard';
import { envBlock, settingsFrom } from '../settingsShape';
import { vendorsFrom } from '../vendors';

/**
 * A consultant carries its WHOLE catalog row to coai-mcp — effort, system prompt, timeout, key name — not only the five
 * launch fields (the cadence consultation for epics 1–3 of research/PLAN_one_model_catalog.md, finding C2).
 *
 * <p>The row crosses beside the entry as `row`, exactly as `COAI_VENDORS` would write it, and only to a binary whose
 * `--features` lists `consultantRow` — an older one would skip the member without a word. The entry's `vendor` does
 * not change: an open consultation resumes by it.</p>
 */

const ALL = [FEATURES.consultantRow, FEATURES.systemPrompt, FEATURES.timeoutMinutes, FEATURES.cliEffort, FEATURES.apiConsultant];

const ROWS = [
  { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true },
  {
    id: 'consult-claude', runtime: 'claude', model: 'opus', enabled: false, uses: ['consultant'], vaultKeyName: 'claude',
    effort: 'high', systemPrompt: 'Answer in plain English.', timeoutMinutes: 9,
  },
];

function consultantsOnTheWire(features: readonly string[]): Record<string, Record<string, unknown>> {
  const stored: Record<string, unknown> = { vendors: ROWS, consultants: { codex: { vendor: 'consult-claude' } } };
  const read = (key: string): unknown => stored[key];
  const env = envBlock(settingsFrom(read), vendorsFrom(read('vendors')), '0.50.0', undefined, features);

  return JSON.parse(env['COAI_CONSULTANTS'] ?? '{}') as Record<string, Record<string, unknown>>;
}

test('a consultant\'s row crosses whole beside its entry, to a binary that lists consultantRow', () => {
  const entry = consultantsOnTheWire(ALL)['codex'];

  assert.equal(entry?.['vendor'], 'claude', 'the id a consultation resumes by did not change');
  const row = entry?.['row'] as Record<string, unknown> | undefined;
  assert.ok(row !== undefined, `the entry carries no row: ${JSON.stringify(entry)}`);
  assert.equal(row['runtime'], 'claude');
  assert.equal(row['effort'], 'high');
  assert.equal(row['systemPrompt'], 'Answer in plain English.');
  assert.equal(row['timeoutMinutes'], 9);
});

test('a binary that does not list consultantRow gets the five fields as before', () => {
  const entry = consultantsOnTheWire(ALL.filter((one) => one !== FEATURES.consultantRow))['codex'];

  assert.deepEqual(Object.keys(entry ?? {}), ['vendor', 'model', 'runtime', 'baseUrl', 'executablePath']);
});

/** Two catalog rows on ONE key and model, told apart only by their effort — the case C2 exists for. */
const TWO_GLMS = [
  { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true },
  { id: 'glm-low', runtime: 'api', model: 'glm-5.3', baseUrl: 'https://glm.example/v4', dialect: 'glm', enabled: false, uses: ['qconsult'], vaultKeyName: 'glm', effort: 'low' },
  { id: 'glm-high', runtime: 'api', model: 'glm-5.3', baseUrl: 'https://glm.example/v4', dialect: 'glm', enabled: false, uses: ['qconsult'], vaultKeyName: 'glm', effort: 'high' },
];

function questionRowsOnTheWire(features: readonly string[]): readonly Record<string, unknown>[] {
  const stored: Record<string, unknown> = {
    vendors: TWO_GLMS,
    qconsultRows: [{ id: 'q-low', vendor: 'glm-low', prompt: 'code', enabled: true }, { id: 'q-high', vendor: 'glm-high', prompt: 'code', enabled: true }],
  };
  const read = (key: string): unknown => stored[key];
  const env = envBlock(settingsFrom(read), vendorsFrom(read('vendors')), '0.50.0', undefined, features);

  return JSON.parse(env['COAI_QCONSULT_ROWS'] ?? '[]') as Record<string, unknown>[];
}

test('each question row carries ITS catalog row, even when two rows share a key and a model', () => {
  const rows = questionRowsOnTheWire(ALL);
  const effortOf = (id: string): unknown => (rows.find((one) => one['id'] === id)?.['row'] as Record<string, unknown> | undefined)?.['effort'];

  assert.equal(effortOf('q-low'), 'low', JSON.stringify(rows));
  assert.equal(effortOf('q-high'), 'high');
  assert.equal(rows.find((one) => one['id'] === 'q-low')?.['vendor'], 'glm', 'the row\'s own fields did not change');
});

test('a binary that does not list consultantRow gets the question rows byte for byte as before', () => {
  const without = questionRowsOnTheWire(ALL.filter((one) => one !== FEATURES.consultantRow));

  assert.equal(without.some((one) => 'row' in one), false);
  assert.deepEqual(Object.keys(without[0] ?? {}), ['id', 'vendor', 'runtime', 'model', 'baseUrl', 'executablePath', 'key', 'prompt', 'enabled']);
});

test('a row that consults says, on its card, when this side\'s binary would drop its settings for a consultation', () => {
  // The epics 4–5 cadence consultation: a binary with every E2 field but no consultantRow takes the row's effort, prompt
  // and timeout for a REVIEW and drops them for a consultation — the card said nothing.
  const consulting = vendorsFrom([{ ...ROWS[1], enabled: true }])[0]!;
  const reviewing = vendorsFrom([{ id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true, effort: 'high' }])[0]!;
  const e2Only = { installed: true, features: ALL.filter((one) => one !== FEATURES.consultantRow) };

  assert.match(ignoredSaid(consulting, e2Only).join(' '), /when it consults/u);
  assert.equal(ignoredSaid(consulting, { installed: true, features: ALL }).length, 0, 'a binary that takes the row is told nothing');
  assert.ok(!ignoredSaid(reviewing, e2Only).join(' ').includes('consults'), 'a row that only reviews is not told about consultations');
});
