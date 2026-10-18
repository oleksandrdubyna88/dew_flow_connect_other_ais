import assert from 'node:assert/strict';
import { execFile, spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { KIMI_TOOLS_NOTE } from '../kimiLinuxLine';
import { fromOfficialSources, vendorInstall, vendorUpdate } from '../vendorTerminal';
import type { Vendor } from '../vendors';

/**
 * ⤓ and ⟳ for Kimi on a Linux host — DL12 of todo/PLAN_kimi_and_vibe_on_linux.md (E6), scenarios S-K9/S-K10/S-K17.
 *
 * <p><b>The line is RUN here, against harmless substitutes, never against a real package manager or Moonshot's installer</b>
 * (common/generated-code-tests.md; an assertion over the text says the line was written, not that it works). The boundary: a
 * temporary folder whose name carries `testsToDelete-` and a SPACE, removed after; an `HOME` inside it; a `PATH` of a stand-in
 * folder and of the two real tools the line itself runs (`mkdir` and `ln` — the verbs S-K17's "fresh account" and "second
 * press" arms are about); and four stand-ins the test defines as shell FUNCTIONS in front of the line — `sudo`, `apt-get`,
 * `dnf`, and `curl`/`bash` for Moonshot's piped script. Each logs what it was asked; a package manager "installs" a package by
 * putting that program's folder on the PATH, the way a real install makes a program resolve. Functions, not scripts, because on
 * a machine that scans every new executable each process costs seconds — measured here at about 5 s a spawn under Git Bash.</p>
 *
 * <p>The shell is bash, found the way a person's terminal would have it: on Windows, Git for Windows' bash — never the
 * `bash.exe` of `System32`, which is WSL — and its symlinks are made as `.lnk` shortcuts (`MSYS=winsymlinks:lnk`), which Git
 * Bash reads back as links and which need no privilege. On Linux and macOS it is the `bash` of the machine. Every press is
 * started when the file loads and awaited by the test that reads it, so the slow spawns overlap.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const MOONSHOT_URL = 'https://code.kimi.com/kimi-code/install.sh';
/** DL12's generic instruction, with the capital the line prints it with (the official-source check reads every lower-case "install"). */
const NO_MANAGER = 'Install ripgrep and fd with your distribution\'s package manager, or place them on PATH or in ~/.local/bin';

const WINDOWS = process.platform === 'win32';

/** Git for Windows' bash on Windows (never WSL's), the machine's bash elsewhere, or '' when there is none. */
function findBash(): string {
  if (WINDOWS) {
    const roots = ['ProgramFiles', 'ProgramW6432', 'ProgramFiles(x86)'].map((name) => process.env[name] ?? '').filter((one) => one.length > 0);

    return roots.map((root) => join(root, 'Git', 'bin', 'bash.exe')).find((candidate) => existsSync(candidate)) ?? '';
  }

  // ABSOLUTE: node looks a command up in the PATH of the env it is given, and the sealed PATH below has no bash in it.
  return spawnSync('sh', ['-c', 'command -v bash'], { encoding: 'utf8', timeout: 30_000 }).stdout.trim();
}

const BASH = findBash();
const skip = BASH === '' ? 'no bash on this machine (Git for Windows on Windows) — the Linux line cannot be run or parsed' : false;

/** A path as the shell inside spells it: `/c/Users/…` for a Windows drive path (a `C:` there would be read as a PATH separator). */
const posix = (path: string): string => (WINDOWS ? path.replace(/^([A-Za-z]):/u, (_all, drive: string) => `/${drive.toLowerCase()}`).replaceAll('\\', '/') : path);

const root = BASH === '' ? '' : mkdtempSync(join(tmpdir(), 'testsToDelete-kimi linux install '));
after(() => {
  if (root.length > 0) {
    rmSync(root, { recursive: true, force: true });
  }
});

/** The folder of real tools on the PATH: Git's own `usr/bin` on Windows, a folder of links to `mkdir`, `ln` and `readlink` elsewhere. */
function coreFolder(): string {
  if (WINDOWS) {
    return join(BASH, '..', '..', 'usr', 'bin');
  }
  const folder = join(root, 'core tools');
  mkdirSync(folder, { recursive: true });
  for (const tool of ['mkdir', 'ln', 'readlink']) {
    const found = spawnSync('sh', ['-c', `command -v ${tool}`], { encoding: 'utf8' }).stdout.trim();
    assert.ok(found.length > 0, `this machine has no ${tool}, which the line needs`);
    symlinkSync(found, join(folder, tool));
  }

  return folder;
}

const CORE = BASH === '' ? '' : coreFolder();

/**
 * A package manager that logs, can be told to fail, and "installs" a package by putting its program's folder on the PATH — and
 * by writing the program's name to a file, which the NEXT press reads back onto its PATH: an install outlives the shell that
 * made it, and a second press must find what the first one installed.
 */
function packageManager(name: string, fdProgram: string): string {
  return `${name}() { echo "${name} $*" >> "$FAKE_LOG"; [ "$FAKE_PM_EXIT" = 0 ] || return "$FAKE_PM_EXIT"; `
    + 'for coai_arg in "$@"; do case "$coai_arg" in '
    + 'ripgrep) PATH="$FAKE_SHELF/rg:$PATH"; echo rg >> "$FAKE_INSTALLED";; '
    + `fd-find) PATH="$FAKE_SHELF/${fdProgram}:$PATH"; echo ${fdProgram} >> "$FAKE_INSTALLED";; `
    + 'esac; done; }';
}

/** The stand-ins, as functions the harness defines before the line. Absent managers are simply not defined. */
function standIns(managers: readonly string[]): string {
  return [
    'sudo() { echo "sudo $*" >> "$FAKE_LOG"; "$@"; }',
    // Moonshot's script: curl hands the pipe a line, and `bash` — the right-hand side — reads it and logs that the script ran.
    'curl() { echo "curl $*" >> "$FAKE_LOG"; echo "the script"; }',
    'bash() { while IFS= read -r coai_line; do :; done; echo moonshot-ran >> "$FAKE_LOG"; }',
    ...managers.map((name) => packageManager(name, name === 'apt-get' ? 'fdfind' : 'fd')),
  ].join('\n');
}

interface World {
  /** The package managers that exist on this machine. */
  readonly managers: readonly ('apt-get' | 'dnf')[];
  /** Programs that already resolve on the PATH. */
  readonly onPath?: readonly string[];
  /** Programs already in `~/.kimi-code/bin`. */
  readonly kimiBin?: readonly string[];
  /** The exit code every package manager answers with. */
  readonly pmExit?: number;
  /** A file somebody already has at `~/.local/bin/fd`. */
  readonly fdFile?: string;
  /** Aliases an interactive terminal has: `command -v` answers their TEXT, not a path (S3 of the security judge). */
  readonly aliases?: readonly string[];
  /** `~/.local/bin/fd` is a symlink to a DIRECTORY somebody planted (S2): `ln -sf` would write inside it. */
  readonly fdDirLink?: boolean;
  /** `~/.local/bin/fd` is a symlink: to a program of the person's own (it resolves), or to nothing (it dangles) — G of the review of 2026-10-18. */
  readonly fdLink?: 'resolving' | 'dangling';
  /** Debian's own `/usr/bin/fdfind` exists (the line's one literal path is pointed at a stand-in for it, so a real one on the test machine never matters). */
  readonly usrBinFdfind?: boolean;
  /** `fdfind` resolves ONLY through a RELATIVE entry of PATH, so `command -v fdfind` answers a relative path. */
  readonly relativeFdfind?: boolean;
  /** Programs already in `~/.local/bin`, as executable files. */
  readonly localBinTools?: readonly string[];
}

interface Place {
  readonly folder: string;
  /** The stand-in for Debian's `/usr/bin/fdfind`: a file only in a world that has one. */
  readonly usrBin: string;
  readonly home: string;
  readonly stub: string;
  readonly shelf: string;
  readonly log: string;
  /** The programs the stand-in package managers installed so far, one name a line. */
  readonly installed: string;
  readonly world: World;
}

let places = 0;

const marker = (path: string): void => {
  writeFileSync(path, '#!/bin/sh\n', { mode: 0o755 });
  chmodSync(path, 0o755);
};

/** A fresh machine: an empty home (no `~/.local`, no `~/.kimi-code` unless the world has one), the stand-in folder, the shelf of installable programs. */
function place(world: World): Place {
  places += 1;
  const folder = join(root, `world ${places}`);
  const [home, stub, shelf] = [join(folder, 'home dir'), join(folder, 'stand ins'), join(folder, 'shelf')];
  for (const dir of [home, stub]) {
    mkdirSync(dir, { recursive: true });
  }
  for (const [dir, program] of [['rg', 'rg'], ['fdfind', 'fdfind'], ['fd', 'fd']] as const) {
    mkdirSync(join(shelf, dir), { recursive: true });
    marker(join(shelf, dir, program));
  }
  for (const name of world.onPath ?? []) {
    marker(join(stub, name));
  }
  if ((world.kimiBin ?? []).length > 0) {
    mkdirSync(join(home, '.kimi-code', 'bin'), { recursive: true });
    for (const name of world.kimiBin ?? []) {
      marker(join(home, '.kimi-code', 'bin', name));
    }
  }
  if (world.fdFile !== undefined) {
    mkdirSync(join(home, '.local', 'bin'), { recursive: true });
    writeFileSync(join(home, '.local', 'bin', 'fd'), world.fdFile);
  }
  for (const name of world.localBinTools ?? []) {
    mkdirSync(join(home, '.local', 'bin'), { recursive: true });
    marker(join(home, '.local', 'bin', name));
  }
  const usrBin = join(folder, 'usr bin', 'fdfind');
  if (world.usrBinFdfind === true) {
    mkdirSync(join(folder, 'usr bin'), { recursive: true });
    marker(usrBin);
  }
  if (world.relativeFdfind === true) {
    mkdirSync(join(folder, 'rel'), { recursive: true });
    marker(join(folder, 'rel', 'fdfind'));
  }
  if (world.fdLink === 'resolving') {
    marker(join(home, 'own fd'));
  }

  return { folder, usrBin, home, stub, shelf, log: join(folder, 'log.txt'), installed: join(folder, 'installed.txt'), world };
}

interface Pressed {
  readonly out: string;
  readonly err: string;
  /** The line's own exit status. */
  readonly status: number;
  /** Everything the stand-ins were asked, in order. */
  readonly log: readonly string[];
  /** `~/.local/bin/fd` is a link. */
  readonly isLink: boolean;
  /** `~/.local/bin/fd` and `fdfind` are the same file. */
  readonly linkedToFdfind: boolean;
  /** The names in `~/.local/bin`, space-separated. */
  readonly localBin: string;
  /** The names that landed inside the planted directory (S2) — none when the link replaced the link. */
  readonly planted: string;
  /** `~/.local/bin/fd` is the person's own program (`~/own fd`), reached by their link. */
  readonly linkedToOwn: boolean;
  /** `~/.local/bin/fd` is the stand-in for `/usr/bin/fdfind`. */
  readonly linkedToUsrBin: boolean;
  /** Where `~/.local/bin/fd` points, as written — read only in a world that asks. */
  readonly target: string;
  /** `~/.local` exists afterwards. */
  readonly localMade: boolean;
}

const PATH_SEPARATOR = WINDOWS ? ';' : ':';

function envFor(where: Place, path: string): Record<string, string> {
  return {
    HOME: where.home,
    PATH: path,
    FAKE_LOG: posix(where.log),
    FAKE_INSTALLED: posix(where.installed),
    FAKE_SHELF: posix(where.shelf),
    FAKE_PM_EXIT: String(where.world.pmExit ?? 0),
    FAKE_USRBIN: posix(where.usrBin),
    FAKE_WANT_TARGET: where.world.fdLink === undefined && where.world.relativeFdfind !== true && where.world.usrBinFdfind !== true ? '' : 'yes',
    // Git Bash: a link is a .lnk shortcut, which needs no privilege and reads back as a link.
    ...(WINDOWS ? { MSYS: 'winsymlinks:lnk', MSYS2_ARG_CONV_EXCL: '*', SystemRoot: process.env['SystemRoot'] ?? 'C:\\Windows' } : {}),
  };
}

/** The probe the harness appends to the line: its status, and what a person would see on disk afterwards — built-ins only, a fork costs seconds here. */
const PROBE = [
  'coai_status=$?',
  'echo "COAI-STATUS=$coai_status"',
  'coai_fdfind=$(command -v fdfind)',
  '[ -L ~/.local/bin/fd ] && echo "COAI-ISLINK=yes"',
  '[ -n "$coai_fdfind" ] && [ ~/.local/bin/fd -ef "$coai_fdfind" ] && echo "COAI-LINKED=yes"',
  '[ -L ~/.local/bin/fd ] && [ ~/.local/bin/fd -ef "$HOME/own fd" ] && echo "COAI-LINKEDOWN=yes"',
  '[ -n "$FAKE_USRBIN" ] && [ -f "$FAKE_USRBIN" ] && [ ~/.local/bin/fd -ef "$FAKE_USRBIN" ] && echo "COAI-LINKEDUSR=yes"',
  '[ -e ~/.local ] && echo "COAI-LOCALMADE=yes"',
  '[ -n "$FAKE_WANT_TARGET" ] && [ -L ~/.local/bin/fd ] && echo "COAI-TARGET=$(readlink ~/.local/bin/fd)"',
  'coai_list=',
  'for coai_f in ~/.local/bin/*; do { [ -e "$coai_f" ] || [ -L "$coai_f" ]; } && coai_list="$coai_list ${coai_f##*/}"; done',
  'echo "COAI-BINLIST=$coai_list"',
  'coai_in=',
  'for coai_f in "$HOME/planted dir"/*; do { [ -e "$coai_f" ] || [ -L "$coai_f" ]; } && coai_in="$coai_in ${coai_f##*/}"; done',
  'echo "COAI-PLANTED=$coai_in"',
].join('\n');

/** What the world has before the line runs: aliases, and a planted directory link. Built by the shell itself, so the link is the one `ln` makes. */
function prelude(world: World): string {
  const aliases = (world.aliases ?? []).length === 0 ? [] : ['shopt -s expand_aliases', ...(world.aliases ?? []).map((one) => `alias ${one.replace('=', "='")}'`)];
  const plant = world.fdDirLink === true ? ['mkdir -p "$HOME/.local/bin" "$HOME/planted dir" && ln -s "$HOME/planted dir" "$HOME/.local/bin/fd"'] : [];
  const link = world.fdLink === undefined ? [] : [`mkdir -p "$HOME/.local/bin" && ln -s "$HOME/${world.fdLink === 'resolving' ? 'own fd' : 'gone fd'}" "$HOME/.local/bin/fd"`];

  return [...aliases, ...plant, ...link].join('\n');
}

/** The shelf folders of what earlier presses installed — a real install outlives the shell that ran it. */
function installedBefore(where: Place): readonly string[] {
  const names = existsSync(where.installed) ? readFileSync(where.installed, 'utf8').split(/\r?\n/u).filter((one) => one.length > 0) : [];

  return [...new Set(names)].map((name) => posix(join(where.shelf, name)));
}

/** One press of the line on a machine: `bash -c` of the stand-ins, the line and the probe, a deadline on it, the stand-in log read back. */
function press(where: Place, line: string): Promise<Pressed> {
  writeFileSync(where.log, '');
  // The line's one literal `/usr/bin/fdfind` is pointed at this world's stand-in, so a real Debian file on the machine running the test changes nothing.
  const script = `${standIns(where.world.managers)}\n${prelude(where.world)}\n${line.replaceAll(USR_BIN_FDFIND, `"${posix(where.usrBin)}"`)}\n${PROBE}`;
  const path = [...(where.world.relativeFdfind === true ? ['rel'] : []), ...installedBefore(where), posix(where.stub), CORE].join(PATH_SEPARATOR);
  const env = envFor(where, path);

  return new Promise((resolve, reject) => {
    execFile(BASH, ['-c', script], { env, cwd: where.folder, encoding: 'utf8', timeout: 300_000 }, (_error, stdout, stderr) => {
      const has = (name: string): boolean => new RegExp(`^COAI-${name}=yes$`, 'mu').test(stdout);
      const status = /^COAI-STATUS=(\d+)$/mu.exec(stdout)?.[1];
      if (status === undefined) {
        reject(new Error(`bash did not run the line and the probe: ${stdout}${stderr}`));

        return;
      }
      resolve({
        out: stdout,
        err: stderr.replace(/\s+/gu, ' '),
        status: Number(status),
        log: readFileSync(where.log, 'utf8').split(/\r?\n/u).filter((one) => one.length > 0),
        isLink: has('ISLINK'),
        linkedToFdfind: has('LINKED'),
        localBin: /^COAI-BINLIST=(.*)$/mu.exec(stdout)?.[1]?.trim() ?? '',
        planted: /^COAI-PLANTED=(.*)$/mu.exec(stdout)?.[1]?.trim() ?? '',
        linkedToOwn: has('LINKEDOWN'),
        linkedToUsrBin: has('LINKEDUSR'),
        target: /^COAI-TARGET=(.*)$/mu.exec(stdout)?.[1]?.trim() ?? '',
        localMade: has('LOCALMADE'),
      });
    });
  });
}

const LINE = vendorInstall(KIMI, 'linux', 'wsl2').command;
/** The one absolute path the line names for Debian's fd program. */
const USR_BIN_FDFIND = '/usr/bin/fdfind';
const sudoLines = (log: readonly string[]): readonly string[] => log.filter((one) => one.startsWith('sudo '));
const installed = (log: readonly string[]): readonly string[] => log.filter((one) => /^(?:apt-get|dnf) /u.test(one));
const MOONSHOT_RAN = ['curl -fsSL ' + MOONSHOT_URL, 'moonshot-ran'];
const moonshotSteps = (log: readonly string[]): readonly string[] => log.filter((one) => one.startsWith('curl ') || one === 'moonshot-ran');

// Every press starts now. A world is made and its press begun at load, so the five-second spawns of a slow machine overlap.
const started = <T>(make: () => Promise<T>): Promise<T> => (BASH === '' ? new Promise<T>(() => undefined) : make());

const debianPlace = BASH === '' ? undefined : place({ managers: ['apt-get'] });
/** Read BEFORE the press starts: the fresh account has no `~/.local` until the line makes it. */
const debianHadNoLocal = debianPlace !== undefined && !existsSync(join(debianPlace.home, '.local'));
const debianFresh = started(() => press(debianPlace!, LINE));
const debianSecond = started(async () => { await debianFresh; return press(debianPlace!, LINE); });
const debianInstalled = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'] }), LINE));
const fedoraFdThere = started(() => press(place({ managers: ['dnf'], onPath: ['fd'] }), LINE));
const noManager = started(() => press(place({ managers: [] }), LINE));
const noManagerToolsThere = started(() => press(place({ managers: [], onPath: ['rg', 'fd'] }), LINE));
const ownersCase = started(() => press(place({ managers: ['apt-get'], kimiBin: ['rg', 'fd'] }), LINE));
const managerFails = started(() => press(place({ managers: ['apt-get'], pmExit: 1 }), LINE));
const realFilePlace = BASH === '' ? undefined : place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], fdFile: 'mine\n' });
const realFile = started(() => press(realFilePlace!, LINE));
const dirLink = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], fdDirLink: true }), LINE));
const aliasFd = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], aliases: ['fd=fdfind'] }), LINE));
const aliasRg = started(() => press(place({ managers: ['apt-get'], onPath: ['fdfind'], aliases: ['rg=grep'] }), LINE));
// The review of 2026-10-18 (G, H): the link is judged by what is at the path, its target is absolute and prefers Debian's own file, and the check follows the server's order.
const ownLink = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], fdLink: 'resolving' }), LINE));
const danglingLink = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], fdLink: 'dangling' }), LINE));
const relativePath = started(() => press(place({ managers: ['apt-get'], onPath: ['rg'], relativeFdfind: true }), LINE));
const usrBinWins = started(() => press(place({ managers: ['apt-get'], onPath: ['rg', 'fdfind'], usrBinFdfind: true }), LINE));
const pathBeforeLocalBin = started(() => press(place({ managers: [], onPath: ['rg', 'fd'], localBinTools: ['rg', 'fd'] }), LINE));
// The pressed worlds, to be read: a rejected press is reported by the test that awaits it, never as an unhandled rejection.
for (const pending of [debianFresh, debianSecond, debianInstalled, fedoraFdThere, noManager, noManagerToolsThere, ownersCase, managerFails, realFile, dirLink, aliasFd, aliasRg, ownLink, danglingLink, relativePath, usrBinWins, pathBeforeLocalBin]) {
  pending.catch(() => undefined);
}

// ---------------------------------------------------------------- the line, read

test('the Linux line is the same on WSL2 and on native Linux, and ⟳ is the same line as ⤓', () => {
  assert.equal(vendorInstall(KIMI, 'linux', 'linux').command, LINE);
  assert.equal(vendorUpdate(KIMI, 'linux', '', 'wsl2').command, LINE);
  assert.equal(vendorUpdate(KIMI, 'linux', '2.2.0', 'linux').command, LINE);
  assert.equal(vendorInstall(KIMI, 'linux', 'wsl2').prerequisite, '', 'nothing is left for the person to run first');
});

test('the Linux line ends with Moonshot’s own script and a check, and is not Moonshot’s script alone any more', () => {
  assert.ok(LINE.includes(`curl -fsSL ${MOONSHOT_URL} | bash`), LINE);
  assert.notEqual(LINE, `curl -fsSL ${MOONSHOT_URL} | bash`, 'ripgrep and fd are provisioned first (DL12)');
  assert.ok(LINE.indexOf('curl -fsSL') < LINE.indexOf('for coai_t in rg fd'), 'the line ENDS by checking its own work, after Moonshot’s step');
});

test('every step of the Linux line is read by the official-source check: the whole line is admitted (S-K10)', () => {
  assert.equal(fromOfficialSources(LINE), true, LINE);
  assert.equal(fromOfficialSources(`${LINE}; sudo rm -rf ~/x`), false, 'and a step added to it is not vouched for by the rest');
  assert.equal(fromOfficialSources(LINE.replace('sudo apt-get install -y ripgrep', 'sudo apt-get install -y curl')), false, 'a changed package is refused');
  assert.equal(fromOfficialSources(LINE.replace('sudo apt-get install -y ripgrep', 'sudo rm -rf ~')), false, 'a changed verb is refused');
});

test('the line names the two packages, both managers, and sudo only in front of them', () => {
  for (const word of ['sudo apt-get install -y ripgrep', 'sudo apt-get install -y fd-find', 'sudo dnf install -y ripgrep', 'sudo dnf install -y fd-find']) {
    assert.ok(LINE.includes(word), `the line never runs: ${word}`);
  }
  assert.equal(LINE.match(/sudo /gu)?.length, 4, 'sudo appears in front of exactly the four package steps');
  assert.ok(LINE.includes('mkdir -p ~/.local/bin'), 'the folder is made before the link (a fresh account has none)');
  assert.ok(LINE.includes('command -v fdfind'), 'the link is made only when Debian’s name for fd resolves');
  assert.ok(LINE.includes('ln -sfn') && !LINE.includes('ln -sf "'), 'the link is made with ln -sfn (never -sf: it follows a planted directory link), on that exact path');
});

test('the line parses: bash -n accepts it, and refuses a broken one (the check has teeth)', { skip }, () => {
  const ok = spawnSync(BASH, ['-n', '-c', LINE], { encoding: 'utf8', timeout: 120_000 });
  const broken = spawnSync(BASH, ['-n', '-c', `${LINE} fi`], { encoding: 'utf8', timeout: 120_000 });

  assert.equal(ok.status, 0, `bash -n refused the line: ${ok.stderr}`);
  assert.notEqual(broken.status, 0, 'bash -n accepted a line with a stray fi — the parse test would pass for anything');
});

// ---------------------------------------------------------------- the line, run (S-K17)

test('the harness PATH resolves no real package manager, sudo, rg or fd — the guard every run below relies on', { skip }, () => {
  const ran = spawnSync(BASH, ['-c', 'command -v apt-get dnf sudo rg fd fdfind; echo guard-ended'], {
    env: envFor(place({ managers: [] }), CORE), encoding: 'utf8', timeout: 120_000,
  });

  assert.equal(ran.stdout.trim(), 'guard-ended', `a real tool resolves on the harness PATH: ${ran.stdout}`);
});

test('Debian, a fresh account: ripgrep and fd-find are installed, ~/.local/bin is made, fd links to fdfind, Moonshot’s step runs after them, and the line says where rg and fd resolve', { skip }, async () => {
  assert.equal(debianHadNoLocal, true, 'the fresh account must have no ~/.local before the first press');
  assert.match(debianPlace!.home, / /u, 'the harness must put a space in HOME: the line has to quote what it must');
  const first = await debianFresh;

  assert.equal(first.status, 0, `${first.out}${first.err}`);
  assert.deepEqual(sudoLines(first.log), ['sudo apt-get install -y ripgrep', 'sudo apt-get install -y fd-find']);
  assert.deepEqual(installed(first.log), ['apt-get install -y ripgrep', 'apt-get install -y fd-find'], 'sudo handed the step to apt-get unchanged');
  assert.deepEqual(moonshotSteps(first.log), MOONSHOT_RAN, 'Moonshot’s script ran, once');
  assert.ok(first.log.indexOf('moonshot-ran') > first.log.indexOf('apt-get install -y fd-find'), 'Moonshot’s step runs after the tools');
  assert.equal(first.isLink, true, 'ln into a missing ~/.local/bin: the folder is made and ~/.local/bin/fd is a link');
  assert.equal(first.linkedToFdfind, true, '…to fdfind');
  assert.match(first.localBin, /\bfd\b/u);
  assert.match(first.out, /^rg: .*\/rg$/mu, 'the closing check names where rg resolved');
  assert.match(first.out, /^fd: .*\/\.local\/bin\/fd$/mu, 'and where fd resolved — the link (~/.local/bin is the last place the server looks, after PATH)');
  assert.ok(!first.out.includes(NO_MANAGER), 'the instruction is for a host with no package manager');
});

test('a second press succeeds and changes nothing: no package manager is asked, the link is still the link, ln does not fail', { skip }, async () => {
  const [first, second] = [await debianFresh, await debianSecond];

  assert.equal(first.status, 0);
  assert.equal(second.status, 0, `the second press failed: ${second.out}${second.err}`);
  assert.deepEqual(sudoLines(second.log), [], 'the second press asked for sudo again');
  assert.deepEqual(installed(second.log), []);
  assert.doesNotMatch(second.err, /File exists|ln:/u, 'the second press failed: ln: … File exists');
  assert.equal(second.isLink && second.linkedToFdfind, true, 'the link was replaced or lost');
  assert.deepEqual(moonshotSteps(second.log), MOONSHOT_RAN, 'Moonshot’s step is still the update (⟳ is the same line)');
  assert.match(second.out, /^fd: .*\/\.local\/bin\/fd$/mu);
});

test('Debian with fdfind and rg already installed but no link: no sudo, the link is made', { skip }, async () => {
  const pressed = await debianInstalled;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.deepEqual(sudoLines(pressed.log), [], 'fdfind resolves, so fd-find is not installed again');
  assert.equal(pressed.isLink && pressed.linkedToFdfind, true);
});

test('a Fedora stand-in where fd already resolves: dnf installs ripgrep alone, and NO link is made', { skip }, async () => {
  const pressed = await fedoraFdThere;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.deepEqual(sudoLines(pressed.log), ['sudo dnf install -y ripgrep']);
  assert.deepEqual(installed(pressed.log), ['dnf install -y ripgrep']);
  assert.equal(pressed.isLink, false, 'a link was made although fd already resolved');
  assert.doesNotMatch(pressed.localBin, /\bfd\b/u);
  assert.match(pressed.out, /^fd: .*\/fd$/mu, 'the closing check finds the program that was there');
  assert.match(pressed.out, /^rg: .*\/rg$/mu, 'and the one dnf provided');
  assert.deepEqual(moonshotSteps(pressed.log), MOONSHOT_RAN);
});

test('a host with neither apt-get nor dnf: nothing is installed, ONE generic instruction is printed, and Moonshot’s script still runs (F2 + F13)', { skip }, async () => {
  const pressed = await noManager;
  const printed = pressed.out.split(NO_MANAGER).length - 1;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.deepEqual(sudoLines(pressed.log), [], 'on a host without apt-get or dnf the line tried \'sudo apt-get …\'');
  assert.deepEqual(installed(pressed.log), []);
  assert.equal(printed, 1, `the instruction is printed ${printed} times: ${pressed.out}`);
  assert.deepEqual(moonshotSteps(pressed.log), MOONSHOT_RAN);
  assert.match(pressed.out, /^rg: not found$/mu, 'a press that installed nothing usable says so in the terminal');
  assert.match(pressed.out, /^fd: not found$/mu);
  assert.doesNotMatch(pressed.out + pressed.err, /winget/u);
});

test('a host with neither manager but with rg and fd already there says nothing about installing them', { skip }, async () => {
  const pressed = await noManagerToolsThere;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.ok(!pressed.out.includes(NO_MANAGER), 'nothing is missing, so nothing is said');
  assert.match(pressed.out, /^rg: .*\/rg$/mu);
  assert.match(pressed.out, /^fd: .*\/fd$/mu);
});

test('the owner’s case: rg and fd already in ~/.kimi-code/bin — nothing is installed, no link, and the check names that folder first', { skip }, async () => {
  const pressed = await ownersCase;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.deepEqual(sudoLines(pressed.log), []);
  assert.equal(pressed.isLink, false);
  assert.match(pressed.out, /^rg: .*\/\.kimi-code\/bin\/rg$/mu);
  assert.match(pressed.out, /^fd: .*\/\.kimi-code\/bin\/fd$/mu);
});

test('a package manager that fails STOPS the line: Moonshot’s script does not run and the status is not zero', { skip }, async () => {
  const pressed = await managerFails;

  assert.notEqual(pressed.status, 0, 'a failed apt-get was walked past');
  assert.deepEqual(sudoLines(pressed.log), ['sudo apt-get install -y ripgrep'], 'the next step ran after a failed one');
  assert.deepEqual(moonshotSteps(pressed.log), [], 'Moonshot’s step ran after a failed tool step');
});

test('a real file at ~/.local/bin/fd is left alone: the link replaces a link, never a file', { skip }, async () => {
  const pressed = await realFile;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.equal(pressed.isLink, false, 'the person’s own file was replaced by a link');
  assert.equal(readFileSync(join(realFilePlace!.home, '.local', 'bin', 'fd'), 'utf8'), 'mine\n', 'the person’s own file was overwritten');
  assert.deepEqual(moonshotSteps(pressed.log), MOONSHOT_RAN);
});

test('a symlink at ~/.local/bin/fd that points to a DIRECTORY is replaced by the link, and nothing lands in that directory (S2: ln -sfn)', { skip }, async () => {
  const pressed = await dirLink;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.equal(pressed.planted, '', `ln followed the planted directory link and wrote inside it: ${pressed.planted}`);
  assert.equal(pressed.isLink && pressed.linkedToFdfind, true, '~/.local/bin/fd must be the link to fdfind, not the planted directory');
});

test('G: a link of the person’s own at ~/.local/bin/fd that resolves is left alone — it is replaced only when it dangles', { skip }, async () => {
  const [own, dangling] = [await ownLink, await danglingLink];

  assert.equal(own.status, 0, `${own.out}${own.err}`);
  assert.equal(own.linkedToOwn, true, 'the person’s own link was replaced');
  assert.equal(own.linkedToFdfind, false, 'the line linked fd to fdfind over a link that works');
  assert.equal(dangling.status, 0, `${dangling.out}${dangling.err}`);
  assert.equal(dangling.isLink && dangling.linkedToFdfind, true, 'a link that points at nothing is replaced by the link to fdfind');
});

test('G: when no link is needed nothing is made — ~/.local is not created for a host whose fd already resolves', { skip }, async () => {
  const [fedora, bothThere] = [await fedoraFdThere, await noManagerToolsThere];

  assert.equal(fedora.localMade, false, '~/.local was made although no link was');
  assert.equal(bothThere.localMade, false, '~/.local was made although nothing needed it');
  assert.equal((await debianFresh).localMade, true, 'and where the link IS made, the folder is made with it');
});

test('G: the link’s target is ABSOLUTE — a relative PATH entry is turned into a path from the current folder', { skip }, async () => {
  const pressed = await relativePath;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.equal(pressed.isLink && pressed.linkedToFdfind, true, 'the link was not made, or points elsewhere');
  assert.match(pressed.target, /^\//u, `a relative link target stops working the moment the folder is not the current one: ${pressed.target}`);
});

test('G: Debian’s own /usr/bin/fdfind is preferred over whatever fdfind PATH resolves to first', { skip }, async () => {
  const pressed = await usrBinWins;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.equal(pressed.linkedToUsrBin, true, 'the link points at the fdfind PATH found first, not at the distribution’s own');
  assert.equal(pressed.linkedToFdfind, false);
  assert.equal(LINE.split(USR_BIN_FDFIND).length - 1, 1, 'the line must name the distribution’s path exactly once: the stand-in above is the ONE place it is pointed elsewhere');
});

test('H: the closing check looks where the SERVER looks — Kimi’s bin, then PATH, then ~/.local/bin', { skip }, async () => {
  const pressed = await pathBeforeLocalBin;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.match(pressed.out, /^rg: .*\/stand ins\/rg$/mu, 'a PATH hit comes before ~/.local/bin (KimiTools.Directories: bin, PATH, then ~/.local/bin)');
  assert.match(pressed.out, /^fd: .*\/stand ins\/fd$/mu);
});

test('an alias-only fd is a missing fd: the link is still made, to the FILE fdfind resolves to (S3)', { skip }, async () => {
  const pressed = await aliasFd;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.equal(pressed.isLink && pressed.linkedToFdfind, true, 'an alias named fd was read as the program: no link was made');
  assert.match(pressed.out, /^fd: .*\/\.local\/bin\/fd$/mu, 'the check finds the link, not the alias');
});

test('an alias-only rg is a missing rg: the package is installed, and the check names the file (S3)', { skip }, async () => {
  const pressed = await aliasRg;

  assert.equal(pressed.status, 0, `${pressed.out}${pressed.err}`);
  assert.deepEqual(sudoLines(pressed.log), ['sudo apt-get install -y ripgrep'], 'an alias named rg was read as the program: nothing was installed');
  assert.match(pressed.out, /^rg: .*\/rg$/mu);
});

// ---------------------------------------------------------------- the review of 2026-10-18 (I): what the person is told when apt has no list yet, and which PATH counts

test('I: the Kimi card’s Linux note and the help in all five languages say what to do when apt cannot find the packages, and which PATH the tools must be on', () => {
  assert.match(KIMI_TOOLS_NOTE, /`sudo apt-get update`/u, 'the card’s note never says what to do when apt cannot find ripgrep or fd-find');
  assert.match(KIMI_TOOLS_NOTE, /VS Code server/u);
  assert.match(KIMI_TOOLS_NOTE, /`\.bashrc`/u, 'the card’s note never says that a PATH only .bashrc adds is not the one the server sees');

  const article = HELP_ARTICLES.find((one) => Object.values(bodyFor(one, 'en').body).join(' ').includes('`sudo apt-get`'));
  assert.ok(article !== undefined, 'no help article describes the Linux install line any more: this test asserts nothing');
  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const said = Object.values(body).join(' ');

    assert.equal(fallback, false, `the ${language} article is missing, so a reader of it gets English`);
    for (const word of ['`sudo apt-get update`', '`.bashrc`', '`~/.local/bin`']) {
      assert.ok(said.includes(word), `the ${language} help never says ${word} beside the Linux install line`);
    }
  }
});
