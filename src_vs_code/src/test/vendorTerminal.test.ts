import assert from 'node:assert/strict';
import { test } from 'node:test';
import { keyVariable, OFFICIAL_SOURCES, vendorInstall, vendorTerminal } from '../vendorTerminal';
import { Vendor } from '../vendors';

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 'codex',
  runtime: 'codex',
  model: '',
  enabled: true,
  executablePath: '',
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

// ---------- the ▶ button must open the vendor's OWN cli ----------

test('an antigravity reviewer opens agy, not codex', () => {
  // The runtime fell through to `codex` for anything it did not recognise, so the row migrated to
  // Antigravity opened a terminal running a different vendor's CLI — the same wrong-model defect
  // the server side had, on the button a person presses to sign that vendor in.
  const term = vendorTerminal({ id: 'gemini', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, baseUrl: '', executablePath: '' });
  assert.match(term.command, /^agy\b/);
  assert.match(term.command, /--model gemini-3\.7-flash-high/);
});

// ---------- installing a CLI you do not have ----------

test('each npm-published CLI offers the exact command that installs it', () => {
  for (const [runtime, pkg] of [
    ['codex', '@openai/codex'],
    ['gemini', '@google/gemini-cli'],
    ['claude', '@anthropic-ai/claude-code'],
  ] as const) {
    const install = vendorInstall({ id: runtime, runtime, model: '', enabled: true, baseUrl: '', executablePath: '' });
    assert.equal(install.command, `npm install -g ${pkg}`);
  }
});

test('the same command in both shells, because npm does not care which one you are in', () => {
  const install = vendorInstall({ id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '' });
  assert.equal(install.powershell, install.command);
  assert.equal(install.bash, install.command);
  // What DOES differ is getting node in the first place, which is the actual reason somebody is
  // reading this on a fresh WSL box.
  assert.match(install.prerequisite.powershell, /winget/);
  assert.match(install.prerequisite.bash, /apt|nvm/);
});

test('antigravity is pointed at, because Google publishes no install command for it', () => {
  // `agy` ships as a Go binary with the Antigravity app and npm has no package for it, so there is
  // nothing to run. The honest answer is the documentation.
  const install = vendorInstall({ id: 'gemini', runtime: 'antigravity', model: '', enabled: true, baseUrl: '', executablePath: '' });

  assert.equal(install.bash, '');
  assert.equal(install.powershell, '');
  assert.match(install.docs, /antigravity\.google/);
  assert.match(install.note, /no install command|ships with/i);
});

test('an install command may only come from a source the vendor itself publishes', () => {
  // The operator's rule, and this is the form that survives a future change: there IS a convenient
  // `antigravity-cli` snap at Google's own version, published by a third party, and it was briefly
  // offered here. A button that installs software gets pressed without reading, so it may only ever
  // offer what the vendor publishes.
  const runtimes = ['codex', 'gemini', 'claude', 'antigravity'] as const;
  for (const runtime of runtimes) {
    const install = vendorInstall({ id: runtime, runtime, model: '', enabled: true, baseUrl: '', executablePath: '' });
    for (const command of [install.command, install.bash, install.powershell]) {
      if (command.length === 0) {
        continue;
      }
      assert.ok(
        OFFICIAL_SOURCES.some((prefix) => command.startsWith(prefix)),
        `${runtime} would install with "${command}", which is not from a source the vendor publishes`,
      );
    }
  }
});

test('a vendor on somebody else’s endpoint installs the CLI it actually rides', () => {
  const install = vendorInstall({ id: 'deepseek', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.deepseek.com/v1', executablePath: '' });
  assert.equal(install.command, 'npm install -g @openai/codex');
});
