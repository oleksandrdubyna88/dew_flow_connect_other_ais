import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FeaturesCache } from '../binaryFeatures';
import type { Run } from '../roundsDbRead';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';

/**
 * A row's system prompt crosses to coai-mcp only when the installed binary lists `systemPrompt` in `--features`
 * (todo/PLAN_one_model_catalog.md, epic 2: "capability, not version numbers").
 *
 * <p>An older binary reads the settings file too and skips a member it does not know, so sending it there would be a
 * prompt the person wrote that silently does nothing; held back, the card can say it is not sent.</p>
 */

const ROW = vendorsFrom([{ id: 'claude', runtime: 'claude', model: '', enabled: true, systemPrompt: 'Be terse.' }]);

const crossed = (features: readonly string[]): unknown => (JSON.parse(vendorsEnv(ROW, '', () => undefined, features)) as Record<string, unknown>[])[0]?.['systemPrompt'];

test('a binary that lists systemPrompt is sent the row\'s prompt; one that does not is sent none', () => {
  assert.equal(crossed(['systemPrompt']), 'Be terse.');
  assert.equal(crossed([]), undefined);
  assert.equal(crossed(['bugzRuntime']), undefined);
});

test('a shipped row whose only change is a system prompt is still written, when the binary takes it', () => {
  const shipped = DEFAULT_VENDORS.map((row, index) => (index === 0 ? { ...row, systemPrompt: 'Cite lines.' } : row));

  assert.match(envBlock(DEFAULTS, shipped, '', () => undefined, ['systemPrompt'])['COAI_VENDORS'] ?? '', /"systemPrompt":"Cite lines\."/u);
  assert.equal(envBlock(DEFAULTS, shipped)['COAI_VENDORS'], undefined, 'nothing the binary takes differs, so nothing is written');
});

test('the cache says what it last settled on, without spawning — what the settings file is written from', async () => {
  const cache = new FeaturesCache();
  assert.deepEqual(cache.known(), [], 'nothing asked yet is nothing known');
  const run = (): Run => async () => ({ code: 0, output: '{"features":["systemPrompt"]}' });
  const { mkdtempSync, writeFileSync, rmSync } = await import('node:fs');
  const { join } = await import('node:path');
  const { tmpdir } = await import('node:os');
  const dir = mkdtempSync(join(tmpdir(), 'coai-known-'));
  try {
    writeFileSync(join(dir, 'coai-mcp'), 'one');
    await cache.of(join(dir, 'coai-mcp'), run);

    assert.deepEqual(cache.known(), ['systemPrompt']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
