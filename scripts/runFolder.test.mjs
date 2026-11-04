import '../src_vs_code/scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { owned } from '../src_vs_code/scripts/owned.mjs';

/**
 * The PROOF FILE of the scope `scripts/` (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 5; the rule's baseline names it as the scope's proof): `TEMP` names the
 * run folder `scripts/run-lib-tests.mjs` made, and a run that cannot make or claim it, or whose temp variables disagree with it, fails BEFORE any test.
 *
 * P-T asserts what a test sees; P-1..P-5 start the scope's RUNNER (never `node --test`) with ONE file argument — a fixture test with no guard that writes a sentinel when
 * it LOADS: an exit 78 with the sentinel absent is "fails before discovery". The five forced failures: no folder, no claim, and each of TEMP, TMP and TMPDIR disagreeing alone
 * (the self-test seam `COAI_RUNTEMP_SELFTEST`).
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const runner = path.join(here, 'run-lib-tests.mjs');
const MARKER = 'COAI_TEST_RUN_FOLDER';
const SENTINEL = 'COAI_S4_SENTINEL';
const PREFIX = 'coai tests: no test ran — ';

test('P-T TEMP names the run folder', () => {
  const folder = process.env[MARKER];
  assert.ok(folder, `${MARKER} is set: the runner made the run folder`);
  assert.equal(process.env.TEMP, folder);
  assert.equal(process.env.TMP, folder);
  assert.equal(process.env.TMPDIR, folder);
  assert.equal(os.tmpdir(), folder);
  assert.deepEqual(owned.classifyName(path.basename(folder)), { form: 'owner', owner: owned.thisOwnerToken(), token: 'coai' });
  assert.ok(path.basename(folder).startsWith(owned.ownedPrefix('run')));
  const claim = owned.parseOwner(fs.readFileSync(`${folder}.owner`, 'utf8').trim());
  assert.notEqual(claim.unreadable, true);
  assert.equal(claim.owner, owned.thisOwnerToken());
  assert.equal(claim.host, owned.thisOwner().host);
  assert.notEqual(claim.pid, process.pid, 'the claim names the runner, not this test process');
  assert.equal(owned.ownerState(claim), 'live');
});

/** A fixture test file with no guard that notes that it ran, and an empty temp root for the child. */
function fixture(t) {
  const base = owned.ownedDir(t, 'proof');
  const file = path.join(base, 'sentinel.test.mjs');
  fs.writeFileSync(file, `import { appendFileSync } from 'node:fs';\nappendFileSync(process.env.${SENTINEL}, 'ran\\n');\n`);
  const root = path.join(base, 'root');
  fs.mkdirSync(root);
  return { file, root, sentinel: path.join(base, 'sentinel.txt') };
}

function forced(t, { env = (root) => ({ TEMP: root, TMP: root, TMPDIR: root }), selfTest }) {
  const f = fixture(t);
  const base = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN', 'COAI_RUNTEMP_SELFTEST']) delete base[name];
  const result = spawnSync(process.execPath, [runner, f.file], {
    encoding: 'utf8', timeout: 120_000, env: { ...base, ...env(f.root), [SENTINEL]: f.sentinel, ...(selfTest ? { COAI_RUNTEMP_SELFTEST: selfTest } : {}) },
  });
  assert.equal(result.status, 78, `${result.stdout}${result.stderr}`);
  const lines = result.stderr.trimEnd().split('\n');
  assert.equal(lines.length, 1, `stderr holds ONLY the refusal: ${result.stderr}`);
  assert.ok(lines[0].startsWith(PREFIX), lines[0]);
  assert.equal(fs.existsSync(f.sentinel), false, 'no test file was loaded: the run failed before discovery');
  return { ...f, line: lines[0] };
}

test('P-1 a folder that cannot be made fails before any test', (t) => {
  let missing;
  const { line, root } = forced(t, { env: (root) => { missing = path.join(root, 'missing'); return { TEMP: missing, TMP: missing, TMPDIR: missing }; } });
  assert.ok(line.includes(missing), line);
  assert.equal(fs.existsSync(missing), false);
  assert.deepEqual(fs.readdirSync(root), []);
});

test('P-2 a claim that cannot be written fails before any test', (t) => {
  const { line, root } = forced(t, { selfTest: 'fail-claim' });
  assert.match(line, /claim/);
  assert.deepEqual(fs.readdirSync(root), [], 'nothing is left');
});

for (const [number, variable, selfTest] of [[3, 'TEMP', 'mismatch-temp'], [4, 'TMP', 'mismatch-tmp'], [5, 'TMPDIR', 'mismatch-tmpdir']]) {
  test(`P-${number} ${variable} that does not name the run folder fails before any test`, (t) => {
    const { line, root } = forced(t, { selfTest });
    assert.ok(line.includes(`${variable}='${root}'`), line);
    assert.deepEqual(fs.readdirSync(root), [], 'the folder and claim the run made are removed');
  });
}
