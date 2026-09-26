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

test('the switch says it takes effect from the NEXT round', () => {
  // Operator, 2026-09-26: the box was ticked while a round ran and the local reviewer went on. The round
  // reads the switch when it STARTS, so a round already running keeps the value it began with — and the
  // panel says so beside the box instead of letting the tick look like it acted at once.
  const page = runPanel(panelState('gate'));
  const at = page.html.indexOf('data-setting="stopLocalWhenQuiet"');
  const field = page.html.slice(at, page.html.indexOf('</div>', at));

  assert.match(field, /from the next round/, 'nothing beside the box says when it takes effect');
});

test('a refused CHECKBOX is repainted to what is stored; a refused text field is not', async () => {
  // 2026-09-25: the tick was refused (the window had not caught up with an update) and the box stayed
  // ticked, so "saved" was on screen with nothing saved. A box has nothing typed to lose, so it snaps
  // back; a text field does not, or the refusal would wipe what the person was writing.
  const { snapsBackWhenRefused } = await import('../refusedWrite');

  assert.equal(snapsBackWhenRefused(true), true);
  assert.equal(snapsBackWhenRefused(false), true);
  for (const value of ['typed text', '', 3, ['a'], { a: 1 }, undefined]) {
    assert.equal(snapsBackWhenRefused(value), false, `${JSON.stringify(value)} would be repainted over`);
  }
});

test('the panel repaints after a refused plain write that snaps back, and save says whether it saved', async () => {
  // The host imports `vscode`, so its wiring is read — each link pinned whole, in its own place.
  const { readFileSync } = await import('node:fs');
  const { join } = await import('node:path');
  const host = readFileSync(join(process.cwd(), 'src', 'panelProvider.ts'), 'utf8');

  assert.match(host,
    /const saved = await this\.save\(config, write\.key, write\.value\);\s*if \(!saved && snapsBackWhenRefused\(write\.value\)\) \{\s*await this\.render\(\);\s*return;/,
    'a refused checkbox is not repainted, so the box keeps claiming a value that was never saved');
  assert.match(host,
    /catch \(error: unknown\) \{\s*reportRefusal\(this\.context, key, error\);\s*return false;/,
    'save no longer says it failed, so no caller can act on a refusal');
});
