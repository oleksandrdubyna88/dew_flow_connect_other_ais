#!/usr/bin/env node
/**
 * Runs every test file this package HAS, found by reading the directories rather than by a list.
 *
 * <p>The glob was the bug, twice over: quoted, Linux's shell passed it verbatim and node 20
 * treated it as a literal path ("Could not find out/test/*.test.js" — the release job's first
 * failure); unquoted, Windows' cmd would pass it verbatim instead. And `node --test <dir>` turned
 * out to execute the DIRECTORY as a module on node 24. A readdir has no version, no shell and no
 * platform.</p>
 *
 * <p><b>And a readdir is also the only thing that cannot forget.</b> The `.test.mjs` files used to
 * be named one by one in `package.json`, because they are not compiled — tsc has no reason to touch
 * a `.mjs`, so they never reach `out/test` and this script could not see them. That made the
 * suite's completeness a property of somebody's memory. Measured on main: a file
 * `src/test/ciDiscoveryProbe.test.mjs` containing only `throw new Error('CI_DISCOVERY_PROBE')` left
 * `npm test` at 3530 passed, 0 failed, exit 0, without naming it once. Both directories are read
 * here now, and `testDiscovery.test.mjs` goes red if the `test` script ever grows a filename
 * again.</p>
 *
 * <p>Each source test gets its OWN process. They are tests OF SCRIPTS — they spawn, they chdir,
 * they load the built bundle — and one of them already had to be invoked separately for exactly
 * that reason. Per-file isolation costs a process start and removes the need for anybody to know
 * which file is the special one.</p>
 */
import fs, { readFileSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { availableParallelism, hostname, tmpdir } from 'node:os';
import { randomBytes } from 'node:crypto';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readRule, readSweepSwitch, startOfRun } from './startOfRun.mjs';

/**
 * Every test file under `root`, by extension: compiled ones from `out/test`, source ones from
 * `src/test`. A directory that does not exist contributes nothing — a package may legitimately
 * have only one kind — and anything that is not exactly `*.test.js` / `*.test.mjs` is ignored.
 */
export function discover(root) {
  const read = (dir, suffix) => {
    const full = join(root, ...dir);
    let entries;
    try {
      entries = readdirSync(full);
    } catch {
      return [];
    }
    return entries
      .filter((f) => f.endsWith(suffix))
      .sort()
      .map((f) => join(full, f));
  };

  return {
    compiled: read(['out', 'test'], '.test.js'),
    sources: read(['src', 'test'], '.test.mjs'),
  };
}

/**
 * The invocations this runner will make, in order — each one a batch `node --test` runs in
 * PARALLEL. The compiled tests share a batch; every source test gets one to itself, because those
 * are tests OF SCRIPTS: they spawn, they chdir, and one of them drives the bundle build, which
 * leaves `src/generated/gateRule.ts` absent for seconds while other tests are reading the tree.
 *
 * <p>Exported, and the arrangement is decided HERE rather than by the caller, so that
 * `theBundleLoads.test.mjs` can assert the isolation by reading what will actually be run. A test
 * that rebuilt the same grouping from `discover()` would be checking its own arithmetic and would
 * pass while this function was broken — measured, in the first version of that port.</p>
 */
export function batches(root) {
  const { compiled, sources } = discover(root);
  return [compiled, ...sources.map((one) => [one])].filter((batch) => batch.length > 0);
}

/**
 * The most test files `node --test` may run at once — the family rule `common.local-cpu-budget`.
 * Node's own default is one per CPU minus one, which on a 24-thread workstation was 23 processes
 * holding the CPU at 100 % while every other session lagged (measured 2026-10-10).
 *
 * <p>A CAP, never a raise: `--test-concurrency` is an exact number, so a bare 8 would take a 4-vCPU
 * CI runner from node's 3 workers to 8. The minimum keeps every machine with fewer than nine
 * threads, CI included, exactly where node's default put it.</p>
 */
export const TEST_CONCURRENCY = Math.max(1, Math.min(8, availableParallelism() - 1));

/**
 * The arguments of every `node --test` this package starts, so the cap is written in one place:
 * `run-contract.mjs` builds its command here too, and `localCpuBudget.test.mjs` pins it.
 */
export function nodeTestArgs(files, extra = []) {
  return ['--test', `--test-concurrency=${TEST_CONCURRENCY}`, ...extra, ...files];
}

function run(files) {
  const result = spawnSync(process.execPath, nodeTestArgs(files), { stdio: 'inherit' });
  return result.status ?? 1;
}

/**
 * The machine's real temp root, captured when this module loads — BEFORE `beginRun` moves TEMP/TMP/TMPDIR into the run's folder, which makes `os.tmpdir()` answer
 * the run folder from then on (S2-A gate 0). The count and the run folder are both made in this one root, as the C# runner uses `RunTemp.Real`.
 */
export const REAL_TEMP_ROOT = tmpdir();

/** This repository's root, from this file's own place: the shared switch file and the mounted conventions are read from here, whatever the working directory. */
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

/**
 * What earlier runs and the product left under `coai-` in temp, COUNTED and said BEFORE this run makes anything (`startOfRun.mjs`) — no test sweep
 * removes them since E5 S2-A, so a pile that grows tells somebody instead of hiding. The legacy `testsToDelete-coai-run-*` folders a killed run leaves are no
 * longer counted here: the start-of-run sweep (`startOfRunSweep`) lists each by name with its command. It never stops the run: housekeeping that fails is
 * reported and stepped over. `log` and `count` are the seams its own test uses.
 */
export function reportLeftoversFirst({
  log = console.log, root = REAL_TEMP_ROOT, repo = REPO_ROOT, prefix = 'run-tests', count = () => startOfRun({ dir: root, rule: readRule(repo) }),
} = {}) {
  try {
    const { leftovers } = count();
    log(leftovers === 0
      ? `${prefix}: no old coai-* leftovers in ${root}`
      : `${prefix}: ${leftovers} old coai-* director${leftovers === 1 ? 'y' : 'ies'} in ${root} — no test sweep removes them (E5 S2-A); see research/module_tests.md § The one-off clean-up`);
  } catch (error) {
    log(`${prefix}: the leftover count did not run (${error.message}); the tests still will`);
  }
}

const firstLine = (text) => String(text).split('\n')[0].trimEnd();

/** The ONE line a missing mount is: what to run. Shared by this runner and `scripts/run-lib-tests.mjs`, each with its own `prefix`. */
export function mountMissingLine(error, prefix = 'run-tests') {
  return `${prefix}: the shared conventions are not mounted (${firstLine(error?.message)}) — run: git submodule update --init .agents/conventions`;
}

/**
 * The repository's one Node module of the leftovers helper (`owned.mjs`, plan D3), loaded inside a `try`: a mount that is not there is ONE stderr line and exit 2 BEFORE
 * anything is spawned or made. `importOwned`, `error` and `exit` are the seams of its test.
 */
export async function loadOwned({ importOwned = () => import('./owned.mjs'), error = console.error, exit = process.exit, prefix = 'run-tests' } = {}) {
  try {
    return await importOwned();
  } catch (caught) {
    error(mountMissingLine(caught, prefix));
    return exit(2);
  }
}

/** The most preserved entries the report names; the rest is one line (the C# fixture's `MaxPreservedLines`). */
const MAX_PRESERVED_LINES = 20;

/** What a sweep said, one line each, under the runner's own prefix: silence when it found nothing. */
function say({ log, prefix, root, result }) {
  const { removed, preserved, listed, errors } = result;
  if (removed.length + preserved.length + listed.length + errors.length === 0) {
    return;
  }
  log(`${prefix}: leftovers in ${root} — removed ${removed.length}, preserved ${preserved.length}, listed ${listed.length}`);
  for (const line of listed) log(`${prefix}: ${line}`);
  for (const kept of preserved.slice(0, MAX_PRESERVED_LINES)) log(`${prefix}: preserved: ${kept.path} — ${kept.reason}`);
  if (preserved.length > MAX_PRESERVED_LINES) {
    log(`${prefix}: … ${preserved.length - MAX_PRESERVED_LINES} more preserved entries are not shown (preserved ${preserved.length} in all; the first ${MAX_PRESERVED_LINES} are named above)`);
  }
  for (const line of errors) log(`${prefix}: error: ${line}`);
}

/** `quarantine` when the name is a sweep's sibling quarantine (`.<name>.…-quarantine-<pid>-<hex>`): the name inside is the one judged. */
const QUARANTINE = /^\.(.+)\.teststodelete-quarantine-\d+-[0-9a-f]+$/i;

/**
 * The switch is OFF: the sweep is NOT called. Every entry whose name is the owner form of this repository's token (any owner) or its legacy form, and every
 * quarantine of such a name, is ONE `switched-off:` line. NAMES only: nothing is stat-ed, read, renamed or removed. A root that cannot be listed is one `errors` line.
 */
function listOnly({ owned, root, io }) {
  const result = { removed: [], preserved: [], listed: [], errors: [] };
  try {
    const helper = owned.owned;
    const ours = (name) => {
      const found = helper.classifyName(name);
      return (found.form === 'owner' || found.form === 'legacy') && found.token === owned.token;
    };
    for (const name of [...io.readdirSync(root)].sort()) {
      const quarantine = QUARANTINE.exec(name);
      const kind = quarantine ? 'quarantine' : name.endsWith('.owner') ? 'claim' : 'folder';
      if (ours(quarantine ? quarantine[1] : name)) {
        result.listed.push(`switched-off: ${kind} ${name} — startOfRunSweep is list-only (shared/leftovers-sweep.json, D11); nothing is removed`);
      }
    }
  } catch (error) {
    result.errors.push(`temp root ${root}: ${firstLine(error.message)}`);
  }
  return result;
}

/**
 * The start-of-run sweep (E5 S2-B): the MOUNTED reference `sweepLeftovers` with this repository's token, over the REAL temp root, before this run makes anything.
 * Behind the ONE switch `shared/leftovers-sweep.json` (read by `readSweepSwitch`; `mode` is the seam): `list-only` calls NO sweep and lists what it would have judged.
 * It never fails the run: a sweep that cannot load or throws is ONE line ("leftovers sweep not run"), and the exit code is the tests'.
 *
 * Returns the sweep's result (or the listing's), or `undefined` when nothing was swept. `sweepOptions` are the reference's own (`probe`, `now`, `platform`, `mine`, `hooks`);
 * the token is bound AFTER them by `owned.loadStartOfRunSweep` and cannot be overridden.
 */
export async function startOfRunSweep({
  owned, log = console.log, root = REAL_TEMP_ROOT, repo = REPO_ROOT, loadSweep, sweepOptions = {}, mode, io = fs, prefix = 'run-tests',
} = {}) {
  try {
    const chosen = mode ?? readSweepSwitch(repo, { warn: log, prefix });
    let result;
    if (chosen === 'collect') {
      const sweep = await (loadSweep ?? owned.loadStartOfRunSweep)();
      result = await sweep({ ...sweepOptions, repo, tmp: root });
    } else {
      result = listOnly({ owned, root, io });
    }
    say({ log, prefix, root, result });
    return result;
  } catch (error) {
    log(`${prefix}: leftovers sweep not run (${firstLine(error.message)}); the tests still will`);
    return undefined;
  }
}

/** The family rule: `testsToDelete-` marks a folder only a test run makes, so a janitor may take it without asking whose it is. */
const RUN_PREFIX = 'testsToDelete-coai-run-';

/**
 * The owner claim, `<host>@<os>:<pid>[:<startUnixSeconds>]` — the C# runner writes the same text (`RunTemp`/`OwnerClaim`), so one sweeper
 * judges both. `os` is Node's own `process.platform` spelling; the start is given only where the OS tells it without spawning a process.
 */
export function claimText({ host, platform, pid, startSeconds }) {
  const safeHost = host.replace(/[@:\s]/g, '-');
  return startSeconds === undefined ? `${safeHost}@${platform}:${pid}` : `${safeHost}@${platform}:${pid}:${startSeconds}`;
}

/**
 * This process's start as unix seconds, from the text of `/proc/self/stat` (field 22, in ticks of 1/100 s since boot — the command name
 * may hold spaces and ")", so the fields are counted after the LAST ")") and of `/proc/stat` (`btime`). `undefined` when either does
 * not parse: no start is better than a wrong one.
 */
export function linuxStartSeconds(statText, procStatText) {
  const ticks = Number(statText.slice(statText.lastIndexOf(')') + 2).split(' ')[19]);
  const boot = /^btime (\d+)$/m.exec(procStatText);
  return Number.isFinite(ticks) && boot ? Number(boot[1]) + Math.floor(ticks / 100) : undefined;
}

function ownStartSeconds() {
  if (process.platform !== 'linux') {
    return undefined;
  }
  try {
    return linuxStartSeconds(readFileSync('/proc/self/stat', 'utf8'), readFileSync('/proc/stat', 'utf8'));
  } catch {
    return undefined;
  }
}

/**
 * Makes this invocation's own temp folder inside `realTmp`, with its owner claim beside it, and answers its path. One per call: two
 * runners at once each get theirs, and never the C# run's.
 *
 * <p><b>Exclusive:</b> `mkdirSync` without `recursive` throws `EEXIST` on a name that exists, and a run that drew a taken name draws again (up to
 * `tries` times) — it never shares a folder, and never claims one it did not make. After the last collision it throws, and `beginRun` then runs
 * without a folder of its own (with a line saying so). `suffix` and `io` are seams for the test that forces a collision.</p>
 */
export function makeRunTemp(realTmp, { suffix = () => randomBytes(3).toString('hex'), tries = 5, io = fs } = {}) {
  const folder = freeFolder(realTmp, suffix, tries, io);
  try {
    io.writeFileSync(`${folder}.owner`, claimText({ host: hostname(), platform: process.platform, pid: process.pid, startSeconds: ownStartSeconds() }));
  } catch (error) {
    console.log(`run-tests: the owner claim for ${folder} could not be written (${error.message}); a killed run's folder is then collected only by the family janitor's age rule, after one hour`);
  }
  return folder;
}

/** The first `tries` draws that `mkdirSync` can make; an existing name is skipped, any other failure is the caller's. */
function freeFolder(realTmp, suffix, tries, io) {
  for (let attempt = 0; attempt < tries; attempt += 1) {
    const folder = join(realTmp, RUN_PREFIX + suffix());
    try {
      io.mkdirSync(folder);
      return folder;
    } catch (error) {
      if (error.code !== 'EEXIST') {
        throw error;
      }
    }
  }
  throw new Error(`no free name for a run folder in ${realTmp} after ${tries} draws (EEXIST each time)`);
}

/**
 * Removes the folder, then its claim — the claim ONLY when the folder is gone, so a folder that could not be removed (a file still held) keeps the
 * claim that says whose it was. Idempotent, and it never throws: a leftover is reported and left to the family janitor.
 */
export function removeRunTemp(folder, io = fs) {
  if (folder === '') {
    return;
  }
  if (removed(folder, io)) {
    removed(`${folder}.owner`, io);
  }
}

/** One removal; answers whether it went, and says why when it did not. */
function removed(target, io) {
  try {
    io.rmSync(target, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
    return true;
  } catch (error) {
    console.log(`run-tests: ${target} could not be removed (${error.message}); the family janitor takes it later`);
    return false;
  }
}

/**
 * Points this process's temp variables at a fresh run folder — ONCE, before the first child, so every `node --test` descendant inherits
 * it (`spawnSync` copies `process.env`). `os.tmpdir()` reads TEMP/TMP on Windows and TMPDIR on Unix at call time, so all three are set.
 * Answers the folder, or `''` when it could not be made (the run then behaves as it did before).
 */
function beginRun() {
  try {
    const folder = makeRunTemp(REAL_TEMP_ROOT);
    process.env.TEMP = process.env.TMP = process.env.TMPDIR = folder;
    return folder;
  } catch (error) {
    console.log(`run-tests: no folder of its own could be made (${error.message}); the tests share the machine's temp as before`);
    return '';
  }
}

/** Every batch, in order; the worst status wins. 1 when there is nothing compiled to run. */
function runAll() {
  const { compiled, sources } = discover('.');

  if (compiled.length === 0) {
    console.error('no compiled test files in out/test — did the compile step run?');
    return 1;
  }

  // Printed, because the count is the thing that silently fell. A suite that runs fewer files than
  // last week should say so in the log, not only in a pass total nobody compares.
  console.log(`run-tests: ${compiled.length} compiled, ${sources.length} source test file(s)`);

  let status = 0;
  for (const batch of batches('.')) {
    status = run(batch) || status;
  }
  return status;
}

async function main() {
  const owned = await loadOwned();
  await startOfRunSweep({ owned });
  reportLeftoversFirst();
  const folder = beginRun();
  let status = 1;
  try {
    status = runAll();
  } finally {
    // BEFORE process.exit, which ends the process without running anything after it — the trap leftovers-E5 recorded for run-host.mjs.
    removeRunTemp(folder);
  }
  process.exit(status);
}

// Importable for its own test; still a script when node is pointed at it.
if (process.argv[1]?.endsWith('run-tests.mjs')) {
  // Lowered before anything is spawned, so every test process inherits it (BelowNormal on Windows,
  // nice 19 elsewhere; nothing under CI). Only the script lowers itself — importing this module
  // from a test does not.
  await import('./lower-priority.mjs');
  await main();
}
