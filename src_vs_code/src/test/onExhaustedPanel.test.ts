import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FEATURES } from '../binaryFeatures';
import { gateNotes, labelOf, ON_EXHAUSTED_CHOICES, workspacePolicyOf } from '../onExhaustedPanel';

/** What the gate section says about When the rounds run out beside the choice itself — pure, so each sentence is a value. */

test('Auto is the first choice, and every choice has a label', () => {
  assert.equal(ON_EXHAUSTED_CHOICES[0]?.value, 'auto');
  assert.deepEqual(
    ON_EXHAUSTED_CHOICES.map((c) => c.value),
    ['auto', 'human', 'continue', 'good_enough', 'escalate'],
  );
  for (const choice of ON_EXHAUSTED_CHOICES) {
    assert.notEqual(labelOf(choice.value), '', choice.value);
  }
  assert.equal(labelOf('auto'), 'Auto', 'the sentence about a workspace names Auto by its word, not by the option text');
  assert.equal(labelOf('human'), 'Ask a human');
});

test('a folder value wins over a workspace value', () => {
  assert.equal(workspacePolicyOf({ workspaceValue: 'continue', workspaceFolderValue: 'human' }, false), 'human');
  assert.equal(workspacePolicyOf({ workspaceValue: 'continue' }, false), 'continue');
});

test('Auto, an unknown word, a non-string, or this side holding the key is no workspace policy', () => {
  assert.equal(workspacePolicyOf({ workspaceValue: 'auto' }, false), undefined, 'a workspace Auto agrees with the default');
  assert.equal(workspacePolicyOf({ workspaceValue: '<b>' }, false), undefined);
  assert.equal(workspacePolicyOf({ workspaceValue: 7 }, false), undefined);
  assert.equal(workspacePolicyOf({}, false), undefined);
  assert.equal(workspacePolicyOf({ workspaceValue: 'human' }, true), undefined, 'this side overlay wins over a workspace value');
  assert.equal(workspacePolicyOf({ workspaceFolderValue: 'human', workspaceValue: 'continue' }, true), undefined);
});

test('a folder value is the one in force, so a folder Auto hides a workspace policy', () => {
  assert.equal(workspacePolicyOf({ workspaceFolderValue: 'auto', workspaceValue: 'human' }, false), undefined);
  assert.equal(workspacePolicyOf({ workspaceFolderValue: 'nonsense', workspaceValue: 'human' }, false), undefined);
});

test('the gate notes are at most two, the older server first', () => {
  const both = gateNotes('auto', [], 'human');
  assert.equal(both.length, 2);
  assert.match(both[0]!, /predates Auto/u);
  assert.match(both[1]!, /This workspace sets When the rounds run out to Ask a human in its \.vscode\/settings\.json/u);

  assert.deepEqual(gateNotes('auto', undefined, undefined), [], 'not settled is not older');
  assert.deepEqual(gateNotes('auto', [FEATURES.onExhaustedAuto], undefined), []);
  assert.deepEqual(gateNotes('human', [], undefined), [], 'a person who chose another policy is not told about Auto');
  assert.equal(gateNotes('human', [], 'continue').length, 1);
});
