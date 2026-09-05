import assert from 'node:assert/strict';
import { test } from 'node:test';
import { LogRow, roundsLogHtml, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';

/**
 * The Cost column says three numbers: what the round read, what it wrote, and the sum.
 *
 * <p>Asked for on 2026-09-05, looking at the log: the column was empty on every row. It rendered
 * `costUsd` — which only a vendor that prices its own runs ever reports, and none of the three
 * here does — so a column that could be computed from tokens and a public price list showed a dash
 * on all ninety-eight rounds.</p>
 *
 * <p>Priced per REVIEWER, not per round: a round's tokens are the sum over vendors whose prices
 * differ by an order of magnitude, so one multiplication over the round total would be a number
 * with no meaning. A reviewer whose model has no listed price contributes nothing and the row says
 * the total is partial rather than quietly under-reporting.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 1,
    reviewers: 'all 2 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-05T07:41:00.000Z',
    completedUtc: '2026-09-05T07:43:10.000Z',
    subject: 'SCOPE — something',
    tokensIn: 1_000_000,
    tokensOut: 200_000,
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', tokensIn: 1_000_000, tokensOut: 200_000 },
    ],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

const NOW = Date.parse('2026-09-05T08:00:00.000Z');
/** $2 per million in, $10 per million out — round numbers so the arithmetic is checkable by eye. */
const PRICES = (provider: string) =>
  provider === 'codex' ? { inPerMillion: 2, outPerMillion: 10 } : undefined;

test('a round is priced per reviewer: in, out and the sum', () => {
  const [row] = rowsFrom([session([round()])], NOW, PRICES) as [LogRow];

  assert.equal(row.costInUsd, 2, '1M tokens at $2/M');
  assert.equal(row.costOutUsd, 2, '200k tokens at $10/M');
  assert.equal(row.costTotalUsd, 4);
  assert.equal(row.costIsEstimate, true, 'derived from a public price list, not billed');
});

test('two vendors at different prices are added up, each at its own rate', () => {
  const two = round({
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 0, note: '', tokensIn: 1_000_000, tokensOut: 100_000 },
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', tokensIn: 1_000_000, tokensOut: 100_000 },
    ],
  });
  const prices = (p: string) =>
    p === 'codex' ? { inPerMillion: 2, outPerMillion: 10 } : { inPerMillion: 0, outPerMillion: 0 };

  const [row] = rowsFrom([session([two])], NOW, prices) as [LogRow];

  assert.equal(row.costInUsd, 2, 'the local model is free, so only codex costs');
  assert.equal(row.costOutUsd, 1);
  assert.equal(row.costTotalUsd, 3);
});

test('a reviewer whose model has no listed price makes the total PARTIAL, never smaller in silence', () => {
  const mixed = round({
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 0, note: '', tokensIn: 1_000_000, tokensOut: 0 },
      { provider: 'mystery', role: 'Architecture', status: 'done', findings: 0, note: '', tokensIn: 5_000_000, tokensOut: 0 },
    ],
  });

  const [row] = rowsFrom([session([mixed])], NOW, PRICES) as [LogRow];

  assert.equal(row.costInUsd, 2);
  assert.equal(row.costPartial, true, 'one reviewer could not be priced and the row says so');
});

test('a round a vendor actually billed uses the billed number, and is not an estimate', () => {
  const billed = round({ costUsd: 7.5 });

  const [row] = rowsFrom([session([billed])], NOW, PRICES) as [LogRow];

  assert.equal(row.costTotalUsd, 7.5, 'what was charged wins over what we worked out');
  assert.equal(row.costIsEstimate, false);
});

test('no prices at all is no cost, never a zero', () => {
  const [row] = rowsFrom([session([round()])], NOW) as [LogRow];

  assert.equal(row.costTotalUsd, null, 'a zero would read as free');
  assert.equal(row.costInUsd, null);
});

test('the page prints the three numbers, and says which is which', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW, PRICES), [], 'n');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));

  assert.ok(html.includes('>Cost</th>') || html.includes('Cost'), 'the column is there');
  assert.match(script, /costInUsd/, 'the page renders the input cost');
  assert.match(script, /costOutUsd/);
  assert.match(script, /costTotalUsd/);
  assert.ok(html.includes('in / out / total'), 'the header or the hint says what the three numbers are');
});

test('a cell that can be cut off carries its full text as a tooltip', () => {
  // Reported from the page: the What and Reviewers columns are cut and there is no way to read the
  // rest. The full string is the cell's own title, so hovering shows it without widening the table.
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW, PRICES), [], 'n');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));

  assert.match(script, /class="what" title=/, 'the subject');
  assert.match(script, /class="who-answered" title=/, 'the reviewer summary');
  assert.match(script, /class="num cost" title=/, 'the cost, whose marks need a sentence');
  assert.match(script, /function costTitle/, 'and the cost tooltip spells the three numbers out');
});
