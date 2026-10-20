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
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { availableParallelism, hostname, tmpdir } from 'node:os';
import { randomBytes } from 'node:crypto';
import { join } from 'node:path';
import { readRule, sweep } from './sweepTemp.mjs';

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
 * What earlier runs left in temp, removed BEFORE this one makes anything (`sweepTemp.mjs`), and
 * said, so a run that starts by removing four thousand directories tells somebody. It never stops
 * the run: housekeeping that fails is reported and stepped over.
 */
function sweepFirst() {
  try {
    const { removed, failed } = sweep(tmpdir(), Date.now(), readRule('..'));
    console.log(`run-tests: swept ${removed} leftover temp director${removed === 1 ? 'y' : 'ies'}`
      + (failed > 0 ? `, ${failed} could not be removed` : ''));
  } catch (error) {
    console.log(`run-tests: the temp sweep did not run (${error.message}); the tests still will`);
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
 */
export function makeRunTemp(realTmp) {
  const folder = join(realTmp, RUN_PREFIX + randomBytes(3).toString('hex'));
  mkdirSync(folder);
  try {
    writeFileSync(`${folder}.owner`, claimText({ host: hostname(), platform: process.platform, pid: process.pid, startSeconds: ownStartSeconds() }));
  } catch (error) {
    console.log(`run-tests: the owner claim for ${folder} could not be written (${error.message}); a killed run's folder then has only the janitor's age rule`);
  }
  return folder;
}

/** Removes the folder and its claim. Idempotent, and it never throws: a leftover is reported and left to the janitor. */
export function removeRunTemp(folder) {
  if (folder === '') {
    return;
  }
  for (const target of [folder, `${folder}.owner`]) {
    try {
      rmSync(target, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
    } catch (error) {
      console.log(`run-tests: ${target} could not be removed (${error.message}); the family janitor takes it later`);
    }
  }
}

/**
 * Points this process's temp variables at a fresh run folder — ONCE, before the first child, so every `node --test` descendant inherits
 * it (`spawnSync` copies `process.env`). `os.tmpdir()` reads TEMP/TMP on Windows and TMPDIR on Unix at call time, so all three are set.
 * Answers the folder, or `''` when it could not be made (the run then behaves as it did before).
 */
function beginRun() {
  try {
    const folder = makeRunTemp(tmpdir());
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

function main() {
  sweepFirst();
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
  main();
}
