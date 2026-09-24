import assert from 'node:assert/strict';
import { test } from 'node:test';

import { STOP_LOCAL_SINCE, stopLocalSkewNote } from '../gateScope';
import { DEFAULTS, envBlock, settingMessageFrom, settingWrite, settingsFrom } from '../settingsShape';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * The switch that lets the local reviewer stand down after a quiet cloud (issue #485): off by default,
 * one env key when on, a box in *The gate* — RUN — and the note an older server earns.
 */

test('it is off by default and sends nothing', () => {
  assert.equal(DEFAULTS.stopLocalWhenQuiet, false);
  assert.equal(envBlock(DEFAULTS)['COAI_STOP_LOCAL_WHEN_QUIET'], undefined);
});

test('switched on, it crosses as COAI_STOP_LOCAL_WHEN_QUIET=true, and only true is on', () => {
  assert.equal(envBlock({ ...DEFAULTS, stopLocalWhenQuiet: true })['COAI_STOP_LOCAL_WHEN_QUIET'], 'true');
  for (const stored of [undefined, 'true', 1, null]) {
    assert.equal(settingsFrom((key) => (key === 'stopLocalWhenQuiet' ? stored : undefined)).stopLocalWhenQuiet, false, String(stored));
  }
});

test('ticking the box in The gate writes coai.stopLocalWhenQuiet', () => {
  const page = runPanel(panelState('gate'));
  const box = page.controls.filter((one) => one.dataset['setting'] === 'stopLocalWhenQuiet');
  assert.equal(box.length, 1, 'the box is drawn once in the gate section');

  box[0]!.checked = true;
  box[0]!.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), { kind: 'plain', key: 'stopLocalWhenQuiet', value: true });
});

test('an older server is named only while the switch is on', () => {
  assert.match(stopLocalSkewNote('0.33.0', true), /does not read this switch/);
  assert.equal(stopLocalSkewNote('0.33.0', false), '');
  assert.equal(stopLocalSkewNote('', true), '', 'an unknown server is not called old');
  assert.equal(stopLocalSkewNote(STOP_LOCAL_SINCE, true), '');
});
