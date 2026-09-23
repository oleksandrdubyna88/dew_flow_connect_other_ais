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
