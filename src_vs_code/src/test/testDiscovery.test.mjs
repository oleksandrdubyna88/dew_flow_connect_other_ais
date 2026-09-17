import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { discover } from '../../scripts/run-tests.mjs';

/**
 * That a test file which EXISTS is a test file which RUNS.
 *
 * <p><b>Why this test exists, precisely.</b> It was not true. `npm test` named five `.test.mjs`
 * files by hand, then `theBundleLoads.test.mjs` by hand, then ran `run-tests.mjs` — which reads
 * `out/test` for `.test.js`, and a `.test.mjs` never arrives there because tsc has no reason to
 * touch it. A SEVENTH `.mjs` test was therefore run by nothing at all, and the only thing standing
 * between the repository and a silently dead test was whether somebody remembered to edit a list in
 * `package.json`.</p>
 *
 * <p><b>Measured, on a checkout of main before the fix.</b> A file
 * `src/test/ciDiscoveryProbe.test.mjs` containing nothing but
 * `throw new Error('CI_DISCOVERY_PROBE')` left `npm test` at <b>3530 passed, 0 failed, exit 0</b>,
 * without so much as naming it; `node --test` on that same path exited 1. A suite that answers
 * "all green" for a file it cannot even import is not reporting on the repository — it is
 * reporting on the list.</p>
 *
 * <p>So the cases below are about the DIRECTORY being the source of truth. The last one pins the
 * `test` script itself, because the defect was never in the runner: it was in a human-maintained
 * list next to it, and a list can grow back.</p>
 */

function fixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-discovery-'));
  fs.mkdirSync(path.join(root, 'out', 'test'), { recursive: true });
  fs.mkdirSync(path.join(root, 'src', 'test'), { recursive: true });
  return root;
}

test('a compiled test is discovered without being named', () => {
  const root = fixture();
  fs.writeFileSync(path.join(root, 'out', 'test', 'alpha.test.js'), '');

  const found = discover(root);

  assert.deepEqual(found.compiled, [path.join(root, 'out', 'test', 'alpha.test.js')]);
  assert.deepEqual(found.sources, []);
});

test('a source .mjs test is discovered without being named — the defect this file exists for', () => {
  const root = fixture();
  fs.writeFileSync(path.join(root, 'src', 'test', 'beta.test.mjs'), '');

  const found = discover(root);

  assert.deepEqual(found.sources, [path.join(root, 'src', 'test', 'beta.test.mjs')]);
  assert.deepEqual(found.compiled, []);
});

test('every .mjs test this repository HAS is discovered — not five of six', () => {
  const onDisk = fs
    .readdirSync(path.join('src', 'test'))
    .filter((f) => f.endsWith('.test.mjs'))
    .sort();

  const found = discover('.')
    .sources.map((f) => path.basename(f))
    .sort();

  assert.ok(onDisk.length > 0, 'the fixture for this case is the repository itself');
  assert.deepEqual(found, onDisk, 'discovery must answer with the directory, never with a list');
});

test('nothing that is not a test is picked up', () => {
  const root = fixture();
  for (const name of ['helper.mjs', 'notes.md', 'fixture.test.mjs.bak']) {
    fs.writeFileSync(path.join(root, 'src', 'test', name), '');
  }
  fs.writeFileSync(path.join(root, 'out', 'test', 'helper.js'), '');

  const found = discover(root);

  assert.deepEqual(found.compiled, []);
  assert.deepEqual(found.sources, []);
});

test('a missing directory is empty rather than a crash', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-discovery-bare-'));

  const found = discover(root);

  assert.deepEqual(found, { compiled: [], sources: [] });
});

test('the test script names no individual test file', () => {
  const pkg = JSON.parse(fs.readFileSync('package.json', 'utf8'));

  // The whole value, not a fragment: this goes red both when a filename is appended to the script
  // and when the composition is replaced by something else that merely happens to contain no path.
  assert.equal(pkg.scripts.test, 'npm run compile && node scripts/run-tests.mjs');
});
