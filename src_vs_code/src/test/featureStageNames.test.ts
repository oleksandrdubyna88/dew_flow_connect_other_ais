import assert from 'node:assert/strict';
import { test } from 'node:test';
import { noteOf, RoundRecord, stageName } from '../rounds';

/**
 * Every stage the server names is spoken the way a person would say it — and a name this build has
 * never heard of is shown as it came, never turned into a word that means something else.
 *
 * <p>§9.5 of `todo/PLAN_feature_review.md`: `stageName` knew `PlanReview` and `CodeReview` only, so a
 * document round sat in the log as the raw enum `DocumentReview` beside rows reading "plan review".
 * The server's own phrases are the `Stages` table's (`src_mcp/core/Rounds/Stages.cs`): "document
 * review" and, since S2.1, "feature review".</p>
 */

test('a document round is a "document review", not the raw enum', () => {
  assert.equal(stageName('DocumentReview'), 'document review');
});

test('a feature round is a "feature review"', () => {
  assert.equal(stageName('FeatureReview'), 'feature review');
});

test('the two older stages still read as they did, and an unknown one is shown raw', () => {
  assert.equal(stageName('PlanReview'), 'plan review');
  assert.equal(stageName('CodeReview'), 'code review');
  // A stage a newer server added must not be renamed into one this build knows — raw is the only
  // honest spelling for a name nobody taught this build.
  assert.equal(stageName('EscalationReview'), 'EscalationReview');
});

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'FeatureReview',
  number: 1,
  verdict: 'skipped',
  gatingCount: 0,
  reviewers: 'no reviewer ran',
  completedUtc: '2026-09-26T10:00:00.000Z',
  ...over,
});

/**
 * A skipped round's note is the server's reason, with `×N` when one row stands for N consecutive
 * skips — the same sentence `RoundRecord.LoggedNote` writes into `--log` (`SessionStore.cs`).
 */
test('a skipped round says why, and how many times in a row', () => {
  assert.equal(noteOf(round({ note: 'no vendor is ticked to review features' })), 'no vendor is ticked to review features');
  assert.equal(noteOf(round({ note: 'no vendor is ticked to review features', repeats: 3 })), 'no vendor is ticked to review features ×3');
  assert.equal(noteOf(round({ note: 'x', repeats: 1 })), 'x', 'one skip is not "×1"');
});

test('a round with no note — every round that ran, and every older file — has none', () => {
  assert.equal(noteOf(round({ verdict: 'proceed' })), '');
  // A session file is JSON somebody else wrote; a number where a string belongs is absent, not a crash.
  assert.equal(noteOf(round({ note: 42 as unknown as string })), '');
  assert.equal(noteOf(round({ note: '   ', repeats: 4 })), '', 'a blank reason is no reason, and ×4 of nothing is nothing');
});
