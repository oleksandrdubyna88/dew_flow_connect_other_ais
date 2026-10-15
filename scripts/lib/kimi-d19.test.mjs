// node --test scripts/lib/*.test.mjs — M-K11c's step arithmetic. One step compares an owned home's inventory with the previous step's and
// classifies what was ADDED; it is a pure function that returns a NEW object, so the run's list of steps is built without mutation
// (the repository's immutability rule) and the inputs it was given are left exactly as they were.
import assert from 'node:assert/strict';
import { test } from 'node:test';
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
