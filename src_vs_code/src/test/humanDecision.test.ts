import assert from 'node:assert/strict';
import { test } from 'node:test';
import { answerJson, DecisionChoice, decisionChoices } from '../escalationAnswer';

/**
 * "Proceed anyway, or fix the findings and review again?" has two answers.
 *
 * <p>It was asked with a free-text input box, which is the control for a question the AI wrote in
 * words — and this one is not that. A person typing a sentence into it got no error and no effect:
 * the file was written and, for a `call_human` notice, nothing on either side ever read it. So the
 * card disappeared and nothing had changed, which is a worse dead end than never being asked,
 * because it looks like it worked.</p>
 */

test('a decision is offered as choices, not as a blank box', () => {
  const choices = decisionChoices();

  assert.equal(choices.length, 3);
  assert.deepEqual(
    choices.map((c: DecisionChoice) => c.decision),
    ['continue', 'fix', 'discuss'],
  );
});

test('none of the choices ships a change over open findings', () => {
  // A human override meaning "ignore all this" is an off switch on the gate. All three keep the
  // findings alive: another pass, a pass after fixing, or a conversation.
  const labels = decisionChoices().map((c: DecisionChoice) => `${c.label} ${c.detail}`.toLowerCase());
  for (const text of labels) {
    assert.doesNotMatch(text, /ignore|anyway|skip the/, text);
  }
});

test('each choice says what it will cause, not just what it is called', () => {
  for (const choice of decisionChoices()) {
    assert.ok(choice.label.length > 3, 'a label');
    assert.ok(choice.detail.length > 30, `${choice.label} does not say what happens next`);
  }
  const [carryOn, fix, discuss] = decisionChoices();
  assert.match(carryOn!.detail, /fresh set of rounds/i, 'continuing must say what it grants');
  assert.match(fix!.detail, /findings[\s\S]*again/i, 'the fix branch must say the review runs again');
  assert.match(discuss!.detail, /nothing advances/i, 'and stopping must say that it stops');
});

test('the written answer carries the decision, so the server can act on it', () => {
  const json = JSON.parse(answerJson('q1', 'Keep going', 'now', 'continue')) as Record<string, unknown>;

  assert.equal(json['id'], 'q1');
  assert.equal(json['decision'], 'continue');
  assert.equal(json['answer'], 'Keep going', 'their words travel too — the AI reads them');
});

test('a typed sentence is an answer with no decision, and must not advance anything', () => {
  const json = JSON.parse(answerJson('q1', 'looks fine to me', 'now')) as Record<string, unknown>;

  assert.equal(json['decision'], '');
  assert.equal(json['answer'], 'looks fine to me');
});
