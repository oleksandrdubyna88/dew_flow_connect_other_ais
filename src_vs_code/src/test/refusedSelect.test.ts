import assert from 'node:assert/strict';
import { test } from 'node:test';

import { saveOrSnapBack, snapsBackWhenRefused, writePlain } from '../refusedWrite';
import { DEFAULTS, settingMessageFrom, settingWrite, type ControlKind } from '../settingsShape';
import { lastWrite, panelState, runPanel, type Page } from './panelPageHarness';

/**
 * A refused DROPDOWN snaps back, as a refused box already does (the follow-up to PR #561).
 *
 * <p>A select posts a string, which the host could not tell from a text field's — and a text field must
 * never be repainted, or a refusal wipes what is being typed. So the page says which control it was, and
 * one rule decides for every kind of write.</p>
 */

function control(page: Page, setting: string) {
  const found = page.controls.filter((one) => one.dataset['setting'] === setting);
  assert.equal(found.length, 1, `${setting} is drawn once`);

  return found[0]!;
}

test('a changed dropdown says it is a select, and nothing else does', () => {
  const page = runPanel(panelState('gate'));
  const picker = control(page, 'onExhausted');
  picker.value = 'continue';
  picker.fire('change');
  assert.equal(lastWrite(page)['control'], 'select', 'the host cannot tell this string from typed text');

  const limits = runPanel(panelState('limits'));
  const number = control(limits, 'maxConcurrency');
  number.value = '3';
  number.fire('change');
  assert.equal('control' in lastWrite(limits), false, 'a number is typed, like text, and must not be repainted');

  const gate = runPanel(panelState('gate'));
  const box = control(gate, 'stopLocalWhenQuiet');
  box.checked = true;
  box.fire('change');
  assert.equal('control' in lastWrite(gate), false, 'a box already snaps back by its value; its write keeps its shape');
});

test('only the literal select crosses from the page, onto every kind of write', () => {
  const base = { key: 'model', value: 'm' };
  assert.equal(settingMessageFrom({ ...base, control: 'select' }).control, 'select');
  for (const forged of ['SELECT', 'text', 1, true, {}]) {
    assert.equal('control' in settingMessageFrom({ ...base, control: forged }), false, `${JSON.stringify(forged)} was trusted`);
  }

  // A split-order picker's key names a slot, and only the two slots route (issue #117).
  const routes = [{}, { vendor: 'grok' }, { role: 'Architecture' }, { caller: 'claude' }, { key: 'strongest', commandModel: 'claude' }];
  for (const route of routes) {
    const write = settingWrite(settingMessageFrom({ ...base, ...route, control: 'select' }));
    assert.ok(write !== undefined, `${JSON.stringify(route)} routes nowhere`);
    assert.equal(write.control, 'select', `a ${write.kind} select write lost what it came from`);
    const typed = settingWrite(settingMessageFrom({ ...base, ...route }));
    assert.equal(typed === undefined ? false : 'control' in typed, false, `a ${typed?.kind} text write claims a control`);
  }
});

test('the rule: a box or a dropdown snaps back, typed text never does', () => {
  assert.equal(snapsBackWhenRefused('continue', 'select'), true);
  assert.equal(snapsBackWhenRefused('typed', undefined), false);
  assert.equal(snapsBackWhenRefused(true), true);
});

/** Runs saveOrSnapBack against fakes and says what happened, in order. */
async function refusedOrNot(saved: boolean, value: unknown, control?: ControlKind): Promise<readonly string[]> {
  const happened: string[] = [];
  const snapped = await saveOrSnapBack(
    async () => { happened.push('save'); return saved; },
    async () => { happened.push('repaint'); },
    value,
    control);
  happened.push(`snapped=${snapped}`);

  return happened;
}

test('every kind of write goes through one save that snaps a refused dropdown back', async () => {
  assert.deepEqual(await refusedOrNot(false, 'continue', 'select'), ['save', 'repaint', 'snapped=true'],
    'a refused dropdown kept showing a choice that was never saved');
  assert.deepEqual(await refusedOrNot(false, 'typed'), ['save', 'snapped=false'], 'refused text was repainted over');
  assert.deepEqual(await refusedOrNot(true, 'continue', 'select'), ['save', 'snapped=false']);
  assert.deepEqual(await refusedOrNot(false, false), ['save', 'repaint', 'snapped=true']);
});

test('a refused plain dropdown repaints and goes no further', async () => {
  const happened: string[] = [];
  await writePlain('onExhausted', 'continue', [], {
    save: async () => { happened.push('save'); return false; },
    repaint: async () => { happened.push('repaint'); },
    follow: async () => { happened.push('follow'); },
  }, 'select');

  assert.deepEqual(happened, ['save', 'repaint']);
});

test('the panel draws a dropdown on what is STORED, which is what the repaint shows', () => {
  // The snap-back is a repaint from the stored configuration, so the page drawn from it must hold the
  // stored choice — read from the option the page marked `selected`, not from anything the test set.
  const drawn = (stored: typeof DEFAULTS.onExhausted) =>
    control(runPanel(panelState('gate', { settings: { ...DEFAULTS, onExhausted: stored } })), 'onExhausted').value;

  assert.equal(drawn('human'), 'human');
  assert.equal(drawn('escalate'), 'escalate', 'the dropdown was drawn on some other choice than the stored one');
});

test('the host saves every non-plain kind through saveOrSnapBack', async () => {
  // Only the wiring is read: the host imports `vscode`. What the helper DOES is run above.
  const { readFileSync } = await import('node:fs');
  const { join } = await import('node:path');
  const host = readFileSync(join(process.cwd(), 'src', 'panelProvider.ts'), 'utf8');

  for (const stored of ["'vendors'", 'write.key', "'consultants'", "'commandModels'"]) {
    // A substring, not a RegExp built from text: nothing here needs a pattern (CodeQL, on the PR).
    assert.ok(host.includes(`await this.saveWrite(config, ${stored}, `), `a ${stored} write is saved without the snap-back`);
  }
  assert.match(host, /await writePlain\(write\.key, write\.value, clearedByWriting\(write\.key\), \{[\s\S]*?\}, write\.control\);/,
    'the plain case no longer hands writePlain the control');
  // Each link pinned whole (PR review): the control reaching the rule, the repaint STARTED rather than
  // awaited inside the queued write (`snapBackQueue.test.ts` runs why), and the paint key cleared, without
  // which the repaint finds nothing changed and paints nothing.
  assert.match(host,
    /await saveOrSnapBack\(\(\) => this\.save\(config, key, stored\), afterTheWrite\(\(\) => this\.snapBack\(\)\), write\.value, write\.control\);/,
    'a composite write drops the control or awaits its repaint inside the write queue');
  assert.match(host, /repaint: afterTheWrite\(\(\) => this\.snapBack\(\)\),/,
    'the plain case awaits its repaint inside the write queue, which is a wait on itself');
  assert.match(host, /private snapBack\(\): Promise<void> \{\s*this\.paintedKey = '';\s*return this\.render\(\);/,
    'the snap-back no longer clears the paint key, so the page is never rebuilt from what is stored');
  assert.match(host, /catch \(error: unknown\) \{\s*\/\/[^\n]*\n\s*reportRefusal\(this\.context, 'promptsPerRound', error\);\s*await afterTheWrite\(\(\) => this\.snapBack\(\)\)\(\);/,
    'a refused prompt pick is neither said nor put back — or its repaint is awaited inside the write queue');
  // A prompt pick is a setting write like any other, so it is serialised with them (the gate's code round).
  assert.match(host, /this\.enqueue\(\(\) => this\.choosePrompt\(role, round, String\(m\.value\)\)\)/,
    'a prompt pick writes outside the write queue, so it can race the writes queued beside it');
});
