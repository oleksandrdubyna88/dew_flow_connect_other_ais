import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULT_VENDORS, normaliseId, VENDOR_PRESETS, vendorsEnv, vendorsFrom } from '../vendors';

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
    { id: 'gemini', runtime: 'gemini', model: 'gemini-flash-latest', enabled: true, baseUrl: '' },
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
    { id: 'mine', runtime: 'gemini', model: 'x', enabled: true, baseUrl: 'https://example.test/v1' },
  ]);
  assert.equal(kept[0]!.runtime, 'gemini', 'a base URL means the vendor is not Google’s at all');
});
