/**
 * Where a cold-start result goes (v2 extension.md § 2; epic E2 story S2): the JSON file `--json` names, and — with `--row` — one more row at the end of
 * `research/RESULTS_v2_start.md`. Shared by `run-host.mjs measure` and `coldstart-report.mjs` so both write the same row. The numbers themselves are
 * `coldstart-lib.mjs`'s; this file only writes them.
 */
import { spawn } from 'node:child_process';
import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { kill } from './team-server-child.mjs';
import { tableRow } from './coldstart-lib.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));

/** The results file of the v2 start measurements, by default. */
export const RESULTS_FILE = join(HERE, '..', '..', 'research', 'RESULTS_v2_start.md');

/** The longest `git rev-parse` may take; a result row is never worth waiting longer for. */
const GIT_TIMEOUT_MS = 5_000;

/**
 * The commit of the tree under test, for a result row; `unknown` when git cannot say (no repository, no git, no answer in time).
 *
 * <p>Executable and argv, no shell, a deadline, and at the deadline the whole process TREE is killed (`security.md`): on POSIX the child owns a process group
 * (`detached`) and the negative pid takes it, on Windows `taskkill /T /F` — both are `team-server-child.mjs`'s `kill`, the one copy of that rule in scripts/.
 * `executable` and `timeoutMs` are the seams of its test (a fake git that hangs).</p>
 */
export function gitHead(cwd = join(HERE, '..'), { executable = 'git', timeoutMs = GIT_TIMEOUT_MS } = {}) {
  return new Promise((resolveHead) => {
    let out = '';
    let child;
    let timer;
    const finish = (value) => {
      clearTimeout(timer);
      resolveHead(value);
    };
    try {
      child = spawn(executable, ['rev-parse', '--short=8', 'HEAD'], { cwd, stdio: ['ignore', 'pipe', 'ignore'], windowsHide: true, detached: process.platform !== 'win32' });
    } catch {
      finish('unknown');

      return;
    }
    timer = setTimeout(() => {
      kill(child, 'SIGKILL');
      finish('unknown');
    }, timeoutMs);
    child.stdout.on('data', (data) => {
      out += data.toString('utf8');
    });
    child.on('error', () => finish('unknown'));
    child.on('close', (code) => finish(code === 0 && out.trim() !== '' ? out.trim() : 'unknown'));
  });
}

/** Writes the JSON and the row the options ask for (`commit` may be asynchronous: the default asks git, bounded); `now`, `commit` and `resultsFile` are the seams of its test. */
export async function writeResult(result, options, { now = () => new Date(), commit = gitHead, resultsFile = RESULTS_FILE } = {}) {
  if (options.json !== undefined) {
    mkdirSync(dirname(resolve(options.json)), { recursive: true });
    writeFileSync(options.json, `${JSON.stringify(result, null, 2)}\n`);
  }
  if (options.row) {
    const row = tableRow(result, { date: now().toISOString().slice(0, 10), commit: options.commit ?? await commit(), label: options.label ?? '' });
    appendFileSync(resultsFile, `${row}\n`);
  }
}
