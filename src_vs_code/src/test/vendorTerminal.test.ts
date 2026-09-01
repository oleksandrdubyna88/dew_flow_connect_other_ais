import assert from 'node:assert/strict';
import { test } from 'node:test';
import { keyVariable, OFFICIAL_SOURCES, vendorInstall, vendorTerminal } from '../vendorTerminal';
import { Vendor } from '../vendors';

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 'codex',
  runtime: 'codex',
  model: '',
  enabled: true,
  baseUrl: '',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
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
  const term = vendorTerminal({ id: 'gemini', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 });
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
    const install = vendorInstall({ id: runtime, runtime, model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, 'linux');
    assert.equal(install.command, `npm install -g ${pkg}`);
  }
});

test('the npm command is the same on every platform, because npm does not care', () => {
  const linux = vendorInstall({ id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, 'linux');
  const windows = vendorInstall({ id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, 'win32');
  assert.equal(linux.command, windows.command);
});


test('antigravity installs from Google’s own script, one per shell family', () => {
  // Verified 2026-09-01 before this was written: both URLs return the real scripts; install.sh
  // branches on `uname` itself and handles Darwin AND Linux (amd64/arm64, musl included), so it is
  // ONE command for both; and the binary it installed on Linux answered a review-shaped call with
  // exit 0. The operator found these by reading the vendor's site — this file had claimed twice that
  // no official Linux install existed.
  const on = (platform: 'win32' | 'linux' | 'darwin'): string =>
    vendorInstall({ id: 'gemini', runtime: 'antigravity', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, platform).command;

  assert.equal(on('linux'), 'curl -fsSL https://antigravity.google/cli/install.sh | bash');
  assert.equal(on('darwin'), on('linux'), 'one script serves both — it branches on uname itself');
  assert.equal(on('win32'), 'irm https://antigravity.google/cli/install.ps1 | iex');
});

test('a piped installer says that it is one', () => {
  // curl | bash is a supply-chain shape. It is the vendor's own documented installer on the
  // vendor's own domain, which is what official means here — and a person may still want to read it
  // first, so the note says so rather than hiding it.
  const note = vendorInstall({ id: 'gemini', runtime: 'antigravity', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, 'linux').note;

  assert.match(note, /piped script|read it first/i);
  assert.match(note, /sign.?in|agy` once/i, 'and that one interactive sign-in follows');
});

test('an install command may only come from a source the vendor itself publishes', () => {
  // The operator's rule, and this is the form that survives a future change: there IS a convenient
  // `antigravity-cli` snap at Google's own version, published by a third party, and it was briefly
  // offered here. A button that installs software gets pressed without reading, so it may only ever
  // offer what the vendor publishes.
  const runtimes = ['codex', 'gemini', 'claude', 'antigravity'] as const;
  for (const runtime of runtimes) {
    for (const p of ['win32', 'linux', 'darwin'] as const) {
      const command = vendorInstall({ id: runtime, runtime, model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, p).command;
      if (command.length === 0) {
        continue;
      }
      assert.ok(
        OFFICIAL_SOURCES.some((prefix) => command.startsWith(prefix)),
        `${runtime} on ${p} would install with "${command}", which is not from a source the vendor publishes`,
      );
    }
  }
});

test('a vendor on somebody else’s endpoint installs the CLI it actually rides', () => {
  const install = vendorInstall({ id: 'deepseek', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.deepseek.com/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }, 'linux');
  assert.equal(install.command, 'npm install -g @openai/codex');
});

// ---------- the buttons must answer for the OS they are actually running on ----------

test('the run button uses the CLI path when one is set', () => {
  // The whole point of the field: PATH could not answer, so somebody said where the binary is.
  // A button that then runs the bare name ignores the one thing they told it.
  const term = vendorTerminal({
    id: 'gemini', runtime: 'antigravity', model: 'gemini-3.7-flash-high', enabled: true, baseUrl: '',
    executablePath: '/mnt/c/Users/strug/AppData/Local/agy/bin/agy.exe', pricePerMillionIn: 0, pricePerMillionOut: 0,
  });

  assert.match(term.command, /^\/mnt\/c\/Users\/strug\/AppData\/Local\/agy\/bin\/agy\.exe\b/);
  assert.ok(term.usageCommand.endsWith('agy.exe usage'), 'the usage line must run the same binary: ' + term.usageCommand);
});

test('a path with a space survives being put on a command line', () => {
  const term = vendorTerminal({
    id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '',
    executablePath: '/home/jinx/my tools/codex', pricePerMillionIn: 0, pricePerMillionOut: 0,
  });

  assert.ok(term.command.startsWith('"/home/jinx/my tools/codex"'), term.command);
});

test('the install prerequisite is the one for THIS operating system', () => {
  for (const [platform, marker] of [
    ['win32', /winget/],
    ['linux', /apt|nvm/],
    ['darwin', /brew/],
  ] as const) {
    const install = vendorInstall(
      { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      platform,
    );
    assert.match(install.prerequisite, marker, `${platform} needs its own way to get node`);
  }
});


