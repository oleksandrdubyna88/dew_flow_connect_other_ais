import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';

import { vendorInstall, vendorUpdate } from '../vendorTerminal';
import type { Vendor } from '../vendors';

/**
 * The Windows ⤓/⟳ line for Mistral Vibe, RUN — against harmless substitutes, never against Astral's installer or a real
 * uv (the epic 2 risk consultation, 2026-10-13: an AST says the line parses, not that it works).
 *
 * <p>The boundary (common/generated-code-tests.md): a temporary folder whose path has SPACES, removed after; a cleared
 * environment naming only what the line reads; a PATH of `System32` alone — no real uv, and no Windows PowerShell to
 * reach the network through; and two substitutes the test writes itself. `powershell` — the installer step — is a
 * FUNCTION defined in the session, which PowerShell resolves before any program; it records its arguments and copies the
 * fake uv where Astral's installer would put it, and the harness refuses to run the line at all unless `powershell`
 * resolves to that function. `uv.exe` is a console program compiled here from twelve lines of C# that appends its
 * arguments to a log and exits with the code the scenario asks for. Every run has a deadline.</p>
 *
 * <p>Windows only: the line is PowerShell for a Windows host, and the fake is compiled by Windows PowerShell's
 * `Add-Type`. It runs under every PowerShell this machine can start — Windows PowerShell 5.1 and pwsh 7 — because a
 * person's terminal may be either.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const SYSTEM_ROOT = process.env['SystemRoot'] ?? 'C:\\Windows';
const WINDOWS_POWERSHELL = join(SYSTEM_ROOT, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');

const FAKE_UV = `using System; using System.IO;
public static class FakeUv {
  public static int Main(string[] args) {
    var line = string.Join(" ", args);
    File.AppendAllText(Environment.GetEnvironmentVariable("FAKE_UV_LOG"), line + Environment.NewLine);
    var fail = Environment.GetEnvironmentVariable("FAKE_UV_FAIL") ?? "";
    if (fail.Length > 0 && line.StartsWith(fail)) { return 3; }
    if (line == "tool dir --bin") { Console.WriteLine(Environment.GetEnvironmentVariable("FAKE_UV_BIN")); }
    return 0;
  }
}`;

/**
 * The session the line runs in: the installer substitute, the guard, the line, and what is left behind. The line runs
 * as a script of its own, NOT inside a try: a `throw` then ends it as it ends a line typed at a prompt (the rest is not
 * run, the message goes to the error stream), and an error that only ends its STATEMENT lets the line go on, as it does
 * at a prompt. A try would catch both alike and hide that difference — the first draft of this harness had one, and
 * reported a command-not-found as a stop that a typed line would not have made.
 */
const HARNESS = [
  'function powershell {',
  '  Add-Content -LiteralPath $env:FAKE_INSTALLER_LOG -Value ($args -join " ")',
  '  if ($env:FAKE_INSTALLER_FAIL) { $global:LASTEXITCODE = 1; return }',
  '  $dir = if ($env:UV_INSTALL_DIR) { $env:UV_INSTALL_DIR } else { Join-Path $HOME ".local\\bin" }',
  '  New-Item -ItemType Directory -Force -Path $dir | Out-Null',
  '  Copy-Item -LiteralPath $env:FAKE_UV -Destination (Join-Path $dir "uv.exe")',
  '  $global:LASTEXITCODE = 0',
  '}',
  'if ((Get-Command powershell).CommandType -ne "Function") { "GUARD: the installer is not the substitute"; exit 9 }',
  '"PATH-BEFORE: " + ($env:Path -split ";")[0]',
  'Invoke-Expression $env:COAI_LINE',
  '"LINE-ENDED"',
  '"PATH-HEAD: " + ($env:Path -split ";")[0]',
].join('\n');

const WINDOWS = process.platform === 'win32';
const root = WINDOWS ? mkdtempSync(join(tmpdir(), 'coai uv run ')) : '';
after(() => {
  if (root.length > 0) {
    rmSync(root, { recursive: true, force: true });
  }
});

/** The fake uv, compiled once into a folder with a space in its name. */
function fakeUv(): string {
  const dir = join(root, 'fake uv');
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, 'fake.cs'), FAKE_UV);
  const exe = join(dir, 'uv.exe');
  const built = spawnSync(WINDOWS_POWERSHELL, ['-NoProfile', '-NonInteractive', '-Command',
    `Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath '${join(dir, 'fake.cs')}') -OutputAssembly '${exe}' -OutputType ConsoleApplication`],
  { encoding: 'utf8', timeout: 120_000 });
  assert.equal(built.status, 0, `the fake uv did not compile: ${built.stderr}`);

  return exe;
}

/** Every PowerShell this machine can start with a cleared environment — the line must work in each. */
function shells(): readonly string[] {
  const candidates = [WINDOWS_POWERSHELL, join(process.env['LOCALAPPDATA'] ?? '', 'Microsoft', 'WindowsApps', 'pwsh.exe')];

  // Asked by STARTING it, never by `existsSync`: pwsh from the Store is an app execution alias, which a stat does not
  // see — the first draft's existsSync dropped it silently and ran every scenario under 5.1 alone.
  return candidates.filter((exe) =>
    spawnSync(exe, ['-NoProfile', '-NonInteractive', '-Command', 'exit 0'], { env: minimalEnv(), timeout: 60_000 }).status === 0);
}

function minimalEnv(): Record<string, string> {
  return {
    SystemRoot: SYSTEM_ROOT,
    PATH: join(SYSTEM_ROOT, 'System32'),
    // Every real terminal has it, and without it neither Get-Command nor & finds a program by its extension.
    PATHEXT: process.env['PATHEXT'] ?? '.COM;.EXE;.BAT;.CMD',
    LOCALAPPDATA: process.env['LOCALAPPDATA'] ?? '',
    TEMP: root,
    TMP: root,
  };
}

const UV = WINDOWS ? fakeUv() : '';
const SHELLS = WINDOWS ? shells() : [];
const skip = !WINDOWS ? 'the line is PowerShell for a Windows host, and the fake uv is compiled by Windows PowerShell' : false;

interface Run {
  readonly out: string;
  /** The error stream — where a `throw` that stopped the line says why. */
  readonly err: string;
  readonly installer: readonly string[];
  readonly uv: readonly string[];
}

let runs = 0;

/** The line, run once in one shell with a fresh home and fresh logs; what it printed and what each substitute saw. */
function run(shell: string, line: string, scenario: Record<string, string> = {}, uvOnPath = false): Run {
  runs += 1;
  const place = join(root, `run ${runs}`);
  const home = join(place, 'my home');
  mkdirSync(home, { recursive: true });
  const logs = { installer: join(place, 'installer log.txt'), uv: join(place, 'uv log.txt') };
  const env = {
    ...minimalEnv(),
    PATH: uvOnPath ? `${join(root, 'fake uv')};${join(SYSTEM_ROOT, 'System32')}` : join(SYSTEM_ROOT, 'System32'),
    USERPROFILE: home,
    FAKE_UV: UV,
    FAKE_UV_LOG: logs.uv,
    FAKE_INSTALLER_LOG: logs.installer,
    FAKE_UV_BIN: join(home, '.local', 'bin'),
    COAI_LINE: line,
    ...scenario,
  };
  const ran = spawnSync(shell, ['-NoProfile', '-NonInteractive', '-Command', HARNESS], { env, encoding: 'utf8', timeout: 120_000 });
  assert.match(ran.stdout, /PATH-BEFORE: /u, `${shell} did not run the harness: ${ran.stdout}${ran.stderr}`);
  assert.doesNotMatch(ran.stdout, /GUARD:/u, 'the installer step would have reached the real installer');

  return { out: ran.stdout, err: ran.stderr.replace(/\s+/gu, ' '), installer: linesOf(logs.installer), uv: linesOf(logs.uv) };
}

function linesOf(file: string): readonly string[] {
  return existsSync(file) ? readFileSync(file, 'utf8').split(/\r?\n/u).filter((line) => line.length > 0) : [];
}

const INSTALL = WINDOWS ? vendorInstall(VIBE, 'win32').command : '';
const UPDATE = WINDOWS ? vendorUpdate(VIBE, 'win32').command : '';

test('the harness has a PowerShell to run in, and its fake uv answers', { skip }, () => {
  assert.ok(SHELLS.length > 0, 'no PowerShell starts with a cleared environment — nothing below would have run');
  assert.ok(existsSync(UV));
});

test('(a) no uv on PATH, a home with spaces: Astral’s step runs, then uv by the path it used, and this session gets the tool folder', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, installer, uv } = run(shell, INSTALL);

    assert.deepEqual(installer, ['-ExecutionPolicy ByPass -c irm https://astral.sh/uv/install.ps1 | iex'], shell);
    assert.deepEqual(uv, ['tool install mistral-vibe', 'tool update-shell', 'tool dir --bin'], `${shell}: ${out}`);
    assert.match(out, /LINE-ENDED/u, shell);
    assert.ok(out.includes(`PATH-HEAD: ${join(root, `run ${runs}`, 'my home', '.local', 'bin')}`), `${shell}: ${out}`);
  }
});

test('(a) a UV_INSTALL_DIR with spaces is where the line looks for the fresh uv', { skip }, () => {
  for (const shell of SHELLS) {
    const where = join(root, 'uv lives here');
    const { out, uv } = run(shell, INSTALL, { UV_INSTALL_DIR: where });

    assert.deepEqual(uv, ['tool install mistral-vibe', 'tool update-shell', 'tool dir --bin'], `${shell}: ${out}`);
    assert.ok(existsSync(join(where, 'uv.exe')));
    rmSync(where, { recursive: true, force: true });
  }
});

test('(b) Astral’s step failing STOPS the line — no uv is called after it, and the reason is said', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, err, uv } = run(shell, INSTALL, { FAKE_INSTALLER_FAIL: '1' });

    assert.deepEqual(uv, [], `${shell}: a later step ran: ${out}`);
    assert.doesNotMatch(out, /LINE-ENDED/u, `${shell}: the line went on past the failed step: ${err}`);
    assert.match(err, /no uv at .*uv\.exe/u, `${shell}: the reason is not said: ${err}`);
  }
});

test('(b) `uv tool install` failing STOPS the line — update-shell and the PATH step do not run', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, err, uv } = run(shell, INSTALL, { FAKE_UV_FAIL: 'tool install' });

    assert.deepEqual(uv, ['tool install mistral-vibe'], `${shell}: a later step ran: ${out}`);
    assert.doesNotMatch(out, /LINE-ENDED/u, `${shell}: the line went on past the failed step`);
    assert.match(err, /uv stopped with an error/u, `${shell}: ${err}`);
  }
});

test('(b) ⟳ stops the same way when `uv tool upgrade` fails', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, uv } = run(shell, UPDATE, { FAKE_UV_FAIL: 'tool upgrade' });

    assert.deepEqual(uv, ['tool upgrade mistral-vibe'], `${shell}: ${out}`);
    assert.doesNotMatch(out, /LINE-ENDED/u);
  }
});

test('the harness runs the line under every PowerShell this machine starts — it names them, so a skipped one is seen', { skip }, (t) => {
  t.diagnostic(`shells: ${SHELLS.join(' | ')}`);
  assert.ok(SHELLS.some((shell) => shell.toLowerCase().endsWith('powershell.exe')), 'Windows PowerShell 5.1, which every Windows has, is missing');
});

test('(c) a uv already on PATH — whoever installed it — is used as it is, and Astral’s step does not run', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, installer, uv } = run(shell, INSTALL, {}, true);

    assert.deepEqual(installer, [], `${shell}: Astral’s step ran beside a uv on PATH`);
    assert.deepEqual(uv, ['tool install mistral-vibe', 'tool update-shell', 'tool dir --bin'], `${shell}: ${out}`);
  }
});
