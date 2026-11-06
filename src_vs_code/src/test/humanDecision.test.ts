import assert from 'node:assert/strict';
import { test } from 'node:test';
import { answerChoices, answerJson, DecisionChoice, decisionChoices, FEATURE_BRANCH } from '../escalationAnswer';

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

/** A card as the server writes it, loosely typed: the watcher hands `answerChoices` whatever the file said. */
const card = (over: Record<string, unknown> = {}) => ({
  branch: 'feature/x',
  openFindings: [{ title: 'the poll has no timeout' }],
  kind: 'notice',
  decisions: ['continue', 'fix', 'discuss', 'proceed'],
  ...over,
});

test('the three decisions keep the findings alive, and the fourth says plainly that it does not', () => {
  // The three that keep the findings alive: another pass, a pass after fixing, or a conversation. None of
  // them reads as shipping over open findings. The fourth DOES ship over them — and is only acceptable
  // because it says so in its own words: a button whose consequence is unstated is a button nobody presses twice.
  const labels = decisionChoices().map((c: DecisionChoice) => `${c.label} ${c.detail}`.toLowerCase());
  for (const text of labels) {
    assert.doesNotMatch(text, /ignore|anyway|skip the/, text);
  }
  const fourth = answerChoices(card())[3];
  assert.ok(fourth, 'a notice that lists proceed offers a fourth choice');
  assert.match(fourth.detail, /findings open/i, 'it names the very thing it does');
});

test('a notice that lists proceed offers it as the fourth choice, last, and says what it ships', () => {
  const choices = answerChoices(card({ branch: 'epic-1' }));

  assert.deepEqual(
    choices.map((c: DecisionChoice) => c.decision),
    ['continue', 'fix', 'discuss', 'proceed'],
    'four, and the strongest is last',
  );
  const proceed = choices[3]!;
  assert.equal(proceed.label, 'Proceed despite the findings');
  assert.match(proceed.detail, /stage advances[\s\S]*findings open/i, 'what it causes');
  assert.match(proceed.detail, /epic-1/, 'it names the branch it advances');
});

test('the fourth choice says it covers this one stage change only, not the same finding raised later', () => {
  // A Proceed authorises one transition; it is not a standing waiver (plan §13) — so the person is told, under the button.
  const proceed = answerChoices(card())[3]!;

  assert.match(proceed.detail, /this one stage change only/i, 'the scope of the press');
  assert.match(proceed.detail, /raised again in a later round[\s\S]*counts/i, 'what a later round does with the same finding');
});

test('on a feature review the fourth choice names the review, not a branch no git ref can spell', () => {
  const proceed = answerChoices(card({ branch: FEATURE_BRANCH }))[3];

  assert.ok(proceed, 'the feature notice lists proceed too');
  assert.match(proceed.detail, /feature review/i);
  assert.doesNotMatch(proceed.detail, /:feature/);
});

test('a notice from an older server offers the three', () => {
  // No list: the card was written before the server could honour a fourth decision, and a button it would
  // read as prose is the dead end this module has paid for twice.
  const { decisions: _decisions, ...older } = card();

  assert.deepEqual(answerChoices(older).map((c: DecisionChoice) => c.decision), ['continue', 'fix', 'discuss']);
  assert.deepEqual(
    answerChoices(card({ decisions: ['continue', 'fix', 'discuss'] })).map((c: DecisionChoice) => c.decision),
    ['continue', 'fix', 'discuss'],
    'a list that does not name it does not offer it',
  );
  assert.deepEqual(
    answerChoices(card({ decisions: 'proceed' })).map((c: DecisionChoice) => c.decision),
    ['continue', 'fix', 'discuss'],
    'a list that is not a list offers nothing it did not name',
  );
});

test('a question card never offers proceed, whatever the file lists', () => {
  // The AI words a question card. The strongest decision is never on a card the AI wrote (G4).
  const choices = answerChoices(card({ kind: 'question' }));

  assert.deepEqual(
    choices.map((c: DecisionChoice) => c.decision),
    ['', 'continue', 'fix', 'discuss'],
    'the box first, then the three',
  );
  assert.equal(answerChoices(card({ kind: undefined })).some((c: DecisionChoice) => c.decision === 'proceed'), false);
  assert.equal(answerChoices(card({ kind: 'a kind from the future' })).some((c: DecisionChoice) => c.decision === 'proceed'), false);
});

test("a question card's Continue and Fix say they act on the held gate", () => {
  // D5: a decision pressed on an AI's question still releases the hold. Its consequence is written under it.
  const [, keepGoing, fix, discuss] = answerChoices(card({ kind: 'question' }));
  const [plainKeepGoing, plainFix, plainDiscuss] = decisionChoices('feature/x');

  assert.match(keepGoing!.detail, /for the held gate/i);
  assert.match(fix!.detail, /for the held gate/i);
  assert.equal(discuss!.detail, plainDiscuss!.detail, 'discuss advances nothing, so it needs no warning');
  assert.ok(keepGoing!.detail.startsWith(plainKeepGoing!.detail), 'the promise is the same, with the warning added');
  assert.ok(fix!.detail.startsWith(plainFix!.detail));
  for (const notice of answerChoices(card()).slice(0, 3)) {
    assert.doesNotMatch(notice.detail, /for the held gate/i, 'the notice IS the gate card; it needs no such sentence');
  }
});

test('the written answer carries proceed exactly as it carries the other decisions', () => {
  const json = JSON.parse(answerJson('n1', 'Proceed despite the findings', 'now', 'proceed')) as Record<string, unknown>;

  assert.equal(json['decision'], 'proceed');
  assert.equal(json['answer'], 'Proceed despite the findings');
  assert.deepEqual(Object.keys(json).sort(), ['answer', 'answeredUtc', 'decision', 'id'], 'the file shape is unchanged');
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

test('on a feature review the first choice is the request for its second and last round, and says so', () => {
  // D23 (S3.4 of the feature-review plan): a feature review is one round unless a reviewer failed, a
  // finding was blocking, or the PERSON asks — and this button, on a question filed under the feature
  // session, is the asking. The same three decisions, the same file: only the promise in the detail
  // changes, because "a fresh set of rounds" is not what a feature review grants.
  const feature = decisionChoices(FEATURE_BRANCH);
  const plain = decisionChoices('feature/x');

  assert.deepEqual(feature.map((c: DecisionChoice) => c.decision), plain.map((c: DecisionChoice) => c.decision),
    'the same road: no fourth decision, no new file shape');
  assert.match(feature[0]!.detail, /second and last round/i, 'a feature review is told what "keep going" buys it');
  assert.match(feature[0]!.detail, /at most two/i);
  assert.doesNotMatch(plain[0]!.detail, /second and last/i, 'a branch review keeps its own promise');
  assert.equal(feature[1]!.detail, plain[1]!.detail, 'the other two choices do not change');
  assert.equal(feature[2]!.detail, plain[2]!.detail);
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
