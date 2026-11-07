'use strict';
/**
 * The check behind "a test file started directly refuses" (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` K1, K5, K6; plan P1).
 *
 * A runner of this repository (`run-tests.mjs`, `scripts/run-lib-tests.mjs`) makes ONE owned run folder, points TEMP, TMP and TMPDIR at it and sets
 * `COAI_TEST_RUN_FOLDER` to its absolute path. Every test file imports `inRunFolder.cjs` as its FIRST import; that module runs `enforce()` here, which ends the
 * process with ONE line and exit 78 before the file's other imports and its tests run, unless the process really is inside a run folder:
 *
 *   1. the marker is set and absolute; 2. it names a directory (a link is no directory: lstat); 3. `<marker>.owner` is a regular file (the claim);
 *   4. TEMP, TMP and TMPDIR each EXACTLY equal the marker (string equality).
 *
 * It does not check the folder's NAME: no `testsToDelete` spelling and no owner grammar live here (the runner's own gate checks the name). The marker is a
 * NEW variable and not `TESTSTODELETE_RUN`: that one is the RECORD every claim is written with, may be inherited from an outer process, and is not a path.
 *
 * Dependency-free CommonJS, `node:` modules only: a direct `node --test x` loads nothing but the file and its imports, so the check must be in the file's
 * own import graph, and a `.cjs` loads from compiled CommonJS, from `.mjs`, without a compile and without `npm ci` (the Windows and macOS steps run none).
 */
const nodeFs = require('node:fs');
const nodePath = require('node:path');

const MARKER = 'COAI_TEST_RUN_FOLDER';
const VARIABLES = ['TEMP', 'TMP', 'TMPDIR'];
const REFUSAL_PREFIX = 'coai tests: no test ran — ';
const REFUSED_EXIT_CODE = 78;

const lstat = (io, file) => {
  try {
    return io.lstatSync(file);
  } catch {
    return null;
  }
};

/** `null` when the process is inside its runner's run folder, else ONE reason (no line break) that names what failed. */
function refusal({ env = process.env, io = nodeFs } = {}) {
  const folder = env[MARKER];
  if (folder === undefined || folder === '') return `${MARKER} is not set: this test file was started directly, not by its runner`;
  if (!nodePath.isAbsolute(folder)) return `${MARKER}='${folder}' is not an absolute path`;
  const stat = lstat(io, folder);
  if (stat === null) return `the run folder '${folder}' does not exist`;
  if (!stat.isDirectory()) return `the run folder '${folder}' is not a directory`;
  const claim = lstat(io, `${folder}.owner`);
  if (claim === null || !claim.isFile()) return `the run folder has no claim '${folder}.owner' (a regular file)`;
  for (const name of VARIABLES) {
    if (env[name] !== folder) return `${name}='${env[name] ?? ''}' is not the run folder '${folder}'`;
  }
  return null;
}

/** The ONE line a refusal is: the reason, then the commands that start a test file the right way. */
function line(reason) {
  return `${REFUSAL_PREFIX}${reason}. Start it through its runner: \`node scripts/run-tests.mjs <file>\` in src_vs_code (after \`npm run compile\`), `
    + '`node scripts/run-lib-tests.mjs <file>` from the repository root, or `npm test`';
}

/** Ends the process with the refusal line and exit 78 when `refusal()` says so. `write` and `exit` are the seams of the test. */
function enforce({ env = process.env, io = nodeFs, write = (text) => process.stderr.write(text), exit = (code) => process.exit(code) } = {}) {
  const reason = refusal({ env, io });
  if (reason !== null) {
    write(`${line(reason)}\n`);
    exit(REFUSED_EXIT_CODE);
  }
}

module.exports = { MARKER, VARIABLES, REFUSAL_PREFIX, REFUSED_EXIT_CODE, refusal, line, enforce };
