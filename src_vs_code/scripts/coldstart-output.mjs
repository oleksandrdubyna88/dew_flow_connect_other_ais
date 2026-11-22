/**
 * Where a cold-start result goes (v2 extension.md § 2; epic E2 story S2): the JSON file `--json` names, and — with `--row` — one more row at the end of
 * `research/RESULTS_v2_start.md`. Shared by `run-host.mjs measure` and `coldstart-report.mjs` so both write the same row. The numbers themselves are
 * `coldstart-lib.mjs`'s; this file only writes them.
 */
import { execFileSync } from 'node:child_process';
import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tableRow } from './coldstart-lib.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));

/** The results file of the v2 start measurements, by default. */
export const RESULTS_FILE = join(HERE, '..', '..', 'research', 'RESULTS_v2_start.md');

/** The commit of the tree under test, for a result row; `unknown` when git cannot say (no repository, no git). */
export function gitHead(cwd = join(HERE, '..')) {
  try {
    return execFileSync('git', ['rev-parse', '--short=8', 'HEAD'], { cwd, encoding: 'utf8' }).trim();
  } catch {
    return 'unknown';
  }
}

/** Writes the JSON and the row the options ask for; `now`, `commit` and `resultsFile` are the seams of its test. */
export function writeResult(result, options, { now = () => new Date(), commit = gitHead, resultsFile = RESULTS_FILE } = {}) {
  if (options.json !== undefined) {
    mkdirSync(dirname(resolve(options.json)), { recursive: true });
    writeFileSync(options.json, `${JSON.stringify(result, null, 2)}\n`);
  }
  if (options.row) {
    const row = tableRow(result, { date: now().toISOString().slice(0, 10), commit: options.commit ?? commit(), label: options.label ?? '' });
    appendFileSync(resultsFile, `${row}\n`);
  }
}
