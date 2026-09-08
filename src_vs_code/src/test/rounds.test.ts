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
