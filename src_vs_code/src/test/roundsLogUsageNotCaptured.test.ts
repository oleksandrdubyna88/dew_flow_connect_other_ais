import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cost3, costTitle, LogRow, money, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';
import { parseUsageLine, UsageEntry } from '../usage';

/**
 * A reviewer whose call ENDED before its vendor reported usage — killed on its deadline, cancelled with
 * the round, or an `api` call whose connection dropped — is UNKNOWN in the rounds log, never `0` tokens
 * and never `$0` (the plan round's accepted finding on the API-vendor calibration branch).
 *
 * <p>The server writes such a ledger line with `tokensIn: 0`, `costUsd: null` and
 * `usageNote: "usage not captured"`, and the round's own record carries the same note beside a total
 * that is then a floor. Priced by the public list, that zero line used to read as `$0.000`.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'FeatureReview',
    number: 1,
    verdict: 'revise',
    gatingCount: 0,
    reviewers: '0 of 1 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-26T10:00:00.000Z',
    completedUtc: '2026-09-26T10:21:00.000Z',
    subject: 'PLAN — feature review',
    tokensIn: 0,
    tokensOut: 0,
    reviewerStates: [{ provider: 'grok', role: 'FeatureReview', status: 'failed', findings: 0, note: '', seconds: 1200 }],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'FeatureReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

/** A ledger line exactly as the server writes it — parsed through the real reader. */
function line(json: Record<string, unknown>): UsageEntry {
  const parsed = parseUsageLine(JSON.stringify({
    utc: '2026-09-26T10:20:00.000Z', provider: 'grok', model: 'grok-4.7', role: 'FeatureReview',
    stage: 'FeatureReview', seconds: 1200, tokensIn: 0, tokensOut: 0, costUsd: null, outcome: 'timeout', ...json,
  }));
  assert.ok(parsed !== undefined, 'the fixture must be a line the real reader accepts');

  return parsed;
}

/** grok-4.7 has a list price, which is exactly what turned an unknown zero into `$0.000`. */
const listed = () => ({ inPerMillion: 2, outPerMillion: 6 });

const NOW = Date.parse('2026-09-26T11:00:00.000Z');
const KILLED = { usageNote: 'usage not captured' };

test('the ledger reader keeps a line’s usage-not-captured note', () => {
  assert.equal(line(KILLED).usageNote, 'usage not captured');
  assert.equal(line({}).usageNote, undefined, 'a line without one says nothing, as an old line always did');
});

test('a round whose only reviewer was killed says usage not captured — never $0, never 0 tokens', () => {
  const [row] = rowsFrom([session([round(KILLED)])], NOW, listed, [line(KILLED)]) as [LogRow];

  assert.equal(cost3(row, money), 'usage not captured', 'a listed price times an unknown is not $0');
  assert.match(costTitle(row, money), /usage/u);
  assert.equal(row.tokensIn, null, 'zero tokens counted is not zero tokens spent');
  assert.equal(row.tokensOut, null);
});

test('a round with an answered reviewer and a killed one shows what was counted, as a floor', () => {
  const answered = line({ provider: 'grok', tokensIn: 900, tokensOut: 40, outcome: 'ok', utc: '2026-09-26T10:02:00.000Z' });
  const [row] = rowsFrom(
    [session([round({ ...KILLED, tokensIn: 900, tokensOut: 40 })])], NOW, listed, [answered, line(KILLED)]) as [LogRow];

  assert.equal(row.tokensIn, 900, 'what the vendor did report is still shown');
  assert.equal(row.costPartial, true, 'the killed reviewer is not in the figure, so the figure is a floor');
  assert.ok(cost3(row, money).endsWith('+'), cost3(row, money));
  assert.match(costTitle(row, money), /usage/u, 'the tooltip says why it is a floor');
});

test('a round every reviewer answered is unchanged', () => {
  const answered = line({ tokensIn: 900, tokensOut: 40, outcome: 'ok' });
  const [row] = rowsFrom([session([round({ tokensIn: 900, tokensOut: 40 })])], NOW, listed, [answered]) as [LogRow];

  assert.equal(row.costPartial, false);
  assert.equal(row.tokensIn, 900);
  assert.doesNotMatch(costTitle(row, money), /usage/u);
});
