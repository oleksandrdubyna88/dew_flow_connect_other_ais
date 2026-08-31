import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  answerJson,
  Escalation,
  modalText,
  parseEscalation,
  renderEscalations,
  shouldPrompt,
  statusBarText,
} from '../escalations';

const escalation = (over: Partial<Escalation> = {}): Escalation => ({
  id: 'abc123',
  sessionId: 's-1',
  repoPath: 'D:/repo',
  branch: 'feature/x',
  question: 'Two findings still gate after three rounds. Ship anyway?',
  openFindings: [
    { severity: 'blocking', category: 'security', file: 'src/A.cs', line: 7, title: 'token compared with ==' },
  ],
  askedUtc: '2026-08-31T15:00:00Z',
  ...over,
});

test('a question file parses; anything else is skipped rather than guessed at', () => {
  assert.deepEqual(parseEscalation(JSON.stringify(escalation()))?.id, 'abc123');
  assert.equal(parseEscalation('{ half-written'), undefined);
  assert.equal(parseEscalation('{"id":"x"}'), undefined, 'no question is not a question');
  assert.equal(parseEscalation('{"id":"x","question":""}'), undefined, 'an empty question asks nothing');
});

test('a question with no findings still parses, with an empty list', () => {
  const parsed = parseEscalation('{"id":"x","question":"ship?"}');
  assert.deepEqual(parsed?.openFindings, []);
});

test('the answer file is exactly what the server parses', () => {
  const parsed = JSON.parse(answerJson('abc123', 'no, fix it first', '2026-08-31T15:05:00Z')) as {
    id: string;
    answer: string;
    answeredUtc: string;
  };
  assert.equal(parsed.id, 'abc123');
  assert.equal(parsed.answer, 'no, fix it first');
  assert.equal(parsed.answeredUtc, '2026-08-31T15:05:00Z');
});

test('the status bar hides itself at zero and counts plainly otherwise', () => {
  assert.equal(statusBarText(0), '', 'a status-bar item that says 0 is furniture');
  assert.match(statusBarText(1), /1 question$/);
  assert.match(statusBarText(3), /3 questions$/);
});

test('a modal is raised once per question, never again after dismissal', () => {
  const prompted = new Set<string>();
  assert.equal(shouldPrompt('abc123', prompted, false), true);
  prompted.add('abc123');
  assert.equal(
    shouldPrompt('abc123', prompted, false),
    false,
    'a modal that reappears every poll cannot be dismissed',
  );
});

test('an already answered question never raises a modal', () => {
  assert.equal(shouldPrompt('abc123', new Set(), true), false);
});

test('the modal shows what is still gating, not just the question', () => {
  const text = modalText(escalation());
  assert.ok(text.includes('Ship anyway?'));
  assert.ok(text.includes('token compared with =='), 'deciding on a summary is deciding blind');
  assert.ok(text.includes('src/A.cs:7'));
  assert.ok(text.includes('feature/x'));
});

test('a question with no findings reads cleanly, with no empty heading', () => {
  const text = modalText(escalation({ openFindings: [] }));
  assert.ok(!text.includes('Still gating'));
  assert.ok(text.includes('Ship anyway?'));
});

test('the rounds view renders open questions, and nothing when there are none', () => {
  assert.equal(renderEscalations([]), '', 'no section at all when nothing is waiting');
  const rendered = renderEscalations([escalation()]);
  assert.ok(rendered.includes('a review is waiting on you'));
  assert.ok(rendered.includes('Ship anyway?'));
  assert.ok(rendered.includes('token compared with =='));
  assert.ok(rendered.includes('feature/x'));
});

test('a question with no findings says so rather than showing an empty list', () => {
  assert.ok(renderEscalations([escalation({ openFindings: [] })]).includes('No findings attached'));
});
