import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cost3, costTitle, LogRow, money, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';
import { parseUsageLine, UsageEntry } from '../usage';

/**
 * S3.7 of PLAN_feature_review.md in the rounds log: an `api` reviewer whose row has no price says
 * "no price set" — never `$0`, and never the dash that means "nobody knows" for a CLI.
 *
 * <p>The ledger line is the server's: a metered run with no rate is written with `costUsd: null` and
 * `costNote: "no price set"`. A priced one carries the cost the server worked out per turn, and the
 * round's own `costUsd` is its sum.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'FeatureReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 0,
    reviewers: 'all 1 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-26T10:00:00.000Z',
    completedUtc: '2026-09-26T10:02:00.000Z',
    subject: 'PLAN — feature review',
    tokensIn: 900,
    tokensOut: 40,
    reviewerStates: [{ provider: 'qwen', role: 'FeatureReview', status: 'done', findings: 0, note: '', seconds: 12 }],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'FeatureReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

/** A ledger line exactly as the server now writes it — parsed through the real reader. */
function line(json: Record<string, unknown>): UsageEntry {
  const parsed = parseUsageLine(JSON.stringify({
    utc: '2026-09-26T10:01:00.000Z', provider: 'qwen', model: 'qwen3.8-max', role: 'FeatureReview',
    stage: 'FeatureReview', seconds: 12, tokensIn: 900, tokensOut: 40, costUsd: null, outcome: 'ok', ...json,
  }));
  assert.ok(parsed !== undefined, 'the fixture must be a line the real reader accepts');

  return parsed;
}

const NOW = Date.parse('2026-09-26T11:00:00.000Z');

test('an api reviewer with no price set says so, never $0 and never a bare dash', () => {
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [line({ costNote: 'no price set' })]) as [LogRow];

  assert.equal(cost3(row, money), 'no price set');
  assert.match(costTitle(row, money), /no price set/u);
});

test('a CLI round nobody could price keeps its dash', () => {
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [line({ provider: 'codex', model: 'gpt-x' })]) as [LogRow];

  assert.equal(cost3(row, money), '—');
});

test('an api reviewer the server priced shows the server’s figure as a cost, not an estimate', () => {
  const [row] = rowsFrom([session([round({ costUsd: 0.0021 })])], NOW, () => undefined, [line({ costUsd: 0.0021 })]) as [LogRow];

  assert.equal(row.costTotalUsd, 0.0021);
  assert.equal(row.costIsEstimate, false);
});
