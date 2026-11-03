import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import * as mount from '../.agents/conventions/tools/lib/owned.mjs';
import * as ownedModule from '../src_vs_code/scripts/owned.mjs';
import { runLibTests } from './run-lib-tests.mjs';

/**
 * The runner in front of the bare `node --test "scripts/**\/*.test.mjs"` (E5 S2-B, `todo/PLAN_test_leftovers_E5_S2_spec.md` B.1 § 7, B.4 L1-L4): the
 * start-of-run sweep over the real temp root FIRST, then node's own glob. Every case goes through the injected seams: the real spawn is never made here.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(here, '..');
const DEAD_PID = 4_000_000;
const owner = mount.thisOwner();
const ownerToken = mount.thisOwnerToken();
const probe = (pid) => (pid === DEAD_PID ? { state: 'dead' } : mount.processProbe(pid));

function privateRoot(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'run-lib-tests-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}

function plantDeadOwnersFolder(root) {
  const dir = path.join(root, `testsToDelete-${ownerToken}-coai-run-lib123`);
  fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'sentinel.txt'), 'x');
  fs.writeFileSync(`${dir}.owner`, `${mount.formatOwner({ ...owner, pid: DEAD_PID })}\n`);
  const then = new Date(Date.now() - 11 * 60_000);
  fs.utimesSync(dir, then, then);
  fs.utimesSync(`${dir}.owner`, then, then);
  return dir;
}

const withMount = async () => ownedModule;

test('L1 the sweep runs over the real root before node --test is started', async (t) => {
  const root = privateRoot(t);
  const dir = plantDeadOwnersFolder(root);
  const spawned = [];
  const spawn = (...args) => {
    spawned.push({ args, folderStillThere: fs.existsSync(dir) });
    return { status: 0 };
  };

  const status = await runLibTests({
    loadOwned: withMount, spawn, log: () => {}, repoRoot, root, mode: 'collect', sweepOptions: { probe, mine: () => true, now: Date.now() },
  });

  assert.equal(status, 0);
  assert.equal(spawned.length, 1, 'node --test is started once');
  assert.equal(spawned[0].folderStillThere, false, 'the planted gone-owner folder was collected before the spawn');
  assert.deepEqual(spawned[0].args, [process.execPath, ['--test', 'scripts/**/*.test.mjs'], { cwd: repoRoot, stdio: 'inherit' }], 'node\'s own glob, inherited streams, the repository root');
});

test('L2 ci.yml runs the lib tests through the runner, never bare', () => {
  const text = fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'ci.yml'), 'utf8');
  const runs = [...text.matchAll(/^\s*run:\s*(.+)$/gm)].map((m) => m[1].trim());

  assert.ok(runs.includes('node scripts/run-lib-tests.mjs'), 'a step runs the runner');
  assert.deepEqual(runs.filter((run) => /node --test\s+"scripts\//.test(run)), [], 'no step starts the lib tests bare');
});

test('L3 the child\'s status is the runner\'s; a thrown sweep is one line and the child still runs', async (t) => {
  const root = privateRoot(t);
  const base = { loadOwned: withMount, repoRoot, root, mode: 'collect', log: () => {} };

  assert.equal(await runLibTests({ ...base, spawn: () => ({ status: 3 }) }), 3);
  assert.equal(await runLibTests({ ...base, spawn: () => ({ status: null }) }), 1, 'a child that was killed is a failure');

  const lines = [];
  let spawned = 0;
  const status = await runLibTests({
    ...base, log: (line) => lines.push(line), sweep: () => () => { throw new Error('boom'); }, spawn: () => { spawned += 1; return { status: 0 }; },
  });

  assert.equal(status, 0);
  assert.equal(spawned, 1, 'the child still runs');
  assert.equal(lines.length, 2, 'the sweep line, then the count line');
  assert.equal(lines[0], 'run-lib-tests: leftovers sweep not run (boom); the tests still will');
  assert.match(lines[1], /^run-lib-tests: no old coai-\* leftovers in /);
});

test('L4 the mount missing is one line and exit 2, nothing spawned', async (t) => {
  const root = privateRoot(t);
  const errors = [];
  let spawned = 0;

  const status = await runLibTests({
    loadOwned: async () => { throw new Error('Cannot find module \'/x/owned.mjs\'\n    at somewhere'); }, spawn: () => { spawned += 1; return { status: 0 }; },
    log: () => {}, error: (line) => errors.push(line), repoRoot, root, mode: 'collect',
  });

  assert.equal(status, 2);
  assert.equal(spawned, 0, 'nothing is spawned');
  assert.equal(errors.length, 1);
  assert.match(errors[0], /^run-lib-tests: the shared conventions are not mounted \(Cannot find module .*\) — run: git submodule update --init \.agents\/conventions$/);
});

test('L6 [S2-B code1 5] the coai-* count follows the sweep and comes before node --test, as in run-tests.mjs', async (t) => {
  const root = privateRoot(t);
  plantDeadOwnersFolder(root);
  fs.mkdirSync(path.join(root, 'coai-panel-x'));
  const events = [];
  const spawn = () => {
    events.push('SPAWN');
    return { status: 0 };
  };

  await runLibTests({
    loadOwned: withMount, spawn, log: (line) => events.push(line), repoRoot, root, mode: 'collect', sweepOptions: { probe, mine: () => true, now: Date.now() },
  });

  const swept = events.findIndex((line) => line.startsWith('run-lib-tests: leftovers in '));
  const counted = events.findIndex((line) => /^run-lib-tests: 1 old coai-\* directory in /.test(line));
  assert.ok(swept >= 0, `the sweep says what it removed: ${JSON.stringify(events)}`);
  assert.ok(counted > swept, `the count line comes after the sweep's: ${JSON.stringify(events)}`);
  assert.equal(events.indexOf('SPAWN'), events.length - 1, 'and node --test is started after both');
  assert.ok(events.indexOf('SPAWN') > counted);
  assert.ok(fs.existsSync(path.join(root, 'coai-panel-x')), 'a count takes nothing');
});
