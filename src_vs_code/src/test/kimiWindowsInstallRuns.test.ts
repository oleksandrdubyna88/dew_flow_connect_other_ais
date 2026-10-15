import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';

import { vendorInstall, vendorUpdate } from '../vendorTerminal';
import type { Vendor } from '../vendors';

/**
 * The Windows ⤓/⟳ line for Kimi Code, RUN — against harmless substitutes, never against winget or Moonshot's installer
 * (S5.15 of todo/PLAN_vibe_and_kimi_runtimes_E4.md; an AST says the line parses, not that it works).
 *
 * <p>The boundary (common/generated-code-tests.md): a temporary folder whose path has SPACES, removed after; a cleared
 * environment naming only what the line reads; a PATH of the stub folder and `System32` alone; and three substitutes the
 * test writes itself. `winget`, `irm` and `iex` are FUNCTIONS defined in the session — PowerShell resolves an alias before
 * a function, so the harness removes the two aliases first — and the harness refuses to run the line at all unless all
 * three resolve to those functions. `git.exe`, `rg.exe` and `fd.exe` are empty files in a folder on PATH, present or absent
 * per scenario: `Get-Command` finds a program by its name and extension and never runs it. Every run has a deadline.</p>
 *
 * <p>Windows only: the line is PowerShell for a Windows host. It runs under every PowerShell this machine can start —
 * Windows PowerShell 5.1 and pwsh 7 — because a person's terminal may be either.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const SYSTEM_ROOT = process.env['SystemRoot'] ?? 'C:\\Windows';
const WINDOWS_POWERSHELL = join(SYSTEM_ROOT, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
const FLAGS = '-e --source winget --accept-source-agreements --accept-package-agreements';

/**
 * The session the line runs in: the three stand-ins, the guard, the line, and what is left behind. The line runs as a
 * script of its own, NOT inside a try: a `throw` then ends it as it ends a line typed at a prompt, and an error that only
 * ends its STATEMENT lets the line go on, as it does at a prompt (the Vibe harness's reasoning, kept).
 */
const HARNESS = [
  'foreach ($alias in "irm", "iex") { if (Test-Path "Alias:\\$alias") { Remove-Item "Alias:\\$alias" -Force } }',
  // A machine with no winget (App Installer missing, an older Windows): the stand-in is not defined at all, and the guard asks that
  // nothing named winget resolves. $LASTEXITCODE is left at 0 on purpose — a profile or an earlier native command leaves it so.
  'if (-not $env:FAKE_WINGET_ABSENT) {',
  '  function winget {',
  '    Add-Content -LiteralPath $env:FAKE_WINGET_LOG -Value ($args -join " ")',
  '    $global:LASTEXITCODE = [int]$env:FAKE_WINGET_EXIT',
  '  }',
  '} else { $global:LASTEXITCODE = 0 }',
  'function irm { Add-Content -LiteralPath $env:FAKE_MOONSHOT_LOG -Value ("irm " + ($args -join " ")); "stand-in script" }',
  'function iex { Add-Content -LiteralPath $env:FAKE_MOONSHOT_LOG -Value ("iex " + ($input -join " ")) }',
  'foreach ($name in "irm", "iex") { if ((Get-Command $name).CommandType -ne "Function") { "GUARD: $name is not the substitute"; exit 9 } }',
  'if ($env:FAKE_WINGET_ABSENT) { if (Get-Command winget -ErrorAction SilentlyContinue) { "GUARD: a real winget resolves"; exit 9 } }',
  'elseif ((Get-Command winget).CommandType -ne "Function") { "GUARD: winget is not the substitute"; exit 9 }',
  '"STARTED"',
  'Invoke-Expression $env:COAI_LINE',
  '"LINE-ENDED"',
].join('\n');

const WINDOWS = process.platform === 'win32';
// A folder named for the cleanup sweep, with spaces in its name: the line must work for a person whose paths have them.
const root = WINDOWS ? mkdtempSync(join(tmpdir(), 'testsToDelete-kimi install run ')) : '';
after(() => {
  if (root.length > 0) {
    rmSync(root, { recursive: true, force: true });
  }
});

function minimalEnv(): Record<string, string> {
  return {
    SystemRoot: SYSTEM_ROOT,
    PATH: join(SYSTEM_ROOT, 'System32'),
    // Every real terminal has it, and without it neither Get-Command nor & finds a program by its extension.
    PATHEXT: process.env['PATHEXT'] ?? '.COM;.EXE;.BAT;.CMD',
    LOCALAPPDATA: process.env['LOCALAPPDATA'] ?? '',
    ProgramFiles: process.env['ProgramFiles'] ?? '',
    ProgramData: process.env['ProgramData'] ?? '',
    ALLUSERSPROFILE: process.env['ALLUSERSPROFILE'] ?? '',
    TEMP: root,
    TMP: root,
  };
}

/** Every PowerShell this machine can start with a cleared environment — the line must work in each. */
function shells(): readonly string[] {
  const candidates = [
    WINDOWS_POWERSHELL,
    join(process.env['ProgramFiles'] ?? '', 'PowerShell', '7', 'pwsh.exe'),
    join(process.env['LOCALAPPDATA'] ?? '', 'Microsoft', 'WindowsApps', 'pwsh.exe'),
  ];

  // Asked by STARTING it, never by `existsSync`: pwsh from the Store is an app execution alias, which a stat does not see.
  return candidates.filter((exe) =>
    spawnSync(exe, ['-NoProfile', '-NonInteractive', '-Command', 'exit 0'], { env: minimalEnv(), timeout: 60_000 }).status === 0);
}

const SHELLS = WINDOWS ? shells() : [];
const skip = !WINDOWS ? 'the line is PowerShell for a Windows host' : false;

interface Run {
  readonly out: string;
  /** The error stream — where a `throw` that stopped the line says why. */
  readonly err: string;
  readonly winget: readonly string[];
  readonly moonshot: readonly string[];
}

let runs = 0;

function linesOf(file: string): readonly string[] {
  return existsSync(file) ? readFileSync(file, 'utf8').split(/\r?\n/u).filter((line) => line.length > 0) : [];
}

/**
 * The line, run once in one shell with fresh logs; what it printed and what each substitute saw.
 *
 * @param present the tools that exist on PATH for this run (`git`, `rg`, `fd`)
 * @param wingetExit the exit code the `winget` stand-in answers every call with
 * @param wingetAbsent no `winget` resolves at all
 */
function run(shell: string, line: string, present: readonly string[], wingetExit = 0, wingetAbsent = false): Run {
  runs += 1;
  const place = join(root, `run ${runs}`);
  const bin = join(place, 'tools bin');
  mkdirSync(bin, { recursive: true });
  for (const tool of present) {
    writeFileSync(join(bin, `${tool}.exe`), '');
  }
  const logs = { winget: join(place, 'winget log.txt'), moonshot: join(place, 'moonshot log.txt') };
  const env = {
    ...minimalEnv(),
    PATH: `${bin};${join(SYSTEM_ROOT, 'System32')}`,
    FAKE_WINGET_LOG: logs.winget,
    FAKE_WINGET_EXIT: String(wingetExit),
    ...(wingetAbsent ? { FAKE_WINGET_ABSENT: '1' } : {}),
    FAKE_MOONSHOT_LOG: logs.moonshot,
    COAI_LINE: line,
  };
  const ran = spawnSync(shell, ['-NoProfile', '-NonInteractive', '-Command', HARNESS], { env, encoding: 'utf8', timeout: 120_000 });
  assert.match(ran.stdout, /STARTED/u, `${shell} did not run the harness: ${ran.stdout}${ran.stderr}`);
  assert.doesNotMatch(ran.stdout, /GUARD:/u, `a step would have reached the real program: ${ran.stdout}`);

  return { out: ran.stdout, err: ran.stderr.replace(/\s+/gu, ' '), winget: linesOf(logs.winget), moonshot: linesOf(logs.moonshot) };
}

const INSTALL = WINDOWS ? vendorInstall(KIMI, 'win32').command : '';
const UPDATE = WINDOWS ? vendorUpdate(KIMI, 'win32', '2.1.1').command : '';
const MOONSHOT = ['irm https://code.kimi.com/kimi-code/install.ps1', 'iex stand-in script'];
const install = (id: string): string => `install --id ${id} ${FLAGS}`;

test('the harness has a PowerShell to run in, and names the ones it ran in', { skip }, (t) => {
  t.diagnostic(`shells: ${SHELLS.join(' | ')}`);
  assert.ok(SHELLS.some((shell) => shell.toLowerCase().endsWith('powershell.exe')), 'Windows PowerShell 5.1, which every Windows has, is missing');
});

test('theToolStepsSkipWhatIsThereAndStopOnFailure (a): git, rg and fd present — winget is never called and Moonshot’s step is reached', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, winget, moonshot } = run(shell, INSTALL, ['git', 'rg', 'fd']);

    assert.deepEqual(winget, [], `${shell}: winget ran for a tool that is there: ${out}`);
    assert.deepEqual(moonshot, MOONSHOT, `${shell}: ${out}`);
    assert.match(out, /LINE-ENDED/u, shell);
  }
});

test('theToolStepsSkipWhatIsThereAndStopOnFailure (b): rg absent, winget answers 0 — one call for ripgrep, then Moonshot’s step', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, winget, moonshot } = run(shell, INSTALL, ['git', 'fd']);

    assert.deepEqual(winget, [install('BurntSushi.ripgrep.MSVC')], `${shell}: ${out}`);
    assert.deepEqual(moonshot, MOONSHOT, `${shell}: ${out}`);
  }
});

test('theToolStepsSkipWhatIsThereAndStopOnFailure (c): fd absent, winget answers 1 — one call, and the line STOPS before Moonshot’s step', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, err, winget, moonshot } = run(shell, INSTALL, ['git', 'rg'], 1);

    assert.deepEqual(winget, [install('sharkdp.fd')], `${shell}: ${out}`);
    assert.deepEqual(moonshot, [], `${shell}: Moonshot’s step ran after a failed tool step: ${out}`);
    assert.doesNotMatch(out, /LINE-ENDED/u, `${shell}: the line went on past the failed step: ${err}`);
    assert.match(err, /fd/u, `${shell}: the reason does not name the tool: ${err}`);
  }
});

test('a machine with NO winget stops the line at its first missing tool: the line does not go on to Moonshot’s step', { skip }, () => {
  // Measured in pwsh 7: a command that is not found leaves $LASTEXITCODE where it was (0 on a fresh session), so a check on the exit code
  // alone let the line walk on past a winget that never ran.
  for (const shell of SHELLS) {
    const { out, err, winget, moonshot } = run(shell, INSTALL, ['git', 'fd'], 0, true);

    assert.deepEqual(winget, [], `${shell}: the stand-in must not exist: ${out}`);
    assert.deepEqual(moonshot, [], `${shell}: Moonshot’s step ran after winget was never found: ${out}`);
    assert.doesNotMatch(out, /LINE-ENDED/u, `${shell}: the line went on past the missing winget: ${err}`);
    assert.match(err, /ripgrep/u, `${shell}: the reason does not name the tool: ${err}`);
  }
});

test('a fresh machine: git, ripgrep and fd are each installed, in that order, then Moonshot’s step', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, winget, moonshot } = run(shell, INSTALL, []);

    assert.deepEqual(winget, [install('Git.Git'), install('BurntSushi.ripgrep.MSVC'), install('sharkdp.fd')], `${shell}: ${out}`);
    assert.deepEqual(moonshot, MOONSHOT, shell);
  }
});

test('a failed Git step stops the line at the first step — neither tool is tried and Moonshot’s step does not run', { skip }, () => {
  for (const shell of SHELLS) {
    const { out, err, winget, moonshot } = run(shell, INSTALL, [], 1);

    assert.deepEqual(winget, [install('Git.Git')], `${shell}: ${out}`);
    assert.deepEqual(moonshot, [], shell);
    assert.match(err, /git/iu, `${shell}: ${err}`);
  }
});

test('⟳ is the same line and behaves the same: it skips what is there and stops on a failure', { skip }, () => {
  for (const shell of SHELLS) {
    const skipped = run(shell, UPDATE, ['git', 'rg', 'fd']);
    const stopped = run(shell, UPDATE, ['git', 'fd'], 1);

    assert.deepEqual(skipped.winget, [], shell);
    assert.deepEqual(skipped.moonshot, MOONSHOT, shell);
    assert.deepEqual(stopped.winget, [install('BurntSushi.ripgrep.MSVC')], shell);
    assert.deepEqual(stopped.moonshot, [], shell);
  }
});
