import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  DEFAULT_VENDORS,
  freeVendorId,
  normaliseId,
  presetsOffered,
  reviewerPickItems,
  VENDOR_PRESETS,
  vendorsEnv,
  vendorsFrom,
} from '../vendors';

test('every default vendor can be added back after being removed', () => {
  // The one-way door the operator walked through: gemini shipped as a default but was missing
  // from the presets, so removing it was permanent.
  for (const vendor of DEFAULT_VENDORS) {
    assert.ok(
      VENDOR_PRESETS.some((p) => p.id === vendor.id),
      `${vendor.id} is a default but cannot be re-added`,
    );
  }
});

test('the presets keep one blank endpoint entry, so the list is never the limit', () => {
  assert.equal(VENDOR_PRESETS.filter((p) => p.id.length === 0).length, 1);
});

test('a stored list of nothing runnable falls back to the defaults', () => {
  assert.deepEqual(vendorsFrom([{ id: '  ' }]), [...DEFAULT_VENDORS]);
  assert.deepEqual(vendorsFrom('not a list'), [...DEFAULT_VENDORS]);
});

test('ids are normalised the same way everywhere they are used', () => {
  assert.equal(normaliseId('  My Vendor! '), 'my-vendor');
});

test('a disabled vendor never reaches the server', () => {
  const env = vendorsEnv([...DEFAULT_VENDORS.map((v) => ({ ...v, enabled: v.id === 'codex' }))]);
  assert.ok(env.includes('codex'));
  assert.ok(!env.includes('antigravity'));
});

// ---------- the retirement, and the migration it needs ----------
//
// The Antigravity ADAPTER shipped and nothing used it: no preset offered it, the defaults still
// named gemini, and a list saved before the retirement went on naming a CLI that now refuses
// before it reaches a model. Supporting a vendor and switching to it are different changes.

test('antigravity can be added from the panel', () => {
  const preset = VENDOR_PRESETS.find((p) => p.runtime === 'antigravity');
  assert.ok(preset, 'the adapter exists but the panel offers no way to choose it');
  assert.ok(preset.model.length > 0, 'a preset with no model leaves the CLI to guess');
});

test('a fresh install reviews with nothing retired', () => {
  assert.ok(
    !DEFAULT_VENDORS.some((v) => v.runtime === 'gemini'),
    'a default is what an install runs before anybody configures anything',
  );
  assert.ok(DEFAULT_VENDORS.some((v) => v.runtime === 'antigravity'));
});

test('a reviewer saved before the retirement is migrated, keeping its name', () => {
  // The id is what names the row, the usage history and the vault key — migrating the RUNTIME
  // moves the vendor to the CLI Google pointed at; renaming it would orphan all three.
  const migrated = vendorsFrom([
    { id: 'gemini', runtime: 'gemini', model: 'gemini-flash-latest', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ]);

  assert.equal(migrated[0]!.id, 'gemini');
  assert.equal(migrated[0]!.runtime, 'antigravity');
  assert.ok(
    migrated[0]!.model !== 'gemini-flash-latest',
    'a model id from the old CLI is not one the new CLI lists',
  );
});

test('a vendor riding its own endpoint is never migrated', () => {
  const kept = vendorsFrom([
    { id: 'mine', runtime: 'gemini', model: 'x', enabled: true, plan: true, code: true, baseUrl: 'https://example.test/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ]);
  assert.equal(kept[0]!.runtime, 'gemini', 'a base URL means the vendor is not Google’s at all');
});

// ---------- where a vendor's CLI actually is ----------

test('a vendor carries the path to its own CLI', () => {
  const stored = [{ id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '/home/user/.npm-global/bin/codex', pricePerMillionIn: 0, pricePerMillionOut: 0 }];

  assert.equal(vendorsFrom(stored)[0]!.executablePath, '/home/user/.npm-global/bin/codex');
});

test('the path travels to the server, because PATH cannot always answer', () => {
  // WSL is why: `codex` resolves there to the Windows npm shim on the interop PATH, which runs
  // Linux node against a Windows install and dies. The native one is in ~/.npm-global/bin and
  // nothing could point at it, so a WSL round failed whatever anybody configured.
  const env = vendorsEnv([
    { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '/usr/local/bin/codex', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ]);

  assert.ok(env.includes('/usr/local/bin/codex'), 'the server reads this list and nothing else');
  assert.ok(env.includes('executablePath'), 'under the name the server parses');
});

test('an absent path stays absent rather than becoming an empty string the server must interpret', () => {
  const env = JSON.parse(vendorsEnv([{ id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }])) as Record<string, string>[];

  assert.equal(env[0]!['executablePath'], '');
});

// ---------- Add a reviewer: finding Claude Code, and adding a second of anything ----------
//
// Two separate things hid the Claude entry, and either alone reads as "the catalogue has no
// Claude Code". The words were not in it — the quick pick filters on the label, and the label
// said `Claude (a second one)` — and a preset whose id was already configured was dropped from
// the list without a word, so an installation with a claude reviewer could not add a second.
// That second half is the one-way door the docblock above VENDOR_PRESETS is already about.

test('the Claude preset is one a person searching for Claude Code can find', () => {
  // By id, not "some preset": an assertion that ANY label contains the words stays green while
  // the claude entry keeps its old label and the search goes on failing. (codex, the plan round.)
  const claude = VENDOR_PRESETS.find((p) => p.id === 'claude');

  assert.ok(claude, 'the catalogue has no claude preset at all');
  assert.match(claude.label, /Claude Code/, 'the words a person types are not in the label they filter on');
  assert.match(
    claude.label + ' ' + claude.hint,
    /second/i,
    'the entry no longer says it is a SECOND, separate process — which is the thing that made it worth explaining',
  );
});

test('a free id is the preset’s own when nothing holds it, and the next one when something does', () => {
  assert.equal(freeVendorId('claude', new Set()), 'claude');
  assert.equal(freeVendorId('claude', new Set(['claude'])), 'claude-2');
  assert.equal(freeVendorId('claude', new Set(['claude', 'claude-2'])), 'claude-3');
});

test('a blank base comes back blank, so the custom-endpoint flow still asks for a name', () => {
  // The blank preset names itself through askCustomEndpoint. Minting `-2` for it here would put a
  // row in the settings under an id the person never chose, and skip the box that asks.
  assert.equal(freeVendorId('', new Set(['claude'])), '');
});

test('a preset already configured is still offered, under a free id', () => {
  const offered = presetsOffered(VENDOR_PRESETS, new Set(['claude']));

  const claude = offered.find((one) => one.preset.id === 'claude');
  assert.ok(claude, 'a preset already in the panel vanished from the list, which is the one-way door');
  assert.equal(claude.id, 'claude-2', 'a second row would collide with the first');
  assert.equal(claude.second, true, 'nothing would tell the person this adds another');

  // Both halves of the condition, or this passes against a function that calls everything a second
  // row: a preset nobody holds keeps its own id and is NOT marked.
  const codex = offered.find((one) => one.preset.id === 'codex');
  assert.ok(codex, 'the catalogue lost codex');
  assert.equal(codex.id, 'codex');
  assert.equal(codex.second, false);
});

test('no two entries in one offering are given the same id', () => {
  // The allocation is over the LIST, not one entry at a time. Against the configured ids alone,
  // a catalogue holding both `claude` and `claude-2` would resolve BOTH to `claude-2` the day
  // `claude` is configured, and whichever was picked second would be refused as a duplicate with
  // nothing said about why. (codex, the code round.)
  const catalogue = [
    { ...VENDOR_PRESETS.find((p) => p.id === 'claude')! },
    { ...VENDOR_PRESETS.find((p) => p.id === 'claude')!, id: 'claude-2', label: 'Claude Code, again' },
  ];

  const ids = presetsOffered(catalogue, new Set(['claude'])).map((one) => one.id);

  assert.equal(new Set(ids).size, ids.length, `two entries were offered the same id: ${ids.join(', ')}`);
});

test('a preset nobody holds is never pushed off its own name by the reservation', () => {
  // The other half: reserving every preset's id must not make an un-taken entry rename itself.
  const offered = presetsOffered(VENDOR_PRESETS, new Set());

  for (const one of offered) {
    assert.equal(one.id, one.preset.id, `${one.preset.id} was renamed although nothing held it`);
    assert.equal(one.second, false);
  }
});

test('every catalogue entry survives the offering, whatever is already configured', () => {
  const taken = new Set(VENDOR_PRESETS.map((p) => p.id).filter((id) => id.length > 0));

  const offered = presetsOffered(VENDOR_PRESETS, taken);

  assert.equal(offered.length, VENDOR_PRESETS.length, 'the list is the catalogue, never what is left of it');
});

test('the item for a second row says which id it will take, and the others say nothing', () => {
  const items = reviewerPickItems(presetsOffered(VENDOR_PRESETS, new Set(['claude'])));

  const claude = items.find((one) => one.offered.preset.id === 'claude');
  assert.ok(claude, 'the claude entry is not among the items');
  assert.match(claude.description, /claude-2/, 'the person is not told what the new row will be called');

  const codex = items.find((one) => one.offered.preset.id === 'codex');
  assert.ok(codex, 'the codex entry is not among the items');
  assert.equal(codex.description, '', 'an ordinary entry is dressed as though it were a duplicate');
});

test('a pick item carries the hint as its detail, which is the text the filter has to reach', () => {
  const claude = reviewerPickItems(presetsOffered(VENDOR_PRESETS, new Set()))
    .find((one) => one.offered.preset.id === 'claude');

  assert.ok(claude);
  assert.equal(claude.detail, VENDOR_PRESETS.find((p) => p.id === 'claude')!.hint);
});
