import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import {
  DEFAULT_QCONSULT,
  MAX_ACTIVE_ROWS,
  QCONSULT_MODES,
  QCONSULT_SETTINGS,
  QCONSULT_SINCE,
  qconsultEnv,
  qconsultSettingsFrom,
  qconsultSkewNote,
} from '../qconsultSettings';
import { DEFAULTS, OVERLAID_SETTINGS, envBlock, settingsFrom } from '../settingsShape';

/**
 * The question consultant's eight settings (todo/PLAN_question_consultant.md, S4 acceptance 1).
 *
 * <p>The server half shipped in S2/S3 (`QuestionConsultSettings`, `QuestionConsultReader`, the
 * `COAI_QCONSULT_*` keys). What these pin is the seam: the panel's defaults ARE the server's, read out of the
 * C# rather than transcribed, because `envBlock` writes a key only when it differs — and a panel default that
 * drifted from the server's would display a consultant nobody runs.</p>
 */

const mcp = (...parts: string[]): string => path.join(__dirname, '..', '..', '..', 'src_mcp', ...parts);
const settingsCs = fs.readFileSync(mcp('src', 'Server', 'QuestionConsult', 'QuestionConsultSettings.cs'), 'utf8');
const rowCs = fs.readFileSync(mcp('core', 'QuestionConsult', 'QuestionRow.cs'), 'utf8');

const reader = (values: Record<string, unknown>) => (key: string): unknown => values[key];

/** `public const int DefaultRowMinutes = 5;` -> 5, out of the C#. */
function serverConstant(source: string, name: string): number {
  const m = new RegExp(`const int ${name}\\s*=\\s*(\\d+);`).exec(source);
  assert.ok(m, `${name} is not in the C# in the shape this test reads`);

  return Number(m[1]);
}

/** Every `public const string X = "COAI_QCONSULT_…";` of `QuestionConsultKeys`, in declaration order — derived, never retyped. */
function serverKeys(): readonly string[] {
  const block = /public static class QuestionConsultKeys\s*\{([^}]*)\}/.exec(settingsCs);
  assert.ok(block, 'QuestionConsultKeys is not in QuestionConsultSettings.cs in the shape this test reads');

  return [...(block[1] ?? '').matchAll(/"(COAI_QCONSULT_[A-Z_]+)"/g)].map((m) => m[1]!);
}

test("every question-consultant default in the panel is the server's own", () => {
  assert.equal(DEFAULT_QCONSULT.rowMinutes, serverConstant(settingsCs, 'DefaultRowMinutes'));
  assert.equal(DEFAULT_QCONSULT.questionsPerSession, serverConstant(settingsCs, 'DefaultQuestionsPerSession'));
  assert.equal(DEFAULT_QCONSULT.freeBatches, serverConstant(settingsCs, 'DefaultFreeBatches'));
  assert.equal(MAX_ACTIVE_ROWS, serverConstant(rowCs, 'MaxActive'));
  assert.match(settingsCs, /public bool Enabled \{ get; init; \} = true;/);
  assert.equal(DEFAULT_QCONSULT.enabled, true);
  assert.match(settingsCs, /public string Mode \{ get; init; \} = Modes\.Require;/);
  assert.equal(DEFAULT_QCONSULT.mode, 'require');
  // The lists default empty on both sides: no row, no custom prompt, no root.
  assert.match(settingsCs, /IReadOnlyList<QuestionRow> Rows \{ get; init; \} = \[\];/);
  assert.match(settingsCs, /IReadOnlyList<string> Roots \{ get; init; \} = \[\];/);
  assert.deepEqual([DEFAULT_QCONSULT.rows, DEFAULT_QCONSULT.prompts, DEFAULT_QCONSULT.roots], [[], [], []]);
});

test('the modes the panel offers are the modes the server parses', () => {
  const declared = [...settingsCs.matchAll(/public const string (Off|Remind|Require) = "([a-z]+)";/g)].map((m) => m[2]);
  assert.deepEqual(declared, [...QCONSULT_MODES]);
});

/** Every setting changed on its own, and the key the panel must then write — the server's keys, in their order. */
const CHANGED: readonly (readonly [Partial<typeof DEFAULT_QCONSULT>, string, string])[] = [
  [{ enabled: false }, 'COAI_QCONSULT_ENABLED', 'false'],
  [{ mode: 'remind' }, 'COAI_QCONSULT_MODE', 'remind'],
  [{ rows: [{ id: 'sonnet-disk', vendor: 'claude', runtime: 'claude', model: 'sonnet', baseUrl: '', executablePath: '', key: '', prompt: 'question-disk', enabled: true }] },
    'COAI_QCONSULT_ROWS', '[{"id":"sonnet-disk","vendor":"claude","runtime":"claude","model":"sonnet","baseUrl":"","executablePath":"","key":"","prompt":"question-disk","enabled":true}]'],
  [{ prompts: [{ id: 'ask-the-docs', title: 'The docs', capability: 'web', text: 'Read the vendor docs.' }] },
    'COAI_QCONSULT_PROMPTS', '[{"id":"ask-the-docs","title":"The docs","capability":"web","text":"Read the vendor docs."}]'],
  [{ roots: ['D:/projects'] }, 'COAI_QCONSULT_ROOTS', '["D:/projects"]'],
  [{ rowMinutes: 3 }, 'COAI_QCONSULT_ROW_MINUTES', '3'],
  [{ questionsPerSession: 4 }, 'COAI_QCONSULT_QUESTIONS_PER_SESSION', '4'],
  [{ freeBatches: 1 }, 'COAI_QCONSULT_FREE_BATCHES', '1'],
];

test('the panel writes exactly the keys the server reads — none more, none fewer', () => {
  assert.deepEqual(CHANGED.map(([, key]) => key), serverKeys(), 'a key the server reads that this panel never writes, or the reverse');
});

for (const [changed, key, value] of CHANGED) {
  test(`${key}, changed alone, reaches the server file and nothing else does`, () => {
    const env = envBlock({ ...DEFAULTS, qconsult: { ...DEFAULT_QCONSULT, ...changed } });

    assert.deepEqual(Object.entries(env).filter(([k]) => k.startsWith('COAI_QCONSULT_')), [[key, value]]);
  });
}

test('a pristine panel writes no question-consultant key', () => {
  assert.deepEqual(Object.keys(envBlock(DEFAULTS)).filter((k) => k.startsWith('COAI_QCONSULT_')), []);
});

test('a server known to be older than QCONSULT_SINCE is sent none of it, and the section says why', () => {
  const settings = { ...DEFAULT_QCONSULT, mode: 'off' as const, roots: ['D:/projects'] };

  assert.deepEqual(qconsultEnv(settings, '0.40.5'), {});
  assert.deepEqual(Object.keys(envBlock({ ...DEFAULTS, qconsult: settings }, undefined, '0.40.5')).filter((k) => k.startsWith('COAI_QCONSULT_')), []);
  assert.match(qconsultSkewNote('0.40.5'), new RegExp(`update it to ${QCONSULT_SINCE.replace(/\./g, '\\.')}`));
  // Unknown is not old, and the version that ships it is not old either.
  assert.deepEqual(Object.keys(qconsultEnv(settings, '')), ['COAI_QCONSULT_MODE', 'COAI_QCONSULT_ROOTS']);
  assert.deepEqual(Object.keys(qconsultEnv(settings, QCONSULT_SINCE)), ['COAI_QCONSULT_MODE', 'COAI_QCONSULT_ROOTS']);
  assert.equal(qconsultSkewNote(''), '');
  assert.equal(qconsultSkewNote(QCONSULT_SINCE), '');
});

test('the reader reads as the server does: any case of a mode, a positive count, and at most six rows on', () => {
  const row = (id: string): Record<string, unknown> => ({ id, vendor: 'claude', runtime: 'claude', prompt: 'question-opinion' });
  const read = qconsultSettingsFrom(reader({
    qconsultMode: ' Remind ',
    qconsultRowMinutes: 0,
    qconsultQuestionsPerSession: 2.5,
    qconsultRows: ['not a row', { vendor: 'no id' }, ...['a', 'b', 'c', 'd', 'e', 'f', 'g'].map(row)],
  }));

  assert.equal(read.mode, 'remind');
  assert.equal(read.rowMinutes, DEFAULT_QCONSULT.rowMinutes, 'IntVar: zero is the default');
  assert.equal(read.questionsPerSession, DEFAULT_QCONSULT.questionsPerSession);
  assert.deepEqual(read.rows.map((r) => r.id), ['a', 'b', 'c', 'd', 'e', 'f', 'g'], 'a row with no id is dropped, every other is kept');
  assert.deepEqual(read.rows.map((r) => r.enabled), [true, true, true, true, true, true, false], 'the seventh is off, as the server switches it off');
  assert.equal(qconsultSettingsFrom(reader({ qconsultMode: 'always' })).mode, 'require', "the server's catch-all");
});

test('every key is a declared setting whose manifest default is the default here', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: { properties: Record<string, { default?: unknown }> } };
  };
  const properties = manifest.contributes.configuration.properties;
  const expected: Record<string, unknown> = {
    qconsultEnabled: DEFAULT_QCONSULT.enabled, qconsultMode: DEFAULT_QCONSULT.mode, qconsultRows: [], qconsultPrompts: [],
    qconsultRoots: [], qconsultRowMinutes: DEFAULT_QCONSULT.rowMinutes,
    qconsultQuestionsPerSession: DEFAULT_QCONSULT.questionsPerSession, qconsultFreeBatches: DEFAULT_QCONSULT.freeBatches,
  };

  assert.deepEqual([...QCONSULT_SETTINGS].sort(), Object.keys(expected).sort());
  for (const key of QCONSULT_SETTINGS) {
    assert.ok(properties[`coai.${key}`] !== undefined, `coai.${key} is not declared, so VS Code would refuse to keep it`);
    assert.deepEqual(properties[`coai.${key}`]!.default, expected[key], `coai.${key}'s manifest default`);
  }
});

test('the settings are a side’s own, like the consultant’s', () => {
  for (const key of QCONSULT_SETTINGS) {
    assert.ok(OVERLAID_SETTINGS.includes(key), `${key} would stay shared between the sides of one machine`);
  }
  assert.deepEqual(settingsFrom(reader({})).qconsult, DEFAULT_QCONSULT);
});
