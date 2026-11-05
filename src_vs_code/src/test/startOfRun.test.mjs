import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readRule, startOfRun } from '../../scripts/startOfRun.mjs';
import * as runner from '../../scripts/run-tests.mjs';

const { reportLeftoversFirst } = runner;

/**
 * The extension's test run touches NO `coai-*` directory at its start, at any age, whatever `neverSwept` holds
 * (E5 S2-A, `todo/PLAN_test_leftovers_E5_S2_spec.md` § A.3).
 *
 * Both cases go through the ENTRY POINT `run-tests.mjs` calls — `startOfRun` — never through a selector function, so a removal re-added anywhere on
 * that path is red. The invariant case hands the rule an EMPTY `neverSwept`: a test that passes because the list protects the directories proves nothing.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(here, '..', '..', '..');
const rule = readRule(repoRoot);
const day = 24 * 60 * 60_000;
const minute = 60_000;

/** What the product makes under `coai-` at runtime, a downloaded server, the old test name and a name nobody has invented yet. */
const names = [
  'coai-answers-x', 'coai-repair-x', 'coai-noworkspace-x', 'coai-plan-x', 'coai-server-job-x', 'coai-chat-x',
  'coai-question-x', 'coai-check-x', 'coai-0.17.5', 'coai-panel-x', 'coai-futurething-x',
];

function privateRoot(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'retired-sweep-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}

function make(root, name, text, ageMs) {
  const dir = path.join(root, name);
  fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'sentinel.txt'), text);
  const then = new Date(Date.now() - ageMs);
  fs.utimesSync(dir, then, then);
}

test('every coai-* name is outside the start of a run, at any age, with neverSwept empty', (t) => {
  const root = privateRoot(t);
  for (const name of names) make(root, name, `still ${name}`, 10 * day);

  startOfRun({ dir: root, rule: { ...rule, neverSwept: [] } });

  const missing = names.filter((name) => !fs.existsSync(path.join(root, name, 'sentinel.txt')));
  assert.deepEqual(missing, [], 'these are missing: none is the start of a run\'s to take, at ten days old or any age');
  for (const name of names) {
    assert.equal(fs.readFileSync(path.join(root, name, 'sentinel.txt'), 'utf8'), `still ${name}`);
  }

  // And over a recording io: nothing is removed and nothing is stat-ed, whatever the listing holds.
  const calls = [];
  const io = {
    readdirSync: () => names.map((name) => ({ name, isDirectory: () => true })),
    statSync: (target) => { calls.push(`statSync ${path.basename(target)}`); return { mtimeMs: Date.now() - 10 * day }; },
    rmSync: (target) => { calls.push(`rmSync ${path.basename(target)}`); },
  };
  startOfRun({ dir: '/nowhere', rule: { ...rule, neverSwept: [] }, io });
  assert.deepEqual(calls, [], 'the start of a run neither removes nor stats a name');
});

test('an unmarked coai-chat-live survives the start of a run when coai-chat- is not excepted', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-chat-live', 'still somebody\'s chat', 11 * minute);

  startOfRun({ dir: root, rule: { ...rule, neverSwept: rule.neverSwept.filter((p) => p !== 'coai-chat-') } });

  const sentinel = path.join(root, 'coai-chat-live', 'sentinel.txt');
  assert.ok(fs.existsSync(sentinel), 'a live chat\'s directory is somebody\'s even when the list that would protect it forgot its prefix');
  assert.equal(fs.readFileSync(sentinel, 'utf8'), 'still somebody\'s chat');
});

test('the shared file names the product\'s directories and nothing that selects', () => {
  // The extension's own runtime directory, made by chatLaunch.ts. The server's are held by its suite.
  assert.ok(rule.neverSwept.includes('coai-chat-'), 'a live chat\'s working directory is not a leftover');
  assert.equal('prefix' in rule, false, 'nothing selects by prefix any more: a key left in the file would read as a decision');
  assert.equal('keepMinutes' in rule, false, 'nor by age: ten minutes beside a sweep that no longer exists would read as "something still removes after ten minutes"');
});

test('every temp directory the extension makes at runtime is one the product\'s list names', () => {
  // A scan of THIS program's own source that cannot go quiet: it must find the one prefix it knows is
  // there. A new runtime directory nobody adds to the shared list is what the one-off clean-up would
  // offer a person as a leftover, beside a live session's. (Plan round, local.)
  const src = path.join(here, '..');
  const made = [...new Set(fs.readdirSync(src, { recursive: true })
    .filter((f) => f.endsWith('.ts') && !f.split(/[\\/]/).includes('test'))
    .flatMap((f) => [...fs.readFileSync(path.join(src, f), 'utf8')
      .matchAll(/mkdtemp(?:Sync)?\(\s*path\.join\(\s*(?:os\.)?tmpdir\(\)\s*,\s*['"`](coai-[a-z-]+)['"`]/g)].map((m) => m[1])))];

  assert.ok(made.includes('coai-chat-'), `the scan must still see chatLaunch.ts's directory; it found ${made.join(', ') || 'nothing'}`);
  for (const prefix of made) {
    assert.ok(rule.neverSwept.includes(prefix), `${prefix} is made at runtime and is not in shared/temp-sweep.json`);
  }
});

/** A directory listing as `readdirSync(dir, { withFileTypes: true })` answers it. */
function entry(name, isDirectory = true) {
  return { name, isDirectory: () => isDirectory };
}

test('the count is the old test names, never a protected one, a cache or a file', () => {
  const io = { readdirSync: () => [...names.map((name) => entry(name)), entry('coai-3.png', false), entry('other-x')] };

  assert.equal(startOfRun({ dir: '/nowhere', rule, io }).leftovers, 2, 'coai-panel-x and coai-futurething-x: the only names no product prefix, cache or file explains');
  assert.equal(startOfRun({ dir: '/nowhere', rule: { ...rule, neverSwept: [] }, io }).leftovers, 10, 'the version cache coai-0.17.5 stays out whatever the list holds');
});

test('a name is never stat-ed: the count reads names only', () => {
  const statted = [];
  const io = {
    readdirSync: () => ['somebody-elses', 'npm-12345', 'coai-x'].map((name) => entry(name)),
    statSync: (target) => { statted.push(target); throw new Error('a count never stats'); },
  };

  assert.deepEqual(startOfRun({ dir: '/nowhere', rule, io }), { leftovers: 1, legacyRuns: 0 });
  assert.deepEqual(statted, []);
});

test('the runner reports leftovers FIRST, before it makes its run folder', () => {
  const runner = fs.readFileSync(path.join(here, '..', '..', 'scripts', 'run-tests.mjs'), 'utf8');
  const main = runner.slice(runner.indexOf('function main() {'));

  assert.match(main, /^function main\(\) \{\s*reportLeftoversFirst\(\);\s*\n/,
    'a count after the run starts would read the run\'s own folder');
});

test('the start-of-run module cannot delete: it imports no remover', () => {
  const text = fs.readFileSync(path.join(here, '..', '..', 'scripts', 'startOfRun.mjs'), 'utf8');

  assert.doesNotMatch(text, /\b(rm|rmSync|rmdir|rmdirSync|unlink|unlinkSync|rename|renameSync|exec|spawn)\b/, 'the start of a run counts, it takes nothing');
  assert.match(text, /readdirSync/, 'the scan must still see what it knows is there');
});

test('the legacy run folders are counted on their own line, the owner form and the lookalikes left out', () => {
  const io = {
    readdirSync: () => [
      entry('testsToDelete-coai-run-abc123'), entry('testsToDelete-coai-run-def456'), entry('testsToDelete-coai-run-xyz'), entry('testsToDelete-coai-runabc123'),
      entry('testsToDelete-u1000-coai-run-abc123'), entry('testsToDelete-coai-run-aaaaaa', false), ...names.map((name) => entry(name)),
    ],
    statSync: () => { throw new Error('a count never stats'); },
  };

  assert.deepEqual(startOfRun({ dir: '/nowhere', rule, io }), { leftovers: 2, legacyRuns: 2 });
});

test('the runner prints the legacy line only when there is something to say', () => {
  const said = (counted) => {
    const lines = [];
    reportLeftoversFirst({ log: (line) => lines.push(line), count: () => counted });
    return lines;
  };

  const none = said({ leftovers: 0, legacyRuns: 0 });
  assert.equal(none.length, 1);
  assert.match(none[0], /^run-tests: no old coai-\* leftovers in /);

  const legacy = said({ leftovers: 0, legacyRuns: 3 });
  assert.equal(legacy.length, 2);
  assert.match(legacy[1], /3 legacy testsToDelete-coai-run-\* directories/);
  assert.match(legacy[1], /research\/module_tests\.md § The one-off clean-up/);

  const old = said({ leftovers: 2, legacyRuns: 0 });
  assert.equal(old.length, 1);
  assert.match(old[0], /2 old coai-\* directories/);

  const broken = [];
  reportLeftoversFirst({ log: (line) => broken.push(line), count: () => { throw new Error('boom'); } });
  assert.deepEqual(broken, ['run-tests: the leftover count did not run (boom); the tests still will']);
});

test('[S2-A gate 0] the runner counts the REAL temp root it captured at its start, not the one TEMP/TMP/TMPDIR were moved to', (t) => {
  const real = runner.REAL_TEMP_ROOT ?? os.tmpdir();
  const moved = privateRoot(t, 'moved-temp-');
  make(moved, 'coai-panel-redirected', 'a leftover where the run folder would be', 11 * minute);
  const saved = { TMPDIR: process.env.TMPDIR, TMP: process.env.TMP, TEMP: process.env.TEMP };
  t.after(() => {
    for (const [key, value] of Object.entries(saved)) {
      if (value === undefined) delete process.env[key]; else process.env[key] = value;
    }
  });
  process.env.TMPDIR = process.env.TMP = process.env.TEMP = moved;
  const lines = [];

  reportLeftoversFirst({ log: (line) => lines.push(line) });

  assert.equal(runner.REAL_TEMP_ROOT, real, 'the runner exports the temp root of this process as it was when the runner was loaded');
  assert.ok(lines[0].includes(` in ${real}`), `the line names the real root: ${lines[0]}`);
  assert.ok(!lines.some((line) => line.includes(moved)), 'and nothing was read from, or said about, the redirected one');

  const explicit = [];
  reportLeftoversFirst({ log: (line) => explicit.push(line), root: moved });
  assert.match(explicit[0], /1 old coai-\* directory in /, 'the root is a parameter: asked for the redirected one, the run reads that one');
});

test('[S2-A gate 0] the count leaves out the run\'s own folder', () => {
  const dir = path.join(path.sep, 'nowhere');
  const io = { readdirSync: () => [entry('testsToDelete-coai-run-abc123'), entry('testsToDelete-coai-run-def456')] };

  assert.deepEqual(startOfRun({ dir, rule, io, ownFolder: path.join(dir, 'testsToDelete-coai-run-def456') }), { leftovers: 0, legacyRuns: 1 });
  assert.deepEqual(startOfRun({ dir, rule, io }), { leftovers: 0, legacyRuns: 2 }, 'with no folder of its own to leave out it counts both');
});
