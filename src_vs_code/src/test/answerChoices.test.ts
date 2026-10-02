import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answerChoices, decisionChoices } from '../escalationAnswer';

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
