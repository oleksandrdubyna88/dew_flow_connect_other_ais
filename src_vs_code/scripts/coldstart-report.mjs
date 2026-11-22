#!/usr/bin/env node
/**
 * `node scripts/coldstart-report.mjs [flags] <sample.json>...` — the verdict of cold-start samples written by a driver that is not `run-host.mjs`
 * (`scripts/measure-remote-wsl.ps1`: each launch of its driver extension writes one raw record). The same rules and the same JSON and row as
 * `run-host.mjs measure` (`coldstart-lib.mjs`, `coldstart-output.mjs`); flags: `--samples N`, `--record-only`, `--json PATH`, `--row`, `--label TEXT`,
 * `--commit SHA`. Exit 0 pass, 1 fail, 64 a bad command line, 66 a sample file that cannot be read.
 */
import { readFileSync } from 'node:fs';
import { isMain } from './bundle-lib.mjs';
import { parseMeasureArgs, summarize } from './coldstart-lib.mjs';
import { writeResult } from './coldstart-output.mjs';

export function report(argv, { log = console.log, error = console.error, output = {} } = {}) {
  let options;
  try {
    options = parseMeasureArgs(argv);
  } catch (reason) {
    error(`coldstart-report: ${reason instanceof Error ? reason.message : String(reason)}`);
    return 64;
  }
  const raws = [];
  for (const file of options.inputs) {
    try {
      raws.push(JSON.parse(readFileSync(file, 'utf8')));
    } catch (reason) {
      error(`coldstart-report: ${file} cannot be read (${reason instanceof Error ? reason.message : String(reason)})`);
      return 66;
    }
  }
  const result = summarize(raws, { expected: options.samples, recordOnly: options.recordOnly });
  writeResult(result, options, output);
  log(`cold start (${result.location}): M1 ${result.m1?.max ?? 'n/a'} ms max, M2 ${JSON.stringify(result.m2)}, M3 ${JSON.stringify(result.m3)}`);
  for (const problem of result.problems) {
    error(`coldstart-report: ${problem}`);
  }
  log(`cold-start measurement: ${result.pass ? 'passed' : 'FAILED'}`);

  return result.pass ? 0 : 1;
}

if (isMain(import.meta.url)) {
  process.exit(report(process.argv.slice(2)));
}
