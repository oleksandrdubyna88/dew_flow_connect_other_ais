#!/usr/bin/env node
/**
 * The runner in front of the measurement scripts' own tests (E5 S2-B, `todo/PLAN_test_leftovers_E5_S2_spec.md` B.1 § 7): the start-of-run sweep of the mounted
 * reference over the REAL temp root FIRST, then node's own glob, `node --test scripts/**\/*.test.mjs`, as `ci.yml` ran it bare before.
 *
 * Why a runner and not a `--import` preload: `--import` sits in `process.execArgv`, which `node --test` hands to every test-file child, so a preload would sweep once per test
 * file, concurrently, over the real temp root. One command, one sweep.
 *
 * E5 S4: it then makes ONE owned run folder through the wrap (claim first, naming this runner), points TEMP, TMP and TMPDIR and the marker `COAI_TEST_RUN_FOLDER` at it, and
 * passes the same gate a test file's first import makes — before it spawns anything; a folder it cannot make is one line and exit 78. A direct
 * `node --test scripts/lib/x.test.mjs` now refuses (the first import of the file); `node scripts/run-lib-tests.mjs scripts/lib/x.test.mjs` runs exactly that file.
 *
 * The sweep's report lines are `run-tests`' own, under this runner's prefix; the switch is the same file (`shared/leftovers-sweep.json`). The sweep never changes the exit code:
 * the child's status is the runner's, and only a missing mount ends the run (exit 2), before anything is spawned.
 */
import { spawnSync } from 'node:child_process';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { beginGated, filesRefusal, mountMissingLine, reportLeftoversFirst, startOfRunSweep } from '../src_vs_code/scripts/run-tests.mjs';

const REPO_ROOT = fileURLToPath(new URL('..', import.meta.url));
const PREFIX = 'run-lib-tests';

/**
 * Sweeps, counts, makes the run folder, then runs the lib tests inside it; answers the status the process ends with. `files` (the command's arguments, paths from the
 * repository root) replace the glob: they are validated FIRST, before anything is loaded, swept or made (exit 2). `loadOwned` (the repository's one Node helper module), `sweep`
 * (the loader of the reference), `sweepOptions`, `spawn`, `log`, `error`, `repoRoot`, `root` (the real temp root; the wrap's `tempRoot()` before the folder is made when
 * absent), `mode` and `begin` are the seams its test uses: the real spawn is never made there. A run folder that cannot be made, or a gate that does not pass, is ONE line and
 * 78, and nothing is spawned.
 */
export async function runLibTests({
  files, loadOwned = () => import('../src_vs_code/scripts/owned.mjs'), sweep, sweepOptions, spawn = spawnSync, log = console.log, error = console.error,
  repoRoot = REPO_ROOT, root, mode, begin,
} = {}) {
  const given = files ?? [];
  const problem = filesRefusal(given, { cwd: repoRoot, prefix: PREFIX });
  if (problem !== null) {
    error(problem);
    return 2;
  }
  let owned;
  try {
    owned = await loadOwned();
  } catch (caught) {
    error(mountMissingLine(caught, PREFIX));
    return 2;
  }
  const realRoot = root ?? owned.owned.tempRoot();
  await startOfRunSweep({ owned, prefix: PREFIX, log, root: realRoot, repo: repoRoot, loadSweep: sweep, sweepOptions, mode });
  // The sweep never looks at `coai-*`, so the count (one line, names only) says how many old ones are there, exactly as `run-tests.mjs` does: sweep, count, then the run.
  reportLeftoversFirst({ prefix: PREFIX, log, root: realRoot, repo: repoRoot });
  const begun = beginGated({ owned, root: realRoot, command: 'node scripts/run-lib-tests.mjs', error, ...(begin === undefined ? {} : { begin }) });
  if (begun.run === undefined) return begun.status;
  try {
    // The glob is node's own, quoted for no shell: a new `scripts/**/*.test.mjs` is picked up without touching this runner or the CI step.
    const result = spawn(process.execPath, ['--test', ...(given.length > 0 ? given : ['scripts/**/*.test.mjs'])], { cwd: repoRoot, stdio: 'inherit' });
    return result.status ?? 1;
  } finally {
    begun.run.end();
  }
}

if (process.argv[1] !== undefined && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await runLibTests({ files: process.argv.slice(2) }));
}
