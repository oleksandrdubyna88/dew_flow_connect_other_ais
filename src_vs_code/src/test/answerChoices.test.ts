import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FEATURE_BRANCH, answerChoices, decisionChoices } from '../escalationAnswer';

/**
 * Answer… on Active questions' card (todo/PLAN_question_consultant.md, S4): an AI's QUESTION is answered in words,
 * so the box for them comes FIRST — and a gate that ran out of rounds is still a choice, as it always was.
 */

test('a question an AI asked — no findings gating — offers a box for your own words first', () => {
  const choices = answerChoices({ branch: 'feat/x', openFindings: [] });

  assert.equal(choices[0]?.decision, '', 'the first thing offered is not the free-text answer');
  assert.match(choices[0]!.label, /type/i);
  assert.deepEqual(choices.slice(1), decisionChoices('feat/x'), 'and the decisions after it, unchanged');
});

test('a gate verdict with findings still gating is the three decisions, as before', () => {
  const finding = { severity: 'blocking', category: 'security', file: 'a.cs', line: 1, title: 'x' };

  assert.deepEqual(answerChoices({ branch: 'feat/x', openFindings: [finding] }), decisionChoices('feat/x'));
});

/**
 * The card says which producer wrote it (research/PLAN_ask_human_is_for_the_gate.md, G4) — the extension no longer guesses
 * from the findings. An AI's question asked on a held gate offers words first even with findings gating; a round's
 * call_human notice offers the decisions ONLY, because no AI waits on a notice for words (the cadence consultation
 * b6e9df3c found a typed answer there went nowhere).
 */
test('a question card offers your own words first even when findings are gating', () => {
  const finding = { severity: 'blocking', category: 'security', file: 'a.cs', line: 1, title: 'x' };
  const choices = answerChoices({ branch: 'feat/x', openFindings: [finding], kind: 'question' });

  assert.equal(choices[0]?.decision, '', 'the first thing offered is the free-text answer');
  assert.deepEqual(choices.slice(1), decisionChoices('feat/x'), 'and the decisions after it, which still release the hold');
  assert.match(choices[0]!.detail, /decide nothing/, 'the box says words decide nothing for the review — the card would otherwise leave with nothing changed');
});

test('a notice offers the three decisions only, even with no findings attached', () => {
  assert.deepEqual(answerChoices({ branch: 'feat/x', openFindings: [], kind: 'notice' }), decisionChoices('feat/x'));
});

test('a question card on a feature review offers the box first and the feature-review decisions after it', () => {
  const choices = answerChoices({ branch: FEATURE_BRANCH, openFindings: [], kind: 'question' });

  assert.equal(choices[0]?.decision, '');
  assert.deepEqual(choices.slice(1), decisionChoices(FEATURE_BRANCH), 'the second-round wording, not the code stage wording');
});

test('an empty kind is an older card, judged by its findings as before', () => {
  const choices = answerChoices({ branch: 'feat/x', openFindings: [], kind: '' });

  assert.equal(choices[0]?.decision, '');
  assert.doesNotMatch(choices[0]!.detail, /decide nothing/, 'an older card says nothing about a review it may not belong to');
});

/**
 * A kind this build does not know comes from a NEWER server. Taking the box away from it would bring back the very
 * symptom of 2026-10-03 — the gate's buttons and no words — so it is judged as a card with no kind (the own review).
 */
test('an unknown kind falls back to the findings rule, never to the decisions alone', () => {
  const finding = { severity: 'blocking', category: 'security', file: 'a.cs', line: 1, title: 'x' };

  assert.equal(answerChoices({ branch: 'feat/x', openFindings: [], kind: 'reply' })[0]?.decision, '', 'no findings: a question, in words');
  assert.deepEqual(answerChoices({ branch: 'feat/x', openFindings: [finding], kind: 'reply' }), decisionChoices('feat/x'), 'findings gating: the decisions');
});
