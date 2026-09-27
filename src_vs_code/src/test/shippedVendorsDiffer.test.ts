import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor } from '../vendors';

/**
 * A change a person makes to a SHIPPED reviewer row reaches the settings file.
 *
 * <p>`envBlock` writes `COAI_VENDORS` only when the list differs from the shipped one — a pristine panel
 * sends nothing and the server's own defaults run. The comparison used to look at id, runtime, model,
 * enabled and base URL only, so every other box on the shipped codex / antigravity cards was dropped on
 * the way to the server: untick "reviews code" on codex and the server went on asking it for code; point
 * it at a native CLI in WSL and the server went on using the Windows shim. Found while building the
 * feature tick (S3.3a of `todo/PLAN_feature_review.md`); D12 — fix what you find.</p>
 */

type Row = { id: string; plan?: boolean; code?: boolean; document?: boolean; executablePath?: string };

/** The shipped pair with one field of the first row changed, and the rows the file then carries. */
function written(change: Partial<Vendor>): readonly Row[] | undefined {
  const vendors: Vendor[] = [{ ...DEFAULT_VENDORS[0]!, ...change }, DEFAULT_VENDORS[1]!];
  const json = envBlock(DEFAULTS, vendors)['COAI_VENDORS'];

  return json === undefined ? undefined : JSON.parse(json) as Row[];
}

function codexIn(rows: readonly Row[] | undefined): Row {
  assert.ok(rows !== undefined, 'the change never reached the settings file — COAI_VENDORS was not written');
  const codex = rows.find((row) => row.id === 'codex');
  assert.ok(codex !== undefined);

  return codex;
}

test('unticking "reviews plans" on a shipped row reaches the file', () => {
  assert.equal(codexIn(written({ plan: false })).plan, false);
});

test('unticking "reviews code" on a shipped row reaches the file', () => {
  assert.equal(codexIn(written({ code: false })).code, false);
});

test('saying yes or no to "reviews documents" on a shipped row reaches the file', () => {
  assert.equal(codexIn(written({ document: false })).document, false);
  assert.equal(codexIn(written({ document: true })).document, true);
});

test('a CLI path on a shipped row reaches the file', () => {
  // The WSL case the field exists for: PATH resolves `codex` to the Windows npm shim.
  assert.equal(codexIn(written({ executablePath: '/home/me/.npm-global/bin/codex' })).executablePath,
    '/home/me/.npm-global/bin/codex');
});

test('the pristine shipped pair still writes nothing', () => {
  assert.equal(envBlock(DEFAULTS, DEFAULT_VENDORS)['COAI_VENDORS'], undefined);
  // A price is the panel's, not the server's: it never crosses, so it is not a reason to write the list.
  assert.equal(written({ pricePerMillionIn: 3 }), undefined);
});
