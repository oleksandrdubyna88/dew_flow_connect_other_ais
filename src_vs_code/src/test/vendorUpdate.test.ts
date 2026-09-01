import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Runtime } from '../models';
import { Vendor } from '../vendors';
import { vendorInstall, vendorUpdate } from '../vendorTerminal';

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

test('codex has no self-update, so it is installed again', () => {
  // Confirmed against the full `codex --help` subcommand list, and OpenAI's own quickstart prints
  // one command under both "Install Codex" and "Update Codex".
  const command = vendorUpdate(vendor('codex'), 'linux').command;

  assert.match(command, /@openai\/codex@latest/);
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
