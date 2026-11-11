import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { AskedSet, askedAsText } from '../claudeQuestion';
import type { Waiting, WaitingSession } from '../claudeSessions';
import { bodyOf, sourceOf } from './sourceReading';
import type { RemoteAnswer } from '../remoteRead';
import { questionSaid, waitingSaid } from '../waitingSaid';

/**
 * The words the "take the question" doors say, moved out of `chatCommand.ts` (E2.S4, contract C5).
 *
 * <p>The local path (a tab) and the remote path (a terminal) both call `waitingSaid`, so the same situation has the same
 * sentence on both roads. Every sentence below is typed here as the text that was in `questionWaitingHere` before the move:
 * this test is the byte-for-byte check, and a changed word fails it by name.</p>
 */

const SET: AskedSet = {
  id: 'toolu_1',
  questions: [{ header: 'Which', question: 'Which database?', multiSelect: false, options: [{ label: 'SQLite', description: 'a file' }, { label: 'Postgres', description: '' }] }],
  answered: false,
  sessionId: 'session-1',
  at: '2026-11-11T10:00:00.000Z',
  names: { latest: 'kb', former: [] },
};

const session = (file: string): WaitingSession => ({ file, asked: SET });

const SEVERAL = 'More than one Claude Code session here is waiting for an answer. Close the ones you do not mean, and press it again.';
const ELSEWHERE = 'A Claude Code session here is waiting for an answer, but it is not the one this tab is showing. Open that conversation and press it there.';
const ANSWERED = 'The last question in this session has already been answered — there is nothing waiting.';
const NOTHING = 'Claude Code has asked nothing in this folder yet.';

test('S4-T1: one waiting question is handed over as its text', () => {
  const said = waitingSaid([{ kind: 'one', session: session('a.jsonl') }]);

  assert.equal(said.refusal, '');
  assert.equal(said.text, askedAsText(SET), 'the question was not handed over as its text');
  assert.ok(said.text.includes('Which database?'), 'the question text is empty');
});

test('S4-T1: two waiting sessions, in one folder or in two, are one refusal and no text', () => {
  const two: readonly Waiting[] = [{ kind: 'one', session: session('a') }, { kind: 'one', session: session('b') }];
  const several: readonly Waiting[] = [{ kind: 'several', sessions: [session('a'), session('b')] }];

  for (const found of [two, several]) {
    assert.deepEqual(waitingSaid(found), { text: '', refusal: SEVERAL }, 'two waiting sessions were not refused in these words');
  }
});

test('S4-T1: a question waiting elsewhere, an answered one, a failed read and nothing asked each have their own sentence', () => {
  assert.deepEqual(waitingSaid([{ kind: 'elsewhere', sessions: [session('a')] }]), { text: '', refusal: ELSEWHERE }, 'the elsewhere sentence changed');
  assert.deepEqual(waitingSaid([{ kind: 'answered', session: session('a') }]), { text: '', refusal: ANSWERED }, 'the answered sentence changed');
  assert.deepEqual(waitingSaid([{ kind: 'failed', refusal: 'the folder is unreadable' }]), { text: '', refusal: 'the folder is unreadable' }, 'a failed read lost its own sentence');
  assert.deepEqual(waitingSaid([{ kind: 'none' }]), { text: '', refusal: NOTHING }, 'the nothing-asked sentence changed');
  assert.deepEqual(waitingSaid([]), { text: '', refusal: NOTHING }, 'no folder at all was not "nothing asked"');
});

test('S4-T1: the order of the cases is the order of the old body', () => {
  // A waiting question wins over a failed folder next to it (one folder of a multi-root window failing is not the answer);
  // "several" beats a waiting one; "elsewhere" beats "answered"; "answered" beats a failed read.
  assert.equal(waitingSaid([{ kind: 'failed', refusal: 'x' }, { kind: 'one', session: session('a') }]).text, askedAsText(SET), 'a failed folder hid the waiting question of another');
  assert.equal(waitingSaid([{ kind: 'one', session: session('a') }, { kind: 'several', sessions: [session('b'), session('c')] }]).refusal, SEVERAL);
  assert.equal(waitingSaid([{ kind: 'answered', session: session('a') }, { kind: 'elsewhere', sessions: [session('b')] }]).refusal, ELSEWHERE);
  assert.equal(waitingSaid([{ kind: 'failed', refusal: 'x' }, { kind: 'answered', session: session('a') }]).refusal, ANSWERED);
  assert.equal(waitingSaid([{ kind: 'none' }, { kind: 'failed', refusal: 'x' }]).refusal, 'x');
});

test('S4-T1: the body of questionWaitingHere calls it, and holds none of the sentences', () => {
  const body = bodyOf(sourceOf('chatCommand.ts'), 'async function questionWaitingHere(');

  assert.ok(body.includes('waitingSaid('), 'questionWaitingHere no longer calls waitingSaid, so the two roads can drift apart');
  for (const sentence of ['More than one Claude Code session', 'has already been answered', 'asked nothing in this folder', 'it is not the one this tab']) {
    assert.ok(!body.includes(sentence), `questionWaitingHere still holds its own copy of “${sentence}…”`);
  }
});

const asks = (id: string): string => JSON.stringify({
  type: 'assistant',
  sessionId: 's',
  timestamp: '2026-11-11T09:00:00.000Z',
  message: { content: [{ type: 'tool_use', id, name: 'AskUserQuestion', input: { questions: [{
    header: 'Scope', question: 'How far should this go?', multiSelect: false, options: [{ label: 'All of it', description: 'every part' }],
  }] } }] },
});
const answers = (id: string): string => JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: id, content: 'All of it' }] } });
const said = (text: string): string => JSON.stringify({ type: 'user', message: { content: text }, origin: { kind: 'human' } });
const lines = (...rows: readonly string[]): RemoteAnswer => ({ kind: 'lines', lines: rows, note: '' });

test('S4-T4: lines with a waiting question are that question as text', () => {
  const taken = questionSaid(lines(said('start'), asks('q1')));

  assert.equal(taken.refusal, '');
  assert.ok(taken.text.includes('How far should this go?') && taken.text.includes('All of it — every part'), 'the question and its option were not handed over');
});

test('S4-T4: lines with no question, a tail that was whole, say nothing was asked — in the local words', () => {
  assert.deepEqual(questionSaid(lines(said('start'))), { text: '', refusal: NOTHING }, 'a session with no question was not "asked nothing"');
  assert.deepEqual(questionSaid(lines()), { text: '', refusal: NOTHING }, 'an empty tail was not "asked nothing"');
});

test('S4-T4: a question that was answered, and one this build cannot read, each have their own sentence', () => {
  assert.deepEqual(questionSaid(lines(asks('q1'), answers('q1'))), { text: '', refusal: ANSWERED }, 'an answered question was handed over or called nothing');
  const strange = JSON.stringify({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'a', name: 'AskUserQuestion', input: { prompts: [] } }] } });
  const unreadable = questionSaid(lines(strange));

  assert.equal(unreadable.text, '');
  assert.ok(unreadable.refusal.startsWith('Claude Code asked something this build does not recognise'), 'a question this build could not read was called silence: ' + unreadable.refusal);
  assert.ok(unreadable.refusal.endsWith('this needs a new release.'), 'the unreadable sentence lost its second half');
});

test('S4-T4: a refusal of the road is handed on unchanged, the truncated sentence among them', () => {
  const truncated = 'No question was found in the last 4 MiB of the session on “h” that were read; an older one may be beyond the cut.';
  for (const refusal of [truncated, 'Reading the Claude session on “h” is turned off.', 'No session was chosen, so nothing was read.']) {
    assert.deepEqual(questionSaid({ kind: 'refused', refusal }), { text: '', refusal }, 'a refusal of the road was changed on its way to the person');
  }
});
