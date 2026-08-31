import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  costPhrase,
  elapsed,
  isRunning,
  parseSession,
  renderRounds,
  renderSession,
  reviewerLines,
  RoundRecord,
  SessionFile,
} from '../rounds';

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
  assert.ok(view.includes('| PlanReview | 1 | ✔ done | `revise` | 3 |'));
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

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'CodeReview',
  number: 1,
  verdict: 'revise',
  gatingCount: 2,
  reviewers: 'all 6 reviewers answered',
  completedUtc: '2026-08-31T12:10:00Z',
  ...over,
});

test('a running round is shown as running, with the reviewers still in flight', () => {
  // The operator's report: a ten-minute round showed nothing at all while it ran.
  const view = renderSession(
    session({}, [
      round({
        status: 'running',
        verdict: 'running',
        startedUtc: '2026-08-31T12:00:00Z',
        reviewerStates: [
          { provider: 'codex', role: 'Architecture', status: 'done', findings: 3, note: '' },
          { provider: 'codex', role: 'SecurityReliability', status: 'running', findings: 0, note: '' },
          { provider: 'claude', role: 'Architecture', status: 'failed', findings: 0, note: 'timeout' },
        ],
      }),
    ]),
    Date.parse('2026-08-31T12:02:30Z'),
  );

  assert.ok(view.includes('⏳ running'), 'the status column says it is alive');
  assert.ok(view.includes('2m 30s'), 'and how long it has been going');
  assert.ok(view.includes('In flight now'));
  assert.ok(view.includes('codex/Architecture — done (3 findings)'));
  assert.ok(view.includes('codex/SecurityReliability — running'));
  assert.ok(view.includes('claude/Architecture — failed (timeout)'), 'a failure says why while the round is open');
});

test('tokens and money are shown per round, and an unpriced vendor is not called free', () => {
  const view = renderSession(session({}, [round({ tokensIn: 29364, tokensOut: 304, costUsd: 0.0489 })]));
  assert.ok(view.includes('29k in / 304 out'));
  assert.ok(view.includes('$0.0489'), 'a round costs fractions of a dollar, so cents are not enough');

  const silent = renderSession(session({}, [round({ tokensIn: 5300, tokensOut: 260, costUsd: null })]));
  assert.ok(silent.includes('no cost reported'), 'a vendor that does not price its run is unknown, not free');
  assert.ok(!silent.includes('$0.00'));
});

test('a round abandoned by a dead server reads as interrupted, never as running forever', () => {
  const view = renderSession(session({}, [round({ status: 'interrupted', verdict: 'interrupted' })]));
  assert.ok(view.includes('⚠️ interrupted'));
});

test('the whole view totals what has been spent', () => {
  const view = renderRounds([
    session({}, [round({ tokensIn: 1000, tokensOut: 100, costUsd: 0.01 })]),
    session({ branch: 'other' }, [round({ tokensIn: 2000, tokensOut: 200, costUsd: 0.02 })]),
  ]);
  assert.ok(view.includes('Across every round here: 3.0k in / 300 out · $0.0300'));
});

test('a file from an older server, with no status or usage, still renders', () => {
  // Forward compatibility in the direction that actually happens: the extension updates first.
  const view = renderSession(session({}, [round()]));
  assert.ok(view.includes('✔ done'));
  assert.ok(view.includes('no usage reported'));
});

test('isRunning, elapsed and reviewerLines are honest about missing data', () => {
  assert.equal(isRunning(round()), false);
  assert.equal(elapsed(round(), Date.now()), '', 'a round with no start time claims no duration');
  assert.deepEqual(reviewerLines(round()), []);
  assert.equal(costPhrase(round()), 'no usage reported');
});
