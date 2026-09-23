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
import { readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
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

function run(files) {
  const result = spawnSync(process.execPath, ['--test', ...files], { stdio: 'inherit' });
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

function main() {
  sweepFirst();
  const { compiled, sources } = discover('.');

  if (compiled.length === 0) {
    console.error('no compiled test files in out/test — did the compile step run?');
    process.exit(1);
  }

  // Printed, because the count is the thing that silently fell. A suite that runs fewer files than
  // last week should say so in the log, not only in a pass total nobody compares.
  console.log(`run-tests: ${compiled.length} compiled, ${sources.length} source test file(s)`);

  let status = 0;
  for (const batch of batches('.')) {
    status = run(batch) || status;
  }
  process.exit(status);
}

// Importable for its own test; still a script when node is pointed at it.
if (process.argv[1]?.endsWith('run-tests.mjs')) {
  main();
}
