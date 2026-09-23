import assert from 'node:assert/strict';
import { test } from 'node:test';

import { GATE_PER_SINCE, gatePerSkewNote } from '../gateScope';
import { DEFAULTS, envBlock, settingMessageFrom, settingWrite, settingsFrom } from '../settingsShape';
import { type Control, type Page, lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * How often split work comes back through the gate (issue #131): the setting, the wire, the radio
 * the panel draws — RUN — and the note an older server earns.
 */

function gate(overrides: Parameters<typeof panelState>[1] = {}): Page {
  return runPanel(panelState('gate', overrides));
}

function radio(page: Page, value: string): Control {
  const found = page.controls.filter((one) => one.dataset['setting'] === 'gatePer' && one.value === value);
  assert.equal(found.length, 1, `no gatePer radio for ${value} — ${found.length} matched`);

  return found[0]!;
}

test('the default is one gate per epic, and it sends nothing', () => {
  assert.equal(DEFAULTS.gatePer, 'epic');
  assert.equal(envBlock(DEFAULTS)['COAI_GATE_PER'], undefined);
});

test('one gate for the whole task crosses as COAI_GATE_PER=task', () => {
  assert.equal(envBlock({ ...DEFAULTS, gatePer: 'task' })['COAI_GATE_PER'], 'task');
});

test('anything stored but "task" reads as per epic', () => {
  for (const stored of [undefined, 'epic', 'story', 5, null]) {
    assert.equal(settingsFrom((key) => (key === 'gatePer' ? stored : undefined)).gatePer, 'epic', String(stored));
  }
});

test('choosing the whole task on the panel writes coai.gatePer = task', () => {
  const page = gate();
  const task = radio(page, 'task');

  task.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), { kind: 'plain', key: 'gatePer', value: 'task' });
});

test('the saved choice is the checked one', () => {
  const page = gate({ settings: { ...DEFAULTS, gatePer: 'task' } });

  assert.match(page.html, /value="task" checked/);
  assert.doesNotMatch(page.html, /value="epic" checked/);
});

test('an older server is named only while the split is on', () => {
  assert.match(gatePerSkewNote('0.32.0', true), /still orders a gate round after EVERY story/);
  assert.equal(gatePerSkewNote('0.32.0', false), '', 'no split order, nothing to warn about');
  assert.equal(gatePerSkewNote('', true), '', 'an unknown server is not called old');
  assert.equal(gatePerSkewNote(GATE_PER_SINCE, true), '');
});
