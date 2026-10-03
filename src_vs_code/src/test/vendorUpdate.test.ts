import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Runtime } from '../models';
import { Vendor } from '../vendors';
import { updateFor, vendorInstall, vendorUpdate } from '../vendorTerminal';
import { bodyFor, HELP_LANGUAGES, helpArticle } from '../helpContent';

/**
 * How each CLI is brought up to date — its own command, verified one at a time.
 *
 * <p>This file exists because the first answer was wrong. `agy --help` lists four subcommands and
 * `update` is not among them, so "antigravity has no update subcommand at all" went into a comment,
 * a changelog, a plan and a module doc. It has one:</p>
 *
 * <pre>
 * $ agy update
 * ⟳ Checking for updates... (current version 1.1.23)
 * ✓ You are already on the latest version.
 * </pre>
 *
 * <p>Reproduced on two machines. The lesson is the cheap one: running a command costs a second, and
 * inferring its absence from an incomplete list cost a false claim in four places — the same shape
 * as the earlier "Antigravity publishes no Linux CLI", which was also an inference from a partial
 * look.</p>
 */

function vendor(runtime: Runtime, over: Partial<Vendor> = {}): Vendor {
  return {
    id: runtime,
    runtime,
    model: '',
    baseUrl: '',
    executablePath: '',
    enabled: true,
    plan: true,
    code: true,
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

test('the CLIs that update themselves are asked to', () => {
  assert.equal(vendorUpdate(vendor('claude'), 'linux').command, 'claude update');
  assert.equal(vendorUpdate(vendor('antigravity'), 'linux').command, 'agy update');
});

test('a self-update touches the binary the reviews actually run', () => {
  // The CLI-path field exists because PATH could not answer. Updating the bare name there would
  // update a different install and leave the reviewed one where it was.
  const pinned = vendor('antigravity', { executablePath: '/home/x/.local/bin/agy' });

  assert.equal(vendorUpdate(pinned, 'linux').command, '/home/x/.local/bin/agy update');
});

test('a path with a space survives being put on a command line', () => {
  const pinned = vendor('claude', { executablePath: 'C:/Program Files/claude/claude.exe' });

  assert.equal(vendorUpdate(pinned, 'win32').command, '"C:/Program Files/claude/claude.exe" update');
});

// ---- Codex updates itself from 0.126.0 (todo/PLAN_codex_updates_itself.md) ----
// This used to say "codex has no self-update, so it is installed again", read off `codex --help`. It
// was the agy mistake a second time: `codex --help` on codex-cli 0.156.1 lists `update  Update Codex
// to the latest version`, and on the Team server host `codex update` took 0.153.4 to 0.160.0
// (2026-10-02). openai/codex #19933 added it, and rust-v0.126.0 is the first release that has it.

test('a codex that has the update command is asked to update itself', () => {
  assert.equal(vendorUpdate(vendor('codex'), 'linux', '0.156.1').command, 'codex update');
  assert.equal(vendorUpdate(vendor('codex'), 'win32', '0.126.0').command, 'codex update', 'the first release that has it');
});

test('a pinned codex is updated through its own path', () => {
  const pinned = vendor('codex', { executablePath: 'C:/Program Files/nodejs/codex.cmd' });

  assert.equal(vendorUpdate(pinned, 'win32', '0.160.0').command, '"C:/Program Files/nodejs/codex.cmd" update');
});

test('a codex older than the update command, or one whose version is unknown, is installed again', () => {
  // `codex update` on 0.125.0 is "unrecognized subcommand"; the installer works on every version.
  assert.match(vendorUpdate(vendor('codex'), 'linux', '0.125.0').command, /npm install -g @openai\/codex@latest/);
  assert.match(vendorUpdate(vendor('codex'), 'linux', '').command, /npm install -g @openai\/codex@latest/,
    'a version that could not be read is not a version that has the command');
  assert.match(vendorUpdate(vendor('codex'), 'linux').command, /npm install -g @openai\/codex@latest/);
});

test('a version that is not a plain X.Y.Z is unknown, and unknown is installed again', () => {
  // Plan round, gemini: read as "not older than 0.126.0", garbage would have typed `codex update` on a codex that
  // may not have it.
  for (const unreadable of ['codex-cli 0.156.1', 'command not found', '0.156', 'v0.156.1', '0.156.1-alpha.2']) {
    assert.match(vendorUpdate(vendor('codex'), 'linux', unreadable).command, /npm install -g @openai\/codex@latest/, unreadable);
  }
});

test('the status the ⟳ colour came from decides the command', () => {
  const status = { codex: { installed: '0.160.0', latest: '0.161.0' }, old: { installed: '0.120.0', latest: '0.161.0' } };
  const choose = updateFor(status);

  assert.equal(choose(vendor('codex'), 'linux').command, 'codex update');
  assert.match(choose(vendor('codex', { id: 'old' }), 'linux').command, /npm install -g @openai\/codex@latest/, 'a second, older codex row');
  assert.match(choose(vendor('codex', { id: 'never-probed' }), 'linux').command, /npm install -g @openai\/codex@latest/,
    'a row the probe has not reached yet');
  assert.equal(choose(vendor('claude'), 'linux').command, 'claude update', 'the others are unchanged');
});

test('the help page says each CLI’s own update, in every language', () => {
  // It said "for every CLI here re-running the installer IS the update… agy has no update subcommand" in all five
  // languages, while ⟳ already ran `claude update` and `agy update`.
  const article = helpArticle('choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article exists');
  for (const language of HELP_LANGUAGES) {
    const paragraph: string = bodyFor(article, language).body.usage;
    for (const command of ['`claude update`', '`agy update`', '`codex update`', '0.126.0']) {
      assert.ok(paragraph.includes(command), `${language}: the ⟳ paragraph names ${command}`);
    }
  }
});

test('gemini updates the way its README says', () => {
  assert.equal(vendorUpdate(vendor('gemini'), 'linux').command, 'npm install -g @google/gemini-cli@latest');
});

test('updating is never a DIFFERENT vendor’s command', () => {
  // The install chain this replaced once quietly opened codex for antigravity, so a vendor's own
  // binary or package must appear in its own update line.
  for (const [runtime, marker] of [
    ['codex', 'codex'],
    ['gemini', 'gemini-cli'],
    ['claude', 'claude'],
    ['antigravity', 'agy'],
  ] as const) {
    assert.match(vendorUpdate(vendor(runtime), 'linux').command, new RegExp(marker));
  }
});

test('installing is still installing', () => {
  // The update path must not have changed what ⤓ does: a machine with no CLI needs the installer,
  // and `agy update` on a machine with no agy is a command not found.
  assert.match(vendorInstall(vendor('antigravity'), 'linux').command, /antigravity\.google\/cli\/install\.sh/);
  assert.match(vendorInstall(vendor('codex'), 'linux').command, /npm install -g @openai\/codex$/);
});
