import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseSession, renderRounds, renderSession, SessionFile } from '../rounds';

const session = (over: Partial<SessionFile['state']> = {}, rounds: SessionFile['rounds'] = []): SessionFile => ({
  state: {
    sessionId: 'abc123',
    repoPath: 'D:/rsd/thing',
    branch: 'feature/x',
    stage: 'CodeReview',
    awaitingResolve: false,
    ...over,
  },
  rounds,
});

test('no sessions renders an honest empty state, not an error', () => {
  const view = renderRounds([]);
  assert.ok(view.includes('No sessions yet'));
  assert.ok(view.includes('`open`'), 'it says what would create one');
});

test('a session with no rounds says so rather than rendering an empty table', () => {
  assert.ok(renderSession(session()).includes('No rounds yet'));
});

test('rounds render as a table with the verdict and the honest reviewer count', () => {
  const view = renderSession(
    session({}, [
      {
        stage: 'PlanReview',
        number: 1,
        verdict: 'revise',
        gatingCount: 3,
        reviewers: '4 of 6 reviewers answered; failed: gemini/UxDxPerformance: timeout',
        completedUtc: '2026-08-31T12:00:00Z',
      },
    ]),
  );
  assert.ok(view.includes('| PlanReview | 1 | `revise` | 3 |'));
  assert.ok(view.includes('4 of 6'), 'a partial round must not read as a full panel');
  assert.ok(view.includes('feature/x'));
});

test('awaiting resolve is visible in the heading', () => {
  assert.ok(renderSession(session({ awaitingResolve: true })).includes('awaiting resolve'));
});

test('sessions are ordered by most recent activity', () => {
  const older = session({ branch: 'old' }, [
    { stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 2', completedUtc: '2026-08-01T00:00:00Z' },
  ]);
  const newer = session({ branch: 'new' }, [
    { stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 2', completedUtc: '2026-08-30T00:00:00Z' },
  ]);
  const view = renderRounds([older, newer]);
  assert.ok(view.indexOf('## new') < view.indexOf('## old'));
});

test('a torn or foreign file is skipped, never a crash', () => {
  assert.equal(parseSession('{not json'), undefined);
  assert.equal(parseSession('{"something": "else"}'), undefined);
  assert.notEqual(parseSession(JSON.stringify(session())), undefined);
});
