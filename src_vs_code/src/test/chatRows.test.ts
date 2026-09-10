import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatTurnRecord } from '../chatUsage';
import { LogRow, chatRows, mergedRows, roundsLogHtml, rowMatches } from '../roundsLog';

/**
 * Conversations in the rounds log: the same table, a Kind of their own.
 *
 * <p>Two ledgers, two writers, one table. The review rounds come from the server's session files and
 * its `usage.jsonl`; the conversations come from the extension's own `chat-usage.jsonl`; they are
 * merged in MEMORY, which is what lets the two halves ship on different days.</p>
 *
 * <p>What is asserted here is mostly about honesty rather than arithmetic — that a chat leaves the
 * columns it has no answer for EMPTY rather than plausible, that a turn nobody priced reads as
 * unknown rather than as free, and that a total worked out from a price list is marked as one.</p>
 */

function record(over: Partial<ChatTurnRecord> = {}): ChatTurnRecord {
  return {
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'claude',
    model: 'sonnet',
    tokensIn: 1_000_000,
    tokensOut: 500_000,
    costUsd: null,
    seconds: 12,
    outcome: 'answered',
    conversation: 'tab-1',
    title: 'PLAN_a_turn.md',
    ...over,
  };
}

/** A round row, complete. No cast — a fixture the compiler does not check is a fixture that lies. */
function reviewRow(over: Partial<LogRow> = {}): LogRow {
  return {
    key: 'r1', kind: 'review',
    startedUtc: '2026-09-09T19:00:00.000Z', completedUtc: '2026-09-09T19:02:00.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE — the thing', status: 'done', decided: null, verdict: 'proceed', gating: 0,
    findings: 2, seconds: 120, tokensIn: 10, tokensOut: 5, costUsd: null, costInUsd: null,
    costOutUsd: null, costTotalUsd: null, costIsEstimate: false, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'], reviewers: ['codex/Architecture — done'],
    reviewerColours: ['#fff'], found: [], foundCount: 0, foundState: 'unasked', origin: 'db',
    dbKey: { sessionId: 's1', stage: 'CodeReview', number: 1 },
    ...over,
  };
}

const DOLLAR_A_MILLION = () => ({ inPerMillion: 3, outPerMillion: 15 });

test('a chat turn becomes a row of its own KIND, and says so', () => {
  const [row] = chatRows([record()]);

  assert.strictEqual(row?.kind, 'conversation');
  assert.strictEqual(row?.subject, 'PLAN_a_turn.md', 'the tab\u2019s name is what identifies the row');
  assert.strictEqual(row?.seconds, 12);
  assert.strictEqual(row?.status, 'done');
});

test('the columns a conversation has no answer for are EMPTY, never plausible', () => {
  // A chat is not held against a branch, and filling these in would make the table read as though a
  // conversation were a kind of review round. The facet selects offer only values that exist, so an
  // empty one adds no option to any filter.
  const [row] = chatRows([record()]);

  assert.strictEqual(row?.repoPath, '');
  assert.strictEqual(row?.repoName, '');
  assert.strictEqual(row?.branch, '');
  assert.strictEqual(row?.stage, '');
  assert.strictEqual(row?.verdict, '');
  assert.strictEqual(row?.findings, null, 'no findings is not zero findings');
  assert.strictEqual(row?.decided, null);
});

test('a stopped turn is an INTERRUPTED row, so the badge is not the same as an answer', () => {
  assert.strictEqual(chatRows([record({ outcome: 'stopped' })])[0]?.status, 'interrupted');
  assert.strictEqual(chatRows([record({ outcome: 'failed' })])[0]?.status, 'interrupted');
  assert.strictEqual(chatRows([record({ outcome: 'answered' })])[0]?.status, 'done');
});

test('a turn nobody reported anything about reads as UNKNOWN, never as free', () => {
  // The ledger stores tokens as numbers, so a vendor that said nothing and a turn killed before it
  // could say anything both arrive as zeroes. A table printing `0` beside `stopped` tells a person
  // the stop cost them nothing, which is the one thing it certainly does not say.
  const [silent] = chatRows([record({ tokensIn: 0, tokensOut: 0, costUsd: null, outcome: 'stopped' })]);

  assert.strictEqual(silent?.tokensIn, null);
  assert.strictEqual(silent?.tokensOut, null);
  assert.strictEqual(silent?.costTotalUsd, null);

  // And a turn that really did report numbers keeps them.
  const [said] = chatRows([record({ tokensIn: 800, tokensOut: 40 })]);
  assert.strictEqual(said?.tokensIn, 800);
});

test('a conversation is priced through the SAME table the rounds are', () => {
  // A million in and half a million out at $3 / $15 per million.
  const [row] = chatRows([record()], DOLLAR_A_MILLION);

  assert.strictEqual(row?.costInUsd, 3);
  assert.strictEqual(row?.costOutUsd, 7.5);
  assert.strictEqual(row?.costTotalUsd, 10.5);
  assert.strictEqual(row?.costIsEstimate, true, 'worked out from a list is an estimate and must say so');
});

test('a vendor that BILLED the turn wins over the price list, exactly as a round does', () => {
  const [row] = chatRows([record({ costUsd: 0.42 })], DOLLAR_A_MILLION);

  assert.strictEqual(row?.costTotalUsd, 0.42, 'the bill is the fact; the list is a guess about it');
  assert.strictEqual(row?.costIsEstimate, false);
});

test('a model nothing prices has no cost figure rather than a cost of zero', () => {
  const [row] = chatRows([record()]);

  assert.strictEqual(row?.costTotalUsd, null);
  assert.strictEqual(row?.costIsEstimate, false, 'there is no estimate to mark');
});

test('a chat row is never PARTIAL, because one turn cannot leave a second reviewer unpriced', () => {
  assert.strictEqual(chatRows([record()])[0]?.costPartial, false);
  assert.strictEqual(chatRows([record()], DOLLAR_A_MILLION)[0]?.costPartial, false);
});

test('turns are numbered within their own conversation, oldest first', () => {
  // So the Round column means something for a chat too. Counted over the list in time order rather
  // than stored on the record: it is a fact about the list, and a turn that never reaches the page
  // cannot leave a gap in it.
  const rows = chatRows([
    record({ utc: '2026-09-09T20:00:02.000Z', conversation: 'a' }),
    record({ utc: '2026-09-09T20:00:00.000Z', conversation: 'a' }),
    record({ utc: '2026-09-09T20:00:01.000Z', conversation: 'b' }),
    record({ utc: '2026-09-09T20:00:03.000Z', conversation: 'a' }),
  ]);
  const numberAt = (utc: string) => rows.find((row) => row.startedUtc === utc)?.number;

  assert.strictEqual(numberAt('2026-09-09T20:00:00.000Z'), 1);
  assert.strictEqual(numberAt('2026-09-09T20:00:02.000Z'), 2);
  assert.strictEqual(numberAt('2026-09-09T20:00:03.000Z'), 3);
  assert.strictEqual(numberAt('2026-09-09T20:00:01.000Z'), 1, 'the other conversation starts at one');
});

test('the rows come back NEWEST first, whatever order the ledger was in', () => {
  const rows = chatRows([
    record({ utc: '2026-09-09T20:00:00.000Z' }),
    record({ utc: '2026-09-09T22:00:00.000Z' }),
    record({ utc: '2026-09-09T21:00:00.000Z' }),
  ]);

  assert.deepStrictEqual(
    rows.map((row) => row.startedUtc),
    ['2026-09-09T22:00:00.000Z', '2026-09-09T21:00:00.000Z', '2026-09-09T20:00:00.000Z'],
  );
});

test('a turn ends when it began plus how long it took, and a broken date ends nowhere', () => {
  // Derived rather than recorded: two instants that must agree is two chances to disagree.
  assert.strictEqual(
    chatRows([record({ utc: '2026-09-09T20:00:00.000Z', seconds: 90 })])[0]?.completedUtc,
    '2026-09-09T20:01:30.000Z',
  );
  assert.strictEqual(chatRows([record({ utc: 'sometime' })])[0]?.completedUtc, '');
});

test('two keys of one conversation never collide, and never collide with a round', () => {
  const rows = chatRows([
    record({ utc: '2026-09-09T20:00:00.000Z' }),
    record({ utc: '2026-09-09T20:00:01.000Z' }),
  ]);

  assert.notStrictEqual(rows[0]?.key, rows[1]?.key);
  assert.ok(rows.every((row) => row.key.startsWith('chat:')), 'a chat key must be recognisable as one');
});

test('a conversation has no findings to READ, which is not the same as findings nobody wrote down', () => {
  // `absent` would offer to explain that the database never heard of it; `unasked` would spawn a
  // process to be told nothing. Loaded-with-none is the truth, and the row opens on it.
  const [row] = chatRows([record()]);

  assert.strictEqual(row?.foundState, 'loaded');
  assert.deepStrictEqual(row?.found, []);
  assert.strictEqual(row?.foundCount, 0);
});

test('the two ledgers merge into one table ordered by time, not by which half they came from', () => {
  const merged = mergedRows(
    [reviewRow({ key: 'r-old', startedUtc: '2026-09-09T18:00:00.000Z' }),
      reviewRow({ key: 'r-new', startedUtc: '2026-09-09T22:00:00.000Z' })],
    chatRows([record({ utc: '2026-09-09T20:00:00.000Z' })]),
  );

  assert.deepStrictEqual(
    merged.map((row) => row.kind),
    ['review', 'conversation', 'review'],
    'the halves must interleave by time',
  );
  assert.strictEqual(merged.length, 3);
});

test('the Kind filter narrows to one half, and no filter shows both', () => {
  const review = reviewRow();
  const [chat] = chatRows([record()]);

  assert.ok(chat !== undefined);
  assert.strictEqual(rowMatches(review, {}, ''), true);
  assert.strictEqual(rowMatches(chat, {}, ''), true);
  assert.strictEqual(rowMatches(review, { kind: 'review' }, ''), true);
  assert.strictEqual(rowMatches(chat, { kind: 'review' }, ''), false);
  assert.strictEqual(rowMatches(chat, { kind: 'conversation' }, ''), true);
  assert.strictEqual(rowMatches(review, { kind: 'conversation' }, ''), false);
});

test('a conversation is found by its own name in the search box', () => {
  const [chat] = chatRows([record({ title: 'PLAN_who_said_it.md' })]);

  assert.ok(chat !== undefined);
  assert.strictEqual(rowMatches(chat, {}, 'who_said_it'), true);
  assert.strictEqual(rowMatches(chat, {}, 'a branch it was never on'), false);
});

test('a date range that excludes a conversation excludes it, as it would a round', () => {
  const [chat] = chatRows([record({ utc: '2026-09-09T20:00:00.000Z' })]);

  assert.ok(chat !== undefined);
  assert.strictEqual(rowMatches(chat, { from: '2026-09-09' }, ''), true);
  assert.strictEqual(rowMatches(chat, { from: '2026-09-10' }, ''), false);
  assert.strictEqual(rowMatches(chat, { to: '2026-09-09' }, ''), true, 'a bare day means the END of it');
});

test('the page has a Kind column and a Kind filter, and the detail row spans every column', () => {
  // The colspan is the column COUNT, and it used to be a literal `15`. A column added without it
  // leaves an opened row's detail short by one and the table visibly ragged — so it is derived, and
  // this is the test that says so.
  const html = roundsLogHtml([reviewRow()], [], 'n0nce');
  const headers = html.match(/<th data-sort="/g) ?? [];

  assert.match(html, /<th data-sort="kind">Kind<\/th>/, 'the column is missing');
  assert.match(html, /<select data-filter="kind">/, 'the facet is missing');
  assert.match(html, /var COLUMN_COUNT = \d+;/, 'the colspan is not derived from the columns');
  assert.match(
    html,
    new RegExp(`var COLUMN_COUNT = ${headers.length};`),
    'the detail row would not span the table',
  );
  assert.ok(!html.includes('colspan="15"'), 'the hand-written colspan is still there');
});

test('the Kind facet offers both halves once they exist, and neither before', () => {
  const both = roundsLogHtml(mergedRows([reviewRow()], chatRows([record()])), [], 'n0nce');
  const options = /<select data-filter="kind">(.*?)<\/select>/.exec(both)?.[1] ?? '';

  assert.match(options, /value="review"/);
  assert.match(options, /value="conversation"/);

  // A filter offers only what exists: with no conversations recorded, the option is not there to be
  // chosen — the same rule every other facet on this page follows.
  const roundsOnly = roundsLogHtml([reviewRow()], [], 'n0nce');
  const alone = /<select data-filter="kind">(.*?)<\/select>/.exec(roundsOnly)?.[1] ?? '';
  assert.match(alone, /value="review"/);
  assert.ok(!alone.includes('value="conversation"'), 'a filter must not offer an empty result');
});

test('a conversation row carries its vendor, so the Vendor facet reaches it too', () => {
  const [row] = chatRows([record({ provider: 'claude', model: 'sonnet' })]);

  assert.ok(row !== undefined);
  assert.deepStrictEqual(row.vendors, ['claude']);
  assert.strictEqual(rowMatches(row, { vendor: 'claude' }, ''), true);
  assert.strictEqual(rowMatches(row, { vendor: 'codex' }, ''), false);
  assert.match(row.answered, /claude\/sonnet/, 'the Reviewers column names who answered');
});
