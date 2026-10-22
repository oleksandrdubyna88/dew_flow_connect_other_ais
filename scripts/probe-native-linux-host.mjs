#!/usr/bin/env node
// scripts/probe-native-linux-host.mjs — the host's FACTS (N-H1, N-V0, N-K0 of todo/PLAN_kimi_and_vibe_on_native_linux.md §5, E1.S1).
//
// Read-only, free, one reading. It prints ONE JSON record to stdout (and writes it to <scratch>/host-facts.json when --scratch is
// named): the kernel banner and whether it names WSL, the OS, glibc, node, the uid, fs.protected_symlinks, the file-system type under
// the places coai writes, the VS Code server's PATH value and every entry of it with its owner and mode, node's absolute path (the
// one make-series.sh resolved), where vibe / kimi / rg / fd / flock resolve in the product's own order, the launcher's three
// programs, the installed coai-mcp (path, --version, sha256, and its --host-facts line — exit 64 is recorded as "no such mode");
// Vibe's `.env` by lstat, the names in ~/.vibe, Vibe's tool Python, the keyring-related distributions, the class name
// keyring.get_keyring() picks under the built environment AND under it plus PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring, the
// keyring-session facts (by THIS script's own raw reading — an independent second reading beside coai-mcp --host-facts, DN10); and,
// only when --kimi names an installed kimi, `kimi --version`, `kimi acp --help` (in an OWNED root under the scratch, after waiting for any other
// kimi to end - DN5) and the credentials folder's NAMES. A file that cannot be read is recorded "unreadable", never absent. One short line per slow
// step goes to STDERR before it starts; stdout stays the one JSON record.
//
// NEVER: it does not open, hash, copy or print the contents of ~/.vibe/.env or any file under ~/.kimi-code/credentials (lstat and
// readdir only — lib/host-facts.test.mjs fails if the file reader is ever called for either), never asks keyring for a password,
// never runs `kimi login`, `vibe --setup` or a Vibe/Kimi turn, never prints another process's environment (it reads the NAMES of the VS
// Code server's variables to answer one yes/no, and its PATH value from the file make-series.sh wrote), never ends a process.
//
//   node scripts/probe-native-linux-host.mjs [--series <folder>] [--scratch <dir>] [--kimi <path>] [--coai-mcp <path>] [--vibe-python <path>]
//
// The pure core is lib/host-facts.mjs (this file runs on import, so a test cannot import it — hence the split).
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstatSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, readlinkSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { FLAG_SPECS, parseFlags } from './lib/cli-args.mjs';
import { collectHostFacts } from './lib/host-facts.mjs';
import { ownedRoot } from './lib/kimi-home.mjs';
import { linuxProcesses, waitForNoOtherKimi } from './lib/proc.mjs';
import { minimalEnv } from './lib/vibe.mjs';

const EXIT = { ok: 0, badRequest: 65, unavailable: 69 };
const FLAGS = FLAG_SPECS.hostProbe.values;

/** `--flag value` pairs only (lib/cli-args.mjs): an unknown or repeated flag, a stray word, a flag with no value (or another --flag as its value) or a value off the safe pattern is a bad request. */
function parseArgs(argv) {
  const parsed = parseFlags(argv, FLAG_SPECS.hostProbe);
  return parsed.ok ? { ok: true, values: parsed.values, error: '' } : { ok: false, values: {}, error: parsed.error };
}

/** The NAMES of the VS Code server's environment variables (never a value): the server is the process whose `exe` lies under ~/.vscode-server/. */
function serverEnvNames(home) {
  const root = `${home}/.vscode-server/`;
  const pids = readdirSync('/proc').filter((name) => /^\d+$/u.test(name));
  for (const pid of pids) {
    try {
      if (readlinkSync(`/proc/${pid}/exe`).startsWith(root)) {
        return readFileSync(`/proc/${pid}/environ`, 'latin1').split('\0').map((pair) => pair.split('=')[0]).filter(Boolean);
      }
    } catch {
      // gone, or not ours to read: the next one
    }
  }
  return [];
}

const fsx = {
  lstat: (p) => lstatSync(p),
  stat: (p) => statSync(p),
  readdir: (p) => readdirSync(p),
  readFile: (p) => readFileSync(p, 'utf8'),
  mkdir: (p) => mkdirSync(p, { recursive: true, mode: 0o700 }),
};

/** The injected runner: no shell, a deadline, the minimal environment unless the caller built one. */
const run = (file, args, options = {}) => spawnSync(file, args, { encoding: 'utf8', timeout: 30_000, shell: false, env: options.env ?? minimalEnv('linux'), cwd: options.cwd });

const processes = () => linuxProcesses().map((p) => ({ pid: p.pid, ppid: p.ppid, comm: p.comm ?? p.name, name: p.name }));
const sha256 = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');
const glibc = () => process.report?.getReport?.().header?.glibcVersionRuntime ?? '';

/** One short line on STDERR before a slow step, so a person watching the run knows it is working; stdout stays the one JSON record. */
const progress = (line) => process.stderr.write(`[native-facts] ${line}\n`);

/** N-K0 starts kimi, which can refresh the person's sign-in: wait for any other kimi first (DN5). True when none runs, or when no kimi is asked for. */
const noOtherKimi = (kimi) => (kimi === '' ? Promise.resolve(true) : waitForNoOtherKimi('N-K0', { tag: 'native-facts' }));

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (!args.ok) {
    process.stderr.write(`usage: node scripts/probe-native-linux-host.mjs [${FLAGS.map((f) => `${f} <value>`).join('] [')}]\n${args.error}\n`);
    return EXIT.badRequest;
  }
  if (process.platform !== 'linux') {
    process.stderr.write('this script reads /proc and the Linux host\'s files — run it on the host, from its series folder\n');
    return EXIT.unavailable;
  }
  const own = args.values['--scratch'] === undefined;
  const scratch = own ? mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-native-facts-')) : args.values['--scratch'];
  try {
    mkdirSync(scratch, { recursive: true, mode: 0o700 });
    const home = homedir();
    const kimi = args.values['--kimi'] ?? '';
    const kimiClear = await noOtherKimi(kimi);
    const facts = collectHostFacts({
      fsx, run, env: process.env, uid: process.getuid(), home, platform: { node: process.version, glibc: glibc() }, listing: processes,
      serverEnvNames: () => serverEnvNames(home), series: args.values['--series'] ?? '', scratch, hash: sha256, now: () => new Date(),
      coaiMcp: args.values['--coai-mcp'] ?? '', vibePython: args.values['--vibe-python'] ?? '', kimi, kimiClear, ownedRoot, progress,
    });
    const text = `${JSON.stringify(facts, null, 2)}\n`;
    if (!own) writeFileSync(join(scratch, 'host-facts.json'), text);
    process.stdout.write(text);
    return EXIT.ok;
  } finally {
    if (own) rmSync(scratch, { recursive: true, force: true });
  }
}

process.exitCode = await main();
