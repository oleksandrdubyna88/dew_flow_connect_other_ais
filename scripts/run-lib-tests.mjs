#!/usr/bin/env node
/**
 * The runner in front of the measurement scripts' own tests (E5 S2-B, `todo/PLAN_test_leftovers_E5_S2_spec.md` B.1 § 7): the start-of-run sweep of the mounted
 * reference over the REAL temp root FIRST, then node's own glob, `node --test scripts/**\/*.test.mjs`, as `ci.yml` ran it bare before.
 *
 * Why a runner and not a `--import` preload: `--import` sits in `process.execArgv`, which `node --test` hands to every test-file child, so a preload would sweep once per test
 * file, concurrently, over the real temp root. One command, one sweep.
 *
 * It makes no run folder and sets no marker. S4 adds the owned folder and the marker its refusal reads (D10's direct-start rule); until then a direct
 * `node --test scripts/lib/x.test.mjs` simply runs without a sweep, as today.
 *
 * The sweep's report lines are `run-tests`' own, under this runner's prefix; the switch is the same file (`shared/leftovers-sweep.json`). The sweep never changes the exit code:
 * the child's status is the runner's, and only a missing mount ends the run (exit 2), before anything is spawned.
 */
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { mountMissingLine, reportLeftoversFirst, startOfRunSweep } from '../src_vs_code/scripts/run-tests.mjs';

const REPO_ROOT = fileURLToPath(new URL('..', import.meta.url));
const PREFIX = 'run-lib-tests';

/** The machine's real temp root, captured at load — no run folder is made here, so this is the root a killed run's folders are in. */
const REAL_TEMP_ROOT = tmpdir();

/**
 * Sweeps, then runs the lib tests; answers the status the process ends with. `loadOwned` (the repository's one Node helper module), `sweep` (the loader of the reference),
 * `sweepOptions`, `spawn`, `log`, `error`, `repoRoot`, `root` and `mode` are the seams its test uses: the real spawn is never made there.
 */
export async function runLibTests({
  loadOwned = () => import('../src_vs_code/scripts/owned.mjs'), sweep, sweepOptions, spawn = spawnSync, log = console.log, error = console.error,
  repoRoot = REPO_ROOT, root = REAL_TEMP_ROOT, mode,
} = {}) {
  let owned;
  try {
    owned = await loadOwned();
  } catch (caught) {
    error(mountMissingLine(caught, PREFIX));
    return 2;
  }
  await startOfRunSweep({ owned, prefix: PREFIX, log, root, repo: repoRoot, loadSweep: sweep, sweepOptions, mode });
  // The sweep never looks at `coai-*`, so the count (one line, names only) says how many old ones are there, exactly as `run-tests.mjs` does: sweep, count, then the run.
  reportLeftoversFirst({ prefix: PREFIX, log, root, repo: repoRoot });
  // The glob is node's own, quoted for no shell: a new `scripts/**/*.test.mjs` is picked up without touching this runner or the CI step.
  const result = spawn(process.execPath, ['--test', 'scripts/**/*.test.mjs'], { cwd: repoRoot, stdio: 'inherit' });
  return result.status ?? 1;
}

if (process.argv[1] !== undefined && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await runLibTests());
}
