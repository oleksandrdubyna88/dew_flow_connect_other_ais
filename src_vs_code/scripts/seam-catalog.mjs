/**
 * The catalog leg of the settings seam (PLAN_one_model_catalog.md E1.4).
 *
 * <p>The migration moves consultant and question-consultant definitions into `coai.vendors` and promises that
 * coai-mcp is handed exactly what it was handed before. The unit suite proves the env block is the same string;
 * this proves it against the REAL binary, from the extension's own writer, over a multi-instance catalog: two rows
 * on one runtime, a consultant definition, a question-consultant definition, an effort and a system prompt on a CLI
 * row. Three things must hold, each of which has a way to fail silently:</p>
 *
 * <ul>
 *   <li>the settings FILE is byte-identical before and after the migration — the promise itself;</li>
 *   <li>the binary reads it without refusing anything, and lists the same reviewers — a row that serves only the
 *       consultant must not turn up as a provider in some round;</li>
 *   <li>the two rows on one runtime are both there, under their own ids.</li>
 * </ul>
 */
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJson } = await import('../out/serverSettingsFile.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { settingsFrom } = await import('../out/settingsShape.js');
const { migrateLayer } = await import('../out/catalogMigration.js');

/** The operator's shape: overlapping reviewers, a consultant and a question row of their own, catalog fields on a CLI row. */
const BEFORE = {
  vendors: [
    { id: 'claude', runtime: 'claude', model: '', enabled: true, effort: 'high', systemPrompt: 'Be terse.', name: 'Claude, terse' },
    { id: 'claude-2', runtime: 'claude', model: 'claude-sonnet-5-5', enabled: true, plan: true, code: false },
    { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true },
  ],
  consultants: {
    codex: { vendor: 'glm', runtime: 'codex', model: 'glm-5.3', baseUrl: 'https://glm.example.invalid/v1', executablePath: '' },
  },
  qconsultRows: [
    { id: 'q1', vendor: 'qwen', runtime: 'codex', model: 'qwen-max', baseUrl: 'https://qwen.example.invalid/v1', prompt: 'p', enabled: true, key: '' },
  ],
};

function after(layer) {
  const outcome = migrateLayer(layer);
  if (outcome.kind !== 'migrate') {
    throw new Error(`the migration did not run: ${JSON.stringify(outcome)}`);
  }

  return Object.fromEntries([...Object.entries(layer), ...outcome.writes.map((write) => [write.key, write.value])]);
}

/** The settings file the extension's own writer produces for one layer. */
function fileFor(layer) {
  const read = (key) => layer[key];

  return serverSettingsJson(settingsFrom(read), vendorsFrom(read('vendors')), '9.9.9');
}

/** @returns {Promise<{ ids: string[] }>} */
export async function catalogSeam({ providersIn, fail }) {
  const migrated = after(BEFORE);
  const before = fileFor(BEFORE);
  const now = fileFor(migrated);
  if (before !== now) {
    fail(`the migration changed the settings file coai-mcp reads.\n  before: ${before}\n  after:  ${now}`);
  }
  if (!vendorsFrom(migrated.vendors).some((row) => row.id === 'consult-codex')) {
    fail('the migration did not add the consultant row, so this leg compares two unmigrated files');
  }

  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-catalog-'));
  try {
    writeFileSync(join(dir, 'settings.json'), now, 'utf8');
    const answer = await providersIn({ COAI_DATA_DIR: dir, COAI_VENDORS: '', COAI_PROVIDERS: '', COAI_CONSULTANTS: '', COAI_QCONSULT_ROWS: '' });
    const ids = (answer.providers ?? []).map((p) => p.provider).sort();
    const expected = ['claude', 'claude-2', 'codex'];
    if (JSON.stringify(ids) !== JSON.stringify(expected)) {
      fail(`the server's reviewers are ${ids.join(', ')} — expected ${expected.join(', ')}; a catalog-only row must not be one`);
    }
    const refused = (answer.providers ?? []).filter((p) => /refus|unknown|not recogni/iu.test(String(p.note ?? '')));
    if (refused.length > 0) {
      fail(`the server refused part of the catalog: ${JSON.stringify(refused)}`);
    }
    if (readFileSync(join(dir, 'settings.json'), 'utf8') !== now) {
      fail('the settings file changed under the server');
    }

    return { ids };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
