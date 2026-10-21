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
//   killed-hard (E1.S3, N-K2) the same as `killed` with SIGKILL instead of SIGTERM: the shim handles TERM and ends the agent's tree itself, so only a KILL
//               leaves a survivor marker (`<key>.survivors.json` in the lock folder) abandoned. It COUNTS only when a marker existed at the kill (the row
//               says "not evidence" otherwise).
//   killed-hard-live (E1.S3 follow-up; N-K2 / cornerstone C2: a survivor keeps the next launch blocked, and only its confirmed disappearance clears the
//               marker — KimiSurvivorGate) a TRIPLE: the SIGKILL, then `-next` started WHILE the killed arm's own kimi (the "survivor") still runs, then `-after`
//               once it has ended. The "no other kimi" wait is bypassed for exactly that survivor — by pid AND start time, never by name; every other kimi
//               still blocks. Expected: `-next` is refused Busy naming the survivor and nothing starts; `-after` reconciles and clears. Recorded: the marker
//               before and after, the refusal text, the survivor's new parent and a verdict. RUNS ONLY with `--allow-live-survivor`, on a host where the owner
//               allowed that window: if the product's gate failed, two refreshers of one rotating token would sign the person out.
//
// Every killed arm's repetition is a PAIR (lib/kimi-schedule.mjs): `<arm>-<n>`, then at once `<arm>-<n>-next`, a normal launch that records the marker
// before it, the shim's report and the marker after it — where the next launch's reconcile of the abandoned marker is observed. Each killed row names every
// survivor of the kill with its NEW parent (pid, comm, exe basename: pid 1 or a `systemd --user` reaper).
//
// Safety: before EVERY run the process list is read (never killed from): another `kimi` — by the PRODUCT's rule, lib/proc.mjs isKimi, which also sees
// a running `kimi acp` whose comm is `MainThread` — means two refreshers could race the rotating refresh token and sign the person out, so the run waits
// up to 10 minutes for it to end, and is SKIPPED (and said) when it does not.
// Processes are ended only by a PID this script started (the shim) — never by image name. A `kimi` orphaned by the killed-shim arms is
// waited for, not killed.
//
//   node scripts/probe-kimi-tree-hold.mjs [--shim <coai-mcp.exe>] [--scratch <dir>] [--repeats 3] [--arms normal,normal-tools,killed,killed-hard,killed-rg]
//
// Linux (E1 of research/PLAN_kimi_and_vibe_on_linux.md, §5; the cells it serves are E4's L-K4/L-K6): the process list is read from
// /proc (lib/proc.mjs — pid, parent, comm and the product's name; read-only), a lock file's state is asked of flock(1) — .NET turns FileShare.None into
// an advisory flock there, which a plain open does not see — the shim is `coai-mcp` without `.exe`, the sign-in link is a
// symlink (DL6), the shim's environment is MinimalUnix with HOME alone redirected (DL11), and the killed-rg arm searches /usr.
// The rule about another `kimi` is the same and matters more: the refresh token rotates (D19).
import { spawn } from 'node:child_process';
import { closeSync, existsSync, mkdirSync, mkdtempSync, openSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, win32 } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import { minimalWindowsEnv } from './lib/vibe.mjs';
import { DEFAULT_MODEL, KIMI_EXE, REAL_HOME, scratchRefusal } from './lib/kimi.mjs';
import { credentialNames, disposeOwnedHome, makeOwnedHome, ownedRoot, shimEnv } from './lib/kimi-home.mjs';
import { KILL_ARMS, armsRefusal, buildSchedule, defaultArms, isLiveArm, killRecord, killSignal, liveSurvivor, liveVerdict, nextRow, refusalText, survivorMarkers } from './lib/kimi-schedule.mjs';
import { FLOCK, isKimi, listProcesses, lockState, startTimeOfPid, waitForNoOtherKimi } from './lib/proc.mjs';

const WINDOWS = process.platform === 'win32';

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
  // The signal each killed arm sends, and what it waits for first, are lib/kimi-schedule.mjs's KILL_ARMS (tested there: killed-hard is SIGKILL).
  killed: { prompt: 'Reply with exactly: OK', killed: true, ...KILL_ARMS.killed },
  'killed-hard': { prompt: 'Reply with exactly: OK', killed: true, ...KILL_ARMS['killed-hard'] },
  // The LIVE variant (N-K2, cornerstone C2): the next launch starts while this arm's own kimi still runs. Only with --allow-live-survivor (see the header).
  'killed-hard-live': { prompt: 'Reply with exactly: OK', killed: true, ...KILL_ARMS['killed-hard-live'] },
  // A pattern no file holds, over a big read-only tree, so `rg` runs for seconds — long enough to kill the shim while it is alive.
  'killed-rg': {
    prompt: `Use your Grep tool to search for the text zzqxjv-no-such-text-anywhere recursively in the folder ${WINDOWS ? String.raw`C:\Windows\System32` : '/usr'} (a case-insensitive plain search is fine). Report only how many matches there were.`,
    killed: true,
    ...KILL_ARMS['killed-rg'],
  },
};
const here = dirname(fileURLToPath(import.meta.url));
const shim = flag('--shim', '') || join(here, '..', 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', WINDOWS ? 'coai-mcp.exe' : 'coai-mcp');
const repeats = Number.parseInt(flag('--repeats', '3'), 10);
const armNames = flag('--arms', defaultArms(Object.keys(ARMS)).join(',')).split(',').filter(Boolean);
if (armNames.some((a) => !(a in ARMS)) || !Number.isInteger(repeats) || repeats <= 0) {
  console.error(`usage: probe-kimi-tree-hold.mjs [--shim <exe>] [--scratch <dir>] [--repeats N] [--arms ${Object.keys(ARMS).join(',')}] [--allow-live-survivor]`);
  process.exit(65);
}
const liveRefusal = armsRefusal(armNames, { allowLive: argv.includes('--allow-live-survivor'), platform: process.platform });
if (liveRefusal) {
  console.error(`refusing: ${liveRefusal}`);
  process.exit(65);
}
const SUPPORTED = WINDOWS || process.platform === 'linux';
if (!SUPPORTED || (!WINDOWS && !FLOCK) || !existsSync(shim) || !existsSync(KIMI_EXE) || !existsSync(join(REAL_HOME, 'credentials'))) {
  console.error(`refusing: needs Windows or Linux (with flock(1): ${WINDOWS || Boolean(FLOCK)}), the built shim (${shim}: ${existsSync(shim)}), kimi (${existsSync(KIMI_EXE)}) and a signed-in ${REAL_HOME}/credentials`);
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

// The listing — `{ pid, ppid, comm, name }`, a LISTING that ends nothing — and the Kimi test are lib/proc.mjs's: `name` is what the PRODUCT's listing reads
// (the exe's basename, else argv[0]'s), `isKimi` the product's rule (ForeignKimiWatch.IsKimi). On Windows `name` is the image name (listProcesses).

/** What a process is called in a row: the product's name, else its comm. */
const displayName = (entry) => entry.name || entry.comm;

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

/** Waits (up to 10 minutes) for any other kimi process to end; false = it did not, and the run is skipped. The guard is lib/proc.mjs's, shared with the confinement probe. */
const noOtherKimi = (label, except = []) => waitForNoOtherKimi(label, { tag: 'mk15', except });

/** The killed arm's kimi is still THAT process: alive, and its start time still the one recorded (a reused pid is a stranger). */
const survivorLives = (survivor) => alive(survivor.pid) && startTimeOfPid(survivor.pid) === survivor.startTime;

/** Waits (up to 5 minutes) for the survivor to end, watched by pid and start time: the ms it took, or -1 when it still runs. */
async function waitForSurvivorEnd(survivor) {
  const t0 = now();
  while (now() - t0 < 5 * 60_000) {
    if (!survivorLives(survivor)) return Math.round(now() - t0);
    await sleep(100);
  }
  return -1;
}

/** killed-hard-live: kill label -> { survivor, orphan, dispose } — what the `-next` and `-after` launches of the same repetition need. */
const live = new Map();

// ------------------------------------------------------------------------------------------------------ the lock files

/**
 * Held / Free / Absent. Windows: by trying to OPEN the file — a file held with FileShare.None refuses every other open (a sharing
 * violation, EBUSY). Linux: a plain open always succeeds (the lock is an advisory flock), so flock(1) is asked (lib/proc.mjs).
 */
function probe(path) {
  if (!WINDOWS) return lockState(path);
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

/**
 * The shim's own environment, as the runtime would build it: Minimal + the seven built names, the person's bin first on PATH.
 * On Linux it is lib/kimi-home.mjs's Linux arm (MinimalUnix, KIMI_CODE_HOME, the pin, HOME → os alone, `:` on PATH — DL11).
 */
function shimEnvironment(root) {
  if (!WINDOWS) return shimEnv({ root, personsBin: join(REAL_HOME, 'bin') });
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

/**
 * One scheduled run (lib/kimi-schedule.mjs buildSchedule): `entry` is `{ arm, n, label, kind, after? }`. A `next` entry is the normal launch right after a
 * kill; it records the survivor marker the kill left, the shim's report and the marker once it is over.
 */
async function runOne(entry) {
  const { arm: armName, n, label } = entry;
  const arm = ARMS[armName];
  const pair = entry.kind === 'next-live' || entry.kind === 'after-live' ? live.get(entry.after) : undefined;
  if (entry.kind.endsWith('-live') && !pair) return { arm: armName, n, label, pairedWith: entry.after, skipped: 'the killed arm left no live kimi with a readable identity, so the live variant did not happen' };
  let survivorGoneAfterMs;
  if (entry.kind === 'after-live') {
    survivorGoneAfterMs = await waitForSurvivorEnd(pair.survivor);
    if (survivorGoneAfterMs < 0) return { arm: armName, n, label, pairedWith: entry.after, skipped: 'the survivor still ran after 5 minutes (it is left alone; its run folder is kept)' };
  }
  const markerBefore = entry.kind === 'arm' ? [] : survivorMarkers(locks);
  if (!(await noOtherKimi(label, entry.kind === 'next-live' ? [pair.survivor] : []))) return { arm: armName, n, label, skipped: 'another kimi process was running for 10 minutes' };
  const survivorAliveBefore = entry.kind === 'next-live' && survivorLives(pair.survivor);
  const root = join(scratch, `kh-${label}`);
  const work = join(scratch, `work-${label}`);
  ownedRoot(root); // the product's layout: home/ beside an empty os/, and on Linux the cache/ folder XDG_CACHE_HOME names (DL11)
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

  const seen = new Map(); // pid -> { comm, name }, every descendant of the shim ever listed
  const sample = () => {
    for (const p of descendantsOf(child.pid, listProcesses())) seen.set(p.pid, { comm: p.comm ?? '', name: p.name });
  };
  const row = { arm: armName, n, label, ...(entry.after ? { pairedWith: entry.after } : {}), ...(survivorGoneAfterMs === undefined ? {} : { survivorGoneAfterMs }), killedAfterMs: 0, shim: { exit: null }, files: {}, descendants: [] };

  if (arm.killed) {
    // The shim takes the tree hold immediately before it starts kimi, so the file going Held is "the agent is starting". Then sample until the
    // process we wait for is a live descendant (kimi; or an rg), and end the shim by its own handle — for the kimi arms no sooner than 4 s
    // after the hold, so the turn is mid-prompt (a plain turn takes 8-11 s here), not mid-handshake. The signal is the arm's (SIGKILL for killed-hard).
    const target = arm.after === 'rg' ? (e) => /^rg(\.exe)?$/iu.test(displayName(e)) : isKimi;
    const limit = now() + 120_000;
    while (now() < limit && child.exitCode === null && !(treeFile() && probe(treeFile()) === 'Held')) await sleep(20);
    const heldAt = now();
    row.heldAfterStartMs = Math.round(heldAt - t0);
    while (now() < limit && child.exitCode === null) {
      sample();
      const hit = [...seen.values()].find(target);
      if (hit && (arm.after === 'rg' || now() - heldAt > 4_000)) break;
      await sleep(50);
    }
    row.firedOn = [...seen.values()].map(displayName).join(',');
    if (child.exitCode === null) {
      const markers = survivorMarkers(locks); // read BEFORE the signal: a killed-hard row counts only if a marker existed at the kill
      const killedAt = now();
      process.kill(child.pid, killSignal(armName));
      row.killedAfterMs = Math.round(killedAt - t0);
      const recording = recordOrphans(row, armName, markers, seen);
      // The live variant leaves the orphan RUNNING into the next launch: it does not wait for it to end (the `-after` launch does, by pid and start time).
      if (isLiveArm(armName)) await recording;
      else await Promise.all([recording, watchAfterKill(row, killedAt, homeFile, seen)]);
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
  if (entry.kind !== 'arm') Object.assign(row, nextRow({ markerBefore, report: row.report, markerAfter: survivorMarkers(locks) }));
  if (entry.kind === 'next-live') Object.assign(row, liveNextFields(pair, survivorAliveBefore, row, done.code, refusalText(stdout, stderr)));
  row.stderrTail = stderr.slice(-300);
  row.descendants = [...new Set([...seen.values()].map(displayName))];
  row.credentials = { namesIntact: credentialsBefore.every((c) => credentialNames(REAL_HOME).includes(c)), rotated: JSON.stringify(shapeBefore) !== JSON.stringify(credentialShape()) };
  row.homeAfter = readdirSync(join(root, 'home')).filter((n) => n !== 'credentials').sort((a, b) => a.localeCompare(b));
  const dispose = () => {
    // D19's order, the product's: the junction first, by its own name, the person's folder verified — then the home, then the rest of the root.
    const disposed = disposeOwnedHome(join(root, 'home'), REAL_HOME, credentialsBefore);
    rmSync(root, { recursive: true, force: true });
    return `link removed ${disposed.linkRemoved}, target intact ${disposed.targetIntact}, home gone ${disposed.homeGone}, root gone ${!existsSync(root)}`;
  };
  const survivor = isLiveArm(armName) ? liveSurvivor(row.orphans ?? []) : null;
  if (survivor) {
    // The orphan still runs on this owned home (its junction to the sign-in included): the folder is disposed only after it has ended — by the `-after` launch.
    live.set(label, { survivor, orphan: row.orphans.find((o) => o.pid === survivor.pid), dispose });
    row.survivor = survivor;
    row.disposed = 'deferred: the survivor still runs on this home; disposed by the -after launch';
  } else {
    if (isLiveArm(armName)) row.note = `${row.note ?? ''} no live kimi with a readable identity was left, so the live variant will not happen`.trim();
    row.disposed = dispose();
  }
  if (entry.kind === 'after-live') row.killRunDisposed = pair.dispose();

  return row;
}

/** The live `-next` launch's own record: the survivor, whether it lived before and after, the refusal text, the survivor's new parent, and the verdict against the cornerstone. */
function liveNextFields(pair, survivorAliveBefore, row, shimExit, text) {
  const survivorAliveAfter = survivorLives(pair.survivor);
  return {
    survivor: pair.survivor, survivorAliveBefore, survivorAliveAfter, survivorNewParent: pair.orphan?.newParent ?? null, refusal: text,
    verdict: liveVerdict({ markerAfter: row.markerAfter, survivor: pair.survivor, survivorAliveBefore, survivorAliveAfter, shimExit, report: row.report, text }),
  };
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

/**
 * Who survived the kill, and under whom they live now: a moment after the signal (the kernel has re-parented the orphans), every descendant ever seen that is
 * still alive is recorded with its NEW parent — pid, comm and exe basename (pid 1, or a `systemd --user` reaper) — beside the signal, the marker seen at the
 * kill and whether the row counts (lib/kimi-schedule.mjs killRecord). It runs beside watchAfterKill, so the lock timings are not delayed.
 */
async function recordOrphans(row, armName, markers, seen) {
  await sleep(300);
  const survivors = [...seen.entries()].filter(([pid]) => alive(pid)).map(([pid, entry]) => ({ pid, ...entry, startTime: startTimeOfPid(pid) }));
  Object.assign(row, killRecord({ arm: armName, markers, survivors, listing: listProcesses() }));
}

/** The shim was killed: Held while `kimi` lives, and when each becomes Free — against `kimi`'s own exit and any other descendant's. */
async function watchAfterKill(row, killedAt, homeFile, seen) {
  const tree = treeFile();
  const kimiPid = [...seen.entries()].find(([, entry]) => isKimi(entry))?.[0] ?? 0;
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
    otherDescendants: others.map(([pid, entry]) => ({ name: displayName(entry), aliveAfterKimiGone: t.kimiGone >= 0 && alive(pid) })),
  };
}

const leftover = (seen) => [...seen.entries()].filter(([pid]) => alive(pid)).map(([, entry]) => displayName(entry));

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
  // The schedule is lib/kimi-schedule.mjs's: repeat-outer, and each killed arm's repetition a PAIR — the kill, then at once its `-next` normal launch.
  for (const entry of buildSchedule(armNames, repeats)) {
    const row = await runOne(entry);
    rows.push(row);
    writeFileSync(join(scratch, 'results.json'), JSON.stringify({ shim, scratch, started: new Date().toISOString(), rows }, null, 2));
    console.error(`[mk15] ${row.label}: ${JSON.stringify(row.files ?? row.skipped)}`);
  }
} catch (e) {
  failure = e.stack ?? String(e);
  console.error(`[mk15] FAILED: ${failure}`);
}
writeFileSync(join(scratch, 'results.json'), JSON.stringify({ shim, scratch, finished: new Date().toISOString(), failure, rows }, null, 2));
console.log(JSON.stringify(rows, null, 2));
console.error(`[mk15] ${rows.length} runs; credentials names intact: ${credentialsBefore.every((c) => credentialNames(REAL_HOME).includes(c))}; scratch ${scratch}`);
if (failure) process.exit(70);
