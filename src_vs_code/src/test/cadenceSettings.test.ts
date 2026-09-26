import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { CADENCE_MODES, CADENCE_SETTINGS, DEFAULT_CADENCE, cadenceBlock, cadenceSettingsFrom } from '../cadenceSettings';
import { DEFAULTS, OVERLAID_SETTINGS, envBlock } from '../settingsShape';

/**
 * The consultation cadence's four settings — the operator's "make 3 and 5 editable in the panel"
 * (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.1).
 *
 * <p>The server half shipped in epic 3 (`PanelSettings.CadenceMode` and the three counts, read from
 * `COAI_CADENCE_*`). What these pin is the seam: the panel's defaults ARE the server's fallbacks,
 * read out of the C# rather than transcribed, because `envBlock` writes a key only when it differs —
 * and a panel default that drifted from the server's would display a cadence nobody runs.</p>
 */

const mcp = (...parts: string[]): string => path.join(__dirname, '..', '..', '..', 'src_mcp', ...parts);
const rule = fs.readFileSync(mcp('core', 'Cadence', 'CadenceRule.cs'), 'utf8');
const panelSettings = fs.readFileSync(mcp('src', 'Server', 'PanelSettings.cs'), 'utf8');

/** `public const int DefaultEvery = 3;` -> 3. */
function serverConstant(name: string): number {
  const m = new RegExp(`const int ${name}\\s*=\\s*(\\d+);`).exec(rule);

  assert.ok(m, `${name} is not in CadenceRule.cs in the shape this test reads`);
  return Number(m[1]);
}

/** A reader over a plain object, the shape `settingsShape` hands every module. */
const reader = (values: Record<string, unknown>) => (key: string): unknown => values[key];

test("every cadence default in the panel is the server's own fallback", () => {
  assert.equal(DEFAULT_CADENCE.every, serverConstant('DefaultEvery'));
  assert.equal(DEFAULT_CADENCE.riskThreshold, serverConstant('DefaultRiskThreshold'));
  assert.equal(DEFAULT_CADENCE.riskMax, serverConstant('DefaultRiskMax'));

  // The mode's fallback is the property initialiser AND the parser's catch-all — both must say remind.
  assert.equal(DEFAULT_CADENCE.mode, 'remind');
  assert.match(panelSettings, /CadenceMode CadenceMode \{ get; init; \} = Core\.Cadence\.CadenceMode\.Remind;/);
  assert.match(panelSettings, /: Core\.Cadence\.CadenceMode\.Remind;/);
});

test('the modes the panel offers are the modes the server parses', () => {
  const declared = /public enum CadenceMode\s*\{([^}]*)\}/.exec(rule);

  assert.ok(declared, 'CadenceMode is not in CadenceRule.cs in the shape this test reads');
  // A member is a line of its own; the doc comments between them are sentences with capitals in them.
  assert.deepEqual(
    [...(declared[1] ?? '').matchAll(/^\s*([A-Z][a-z]+),?\s*$/gm)].map((one) => one[1]!.toLowerCase()),
    [...CADENCE_MODES],
    'the panel would offer a mode the server does not know, or hide one it does',
  );
});

test('each count is read from the key the server reads it from', () => {
  for (const key of ['COAI_CADENCE_EVERY', 'COAI_CADENCE_RISK_THRESHOLD', 'COAI_CADENCE_RISK_MAX']) {
    assert.match(panelSettings, new RegExp(`IntVar\\(env, "${key}", Core\\.Cadence\\.CadenceRule\\.Default`), key);
  }
  assert.match(panelSettings, /CadenceMode = "COAI_CADENCE_MODE"/);
});

test('a pristine panel writes no cadence key', () => {
  assert.deepEqual(Object.keys(envBlock(DEFAULTS)).filter((k) => k.startsWith('COAI_CADENCE')), []);
});

test('a changed cadence reaches the server, each value under its own key', () => {
  const env = envBlock({ ...DEFAULTS, cadence: { mode: 'require', every: 4, riskThreshold: 6, riskMax: 2 } });

  assert.equal(env['COAI_CADENCE_MODE'], 'require');
  assert.equal(env['COAI_CADENCE_EVERY'], '4');
  assert.equal(env['COAI_CADENCE_RISK_THRESHOLD'], '6');
  assert.equal(env['COAI_CADENCE_RISK_MAX'], '2');
});

test('switching the cadence off reaches the server too', () => {
  assert.equal(envBlock({ ...DEFAULTS, cadence: { ...DEFAULT_CADENCE, mode: 'off' } })['COAI_CADENCE_MODE'], 'off');
});

test('a count the server would refuse is the default here as well — zero, negative, fractional, too big', () => {
  // The server's `IntVar` takes a positive int or its fallback; a panel that showed 0 while the server
  // ran 3 would be the drift the first test exists to prevent, reached through settings.json.
  for (const bad of [0, -1, 2.5, 'three', 2_147_483_648, null]) {
    const read = cadenceSettingsFrom(reader({ cadenceEvery: bad, cadenceRiskThreshold: bad, cadenceRiskMax: bad }));

    assert.equal(read.every, DEFAULT_CADENCE.every, String(bad));
    assert.equal(read.riskThreshold, DEFAULT_CADENCE.riskThreshold, String(bad));
    assert.equal(read.riskMax, DEFAULT_CADENCE.riskMax, String(bad));
  }
});

test('a stored mode the panel does not know reads as remind, as the server reads it — and writes nothing', () => {
  const read = cadenceSettingsFrom(reader({ cadenceMode: 'urgent' }));

  assert.equal(read.mode, 'remind');
  assert.deepEqual(Object.keys(envBlock({ ...DEFAULTS, cadence: read })).filter((k) => k.startsWith('COAI_CADENCE')), []);
});

test('a stored mode is read whatever its case, as the server parses it', () => {
  assert.equal(cadenceSettingsFrom(reader({ cadenceMode: ' Require ' })).mode, 'require');
});

test('the cadence is a fact about the work, so each side keeps its own', () => {
  for (const key of CADENCE_SETTINGS) {
    assert.ok(OVERLAID_SETTINGS.includes(key), `${key} would stay shared between sides`);
  }
});

test('the panel block offers the three modes with the current one chosen, and the three counts', () => {
  const html = cadenceBlock({ mode: 'require', every: 4, riskThreshold: 6, riskMax: 2 });

  for (const mode of CADENCE_MODES) {
    assert.match(html, new RegExp(`data-setting="cadenceMode" value="${mode}"`));
  }
  assert.match(html, /value="require" checked/);
  assert.match(html, /data-setting="cadenceEvery" value="4"/);
  assert.match(html, /data-setting="cadenceRiskThreshold" value="6"/);
  assert.match(html, /data-setting="cadenceRiskMax" value="2"/);
});

test('the four settings are declared with the defaults the panel and the server share', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: { properties: Record<string, { default?: unknown; enum?: unknown[]; minimum?: number }> } };
  };
  const declared = manifest.contributes.configuration.properties;

  assert.equal(declared['coai.cadenceMode']?.default, DEFAULT_CADENCE.mode);
  assert.deepEqual(declared['coai.cadenceMode']?.enum, [...CADENCE_MODES]);
  assert.equal(declared['coai.cadenceEvery']?.default, DEFAULT_CADENCE.every);
  assert.equal(declared['coai.cadenceRiskThreshold']?.default, DEFAULT_CADENCE.riskThreshold);
  assert.equal(declared['coai.cadenceRiskMax']?.default, DEFAULT_CADENCE.riskMax);
  for (const key of ['coai.cadenceEvery', 'coai.cadenceRiskThreshold', 'coai.cadenceRiskMax']) {
    assert.equal(declared[key]?.minimum, 1, `${key}: the server refuses anything under one`);
  }
});
