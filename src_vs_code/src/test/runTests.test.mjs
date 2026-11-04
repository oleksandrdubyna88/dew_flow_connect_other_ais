import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { loadStartOfRunSweep, owned } from '../../scripts/owned.mjs';

/**
 * The extension's test runner (`scripts/run-tests.mjs`) in the contract's grammar (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.1, R1-R7; it replaces
 * `runTemp.test.mjs`, whose legacy-name run folder and "as before" fallback are gone). The runner makes ONE run folder through the wrap — claim first, naming the
 * runner — moves TEMP, TMP and TMPDIR there, sets the marker its test files check, exports its own record as TESTSTODELETE_RUN, and fails closed before any test.
 *
 * Each case runs the real runner as a CHILD on a fixture tree in a private root of the wrap (`owned.ownedDir`). The child's `NODE_TEST_CONTEXT` is dropped
 * (or its own `node --test` sees itself nested and skips every file) and so is the marker; TESTSTODELETE_RUN only where the case sets it.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const runner = path.join(pkg, 'scripts', 'run-tests.mjs');
const wrapUrl = pathToFileURL(path.join(pkg, 'scripts', 'owned.mjs')).href;
const MARKER = 'COAI_TEST_RUN_FOLDER';
const PREFIX = 'coai tests: no test ran — ';
const DEAD_PID = 4_000_000;
const minute = 60_000;

/** A compiled fixture test: it notes what it sees to `$SEEN`, runs a grandchild that does the same, and may fail. */
const compiledBody = (name, outcome) => `
const fs = require('node:fs'); const os = require('node:os'); const { spawnSync } = require('node:child_process');
const test = require('node:test');
test(${JSON.stringify(name)}, () => {
  const folder = os.tmpdir();
  fs.appendFileSync(process.env.SEEN, ['child', folder, fs.existsSync(folder + '.owner'), process.env.${MARKER}, process.env.TESTSTODELETE_RUN].join('|') + '\\n');
  fs.appendFileSync(process.env.SEEN, 'claim|' + fs.readFileSync(folder + '.owner', 'utf8').trim() + '\\n');
  spawnSync(process.execPath, ['-e', "require('node:fs').appendFileSync(process.env.SEEN, 'grandchild|' + require('node:os').tmpdir() + '\\\\n')"], { stdio: 'inherit' });
  ${outcome === 'fails' ? 'throw new Error("on purpose");' : ''}
});
`;

/** A LEAF source fixture: it makes a folder through the real wrap and notes its claim. */
const leafBody = `
import * as fs from 'node:fs';
import { test } from 'node:test';
const { owned } = await import(${JSON.stringify(wrapUrl)});
test('leaf', (t) => {
  const dir = owned.ownedDir(t, 'leaf');
  fs.appendFileSync(process.env.SEEN, 'leaf|' + fs.readFileSync(dir + '.owner', 'utf8').trim() + '\\n');
});
`;

function fixtureTree(root, { compiled = ['passes', 'fails'], sources = {} } = {}) {
  const ext = path.join(root, 'ext');
  fs.mkdirSync(path.join(ext, 'out', 'test'), { recursive: true });
  fs.mkdirSync(path.join(ext, 'src', 'test'), { recursive: true });
  for (const name of compiled) fs.writeFileSync(path.join(ext, 'out', 'test', `${name}.test.js`), compiledBody(name, name === 'fails' ? 'fails' : 'passes'));
  for (const [name, body] of Object.entries(sources)) fs.writeFileSync(path.join(ext, 'src', 'test', name), body);
  const machinesTemp = path.join(root, 'machines-temp');
  fs.mkdirSync(machinesTemp);
  return { ext, machinesTemp, seen: path.join(root, 'seen.txt') };
}

function runTheRunner({ ext, machinesTemp, seen, args = [], env = {} }) {
  const base = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN']) delete base[name];
  return spawnSync(process.execPath, [runner, ...args], {
    cwd: ext, encoding: 'utf8', timeout: 120_000, env: { ...base, TEMP: machinesTemp, TMP: machinesTemp, TMPDIR: machinesTemp, SEEN: seen, ...env },
  });
}

const rowsOf = (seen) => fs.readFileSync(seen, 'utf8').trim().split('\n').map((line) => line.split('|'));

test('R1 the runner\'s children and grandchildren share ONE run folder of the owner form, and it is gone with its claim after the run — pass or fail', (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r1'));

  const result = runTheRunner(fixture);

  assert.notEqual(result.status, 0, `one test fails, so the runner fails: ${result.stdout}${result.stderr}`);
  const rows = rowsOf(fixture.seen);
  const children = rows.filter(([who]) => who === 'child');
  assert.equal(children.length, 2, 'both tests ran');
  assert.equal(rows.filter(([who]) => who === 'grandchild').length, 2, 'both grandchildren ran');
  const folders = new Set(rows.filter(([who]) => who === 'child' || who === 'grandchild').map(([, folder]) => folder));
  assert.equal(folders.size, 1, `every process saw the same folder: ${[...folders].join(', ')}`);
  const [folder] = folders;
  assert.equal(path.dirname(folder), fixture.machinesTemp);
  assert.ok(path.basename(folder).startsWith(owned.ownedPrefix('run')), path.basename(folder));
  assert.match(path.basename(folder).slice(owned.ownedPrefix('run').length), /^[A-Za-z0-9]{6}$/);
  assert.ok(children.every(([, , claim, marker]) => claim === 'true' && marker === folder), 'the claim existed and the marker named the folder while the children ran');
  assert.deepEqual(fs.readdirSync(fixture.machinesTemp), [], 'the folder and its claim are gone');
});

test('R2 the claim is in the contract\'s grammar and names the runner, not an inherited TESTSTODELETE_RUN; a leaf\'s claim names the run', (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r2'), { compiled: ['passes'], sources: { 'leaf.test.mjs': leafBody } });
  const inherited = owned.formatOwner(owned.thisOwner());

  const result = runTheRunner({ ...fixture, env: { TESTSTODELETE_RUN: inherited } });

  assert.equal(result.status, 0, `${result.stdout}${result.stderr}`);
  const rows = rowsOf(fixture.seen);
  const claim = owned.parseOwner(rows.find(([who]) => who === 'claim')[1]);
  assert.notEqual(claim.unreadable, true, 'the claim is the owner grammar');
  assert.equal(claim.pid, result.pid, 'the claim names the spawned runner');
  assert.notEqual(claim.pid, process.pid, 'and not the inherited record\'s process (this test)');
  assert.equal(claim.owner, owned.thisOwnerToken());
  assert.equal(claim.host, owned.thisOwner().host, 'the host is the contract\'s tag (linux, macos, windows), never win32 or darwin');

  const exported = owned.parseOwner(rows.find(([who]) => who === 'child')[4]);
  assert.equal(exported.pid, result.pid, 'the runner exports its OWN record as TESTSTODELETE_RUN to its children (the detail, §3)');
  const leaf = owned.parseOwner(rows.find(([who]) => who === 'leaf')[1]);
  assert.equal(leaf.pid, result.pid, 'a leaf of the run scope: its folder\'s claim names the run\'s record');
});

test('R3 a root that cannot take the folder fails before any test', (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r3'));
  const missing = path.join(fixture.machinesTemp, 'missing');

  const result = runTheRunner({ ...fixture, machinesTemp: missing });

  assert.equal(result.status, 78, `${result.stdout}${result.stderr}`);
  const lines = result.stderr.trimEnd().split('\n');
  assert.equal(lines.length, 1, result.stderr);
  assert.ok(lines[0].startsWith(PREFIX) && lines[0].includes(missing), lines[0]);
  assert.equal(fs.existsSync(fixture.seen), false, 'no test ran');
  assert.equal(fs.existsSync(missing), false, 'the missing root was not made');
});

test('R4 no compiled tests: exit 1, and nothing is left', (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r4'), { compiled: [] });

  const result = runTheRunner(fixture);

  assert.equal(result.status, 1, `${result.stdout}${result.stderr}`);
  assert.match(result.stderr, /no compiled test files/);
  assert.deepEqual(fs.readdirSync(fixture.machinesTemp), [], 'folder and claim');
});

const sourceFixture = (name) => `import * as fs from 'node:fs';\nimport { test } from 'node:test';\ntest('${name}', () => { fs.appendFileSync(process.env.SEEN, '${name}|' + process.env.${MARKER} + '\\n'); });\n`;

function plantStaleFolder(root) {
  const dir = path.join(root, `${owned.ownedPrefix('stale')}abc123`);
  fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'sentinel.txt'), 'x');
  fs.writeFileSync(`${dir}.owner`, `${owned.formatOwner({ ...owned.thisOwner(), pid: DEAD_PID })}\n`);
  const then = new Date(Date.now() - 11 * minute);
  fs.utimesSync(dir, then, then);
  fs.utimesSync(`${dir}.owner`, then, then);
  return dir;
}

test('R5 file arguments run exactly those files, inside the run folder; a bad argument makes nothing, not even the sweep', (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r5'), { compiled: [], sources: { 'a.test.mjs': sourceFixture('a'), 'b.test.mjs': sourceFixture('b') } });

  const result = runTheRunner({ ...fixture, args: [path.join('src', 'test', 'a.test.mjs')] });

  assert.equal(result.status, 0, `${result.stdout}${result.stderr}`);
  const rows = rowsOf(fixture.seen);
  assert.deepEqual(rows.map(([who]) => who), ['a'], 'a ran, b did not');
  assert.ok(path.basename(rows[0][1]).startsWith(owned.ownedPrefix('run')), 'inside the run folder');

  const stale = plantStaleFolder(fixture.machinesTemp);
  const before = fs.readdirSync(fixture.machinesTemp).sort();
  for (const bad of [path.join('src', 'test', 'missing.test.mjs'), 'package.json']) {
    fs.writeFileSync(path.join(fixture.ext, 'package.json'), '{}');
    const refused = runTheRunner({ ...fixture, args: [path.join('src', 'test', 'a.test.mjs'), bad] });
    assert.equal(refused.status, 2, `${bad}: ${refused.stdout}${refused.stderr}`);
    assert.equal(refused.stderr.trimEnd().split('\n').length, 1, refused.stderr);
    assert.match(refused.stderr, new RegExp(`^run-tests: '${bad.replace(/[.\\/]/g, '\\$&')}' is not a test file`));
  }
  assert.deepEqual(fs.readdirSync(fixture.machinesTemp).sort(), before, 'nothing was made');
  assert.ok(fs.existsSync(stale), 'validation runs BEFORE the sweep: the stale owned folder is still there');
  assert.ok(fs.existsSync(`${stale}.owner`));
});

const killerBody = `
const fs = require('node:fs');
const claim = fs.readFileSync(process.env.${MARKER} + '.owner', 'utf8').trim();
fs.appendFileSync(process.env.SEEN, 'killing|' + claim + '\\n');
process.kill(Number(claim.split(':')[2]), 'SIGKILL');
`;

test('R6 a killed runner\'s folder is collected by the next sweep', async (t) => {
  const fixture = fixtureTree(owned.ownedDir(t, 'r6'), { compiled: [] });
  fs.writeFileSync(path.join(fixture.ext, 'out', 'test', 'killer.test.js'), killerBody);

  const result = runTheRunner(fixture);

  assert.equal(result.signal, 'SIGKILL', `${result.stdout}${result.stderr}`);
  const left = fs.readdirSync(fixture.machinesTemp);
  assert.equal(left.length, 2, `the folder and its claim remain: ${left.join(', ')}`);
  const claimFile = left.find((name) => name.endsWith('.owner'));
  const runnerPid = owned.parseOwner(fs.readFileSync(path.join(fixture.machinesTemp, claimFile), 'utf8').trim()).pid;
  assert.equal(runnerPid, result.pid);

  const sweep = await loadStartOfRunSweep();
  const outcome = await sweep({
    repo: repoRoot, tmp: fixture.machinesTemp, now: Date.now() + 11 * minute, probe: (pid) => (pid === runnerPid ? { state: 'dead' } : owned.processProbe(pid)),
  });

  if (process.platform === 'win32') {
    assert.equal(fs.readdirSync(fixture.machinesTemp).length, 2, 'on Windows the sweep lists and changes nothing (F16 interim)');
    assert.ok(outcome.listed.length > 0);
  } else {
    assert.deepEqual(fs.readdirSync(fixture.machinesTemp), [], `the sweep removed both: ${JSON.stringify(outcome)}`);
  }
});

test('R7 the runner makes and removes folders only through the wrap', () => {
  const text = fs.readFileSync(runner, 'utf8').split('\n').filter((line) => !/^\s*(\/\/|\*|\/\*)/.test(line)).join('\n');
  for (const call of ['rmSync', 'mkdirSync', 'writeFileSync', 'mkdtemp']) {
    assert.equal(text.includes(call), false, `run-tests.mjs calls ${call}`);
  }
});
