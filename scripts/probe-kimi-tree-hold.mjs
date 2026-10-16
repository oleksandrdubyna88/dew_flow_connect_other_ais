#!/usr/bin/env node
// scripts/probe-kimi-tree-hold.mjs — M-K15: how long the shim's inheritable tree hold lives with the REAL Kimi 2.1.1.
// Plan: todo/PLAN_vibe_and_kimi_runtimes_E4.md (E4.2 decision 10, E4.3 S3.17). Results: research/RESULTS_kimi_confinement.md §27.
//
// The product's own path and nothing else: the SHIPPED `coai-mcp --ask-acp --profile kimi` on an OWNED home (`<root>/home` holding
// the `credentials` junction and the one coai-written config.toml, `<root>/os` empty), in the environment the runtime hands it
// (Minimal + the seven built names), with `--locks` naming a lock folder of this scratch. The person's sign-in is reached ONLY through
// that junction. This script never copies, prints or reads a credential file: it records the credentials folder's NAMES, sizes and
// modification times around each run (a refresh rotates the file in place), and nothing else about it. It never runs `kimi login`.
//
// What it asks (the three questions of S3.17):
//   normal      a plain answered turn: are both lock files Free within ~2 s of the shim's exit?
//   normal-tools  the same with a Grep and a Glob in the turn (so `rg`/`fd` are Kimi's children): still Free after the shim's tree kill?
//   killed      the shim killed by its own handle mid-turn, `kimi` left to itself: Held while `kimi` lives, Free within ~2 s of ITS exit —
//               and is anything else of Kimi's (a helper, an updater) still alive and holding the files after `kimi` is gone?
//   killed-rg   the same, killed while an `rg` child of `kimi` is running: does a child outlive `kimi` and keep the hold?
//
// Safety: before EVERY run the process list is read (never killed from): another `kimi` means two refreshers could race the rotating
// refresh token and sign the person out, so the run waits up to 10 minutes for it to end, and is SKIPPED (and said) when it does not.
// Processes are ended only by a PID this script started (the shim) — never by image name. A `kimi` orphaned by the killed-shim arms is
// waited for, not killed.
//
//   node scripts/probe-kimi-tree-hold.mjs [--shim <coai-mcp.exe>] [--scratch <dir>] [--repeats 3] [--arms normal,normal-tools,killed,killed-rg]
import { spawn, spawnSync } from 'node:child_process';
import { closeSync, existsSync, mkdirSync, mkdtempSync, openSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, win32 } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import { SYSTEM32, minimalWindowsEnv } from './lib/vibe.mjs';
import { DEFAULT_MODEL, KIMI_EXE, REAL_HOME, scratchRefusal } from './lib/kimi.mjs';
import { credentialNames, disposeOwnedHome, makeOwnedHome } from './lib/kimi-home.mjs';

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = argv.indexOf(name);
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : fallback;
};
const ARMS = {
  normal: { prompt: 'Reply with exactly: OK', killed: false },
  'normal-tools': {
    prompt: 'Use your Grep tool to find the line containing OSPREY in notes.txt and your Glob tool to list *.txt files in this folder. Then reply with exactly: DONE',
    killed: false,
  },
  killed: { prompt: 'Reply with exactly: OK', killed: true, after: 'kimi' },
  // A pattern no file holds, over a big read-only tree, so `rg` runs for seconds — long enough to kill the shim while it is alive.
  'killed-rg': {
    prompt: String.raw`Use your Grep tool to search for the text zzqxjv-no-such-text-anywhere recursively in the folder C:\Windows\System32 (a case-insensitive plain search is fine). Report only how many matches there were.`,
    killed: true,
    after: 'rg',
  },
};
const here = dirname(fileURLToPath(import.meta.url));
const shim = flag('--shim', '') || join(here, '..', 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', 'coai-mcp.exe');
const repeats = Number.parseInt(flag('--repeats', '3'), 10);
const armNames = flag('--arms', Object.keys(ARMS).join(',')).split(',').filter(Boolean);
if (armNames.some((a) => !(a in ARMS)) || !Number.isInteger(repeats) || repeats <= 0) {
  console.error(`usage: probe-kimi-tree-hold.mjs [--shim <exe>] [--scratch <dir>] [--repeats N] [--arms ${Object.keys(ARMS).join(',')}]`);
  process.exit(65);
}
if (process.platform !== 'win32' || !existsSync(shim) || !existsSync(KIMI_EXE) || !existsSync(join(REAL_HOME, 'credentials'))) {
  console.error(`refusing: needs Windows, the built shim (${shim}: ${existsSync(shim)}), kimi (${existsSync(KIMI_EXE)}) and a signed-in ${REAL_HOME}/credentials`);
  process.exit(65);
}
const scratch = flag('--scratch', '') || mkdtempSync(join(realpathSync.native(tmpdir()), `testsToDelete-kimi-mk15-${randomBytes(3).toString('hex')}-`));
const privacy = scratchRefusal(scratch);
if (privacy.length > 0) {
  console.error(privacy);
  process.exit(65);
}
mkdirSync(scratch, { recursive: true });
const locks = join(scratch, 'locks');
mkdirSync(locks, { recursive: true });

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const now = () => performance.now();

// ------------------------------------------------------------------------------------------------------ processes (read-only)

/**
 * Every live process as { pid, ppid, name } — a LISTING; nothing here ends a process. Windows PowerShell by its FIXED path under
 * System32 and never a bare name, so no folder of the PATH (which a person's own tools can write to) decides what runs (S4036);
 * the cmdlet and the string are the same in 5.1 as in pwsh 7.
 */
function listProcesses() {
  const r = spawnSync(SYSTEM32(String.raw`WindowsPowerShell\v1.0\powershell.exe`), ['-NoProfile', '-NonInteractive', '-Command',
    'Get-CimInstance Win32_Process | ForEach-Object { "$($_.ProcessId),$($_.ParentProcessId),$($_.Name)" }'], { encoding: 'utf8', timeout: 30_000 });
  return String(r.stdout ?? '').split(/\r?\n/u).filter(Boolean).map((line) => {
    const [pid, ppid, ...name] = line.split(',');
    return { pid: Number(pid), ppid: Number(ppid), name: name.join(',') };
  });
}

const descendantsOf = (root, all) => {
  const found = [];
  const queue = [root];
  while (queue.length > 0) {
    const parent = queue.shift();
    for (const p of all.filter((x) => x.ppid === parent && x.pid !== parent)) {
      found.push(p);
      queue.push(p.pid);
    }
  }
  return found;
};

const alive = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return e.code === 'EPERM';
  }
};

/** Waits (up to 10 minutes) for any other kimi process to end; false = it did not, and the run is skipped. */
async function noOtherKimi(label) {
  const until = now() + 10 * 60_000;
  while (true) {
    const others = listProcesses().filter((p) => /^kimi(\.exe)?$/iu.test(p.name));
    if (others.length === 0) return true;
    if (now() > until) {
      console.error(`[mk15] ${label}: SKIPPED — another kimi still runs after 10 minutes (pids ${others.map((p) => p.pid).join(', ')}); two refreshers could sign the person out`);
      return false;
    }
    console.error(`[mk15] ${label}: waiting — another kimi runs (pids ${others.map((p) => p.pid).join(', ')})`);
    await sleep(10_000);
  }
}

// ------------------------------------------------------------------------------------------------------ the lock files

/** Held / Free / Absent by trying to OPEN the file: a file held with FileShare.None refuses every other open (a sharing violation, EBUSY). */
function probe(path) {
  try {
    closeSync(openSync(path, 'r'));
    return 'Free';
  } catch (e) {
    if (e.code === 'ENOENT') return 'Absent';
    return e.code === 'EBUSY' || e.code === 'EPERM' || e.code === 'EACCES' ? 'Held' : `Unknown:${e.code}`;
  }
}

/** The files this run's shim made: the sign-in's `.tree.lock` in the lock folder and the home's `coai-tree.lock` in the root. */
const treeFile = () => readdirSync(locks).map((n) => join(locks, n)).find((p) => p.endsWith('.tree.lock')) ?? '';

// ------------------------------------------------------------------------------------------------------ the credentials (names only)

/** Names, sizes and mtimes — never a byte of content; a refresh rotates a file in place, so mtime says it happened. */
const credentialShape = () => credentialNames(REAL_HOME).map((n) => {
  const s = statSync(join(REAL_HOME, 'credentials', n));
  return `${n}:${s.size}:${Math.round(s.mtimeMs)}`;
});

// ------------------------------------------------------------------------------------------------------ one run

const credentialsBefore = credentialNames(REAL_HOME);

/** The shim's own environment, as the runtime would build it: Minimal + the seven built names, the person's bin first on PATH. */
function shimEnvironment(root) {
  const os = join(root, 'os');
  // The environment is a Windows one: its drive is read as a Windows path, whatever host runs this file.
  const { root: drive } = win32.parse(os);
  const bin = join(REAL_HOME, 'bin');
  const base = minimalWindowsEnv();
  const parentPath = base.PATH ?? process.env.PATH ?? '';
  return {
    ...base,
    KIMI_CODE_HOME: join(root, 'home'),
    KIMI_CODE_NO_AUTO_UPDATE: '1',
    USERPROFILE: os,
    HOME: os,
    HOMEDRIVE: drive.replace(/[\\/]+$/u, ''),
    HOMEPATH: os.slice(drive.replace(/[\\/]+$/u, '').length),
    PATH: [bin, parentPath].join(';'),
  };
}

async function runOne(armName, n) {
  const arm = ARMS[armName];
  const label = `${armName}-${n}`;
  if (!(await noOtherKimi(label))) return { arm: armName, n, skipped: 'another kimi process was running for 10 minutes' };
  const root = join(scratch, `kh-${label}`);
  const work = join(scratch, `work-${label}`);
  mkdirSync(join(root, 'os'), { recursive: true });
  mkdirSync(work, { recursive: true });
  writeFileSync(join(work, 'notes.txt'), 'a line\nOSPREY-1 is here\nanother line\n');
  const made = makeOwnedHome(join(root, 'home'), REAL_HOME, ['config-models'], { defaultModel: DEFAULT_MODEL });
  if (made.junctionError) throw new Error(made.junctionError);
  const shapeBefore = credentialShape();
  const homeFile = join(root, 'coai-tree.lock');
  const args = [
    '--ask-acp', '--profile', 'kimi', '--cwd', work, '--model', DEFAULT_MODEL, '--deadline-seconds', '240', '--executable', KIMI_EXE,
    '--home', root, '--persons-bin', join(REAL_HOME, 'bin'), '--locks', locks,
  ];
  console.error(`[mk15] ${label}: starting the shim`);
  const t0 = now();
  const child = spawn(shim, args, { cwd: work, env: shimEnvironment(root), stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', (d) => { stdout += d; });
  child.stderr.on('data', (d) => { stderr += d; });
  const exited = new Promise((resolve) => child.on('exit', (code) => resolve({ code, at: now() })));
  child.stdin.end(arm.prompt);

  const seen = new Map(); // pid -> name, every descendant of the shim ever listed
  const sample = () => {
    for (const p of descendantsOf(child.pid, listProcesses())) seen.set(p.pid, p.name);
  };
  const row = { arm: armName, n, label, killedAfterMs: 0, shim: { exit: null }, files: {}, descendants: [] };

  if (arm.killed) {
    // The shim takes the tree hold immediately before it starts kimi, so the file going Held is "the agent is starting". Then sample until the
    // process we wait for is a live descendant (kimi; or an rg), and kill the shim by its own handle — for the kimi arm no sooner than 4 s
    // after the hold, so the turn is mid-prompt (a plain turn takes 8-11 s here), not mid-handshake.
    const target = arm.after === 'rg' ? /^rg(.exe)?$/iu : /^kimi(.exe)?$/iu;
    const limit = now() + 120_000;
    while (now() < limit && child.exitCode === null && !(treeFile() && probe(treeFile()) === 'Held')) await sleep(20);
    const heldAt = now();
    row.heldAfterStartMs = Math.round(heldAt - t0);
    while (now() < limit && child.exitCode === null) {
      sample();
      const hit = [...seen.entries()].find(([, name]) => target.test(name));
      if (hit && (arm.after === 'rg' || now() - heldAt > 4_000)) break;
      await sleep(50);
    }
    row.firedOn = [...seen.values()].join(',');
    if (child.exitCode === null) {
      const killedAt = now();
      child.kill();
      row.killedAfterMs = Math.round(killedAt - t0);
      await watchAfterKill(row, killedAt, homeFile, seen);
    } else {
      row.note = 'the shim ended before the trigger';
    }
  } else {
    // A normal turn: sample the tree while it runs (so rg/fd are on the list), then the files after the shim's exit.
    const watcher = (async () => { while (child.exitCode === null) { sample(); await sleep(150); } })();
    const result = await exited;
    await watcher;
    await watchAfterShim(row, result.at, homeFile, seen);
  }
  const done = await exited;
  row.shim = { exit: done.code, seconds: Math.round((done.at - t0) / 100) / 10 };
  row.report = reportOf(stdout);
  row.stderrTail = stderr.slice(-300);
  row.descendants = [...new Set(seen.values())];
  row.credentials = { namesIntact: credentialsBefore.every((c) => credentialNames(REAL_HOME).includes(c)), rotated: JSON.stringify(shapeBefore) !== JSON.stringify(credentialShape()) };
  row.homeAfter = readdirSync(join(root, 'home')).filter((n) => n !== 'credentials').sort((a, b) => a.localeCompare(b));
  // D19's order, the product's: the junction first, by its own name, the person's folder verified — then the home, then the rest of the root.
  const disposed = disposeOwnedHome(join(root, 'home'), REAL_HOME, credentialsBefore);
  rmSync(root, { recursive: true, force: true });
  row.disposed = `link removed ${disposed.linkRemoved}, target intact ${disposed.targetIntact}, home gone ${disposed.homeGone}, root gone ${!existsSync(root)}`;

  return row;
}

/** The shim exited by itself: when does each file become Free, polled every 50 ms for up to 15 s. */
async function watchAfterShim(row, exitedAt, homeFile, seen) {
  const tree = treeFile();
  row.treeFileSeen = tree.length > 0;
  const free = { tree: -1, home: -1 };
  while (now() - exitedAt < 15_000 && (free.tree < 0 || free.home < 0)) {
    if (free.tree < 0 && (tree.length === 0 || probe(tree) === 'Free')) free.tree = Math.round(now() - exitedAt);
    if (free.home < 0 && probe(homeFile) === 'Free') free.home = Math.round(now() - exitedAt);
    await sleep(50);
  }
  row.files = { freeAfterShimExitMs: free, leftover: leftover(seen) };
}

/** The shim was killed: Held while `kimi` lives, and when each becomes Free — against `kimi`'s own exit and any other descendant's. */
async function watchAfterKill(row, killedAt, homeFile, seen) {
  const tree = treeFile();
  const kimiPid = [...seen.entries()].find(([, name]) => /^kimi(\.exe)?$/iu.test(name))?.[0] ?? 0;
  const others = [...seen.entries()].filter(([pid]) => pid !== kimiPid);
  const t = { kimiGone: -1, treeFree: -1, homeFree: -1, allGone: -1 };
  let heldWhileKimiLived = true;
  let sawHeld = false;
  const limit = killedAt + 5 * 60_000;
  while (now() < limit && (t.treeFree < 0 || t.homeFree < 0 || t.kimiGone < 0 || t.allGone < 0)) {
    const kimiLives = kimiPid !== 0 && alive(kimiPid);
    const treeState = tree.length === 0 ? 'Absent' : probe(tree);
    const homeState = probe(homeFile);
    if (kimiLives) {
      sawHeld = true;
      if (treeState !== 'Held' || homeState !== 'Held') heldWhileKimiLived = false;
    }
    const at = Math.round(now() - killedAt);
    if (t.kimiGone < 0 && !kimiLives) t.kimiGone = at;
    if (t.treeFree < 0 && treeState === 'Free') t.treeFree = at;
    if (t.homeFree < 0 && homeState === 'Free') t.homeFree = at;
    if (t.allGone < 0 && !kimiLives && others.every(([pid]) => !alive(pid))) t.allGone = at;
    await sleep(50);
  }
  row.files = {
    msAfterKill: t,
    heldWhileKimiLived: sawHeld && heldWhileKimiLived,
    freeAfterKimiExitMs: { tree: t.treeFree >= 0 && t.kimiGone >= 0 ? t.treeFree - t.kimiGone : null, home: t.homeFree >= 0 && t.kimiGone >= 0 ? t.homeFree - t.kimiGone : null },
    otherDescendants: others.map(([pid, name]) => ({ name, aliveAfterKimiGone: t.kimiGone >= 0 && alive(pid), pidSeen: true })).map(({ name, aliveAfterKimiGone }) => ({ name, aliveAfterKimiGone })),
  };
}

const leftover = (seen) => [...seen.entries()].filter(([pid]) => alive(pid)).map(([, name]) => name);

function reportOf(text) {
  try {
    const j = JSON.parse(text.trim());
    return {
      end: j.end, stopReason: j.stopReason, failure: j.failure ?? '', answerChars: String(j.answer ?? '').length, toolCalls: (j.toolCalls ?? []).map((c) => c.kind).join(','),
      tokensIn: j.usage?.tokensIn, tokensOut: j.usage?.tokensOut, notCaptured: j.usage?.notCaptured, reason: String(j.reason ?? '').slice(0, 160),
    };
  } catch {
    return { end: '(unparsed)', raw: text.slice(0, 120) };
  }
}

// ------------------------------------------------------------------------------------------------------ main

const rows = [];
let failure = '';
try {
  for (const armName of armNames) {
    for (let n = 1; n <= repeats; n++) {
      const row = await runOne(armName, n);
      rows.push(row);
      writeFileSync(join(scratch, 'results.json'), JSON.stringify({ shim, scratch, started: new Date().toISOString(), rows }, null, 2));
      console.error(`[mk15] ${row.label ?? `${armName}-${n}`}: ${JSON.stringify(row.files ?? row.skipped)}`);
    }
  }
} catch (e) {
  failure = e.stack ?? String(e);
  console.error(`[mk15] FAILED: ${failure}`);
}
writeFileSync(join(scratch, 'results.json'), JSON.stringify({ shim, scratch, finished: new Date().toISOString(), failure, rows }, null, 2));
console.log(JSON.stringify(rows, null, 2));
console.error(`[mk15] ${rows.length} runs; credentials names intact: ${credentialsBefore.every((c) => credentialNames(REAL_HOME).includes(c))}; scratch ${scratch}`);
if (failure) process.exit(70);
