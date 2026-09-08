import assert from 'node:assert/strict';
import { test } from 'node:test';
import { costPhrase, elapsed, isRunning, parseSession, reviewerLines, RoundRecord } from '../rounds';

/**
 * What `rounds.ts` still guarantees now that the markdown log is gone.
 *
 * <p>This file used to be mostly about `renderRounds` / `renderSession` — the `rounds.md` tables
 * the extension wrote and rewrote every five seconds. That view was replaced by a page with a
 * sortable table on 2026-09-05 (`roundsLog.test.ts`), and the renderer went with it; the dating
 * and duration rules its tests pinned moved to the page's own rows. What is left here is the
 * parser and the small honest helpers the sidebar and the page both use.</p>
 */

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'CodeReview',
  number: 1,
  verdict: 'revise',
  gatingCount: 2,
  reviewers: 'all 6 reviewers answered',
  completedUtc: '2026-08-31T12:10:00Z',
  ...over,
});

test('a torn or foreign file is skipped, never a crash', () => {
  assert.equal(parseSession('{not json'), undefined);
  assert.equal(parseSession('{"something": "else"}'), undefined);
  assert.notEqual(
    parseSession(JSON.stringify({ state: { sessionId: 'a', repoPath: 'D:/r', branch: 'main', stage: 'CodeReview', awaitingResolve: false }, rounds: [round()] })),
    undefined,
  );
});

test('isRunning, elapsed and reviewerLines are honest about missing data', () => {
  assert.equal(isRunning(round()), false);
  assert.equal(elapsed(round(), Date.now()), '', 'a round with no start time claims no duration');
  assert.deepEqual(reviewerLines(round()), []);
  assert.equal(costPhrase(round()), 'no usage reported');
});

test('a start date from an older server does not render as a billion minutes', () => {
  // .NET's default date is year ONE, and the subtraction produced "1065396701m 44s" in the panel.
  assert.equal(elapsed(round({ startedUtc: '0001-01-01T00:00:00' }), Date.parse('2026-09-01T09:00:00Z')), '');
});

/**
 * The log names the model, and says nothing where it does not know one.
 *
 * <p>A round that names its vendor and its role but not its model cannot answer the question people
 * actually ask about a slow or a weak reviewer — which is how the claude row came to be investigated
 * by reading a spending ledger instead of the log
 * (`research/RESULTS_reviewer_input_sizes.md`).</p>
 *
 * <p>The absent case matters as much: every round written before this field exists has none, and
 * must read exactly as it did rather than growing an empty separator.</p>
 */
test('a reviewer row names the model it was launched with', () => {
  const withModel = round({
    reviewerStates: [
      { provider: 'remsoftdev-claude', role: 'Architecture', status: 'done', findings: 3, note: '', model: 'claude-haiku-4-5' },
    ],
  });

  assert.deepEqual(reviewerLines(withModel), [
    'remsoftdev-claude/Architecture · claude-haiku-4-5 — done (3 findings)',
  ]);
});

test('a reviewer launched without a model gets no separator, not an empty one', () => {
  // The case the "older round" test below does NOT cover, and a reviewer on the plan round was
  // right to separate them: an old file has no field at all, while a CURRENT round can carry an
  // empty string — a local engine with no model configured. Both must read the same.
  const noModel = round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', model: '' },
      { provider: 'local', role: 'SecurityReliability', status: 'done', findings: 0, note: '', model: '   ' },
    ],
  });

  assert.deepEqual(reviewerLines(noModel), [
    'local/Architecture — done (0 findings)',
    'local/SecurityReliability — done (0 findings)',
  ]);
});

test('a round from before the field reads exactly as it did', () => {
  const older = round({
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '' }],
  });

  assert.deepEqual(reviewerLines(older), ['codex/Architecture — done (1 finding)']);
});
