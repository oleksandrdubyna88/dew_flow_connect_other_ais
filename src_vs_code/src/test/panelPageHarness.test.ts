import assert from 'node:assert/strict';
import { test } from 'node:test';

import { click, controlFrom, panelState, runPanel } from './panelPageHarness';

/**
 * The panel harness is code under test (`generated-code-tests.md` §3): a fake more permissive — or,
 * as this one was, blind — where a real DOM is not, turns a suite green for the wrong reason.
 */

test('a hyphenated data attribute reaches dataset under the name a DOM gives it', () => {
  // The first copy of this harness read only single-word names, so `data-command-model` was simply
  // absent and a control routed on it could never have been written in a test.
  const control = controlFrom('select', ' id="x" data-setting="strongest" data-command-model="codex"');

  assert.deepEqual(control.dataset, { setting: 'strongest', commandModel: 'codex' });
});

test('a single-word data attribute and the value are read as before', () => {
  const control = controlFrom('input', ' type="text" data-setting="consultBaseUrl" data-caller="gemini" value="https://a"');

  assert.deepEqual(control.dataset, { setting: 'consultBaseUrl', caller: 'gemini' });
  assert.equal(control.value, 'https://a');
  assert.equal(control.type, 'text');
  assert.equal(control.tagName, 'INPUT');
});

test('a checkbox starts as the page rendered it — and a data attribute naming "checked" does not tick it', () => {
  // A DOM reads the boolean `checked` attribute; it does not read a substring of some other attribute.
  // The harness must not be more permissive than that, or a box the page drew unticked reads as ticked.
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex" checked').checked, true);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex" checked disabled').checked, true);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex"').checked, false);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="checked" data-vendor="codex"').checked, false);
});

test('a dropdown carries the options the page drew, value and text, in order — and a text box carries none', () => {
  const page = runPanel(panelState('reviewers'));
  const model = page.controls.find((one) => one.tagName === 'SELECT' && one.dataset['setting'] === 'model');
  const path = page.controls.find((one) => one.dataset['setting'] === 'executablePath');

  assert.ok(model !== undefined && model.options.length > 0, 'the model dropdown came back with no options');
  assert.ok(model.options.some((one) => one.value === model.value), 'the selected value is not among the options read');
  assert.deepEqual(path?.options, []);
});

test('a data-command button is bound by the page and a click posts exactly its command and id', () => {
  const page = runPanel(panelState('reviewers'));

  assert.ok(page.commands.some((one) => one.dataset['command'] === 'removeVendor'), 'no command button was read off the page');
  click(page, 'removeVendor', 'codex');

  assert.deepEqual(page.posted.at(-1), { type: 'command', command: 'removeVendor', id: 'codex' });
  assert.throws(() => click(page, 'removeVendor', 'nobody'), /no removeVendor button for nobody/u);
});

test('a box carries the placeholder the page drew, and a data attribute naming it is not one', () => {
  assert.equal(controlFrom('input', ' type="number" data-setting="reviewMinutes" placeholder="20 — calibrated default"').placeholder, '20 — calibrated default');
  assert.equal(controlFrom('input', ' type="number" data-setting="reviewMinutes" data-placeholder="x"').placeholder, '');
});
