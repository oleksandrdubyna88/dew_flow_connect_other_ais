import assert from 'node:assert/strict';
import { test } from 'node:test';
import { keyVariable, vendorTerminal } from '../vendorTerminal';
import { Vendor } from '../vendors';

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 'codex',
  runtime: 'codex',
  model: '',
  enabled: true,
  baseUrl: '',
  ...over,
});

test('each vendor opens its own CLI with its own usage command', () => {
  assert.deepEqual(
    { c: vendorTerminal(vendor()).command, u: vendorTerminal(vendor()).usageCommand },
    { c: 'codex', u: '/status' },
  );
  const claude = vendorTerminal(vendor({ id: 'claude', runtime: 'claude', model: 'haiku' }));
  assert.equal(claude.command, 'claude --model haiku');
  assert.equal(claude.usageCommand, '/usage');
  const gemini = vendorTerminal(vendor({ id: 'gemini', runtime: 'gemini', model: 'gemini-flash-latest' }));
  assert.equal(gemini.command, 'gemini -m gemini-flash-latest');
  assert.equal(gemini.usageCommand, '/stats');
});

test('a custom endpoint is reached with the same overrides the reviewer uses', () => {
  const terminal = vendorTerminal(
    vendor({ id: 'deepseek', runtime: 'codex', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat' }),
  );

  assert.ok(terminal.command.includes('-c model_provider=deepseek'));
  assert.ok(terminal.command.includes('-c model_providers.deepseek.base_url=https://api.deepseek.com/v1'));
  assert.ok(terminal.command.includes('-c model_providers.deepseek.env_key=DEEPSEEK_API_KEY'));
  assert.ok(terminal.command.includes('-m deepseek-chat'));
  assert.ok(terminal.note.includes('DEEPSEEK_API_KEY'), 'the terminal has no vault, and says so');
});

test('a vendor on its own CLI needs no note — nothing about it is surprising', () => {
  assert.equal(vendorTerminal(vendor()).note, '');
});

test('the key variable matches the server derivation exactly', () => {
  assert.equal(keyVariable('mistral'), 'MISTRAL_API_KEY');
  assert.equal(keyVariable('my-vendor.eu'), 'MY_VENDOR_EU_API_KEY');
});
