import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { owned } from '../../scripts/owned.mjs';
import { report } from '../../scripts/coldstart-report.mjs';
import { writeResult } from '../../scripts/coldstart-output.mjs';
import { summarize } from '../../scripts/coldstart-lib.mjs';

/**
 * `coldstart-report.mjs` (the verdict of samples a driver other than `run-host.mjs` wrote — the Remote-WSL script's) and `coldstart-output.mjs` (the JSON
 * file and the table row both launchers write): same rule, same row, and a clear exit code for each way to be wrong.
 */

const quiet = () => {};
const T0 = 7_000_000;
const sample = (n, remoteName = 'wsl', m2 = 900) => ({
  schema: 1, sample: n, t0: T0, startedActive: false,
  marks: [{ name: 'eval', at: T0 - 100 }, { name: 'activateReturn', at: T0 - 90 }, { name: 'firstData', at: T0 + 500 }, { name: 'ready', at: T0 + m2 }],
  host: { remoteName, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Code' },
});

function files(t, records) {
  const dir = owned.ownedDir(t, 'mr');

  return records.map((record, at) => {
    const file = path.join(dir, `sample-${at + 1}.json`);
    fs.writeFileSync(file, JSON.stringify(record));

    return file;
  });
}

test('R1 three good records pass: the exit code is 0, the JSON is written, and the row is appended with the place named', (t) => {
  const inputs = files(t, [sample(1), sample(2), sample(3)]);
  const out = path.join(owned.ownedDir(t, 'mr-out'), 'result.json');
  const results = path.join(owned.ownedDir(t, 'mr-res'), 'RESULTS.md');
  fs.writeFileSync(results, '| h |\n');
  const lines = [];
  const code = report(['--json', out, '--row', '--label', 'remote-wsl', '--commit', 'abc12345', ...inputs], { log: (line) => lines.push(line), error: quiet, output: { resultsFile: results, now: () => new Date('2026-11-22T00:00:00Z') } });

  assert.equal(code, 0);
  assert.equal(JSON.parse(fs.readFileSync(out, 'utf8')).location, 'wsl');
  assert.equal(fs.readFileSync(results, 'utf8'), '| h |\n| 2026-11-22 | abc12345 | remote-wsl | wsl | linux | 10 | 900 / 900 / 900 | 500 / 500 / 500 | pass |\n');
  assert.ok(lines.some((line) => line.includes('passed')));
});

test('R2 a slow sample, a missing sample, a bad flag and an unreadable file each fail with their own code', (t) => {
  const errors = [];
  const slow = report(files(t, [sample(1), sample(2, 'wsl', 2500), sample(3)]), { log: quiet, error: (line) => errors.push(line) });
  assert.equal(slow, 1);
  assert.match(errors.join('\n'), /M2 max 2500 ms is over the 2000 ms limit/);

  assert.equal(report(files(t, [sample(1), sample(2)]), { log: quiet, error: (line) => errors.push(line) }), 1);
  assert.match(errors.join('\n'), /2 sample\(s\) came back, 3 were needed/);
  assert.equal(report(['--nope'], { log: quiet, error: quiet }), 64);
  assert.equal(report([path.join(owned.ownedDir(t, 'mr-none'), 'missing.json')], { log: quiet, error: quiet }), 66);
});

test('R3 writeResult writes nothing the options do not ask for', (t) => {
  const dir = owned.ownedDir(t, 'mr-w');
  const results = path.join(dir, 'RESULTS.md');
  fs.writeFileSync(results, 'x\n');
  writeResult(summarize([sample(1), sample(2), sample(3)]), { json: undefined, row: false }, { resultsFile: results });

  assert.equal(fs.readFileSync(results, 'utf8'), 'x\n');
  assert.deepEqual(fs.readdirSync(dir), ['RESULTS.md']);
});
