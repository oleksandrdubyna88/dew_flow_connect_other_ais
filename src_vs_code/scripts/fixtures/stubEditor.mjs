/**
 * A stand-in for the VS Code executable, for `src/test/runHost.test.mjs` — never a real editor.
 *
 * <p>`run-host.mjs` launches an executable, reads what it prints and decides from the exit code and
 * from one line (`host started: N scenarios`) what happened. This script prints exactly what a real
 * editor prints in each of the situations that decision tells apart, so the decision can be driven
 * in milliseconds with no 325 MB download. The test runs it as `node stubEditor.mjs <the arguments
 * the harness gives VS Code>`; the arguments are ignored.</p>
 *
 * <p>The behaviour comes from `STUB_MODE`, and every launch appends a line to the file `STUB_LOG`
 * names, which is how a test counts launches without trusting anything the harness says about them.</p>
 */
import { appendFileSync, readFileSync } from 'node:fs';

const mode = process.env.STUB_MODE ?? 'ok';
const log = process.env.STUB_LOG;
if (log) {
  appendFileSync(log, `${process.pid}\n`);
}
const launchNumber = log ? readFileSync(log, 'utf8').split('\n').filter(Boolean).length : 1;

/** What the editor's main process printed in the C22 failure: two [main] lines, then an unrelated one. */
function diesBeforeTheHost() {
  console.log('[main 2026-10-22T09:59:44.000Z] CodeWindow: created');
  console.log('[main 2026-10-22T10:00:00.000Z] CodeWindow: detected unresponsive');
  console.error('ERROR: the window did not answer');
  process.exit(1);
}

/** The host's own lines, as `scenarios.ts` prints them. */
function hostRuns(failing) {
  console.log('  host started: 3 scenarios');
  console.log('  ok  first');
  console.log(failing ? '  FAIL second' : '  ok  second');
  console.log('  ok  third');
  console.log(failing ? '  2/3 scenarios passed' : '  3/3 scenarios passed');
  process.exit(failing ? 1 : 0);
}

switch (mode) {
  case 'exit-before-host':
    diesBeforeTheHost();
    break;
  case 'hang':
    // An editor that never answers; it leaves by itself so a test never leaves a process behind.
    setTimeout(() => { process.exit(1); }, 1500);
    break;
  case 'exit-silent':
    process.exit(1);
    break;
  case 'host-then-fail':
    hostRuns(true);
    break;
  case 'fail-first-then-ok':
    if (launchNumber === 1) {
      diesBeforeTheHost();
    }
    hostRuns(false);
    break;
  default:
    hostRuns(false);
}
