import assert from 'node:assert/strict';
import { test } from 'node:test';

import { controlFrom } from './panelPageHarness';

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
