import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import * as mount from '../.agents/conventions/tools/lib/owned.mjs';
import * as ownedModule from '../src_vs_code/scripts/owned.mjs';
import { runLibTests } from './run-lib-tests.mjs';

const MARKER = 'COAI_TEST_RUN_FOLDER';
const PREFIX = 'coai tests: no test ran — ';
const ENV_NAMES = ['TEMP', 'TMP', 'TMPDIR', MARKER, 'TESTSTODELETE_RUN'];

/** The runner moves THIS process's TEMP, TMP, TMPDIR and marker (it is how its children inherit them): every case that reaches the run folder puts them back. */
async function restoringEnv(fn) {
  const before = Object.fromEntries(ENV_NAMES.map((name) => [name, process.env[name]]));
  try {
    return await fn();
  } finally {
    for (const [name, value] of Object.entries(before)) {
      if (value === undefined) delete process.env[name];
      else process.env[name] = value;
    }
  }
}

/** The wrap's own begin, with its exit hook off: the runner's folder is removed by `end()` in these cases, not by this test process's exit. */
const begin = (options) => ownedModule.beginOwnedRun({ ...options, onExit: () => {}, selfTest: '' });

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

  const status = await restoringEnv(() => runLibTests({
    loadOwned: withMount, spawn, log: () => {}, repoRoot, root, mode: 'collect', begin, sweepOptions: { probe, mine: () => true, now: Date.now() },
  }));

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
  const base = { loadOwned: withMount, repoRoot, root, mode: 'collect', log: () => {}, begin };

  assert.equal(await restoringEnv(() => runLibTests({ ...base, spawn: () => ({ status: 3 }) })), 3);
  assert.equal(await restoringEnv(() => runLibTests({ ...base, spawn: () => ({ status: null }) })), 1, 'a child that was killed is a failure');

  const lines = [];
  let spawned = 0;
  const status = await restoringEnv(() => runLibTests({
    ...base, log: (line) => lines.push(line), sweep: () => () => { throw new Error('boom'); }, spawn: () => { spawned += 1; return { status: 0 }; },
  }));

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

  await restoringEnv(() => runLibTests({
    loadOwned: withMount, spawn, log: (line) => events.push(line), repoRoot, root, mode: 'collect', begin, sweepOptions: { probe, mine: () => true, now: Date.now() },
  }));

  const swept = events.findIndex((line) => line.startsWith('run-lib-tests: leftovers in '));
  const counted = events.findIndex((line) => /^run-lib-tests: 1 old coai-\* directory in /.test(line));
  assert.ok(swept >= 0, `the sweep says what it removed: ${JSON.stringify(events)}`);
  assert.ok(counted > swept, `the count line comes after the sweep's: ${JSON.stringify(events)}`);
  assert.equal(events.indexOf('SPAWN'), events.length - 1, 'and node --test is started after both');
  assert.ok(events.indexOf('SPAWN') > counted);
  assert.ok(fs.existsSync(path.join(root, 'coai-panel-x')), 'a count takes nothing');
});

test('L-a the run folder is made after the sweep and the count, and before the spawn', async (t) => {
  const root = privateRoot(t);
  plantDeadOwnersFolder(root);
  const events = [];
  let seen;
  const spawn = () => {
    events.push('SPAWN');
    seen = Object.fromEntries(ENV_NAMES.map((name) => [name, process.env[name]]));
    return { status: 0 };
  };

  const status = await restoringEnv(() => runLibTests({
    loadOwned: withMount, spawn, log: (line) => events.push(line), repoRoot, root, mode: 'collect',
    begin: (options) => { events.push('BEGIN'); return begin(options); }, sweepOptions: { probe, mine: () => true, now: Date.now() },
  }));

  assert.equal(status, 0);
  const swept = events.findIndex((line) => line.startsWith('run-lib-tests: leftovers in '));
  const counted = events.findIndex((line) => /^run-lib-tests: no old coai-\* leftovers in /.test(line));
  assert.deepEqual([swept >= 0, counted > swept, events.indexOf('BEGIN') > counted, events.indexOf('SPAWN') > events.indexOf('BEGIN')], [true, true, true, true], JSON.stringify(events));
  const folder = seen[MARKER];
  assert.equal(path.dirname(folder), root, 'the run folder is made in the real root');
  assert.ok(path.basename(folder).startsWith(ownedModule.owned.ownedPrefix('run')), path.basename(folder));
  for (const name of ['TEMP', 'TMP', 'TMPDIR']) assert.equal(seen[name], folder, `${name} names the run folder when the child is spawned`);
  assert.equal(seen.TESTSTODELETE_RUN, ownedModule.owned.formatOwner(ownedModule.owned.thisOwner()), 'the runner exports its own record');
  assert.equal(fs.existsSync(folder), false, 'and the folder is removed after the child');
  assert.equal(fs.existsSync(`${folder}.owner`), false);
});

test('L-b a refused begin is one line, exit 78, and nothing is spawned', async (t) => {
  const root = privateRoot(t);
  const errors = [];
  let spawned = 0;

  const status = await restoringEnv(() => runLibTests({
    loadOwned: withMount, spawn: () => { spawned += 1; return { status: 0 }; }, log: () => {}, error: (line) => errors.push(line), repoRoot, root, mode: 'list-only',
    begin: () => { throw new ownedModule.RunFolderRefused(`the run folder could not be made in '${root}' (EACCES)`); },
  }));

  assert.equal(status, 78);
  assert.equal(spawned, 0);
  assert.equal(errors.length, 1);
  assert.ok(errors[0].startsWith(PREFIX) && errors[0].includes(root), errors[0]);
});

test('L-c file arguments are passed instead of the glob; a bad file is exit 2 before the sweep', async (t) => {
  const root = privateRoot(t);
  const spawned = [];
  const status = await restoringEnv(() => runLibTests({
    files: ['scripts/lib/kimi.test.mjs'], loadOwned: withMount, spawn: (...args) => { spawned.push(args); return { status: 0 }; }, log: () => {}, repoRoot, root, mode: 'list-only', begin,
  }));

  assert.equal(status, 0);
  assert.deepEqual(spawned.map(([, args]) => args), [['--test', 'scripts/lib/kimi.test.mjs']]);

  const errors = [];
  let loaded = 0;
  const refused = await runLibTests({
    files: ['scripts/lib/no-such.test.mjs'], loadOwned: async () => { loaded += 1; return ownedModule; }, spawn: () => ({ status: 0 }), log: () => {}, error: (line) => errors.push(line), repoRoot, root, mode: 'list-only',
  });
  assert.equal(refused, 2);
  assert.equal(loaded, 0, 'nothing was loaded, swept or made');
  assert.equal(errors.length, 1);
  assert.match(errors[0], /^run-lib-tests: 'scripts\/lib\/no-such\.test\.mjs' is not a test file/);
});
