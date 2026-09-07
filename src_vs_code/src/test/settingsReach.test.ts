import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CoaiSettings, DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { serverSettingsJson } from '../serverSettingsFile';
import { selectedFor } from '../prompts';

/**
 * No setting may be silently dropped on its way to the server.
 *
 * <p>The env block writes only what DIFFERS from the defaults, which keeps a pristine config
 * empty and readable — and makes forgetting one field invisible: the panel saves, the file has no
 * key, the server uses its own default, and nothing anywhere says so. This walks every field.</p>
 */
const CHANGED: { readonly [K in keyof CoaiSettings]: CoaiSettings[K] } = {
  rounds: { PlanCritique: 5, Architecture: 4, SecurityReliability: 4, UxDxPerformance: 4 },
  thresholds: { PlanCritique: 1, Architecture: 5, SecurityReliability: 5, UxDxPerformance: 5 },
  dealPlanLenses: true,
  onExhausted: 'escalate',
  maxConcurrency: 7,
  maxPerProvider: 4,
  reviewerTimeoutMinutes: 12,
  credsKey: 'coai-key',
  escalationMinutes: 45,
  promptsPerRound: { SecurityReliability: ['sec-attack'] },
  dealCodeLenses: true,
  codeWorkspace: 'worktree',
  autonomous: true,
  splitPlan: true,
  splitWithFable: true,
};

test('every setting, changed on its own, reaches the server file', () => {
  for (const field of Object.keys(DEFAULTS) as (keyof CoaiSettings)[]) {
    const settings = { ...DEFAULTS, [field]: CHANGED[field] } as CoaiSettings;
    const env = envBlock(settings, DEFAULT_VENDORS);
    const pristine = envBlock(DEFAULTS, DEFAULT_VENDORS);

    assert.notDeepEqual(
      env,
      pristine,
      `changing ${field} produced an identical env block — the setting would never reach the server`,
    );
  }
});

test('a pristine configuration writes nothing, so the block stays readable', () => {
  assert.deepEqual(envBlock(DEFAULTS, DEFAULT_VENDORS), {});
});

test('the file the panel writes is the shape the server parses', () => {
  const written = JSON.parse(serverSettingsJson({ ...DEFAULTS, ...CHANGED }, DEFAULT_VENDORS)) as Record<string, string>;

  // Keys are env names and values are STRINGS, because the server has one parser for the file and
  // the environment; a second encoding would be a second thing to keep in step.
  for (const [key, value] of Object.entries(written)) {
    assert.match(key, /^COAI_[A-Z_]+$/, `${key} is not an env-shaped key`);
    assert.equal(typeof value, 'string', `${key} must be a string, the env carries no other type`);
  }
  assert.equal(written['COAI_DEAL_PLAN'], 'true');
  assert.equal(written['COAI_DEAL_CODE'], 'true');
  assert.equal(written['COAI_CODE_WORKSPACE'], 'worktree');
  assert.deepEqual(JSON.parse(written['COAI_PROMPTS_PER_ROUND']!), { SecurityReliability: ['sec-attack'] });
});

test('a vendor added in the panel travels with its runtime and model', () => {
  const written = JSON.parse(
    serverSettingsJson(DEFAULTS, [{ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }]),
  ) as Record<string, string>;

  const vendors = JSON.parse(written['COAI_VENDORS']!) as { id: string; runtime: string; model: string }[];
  assert.equal(vendors[0]!.id, 'antigravity');
  assert.equal(vendors[0]!.runtime, 'antigravity', 'a runtime that does not travel runs the wrong vendor');
  assert.equal(vendors[0]!.model, 'gemini-3.7-flash-high');
});

test('a stored vendor keeps its runtime through the parser, not only through the type', () => {
  // This is the path that actually runs: VS Code hands back whatever JSON is stored, and
  // `vendorsFrom` decides what it means. A runtime the parser does not recognise falls back to
  // codex — which silently runs the wrong vendor's model under the right vendor's name.
  const stored = [{ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }];

  const parsed = vendorsFrom(stored);

  assert.equal(parsed[0]!.runtime, 'antigravity', 'the parser dropped a runtime this build supports');
  assert.equal(parsed[0]!.model, 'gemini-3.7-flash-high');
});

test('a runtime nobody knows still falls back to codex, which is the one that takes a base URL', () => {
  assert.equal(vendorsFrom([{ id: 'x', runtime: 'llama.cpp', baseUrl: 'https://x/v1' }])[0]!.runtime, 'codex');
});

test('a round nobody chose is padded with nothing, not with what it resolves to today', () => {
  // The panel pads the array up to the round being set. Padding with a RESOLVED id would freeze
  // today's default into a stored choice, so a later change to the default would never reach a
  // person who had once set round 3 — a fix that was made, lost to a careless whole-file write,
  // and only noticed because the panel was re-read.
  const stored: Record<string, string[]> = {};
  const rounds = [...(stored['Architecture'] ?? [])];
  while (rounds.length < 3) {
    rounds.push('');
  }
  rounds[2] = 'arch-evolution';

  assert.deepEqual(rounds, ['', '', 'arch-evolution']);
  // An empty slot still resolves the way the server resolves it: conventions in round 1 when the
  // repository has written rules, that role's universal prompt when it has none.
  assert.equal(selectedFor('Architecture', 1, { Architecture: rounds }), 'conventions');
  assert.equal(selectedFor('Architecture', 1, { Architecture: rounds }, false), 'architecture');
  assert.equal(selectedFor('Architecture', 2, { Architecture: rounds }), 'architecture');
  assert.equal(selectedFor('Architecture', 3, { Architecture: rounds }), 'arch-evolution');
});

/**
 * A Team-server row carries TWO names and the server needs the second one.
 *
 * <p>The row id is `<server>-<vendor>` because it must stay unique across servers — it names the
 * row, its usage history and its vault key. What `--vendor` must carry is the SERVER's own
 * spelling. `vendorsEnv` wrote only the first for the whole life of the `remote` runtime, so every
 * Team-server review was refused as a vendor the server "does not offer", which reads exactly like
 * a typo in a name nobody typed. `research/architecture.md` predicted this failure in those words
 * before it happened.</p>
 */
const REMOTE_ROW = {
  id: 'remsoftdev-claude',
  runtime: 'remote',
  teamServerId: 'remsoftdev',
  remoteVendor: 'claude',
  model: 'haiku',
  enabled: true,
  plan: true,
  code: true,
  baseUrl: 'https://coai.remsoft.dev',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
} as const;

test('a Team-server row carries the name its SERVER knows it by, not only the row id', () => {
  const written = JSON.parse(serverSettingsJson(DEFAULTS, [REMOTE_ROW])) as Record<string, string>;
  const vendors = JSON.parse(written['COAI_VENDORS']!) as { id: string; runtime: string; remoteVendor?: string }[];

  assert.equal(vendors[0]!.runtime, 'remote');
  assert.equal(
    vendors[0]!.remoteVendor,
    'claude',
    'without this the server is sent the row id and refuses it as a vendor it does not offer',
  );
});

test('the row id is NOT what travels as the vendor name — the two are different on purpose', () => {
  const vendors = JSON.parse(
    (JSON.parse(serverSettingsJson(DEFAULTS, [REMOTE_ROW])) as Record<string, string>)['COAI_VENDORS']!,
  ) as { id: string; remoteVendor?: string }[];

  assert.equal(vendors[0]!.id, 'remsoftdev-claude');
  // Typed, then compared. `notEqual` alone passes while the field is absent entirely, which is the
  // very defect this file is here about — an assertion that holds in the broken state is decoration.
  assert.equal(typeof vendors[0]!.remoteVendor, 'string');
  assert.notEqual(vendors[0]!.remoteVendor, vendors[0]!.id);
});

test('a codex row gains no remoteVendor key at all, so the file a person opens is unchanged', () => {
  const codex = { ...REMOTE_ROW, id: 'codex', runtime: 'codex', baseUrl: '', model: 'gpt-5.6-luna' } as Record<string, unknown>;
  delete codex['remoteVendor'];
  delete codex['teamServerId'];

  const vendors = JSON.parse(
    (JSON.parse(serverSettingsJson(DEFAULTS, vendorsFrom([codex]))) as Record<string, string>)['COAI_VENDORS']!,
  ) as object[];

  assert.ok(!('remoteVendor' in vendors[0]!), 'an empty key on every codex row is noise that means nothing');
});

test('teamServerId stays on this side — the server has no field for it and no question it answers', () => {
  const vendors = JSON.parse(
    (JSON.parse(serverSettingsJson(DEFAULTS, [REMOTE_ROW])) as Record<string, string>)['COAI_VENDORS']!,
  ) as object[];

  assert.ok(!('teamServerId' in vendors[0]!), 'the server would carry a field it cannot use');
});
