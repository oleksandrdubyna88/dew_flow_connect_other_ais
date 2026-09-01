import assert from 'node:assert/strict';
import { test } from 'node:test';
import { renderSession, RoundRecord, SessionFile } from '../rounds';

/**
 * Every round says WHEN it ran, and the newest is at the top.
 *
 * <p>Asked for from the rounds view, and the reason is plain once you open the file: rows arrived in
 * whatever order the session file happened to hold, and nothing on the row said which day it was.
 * A table you have to date by counting downwards is a table you read wrong.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'PlanReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 0,
    reviewers: 'all 2 reviewers answered',
    completedUtc: '2026-09-01T14:05:00Z',
    status: 'done',
    ...over,
  };
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: {
      repoPath: 'D:/repo',
      branch: 'main',
      stage: 'PlanReview',
      sessionId: 'abc123',
      awaitingResolve: false,
    },
    rounds,
  } as SessionFile;
}

test('each round carries the date and time it finished', () => {
  const table = renderSession(session([round({ completedUtc: '2026-09-01T14:05:00Z' })]));

  assert.match(table, /\| When \|/, 'the column is missing from the header');
  assert.match(table, /2026-09-01 14:05/, 'the row does not say when it ran');
});

test('the newest round is the first row', () => {
  // The order in the FILE is the order rounds were appended, which is oldest first — and a session
  // that was resumed can hold them in neither order. What a reader wants at the top is the last
  // thing that happened.
  const table = renderSession(session([
    round({ number: 1, completedUtc: '2026-09-01T10:00:00Z', subject: 'oldest' }),
    round({ number: 3, completedUtc: '2026-09-01T18:00:00Z', subject: 'newest' }),
    round({ number: 2, completedUtc: '2026-09-01T14:00:00Z', subject: 'middle' }),
  ]));

  const order = [...table.matchAll(/\| (oldest|middle|newest) \|/g)].map((m) => m[1]);
  assert.deepEqual(order, ['newest', 'middle', 'oldest']);
});

test('a round still running is dated by when it STARTED', () => {
  // It has no completion time yet, and an empty cell in the column everything is sorted by is the
  // one row a reader cannot place.
  const table = renderSession(session([
    round({ status: 'running', verdict: 'running', completedUtc: '', startedUtc: '2026-09-01T20:30:00Z' }),
  ]));

  assert.match(table, /2026-09-01 20:30/);
});

test('a round with no time at all still renders a row', () => {
  // Files written by an older server have no `startedUtc`, and a missing time must not drop the
  // round out of the table or push it somewhere misleading.
  const table = renderSession(session([
    round({ number: 7, completedUtc: '', subject: 'undated' }),
  ]));

  assert.match(table, /\| undated \|/);
  assert.match(table, /\| — \|/, 'an unknown time should read as a dash, not as an empty cell');
});

test('an undated round sorts last, never above something known', () => {
  const table = renderSession(session([
    round({ completedUtc: '', subject: 'undated' }),
    round({ completedUtc: '2026-09-01T09:00:00Z', subject: 'dated' }),
  ]));

  const order = [...table.matchAll(/\| (dated|undated) \|/g)].map((m) => m[1]);
  assert.deepEqual(order, ['dated', 'undated']);
});

test('the delimiter still matches the header, which is what makes it a table', () => {
  // The column count changed, and the last time that happened the hand-written delimiter row did
  // not — so the whole block rendered as one paragraph of pipes.
  const table = renderSession(session([round()]));
  const header = table.split('\n').find((l) => l.startsWith('| When'))!;
  const delimiter = table.split('\n').find((l) => l.startsWith('|---'))!;

  assert.equal(delimiter.split('|').length, header.split('|').length);
});
