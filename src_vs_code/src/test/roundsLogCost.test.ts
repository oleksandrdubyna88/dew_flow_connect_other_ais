import assert from 'node:assert/strict';
import { test } from 'node:test';
import { asInstant, cost3, LogRow, money, roundsLogHtml, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';
import { UsageEntry } from '../usage';

/**
 * The Cost column says three numbers: what the round read, what it wrote, and the sum.
 *
 * <p>Asked for on 2026-09-05, looking at the log: the column was empty on every row. It rendered
 * `costUsd` — which only a vendor that prices its own runs ever reports, and none of the three here
 * does — so a column that could be computed from tokens and a public price list showed nothing on
 * all ninety-eight rounds.</p>
 *
 * <p>The first version priced each REVIEWER STATE of the round, and the column stayed empty in the
 * installed extension. The reason is in the session file: a reviewer state records
 * `{provider, role, status, findings, note, seconds}` and <b>no tokens at all</b> — the totals live
 * on the round, summed over vendors whose prices differ by an order of magnitude, which is not a
 * number anything can be multiplied by.</p>
 *
 * <p>What DOES record tokens per reviewer is the usage ledger, one line per reviewer run with the
 * model that answered: `{utc, provider, model, role, stage, seconds, tokensIn, tokensOut, costUsd}`.
 * So a round is priced from the ledger lines that fall inside it, each at the price of the model
 * that line names — which also settles what the gate raised twice: a round priced from the vendor's
 * CURRENT model changes its historical cost the moment somebody switches models.</p>
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
    // Exactly as the server writes it: no tokens on a reviewer state.
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: 23 },
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

function used(over: Partial<UsageEntry> = {}): UsageEntry {
  return {
    utc: '2026-09-05T07:42:00.000Z',
    provider: 'codex',
    model: 'gpt-5.6-sol',
    role: 'Architecture',
    stage: 'CodeReview',
    seconds: 23,
    tokensIn: 1_000_000,
    tokensOut: 200_000,
    costUsd: null,
    ...over,
  } as UsageEntry;
}

const NOW = Date.parse('2026-09-05T08:00:00.000Z');

/** $2 per million in, $10 per million out — round numbers so the arithmetic is checkable by eye. */
const PRICES = (model: string) =>
  model === 'gpt-5.6-sol'
    ? { inPerMillion: 2, outPerMillion: 10 }
    : model === 'gemini-3-pro'
      ? { inPerMillion: 4, outPerMillion: 20 }
      : undefined;

test('a round as the server actually writes it is priced from the ledger, not left blank', () => {
  // The defect the operator photographed: reviewer states carry no tokens, so nothing could be
  // priced and every Cost cell was empty.
  const [row] = rowsFrom([session([round()])], NOW, PRICES, [used()]) as [LogRow];

  assert.equal(row.costInUsd, 2, '1M read at $2/M');
  assert.equal(row.costOutUsd, 2, '200k written at $10/M');
  assert.equal(row.costTotalUsd, 4);
  assert.equal(row.costIsEstimate, true, 'a list price, not a bill');
  assert.equal(row.costPartial, false);
});

test('each reviewer is priced at the rate of the model that answered', () => {
  const two = round({
    tokensIn: 2_000_000,
    tokensOut: 400_000,
    reviewers: 'all 2 reviewers answered',
  });
  const [row] = rowsFrom([session([two])], NOW, PRICES, [
    used(),
    used({ provider: 'gemini', model: 'gemini-3-pro', role: 'SecurityReliability' }),
  ]) as [LogRow];

  assert.equal(row.costInUsd, 2 + 4, 'a million each, at each model\u2019s own rate');
  assert.equal(row.costOutUsd, 2 + 4);
  assert.equal(row.costTotalUsd, 12);
});

test('the price of the model that ANSWERED, not the one the vendor is set to now', () => {
  // Raised twice by the gate: switching a vendor's model must not move what a finished round cost.
  const [row] = rowsFrom([session([round()])], NOW, PRICES, [
    used({ model: 'gemini-3-pro' }),
  ]) as [LogRow];

  assert.equal(row.costTotalUsd, 4 + 4, 'priced as gemini-3-pro, whatever codex is set to today');
});

test('a model no list prices leaves the total a floor, and says so', () => {
  const [row] = rowsFrom([session([round()])], NOW, PRICES, [
    used(),
    used({ provider: 'local', model: 'Qwen3.5-35B-A3B:latest', role: 'UxDxPerformance' }),
  ]) as [LogRow];

  assert.equal(row.costTotalUsd, 4, 'what could be priced');
  assert.equal(row.costPartial, true, 'and a mark that something could not');
});

test('a round with no ledger lines has NO cost — not a zero', () => {
  const [row] = rowsFrom([session([round()])], NOW, PRICES, []) as [LogRow];

  assert.equal(row.costTotalUsd, null, 'absent is not free');
  assert.equal(row.costInUsd, null);
  assert.equal(row.costPartial, false, 'nothing is claimed at all, so nothing is partial');
});

test('a reviewer whose tokens were never recorded is not priced as zero', () => {
  // A line written before the ledger recorded tokens: the fields are simply absent.
  const noTokens = { ...used({ provider: 'gemini', model: 'gemini-3-pro' }) } as Record<string, unknown>;
  delete noTokens['tokensIn'];
  delete noTokens['tokensOut'];
  const [row] = rowsFrom([session([round()])], NOW, PRICES, [used(), noTokens as unknown as UsageEntry]) as [LogRow];

  assert.equal(row.costTotalUsd, 4, 'only the reviewer that reported tokens is counted');
  assert.equal(row.costPartial, true, 'and the total says it is a floor');
});

test('only the lines inside the round, and of its own stage, are its cost', () => {
  const [row] = rowsFrom([session([round()])], NOW, PRICES, [
    used(),
    used({ utc: '2026-09-05T07:50:00.000Z' }),
    used({ utc: '2026-09-05T07:42:30.000Z', stage: 'PlanReview' }),
  ]) as [LogRow];

  assert.equal(row.costTotalUsd, 4, 'the later line and the other stage belong to other rounds');
});

test('a total that does not match the tokens the round recorded is a floor', () => {
  // Two rounds of one stage running at once — the five-window case — can each match the other's
  // lines by time alone. When the tokens do not add up to what the round recorded, say so.
  const [row] = rowsFrom([session([round({ tokensIn: 5_000_000 })])], NOW, PRICES, [used()]) as [LogRow];

  assert.equal(row.costPartial, true);
});

test('a vendor that billed the round wins over any list price', () => {
  const billed = round({ costUsd: 0.42 });
  const [row] = rowsFrom([session([billed])], NOW, PRICES, [used()]) as [LogRow];

  assert.equal(row.costTotalUsd, 0.42);
  assert.equal(row.costIsEstimate, false, 'a bill is not an estimate');
});

test('the column shows in / out / total, and a dash when there is nothing to show', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW, PRICES, [used()]), [], 'n');

  assert.match(html, /data-sort="costTotalUsd"/, 'the column sorts like every other');
  assert.match(html, /<th[^>]*>Cost<\/th>/, 'and it is named');
  assert.match(html.slice(html.indexOf('<script')), /var cost3 = function/, 'the page uses the tested function itself');
});

test('three figures, always three, with a dash for whatever is unknown', () => {
  assert.equal(
    cost3({ costInUsd: 2, costOutUsd: 2, costTotalUsd: 4, costIsEstimate: true, costPartial: false }),
    '~$2.00 / $2.00 / $4.00');
  assert.equal(
    cost3({ costInUsd: null, costOutUsd: null, costTotalUsd: 0.42, costIsEstimate: false, costPartial: false }),
    '— / — / $0.420',
    'a billed total with no split still reads as three');
  assert.equal(
    cost3({ costInUsd: null, costOutUsd: null, costTotalUsd: null, costIsEstimate: false, costPartial: false }),
    '—',
    'and nothing priced is a dash, not an empty cell');
});

test('a money figure never renders as NaN or undefined', () => {
  assert.equal(money(undefined), '—');
  assert.equal(money(null), '—');
  assert.equal(money(Number.NaN), '—');
  assert.equal(money(1.5), '$1.50');
  assert.equal(money(0.004), '$0.004', 'a fraction of a cent still says something');
});

test('the upper bound of a range includes the minute it names', () => {
  // "Today" ends at 23:59, and a round at 23:59:30 belongs to today. Raised by three reviewers at
  // once on 2026-09-05.
  const chosen = Date.parse('2026-09-05T23:59');

  assert.equal(asInstant('2026-09-05T23:59', true), new Date(chosen + 59_999).toISOString());
  assert.equal(asInstant('2026-09-05T00:00', false), new Date(Date.parse('2026-09-05T00:00')).toISOString());
  assert.equal(asInstant('', true), '', 'no bound is no bound');
  assert.equal(asInstant('not a date', true), '', 'and neither is nonsense');
});
