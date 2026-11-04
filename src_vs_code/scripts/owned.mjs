/**
 * The ONE module of this repository that holds the leftovers token for Node (E5 S2-B, plan D3): the mounted `owned.mjs` bound to `coai`, and the mounted
 * reference sweep bound to the same token. Every other script imports from HERE; nothing else writes the token. E5 S4 added the owned RUN FOLDER of a runner
 * or an entry script (`beginOwnedRun`) and the owned SCRATCH of a probe (`ownedScratch`): both through the reference's `ownedDir`, never a folder made here.
 *
 * A missing mount throws at LOAD (the static import below): a caller that wants a controlled exit imports this module inside its own `try`
 * (`run-tests.mjs` `loadOwned`, `scripts/run-lib-tests.mjs`) and says `git submodule update --init .agents/conventions`.
 *
 * This module imports `node:` modules and repository files only: the `windows` CI job runs the sweep's test file without `npm ci` (L5 pins that).
 */
import fs from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ownedFor } from '../../.agents/conventions/tools/lib/owned.mjs';
import guard from './runFolderGuard.cjs';

/** Rule §1: this repository's token — written ONCE in Node, here. */
export const REPO_TOKEN = 'coai';

/** The `repository` column of the `coai` row of the rule's `## Tokens` table: what the guard's identity check reads. */
export const REPO_ROW = 'the multi-model review extension and server';

/** The mounted helper bound to this repository's token. */
export const owned = ownedFor({ token: REPO_TOKEN, row: REPO_ROW });
export { REPO_TOKEN as token };

const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));
const SWEEP_MODULE = path.join(REPO_ROOT, '.agents', 'conventions', 'tools', 'lib', 'leftovers-sweep.mjs');

/**
 * The mounted reference's `sweepLeftovers` with THIS repository's token bound AFTER the caller's options (F23), so nothing a caller passes can override it.
 * Loaded by file URL in its own step: a load failure is the caller's ONE line and no sweep, never a failed run. `importer` is the seam of the test that
 * hands in a recording sweep.
 */
export async function loadStartOfRunSweep({ importer = (href) => import(href) } = {}) {
  const { sweepLeftovers } = await importer(pathToFileURL(SWEEP_MODULE).href);
  if (typeof sweepLeftovers !== 'function') {
    throw new Error(`${SWEEP_MODULE} exports no sweepLeftovers`);
  }
  return (options = {}) => sweepLeftovers({ ...options, token: REPO_TOKEN });
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------
// The owned run folder (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 6.1; the contract is `common/test-leftovers.md` §2 and the detail §3).
// ---------------------------------------------------------------------------------------------------------------------------------------------------

/** The self-test seam shared with the C# runner: a value makes the run FAIL (never pass). Any other value is ignored. */
export const SELF_TEST_VARIABLE = 'COAI_RUNTEMP_SELFTEST';

/** The run folder could not be made, claimed or verified: a runner prints ONE line from `message` and exits 78. */
export class RunFolderRefused extends Error {
  constructor(reason, options) {
    super(reason, options);
    this.name = 'RunFolderRefused';
  }
}

const firstLine = (text) => String(text).split('\n')[0].trimEnd();
const RECORD = 'TESTSTODELETE_RUN';
const MISMATCH = { 'mismatch-temp': 'TEMP', 'mismatch-tmp': 'TMP', 'mismatch-tmpdir': 'TMPDIR' };

/** A scope the reference accepts (`after`) whose close is SYNCHRONOUS, so it can run from an `exit` listener. Last registered first; a failing step is one stderr line. */
function exitScope() {
  const steps = [];
  return {
    after(step) {
      steps.push(step);
    },
    closeSync() {
      while (steps.length > 0) {
        try {
          steps.pop()();
        } catch (error) {
          process.stderr.write(`owned: a cleanup step failed (${firstLine(error?.message)})\n`);
        }
      }
    },
  };
}

/**
 * Runs `make` with an inherited TESTSTODELETE_RUN taken out of `process.env` (the reference reads it THERE, and would write that record's pid into the claim) and put back
 * exactly after: absent stays absent. The claim of a run folder names THIS process (E5 S2-B `6695ab36`).
 */
function withInheritedRunWithheld(make) {
  const had = Object.hasOwn(process.env, RECORD);
  const inherited = process.env[RECORD];
  delete process.env[RECORD];
  try {
    return make();
  } finally {
    if (had) process.env[RECORD] = inherited;
    else delete process.env[RECORD];
  }
}

/** `fail-claim`: the CommonJS `fs.writeFileSync` throws EACCES for a `.owner` path, and the ES module view is synced so the reference sees it; both restored. */
function withClaimFailure(make) {
  const original = fs.writeFileSync;
  fs.writeFileSync = function failingClaim(file, ...rest) {
    if (typeof file === 'string' && file.endsWith('.owner')) {
      throw Object.assign(new Error(`EACCES: permission denied, open '${file}'`), { code: 'EACCES' });
    }
    return original.call(this, file, ...rest);
  };
  syncBuiltinESMExports();
  try {
    return make();
  } finally {
    fs.writeFileSync = original;
    syncBuiltinESMExports();
  }
}

/** Make + verify: the reference's `ownedDir` in `root` (claim first, exclusive; the wrap never makes a folder itself), then the claim read back. */
function claimedFolder({ what, root, selfTest }) {
  const scope = exitScope();
  try {
    const make = () => owned.ownedDir(scope, what, { parent: root });
    const folder = withInheritedRunWithheld(() => (selfTest === 'fail-claim' ? withClaimFailure(make) : make()));
    const claim = owned.parseOwner(fs.readFileSync(`${folder}.owner`, 'utf8'));
    if (claim.unreadable === true || claim.pid !== process.pid || claim.owner !== owned.thisOwnerToken()) {
      throw new RunFolderRefused(`the claim beside '${folder}' does not name this process`);
    }
    return { folder, end: () => scope.closeSync() };
  } catch (cause) {
    scope.closeSync();
    if (cause instanceof RunFolderRefused) throw cause;
    throw new RunFolderRefused(`the run folder could not be made in '${root}' (${firstLine(cause?.message)})`, { cause });
  }
}

/**
 * The run folder of a runner or an entry script: ONE owned folder (`what`) in `root`, claimed by this process BEFORE it is made, removed with its claim by `end()` —
 * registered with `onExit` at once, so a `process.exit`, a normal end and an uncaught exception all remove it (a SIGKILL leaves it for the next start-of-run sweep).
 * It then points TEMP, TMP and TMPDIR and the guard's marker `COAI_TEST_RUN_FOLDER` at the folder in `env`, and exports this process's own record as TESTSTODELETE_RUN
 * to the children (the detail §3). A folder or claim that cannot be made is a `RunFolderRefused`, with nothing left behind.
 *
 * `selfTest` (the variable `COAI_RUNTEMP_SELFTEST`): `fail-claim`, `mismatch-temp`, `mismatch-tmp`, `mismatch-tmpdir` (that ONE variable is moved to `root`, so a runner's
 * gate must refuse) and `exit-after-begin` (exit 3 once the folder exists).
 */
export function beginOwnedRun({
  what, root = owned.tempRoot(), env = process.env, onExit = (end) => process.on('exit', () => end()), exit = (code) => process.exit(code),
  selfTest = process.env[SELF_TEST_VARIABLE],
} = {}) {
  const { folder, end } = claimedFolder({ what, root, selfTest });
  onExit(end);
  env.TEMP = env.TMP = env.TMPDIR = env[guard.MARKER] = folder;
  env[RECORD] = owned.formatOwner(owned.thisOwner());
  if (Object.hasOwn(MISMATCH, selfTest)) env[MISMATCH[selfTest]] = root;
  if (selfTest === 'exit-after-begin') exit(3);
  return { folder, root, end };
}

/**
 * An owned folder for a person-run probe that must NOT have TEMP moved (it measures where a vendor CLI writes): claimed by this process, removed at exit by `end()`,
 * the process's temp variables untouched. `path` is the folder.
 */
export function ownedScratch(what, {
  root = owned.tempRoot(), onExit = (end) => process.on('exit', () => end()), selfTest = process.env[SELF_TEST_VARIABLE],
} = {}) {
  const { folder, end } = claimedFolder({ what, root, selfTest });
  onExit(end);
  return { path: folder, end };
}
