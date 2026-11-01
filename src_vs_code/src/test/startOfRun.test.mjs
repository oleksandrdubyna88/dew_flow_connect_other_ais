import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readRule } from '../../scripts/sweepTemp.mjs';
import { startOfRun } from '../../scripts/startOfRun.mjs';

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
