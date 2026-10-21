import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { ANTIGRAVITY_MODELS } from '../models';
import { ANTIGRAVITY_DEFAULT_MODEL, DEFAULT_VENDORS } from '../vendors';

/**
 * The model an Antigravity row starts on is one agy offers, and the panel and the server agree on it
 * (research/PLAN_the_agy_default_is_a_model_agy_offers.md).
 *
 * <p>agy 1.4.4 refuses `gemini-3.7-flash-high` before any model call. The server's fallback for a row
 * with no model (C# `AntigravityRuntime.DefaultModel`) and the panel's starting row
 * (`ANTIGRAVITY_DEFAULT_MODEL`, the `package.json` default) are two copies of one decision, so the C# is
 * read rather than transcribed — a third hand-copied constant is one more place to forget.</p>
 */

const root = path.join(__dirname, '..', '..', '..');

function serverDefaultModel(): string {
  // reads-another-program: the panel's default and the server's fallback are one decision in two languages; the read
  // is a positive assertion that fails LOUDLY — the match below throws when the constant moves or changes shape, it
  // never derives an empty expectation and passes over nothing.
  const source = fs.readFileSync(path.join(root, 'src_mcp', 'runners', 'Reviewers', 'AntigravityRuntime.cs'), 'utf8');
  const m = /const\s+string\s+DefaultModel\s*=\s*"([^"]+)"/.exec(source);

  assert.ok(m, 'AntigravityRuntime.DefaultModel not found — the shape this test reads has changed');
  return m[1]!;
}

test('the panel starts an Antigravity row on the model agy offers', () => {
  assert.equal(ANTIGRAVITY_DEFAULT_MODEL, 'gemini-3.8-flash-low');
  assert.ok(ANTIGRAVITY_MODELS.some((m) => m.id === ANTIGRAVITY_DEFAULT_MODEL), 'and the curated list offers it');
});

test('the panel default is the model the server falls back to', () => {
  assert.equal(ANTIGRAVITY_DEFAULT_MODEL, serverDefaultModel());
});

test('a fresh install’s Antigravity row, in the code and in the manifest, carries that default', () => {
  const row = DEFAULT_VENDORS.find((v) => v.runtime === 'antigravity');
  assert.equal(row?.model, ANTIGRAVITY_DEFAULT_MODEL);

  // Parsed, not matched as text: indentation or line endings in the manifest must not decide this.
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: { properties: Record<string, { default?: unknown }> } };
  };
  const defaults = manifest.contributes.configuration.properties['coai.vendors']?.default as
    | readonly { runtime: string; model: string }[]
    | undefined;
  assert.equal(
    defaults?.find((v) => v.runtime === 'antigravity')?.model,
    ANTIGRAVITY_DEFAULT_MODEL,
    'package.json coai.vendors default names the same model',
  );
});
