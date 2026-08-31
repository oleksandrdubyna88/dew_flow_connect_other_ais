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
  assert.ok(!env.includes('gemini'));
});
