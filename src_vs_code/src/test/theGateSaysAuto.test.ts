import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FEATURES } from '../binaryFeatures';
import { workspacePolicyOf } from '../onExhaustedPanel';
import { DEFAULTS } from '../settingsShape';
import { lastWrite, panelState, runPanel, type Page } from './panelPageHarness';

/**
 * Reviews › The gate says what Auto is, and says it when it is not what is running.
 *
 * <p>The page is RUN, not read: the select a person sees, the write a change makes, and the two sentences that name a
 * binary older than Auto and a workspace value that wins over the choice.</p>
 */

function picker(page: Page) {
  const found = page.controls.filter((one) => one.dataset['setting'] === 'onExhausted');
  assert.equal(found.length, 1, 'onExhausted is drawn once');

  return found[0]!;
}

const occurrences = (html: string, text: string): number => html.split(text).length - 1;

test('AutoIsFirstAndDrawnForAPristinePanel', () => {
  const control = picker(runPanel(panelState('gate')));

  assert.equal(control.value, 'auto', 'a pristine panel shows Auto');
  assert.equal(control.options[0]?.value, 'auto', 'and offers it first');
  assert.deepEqual(control.options.map((o) => o.value), ['auto', 'human', 'continue', 'good_enough', 'escalate']);
});

test('PickingAutoWritesAuto', () => {
  const page = runPanel(panelState('gate', { settings: { ...DEFAULTS, onExhausted: 'human' } }));
  const control = picker(page);
  assert.equal(control.value, 'human', 'the control is drawn on what is stored');

  control.value = 'auto';
  control.fire('change');

  assert.deepEqual(
    { key: lastWrite(page)['key'], value: lastWrite(page)['value'] },
    { key: 'onExhausted', value: 'auto' },
  );
});

test('AnOlderBinaryIsNamed', () => {
  const said = (html: string) => occurrences(html, 'predates Auto');

  assert.equal(said(runPanel(panelState('gate', { serverFeatures: [] })).html), 1);
  assert.equal(said(runPanel(panelState('gate', { serverFeatures: undefined })).html), 0, 'not settled is not older');
  assert.equal(said(runPanel(panelState('gate', { serverFeatures: [FEATURES.onExhaustedAuto] })).html), 0);
  assert.equal(
    said(runPanel(panelState('gate', { serverFeatures: [], settings: { ...DEFAULTS, onExhausted: 'human' } })).html),
    0,
    'a person who chose Ask a human is not told about Auto',
  );
});

test('AWorkspaceValueIsNamed', () => {
  const sentence = 'This workspace sets When the rounds run out to Ask a human';

  assert.equal(occurrences(runPanel(panelState('gate', { workspacePolicy: 'human' })).html, sentence), 1);
  assert.equal(occurrences(runPanel(panelState('gate', { workspacePolicy: undefined })).html, sentence), 0);
});

test('AHostileLabelCannotReachThePage', () => {
  assert.equal(workspacePolicyOf({ workspaceValue: '<b>' }, false), undefined, 'only a known choice becomes a sentence');
  assert.equal(workspacePolicyOf({ workspaceFolderValue: '<img src=x onerror=alert(1)>' }, false), undefined);
});
