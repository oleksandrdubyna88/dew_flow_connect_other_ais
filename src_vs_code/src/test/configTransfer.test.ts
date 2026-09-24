import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  CONFIG_FORMAT,
  CONFIG_VERSION,
  configFile,
  declaredSettings,
  exportedSettings,
  importQuestion,
  importedConfig,
  withLocalPaths,
  type Declared,
} from '../configTransfer';

/**
 * Export config / Import config — issue #467. Everything a person edited travels in a JSON file; a secret,
 * a path on this machine and a per-side override never do.
 */

const declared: Declared = {
  reviewTimeoutSeconds: { default: 600 },
  vendors: { default: [] },
  consultants: { default: {} },
  credsKey: { default: '' },
  dataDirectory: { default: '' },
  teamServers: { default: [] },
};

const base = (values: Record<string, unknown>) => (key: string): unknown => values[key];

test('a setting left at its default is not exported, and a changed one is', () => {
  const out = exportedSettings(declared, base({ reviewTimeoutSeconds: 900 }));

  assert.deepEqual(out, { reviewTimeoutSeconds: 900 });
  assert.deepEqual(exportedSettings(declared, base({ reviewTimeoutSeconds: 600 })), {}, 'the default was exported');
});

test('a value set equal to its default, in another key order, is still the default', () => {
  const withDefault: Declared = { thing: { default: { a: 1, b: 2 } } };

  assert.deepEqual(exportedSettings(withDefault, base({ thing: { b: 2, a: 1 } })), {});
});

test('a secret and this machine\u2019s layout are never exported, whatever they hold', () => {
  const out = exportedSettings(declared, base({ credsKey: 'k-123', dataDirectory: 'D:/somewhere' }));

  assert.deepEqual(out, {});
});

test('a path on this machine inside a vendor or a consultant is removed, the rest of the entry kept', () => {
  const out = exportedSettings(declared, base({
    vendors: [{ id: 'codex', model: 'm', executablePath: 'C:/tools/codex.cmd' }],
    consultants: { claude: { vendor: 'codex', executablePath: 'C:/tools/codex.cmd' } },
  }));

  assert.deepEqual(out['vendors'], [{ id: 'codex', model: 'm' }]);
  assert.deepEqual(out['consultants'], { claude: { vendor: 'codex' } });
  assert.equal(JSON.stringify(out).includes('executablePath'), false);
});

test('a Team-server URL is configuration a person typed, and is exported', () => {
  const servers = [{ id: 'acme', name: 'Acme', url: 'https://coai.acme.example' }];

  assert.deepEqual(exportedSettings(declared, base({ teamServers: servers }))['teamServers'], servers);
});

test('an export read back gives the same settings and prompts', () => {
  const settings = exportedSettings(declared, base({ reviewTimeoutSeconds: 900, teamServers: [{ id: 'a', url: 'u' }] }));
  const prompts = { 'role2-general': 'Is the requirement met?', 'consult': 'Be brief.' };
  const text = JSON.stringify(configFile(settings, prompts, '2026-09-24T10:00:00Z'));

  const back = importedConfig(text, declared);

  assert.ok(back.ok, JSON.stringify(back));
  assert.deepEqual(back.settings, settings);
  assert.deepEqual(back.prompts, prompts);
  assert.deepEqual(back.refused, []);
});

test('a file that is not a config, or of another version, is refused with a sentence', () => {
  assert.deepEqual(importedConfig('not json', declared), { ok: false, why: 'The file is not JSON.' });
  const other = importedConfig(JSON.stringify({ format: 'something-else', version: 1, settings: {}, prompts: {} }), declared);
  assert.equal(other.ok, false);
  const newer = importedConfig(JSON.stringify({ format: CONFIG_FORMAT, version: CONFIG_VERSION + 1, settings: {}, prompts: {} }), declared);
  assert.equal(newer.ok, false);
  assert.match(newer.ok ? '' : newer.why, /version 2/);
});

test('an unknown key, a never-transferred key and a bad prompt name are refused by name, WITH a reason, and the rest applies', () => {
  const text = JSON.stringify({
    format: CONFIG_FORMAT, version: CONFIG_VERSION, exportedAt: 'x', note: '',
    settings: { reviewTimeoutSeconds: 900, fromANewerBuild: true, credsKey: 'stolen' },
    prompts: { 'good-one': 'text', '../escape': 'x', 'con': 'x' },
  });

  const back = importedConfig(text, declared);

  assert.ok(back.ok);
  assert.deepEqual(back.settings, { reviewTimeoutSeconds: 900 });
  assert.deepEqual(back.prompts, { 'good-one': 'text' });
  const why = Object.fromEntries(back.refused.map((one) => [one.name, one.why]));
  assert.match(why['fromANewerBuild'] ?? '', /unknown to this build/);
  assert.match(why['credsKey'] ?? '', /never transferred: a secret/);
  assert.match(why['../escape'] ?? '', /not a valid prompt name/);
  assert.match(why['con'] ?? '', /not a valid prompt name/);
});

test('an import keeps this machine\u2019s CLI paths for the entries it already has', () => {
  // Export leaves them out, so a whole-value import would wipe every local path. (gemini, the plan round.)
  const current = [{ id: 'codex', executablePath: 'C:/tools/codex.cmd' }, { id: 'gone', executablePath: 'x' }];
  const imported = [{ id: 'codex', model: 'new' }, { id: 'fresh', model: 'm' }];

  assert.deepEqual(withLocalPaths(imported, current), [
    { id: 'codex', model: 'new', executablePath: 'C:/tools/codex.cmd' },
    { id: 'fresh', model: 'm' },
  ]);
  assert.deepEqual(
    withLocalPaths({ claude: { vendor: 'codex' } }, { claude: { vendor: 'x', executablePath: 'C:/a.cmd' } }),
    { claude: { vendor: 'codex', executablePath: 'C:/a.cmd' } },
  );
  assert.equal(withLocalPaths(900, 600), 900, 'a plain value is imported as it is');
});

test('the confirmation counts, names what replaces existing text, and gives every refusal its reason', () => {
  const back = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: CONFIG_VERSION, settings: { reviewTimeoutSeconds: 900, nope: 1 },
    prompts: { 'a-one': 't', 'b-two': 't' },
  }), declared);
  assert.ok(back.ok);

  const asked = importQuestion(back, 'mine.json', ['a-one']);

  assert.match(asked, /Apply 1 setting\(s\) and 2 prompt text\(s\) from mine\.json\?/);
  assert.match(asked, /1 of the prompt texts replace text you already have/);
  assert.match(asked, /Not applied: nope \(unknown to this build\)/);
});

test('a CLI path inside an imported file is never applied, and the confirmation says it was left out', () => {
  // A config from someone else naming `executablePath` for a vendor this machine does not have yet would
  // otherwise be written into the settings, and the next round would RUN that binary. (gemini, code round.)
  const back = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: CONFIG_VERSION, prompts: {},
    settings: { vendors: [{ id: 'fresh', executablePath: 'C:/Users/Public/evil.cmd' }], consultants: { a: { executablePath: '/tmp/x' } } },
  }), declared);

  assert.ok(back.ok);
  assert.equal(JSON.stringify(back.settings).includes('executablePath'), false, JSON.stringify(back.settings));
  assert.deepEqual(back.refused.map((one) => one.name), ['consultants → executablePath', 'vendors → executablePath']);
  assert.match(back.refused[0]?.why ?? '', /a path on the machine that wrote the file/);
});

test('a value of the wrong kind for a setting is refused by name, and the rest applies', () => {
  const typed: Declared = {
    reviewTimeoutSeconds: { default: 600, type: 'number' },
    vendors: { default: [], type: 'array' },
    either: { default: 'x', type: ['string', 'null'] },
    rounds: { default: 3, type: 'integer' },
  };
  const back = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: CONFIG_VERSION, prompts: {},
    settings: { reviewTimeoutSeconds: 'soon', vendors: 'codex', either: null, rounds: 2.5 },
  }), typed);

  assert.ok(back.ok);
  assert.deepEqual(back.settings, { either: null });
  const why = Object.fromEntries(back.refused.map((one) => [one.name, one.why]));
  assert.match(why['reviewTimeoutSeconds'] ?? '', /its value is not a number, which this build declares/);
  assert.match(why['vendors'] ?? '', /its value is not a array|its value is not an array/);
  assert.match(why['rounds'] ?? '', /not a integer|not an integer/);
});

test('a setting that differs from its default ONLY by a local path is the default, and is not exported', () => {
  const withEntry: Declared = { vendors: { default: [{ id: 'codex' }] } };

  assert.deepEqual(exportedSettings(withEntry, base({ vendors: [{ id: 'codex', executablePath: 'C:/tools/codex.cmd' }] })), {});
});

test('a key every object inherits is unknown to this build, not a never-transferred one', () => {
  // `key in NEVER_TRANSFERRED` answers true for `toString`, which then reads a FUNCTION as the reason.
  const back = importedConfig(JSON.stringify({
    format: CONFIG_FORMAT, version: CONFIG_VERSION, prompts: {}, settings: { toString: 1, constructor: 2 },
  }), declared);

  assert.ok(back.ok);
  assert.deepEqual(back.refused, [
    { name: 'toString', why: 'unknown to this build' },
    { name: 'constructor', why: 'unknown to this build' },
  ]);
});

test('a section that is there but is not an object is refused with the sentence, not a crash', () => {
  const back = importedConfig(JSON.stringify({ format: CONFIG_FORMAT, version: CONFIG_VERSION, settings: null, prompts: [] }), declared);

  assert.deepEqual(back, { ok: false, why: 'The file has no settings or no prompts section.' });
});

// ---------- the classification, against the REAL manifest ----------

test('no setting the manifest declares that looks like a secret can be exported', () => {
  // Classified, not listed from memory: a secret added tomorrow must fail here rather than leak through a
  // list nobody updated. (codex, the plan round.) By NAME, and by any property of a value's schema.
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: { properties: Record<string, unknown> } | { properties: Record<string, unknown> }[] };
  };
  const sections = Array.isArray(manifest.contributes.configuration) ? manifest.contributes.configuration : [manifest.contributes.configuration];
  const secret = /(key|token|secret|password)$/i;
  const secretKeys: string[] = [];

  for (const section of sections) {
    for (const [full, schema] of Object.entries(section.properties)) {
      const key = full.replace(/^coai\./, '');
      const inner = JSON.stringify(schema).match(/"([A-Za-z]+)":\{"type"/g) ?? [];
      const secretInside = inner.map((one) => one.slice(2, one.indexOf('"', 2))).filter((name) => secret.test(name));
      if (secret.test(key)) {
        secretKeys.push(key);
      }
      assert.deepEqual(secretInside, [], `${key} carries a secret-looking field (${secretInside.join(', ')}) that would be exported`);
    }
  }

  // Not "is it on a list" but "does an export carry it": every secret-looking key set, and none comes out.
  const exported = exportedSettings(declaredSettings(manifest), (key) => (secretKeys.includes(key) ? 'a-real-secret' : undefined));
  assert.deepEqual(Object.keys(exported), [], `exported: ${Object.keys(exported).join(', ')}`);
  assert.ok(secretKeys.includes('credsKey'), 'the walk found no secret at all, so it is walking the wrong thing');
});

test('the declared settings are read from the manifest with their defaults', () => {
  const manifest: unknown = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));

  const out = declaredSettings(manifest);

  assert.ok(Object.keys(out).length > 30, `only ${Object.keys(out).length} settings read`);
  assert.equal(Object.keys(out).some((key) => key.startsWith('coai.')), false, 'the prefix was kept');
  assert.deepEqual(declaredSettings(undefined), {});
  assert.deepEqual(
    declaredSettings({ contributes: { configuration: { properties: { 'coai.x': { default: 3, type: 'number' }, 'coai.y': { default: 1 } } } } }),
    { x: { default: 3, type: 'number' }, y: { default: 1, type: undefined } },
  );
});
