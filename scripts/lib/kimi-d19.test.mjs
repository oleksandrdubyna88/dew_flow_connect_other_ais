// node --test scripts/lib/*.test.mjs — M-K11c's step arithmetic. One step compares an owned home's inventory with the previous step's and
// classifies what was ADDED; it is a pure function that returns a NEW object, so the run's list of steps is built without mutation
// (the repository's immutability rule) and the inputs it was given are left exactly as they were.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { ownedRefusal, shimRefusal } from './kimi.mjs';
import * as d19 from './kimi-d19.mjs';

const classify = (path) => (path.endsWith('.toml') ? 'configuration' : 'data');

test('a step names what was added, changed and removed against the previous inventory, and classifies the additions', () => {
  assert.equal(typeof d19.inventoryStep, 'function', 'inventoryStep is not exported — runInventory builds its steps by pushing into a list');
  const previous = Object.freeze({ 'config.toml': 'h1', 'device_id': 'h2', 'gone.txt': 'h3' });
  const inv = Object.freeze({ 'config.toml': 'h1', 'device_id': 'h9', 'new.toml': 'h4', 'sessions/a': 'h5' });

  const step = d19.inventoryStep(previous, 'after turn 1', inv, classify, { exit: 0 });

  assert.deepEqual(step.added, ['new.toml', 'sessions/a']);
  assert.deepEqual(step.changed, ['device_id']);
  assert.deepEqual(step.removed, ['gone.txt']);
  assert.deepEqual(step.classes, { 'new.toml': 'configuration', 'sessions/a': 'data' });
  assert.equal(step.step, 'after turn 1');
  assert.equal(step.exit, 0, 'the extra fields ride on the step');
  assert.equal(step.inventory, inv);
});

test('the first step has nothing before it: everything is added', () => {
  const step = d19.inventoryStep({}, 'made', { 'credentials': '(junction)' }, classify);

  assert.deepEqual(step.added, ['credentials']);
  assert.deepEqual(step.changed, []);
  assert.deepEqual(step.removed, []);
});

test('the steps of a run are built by returning a new list each time, never by appending to one', () => {
  assert.equal(typeof d19.withStep, 'function', 'withStep is not exported');
  const none = Object.freeze([]);
  const one = d19.withStep(none, 'made', { a: '1' }, classify);
  const two = d19.withStep(one, 'after turn 1', { a: '1', b: '2' }, classify);

  assert.equal(none.length, 0);
  assert.equal(one.length, 1);
  assert.notEqual(one, two);
  assert.deepEqual(two.map((s) => s.step), ['made', 'after turn 1']);
  assert.deepEqual(two[1].added, ['b'], 'the second step compares with the first');
});

// ------------------------------------------------------------------------------------------------------ G10: one enforcement pattern for every arm
// kimi-arms.mjs makes every arm of its cells carry `requireOwned` (via ownedSpec), so the launch refuses one that lost its owned root. The D19 cells (M-K11, M-K13,
// M-K9e, M-K7b) used to build the same fields by hand - without `requireOwned` - so the refusal never applied to them.

/** The table with the probe's helpers as fakes: a real temp folder for `write` (M-K9e's extended kit reads back what the project kit wrote). */
function d19Table() {
  const write = (path, text) => { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, text); };
  const ownedHome = (runDir) => ({ path: join(runDir, 'kh', 'home'), notes: 'holds: (junction only)' });
  const projectKit = (workdir) => {
    for (const f of ['config.toml', 'local.toml']) write(join(workdir, '.kimi-code', f), '[x]\n');
    return {};
  };
  return d19.d19Arms({ write, ownedHome, projectKit, KIT_PROMPT: 'p', KIT_EXPECT: {} });
}

test('G10: every D19 arm that runs on an owned home asks for one (requireOwned), is judged fine with it and refused without it', () => {
  const root = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-d19-arms-'));
  try {
    const arms = d19Table();
    const owned = Object.entries(arms).filter(([cell]) => cell !== 'M-K11b').flatMap(([cell, list]) => list.map((a, i) => ({ cell, a, spec: a.setup(join(root, `${cell}-${i}`), 1, {}) })));

    assert.ok(owned.length >= 9, 'the loop would check nothing');
    for (const { cell, a, spec } of owned) {
      assert.equal(spec.requireOwned, true, `${cell} ${a.arm}: not marked requireOwned - the launch would run it on the real KIMI_CODE_HOME without a word`);
      assert.equal(ownedRefusal(spec), '', `${cell} ${a.arm}`);
      assert.match(ownedRefusal({ ...spec, home: undefined }), /no owned root/u, `${cell} ${a.arm} was not refused without its owned root`);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('G10: moving the arms onto the shared pattern keeps what they measure - shim arms are never given an empty OS home, the -p arms of M-K13 keep the REAL one', () => {
  const root = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-d19-arms-'));
  try {
    const arms = d19Table();
    const all = Object.entries(arms).flatMap(([cell, list]) => list.map((a, i) => ({ cell, a, spec: a.setup(join(root, `${cell}-${i}`), 1, {}) })));

    for (const { cell, a, spec } of all.filter((s) => s.cell !== 'M-K11b' && (s.spec.kind ?? 'shim') === 'shim' && s.spec.pin !== false)) assert.equal(shimRefusal(spec), '', `${cell} ${a.arm}`);
    const prompt = all.filter((s) => s.spec.kind === 'prompt');
    assert.equal(prompt.length, 2);
    for (const { spec } of prompt) assert.equal(spec.profile ?? '', '', 'M-K13 -p measures the pin with the real OS home, as before');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// Item 4 of the re-check: with no --cells, all twenty cells ran - the legacy ones too, which launch directly on the person's real ~/.kimi-code and ~/.cache.
test('re-check item 4: on Linux the default cells are the OWNED ones; a real-home cell is refused unless --allow-real-home; Windows keeps every cell', () => {
  const all = ['M-K1', 'M-K2', 'M-K3', 'M-K4', 'M-K5', 'M-K6', 'M-K7', 'M-K8', 'M-K8b', 'M-K9', 'M-K10', ...d19.D19_CELLS];
  const plan = (extra) => d19.cellsPlan({ requested: [], all, platform: 'linux', allowRealHome: false, ...extra });

  const byDefault = plan({});
  assert.deepEqual(byDefault.cells, all.filter((c) => d19.OWNED_CELLS.includes(c)), 'the Linux default is the owned set, in the probe\'s order');
  assert.equal(byDefault.refusal, '');
  for (const real of ['M-K1', 'M-K2', 'M-K7', 'M-K8', 'M-K8b', 'M-K9', 'M-K10', 'M-K11b']) assert.equal(byDefault.cells.includes(real), false, `${real} runs on the real home`);

  const asked = plan({ requested: ['M-K3', 'M-K7', 'M-K1'] });
  assert.match(asked.refusal, /--allow-real-home/u);
  assert.match(asked.refusal, /M-K7, M-K1/u, 'the refusal names the cells');
  assert.equal(plan({ requested: ['M-K3', 'M-K7'], allowRealHome: true }).refusal, '');
  assert.equal(plan({ requested: ['M-K3', 'M-K12'] }).refusal, '', 'owned cells need no flag');

  assert.deepEqual(plan({ platform: 'win32' }).cells, all, 'Windows is unchanged');
  assert.equal(plan({ platform: 'win32', requested: ['M-K1'] }).refusal, '');
});
