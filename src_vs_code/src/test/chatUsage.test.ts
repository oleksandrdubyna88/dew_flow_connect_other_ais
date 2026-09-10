import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChatTurnRecord,
  chatTurnRecord,
  chatUsageLine,
  conversationTotal,
  parseChatUsage,
  parseChatUsageLine,
  reportsCumulative,
  spent,
  turnCost,
  turnTokens,
} from '../chatUsage';

/**
 * What a chat turn cost, recorded so it can be read back.
 *
 * <p>The file is written by the extension and read by the log page. It is NOT `usage.jsonl` — that
 * one belongs to the server, which ships on its own days: a file with two writers that release
 * separately is a file whose format is a contract nobody wrote down. Both reviewers on the plan round
 * raised that independently.</p>
 *
 * <p>They gave a second reason too — that concurrent appenders tear each other's lines — and that
 * half was MEASURED and did not hold: `npm run measure:append`, eight processes × 1000 records,
 * 116 MB with 60 KB lines among them, 8000 whole records of 8000 and nothing torn. The separation
 * stands on the first reason alone.</p>
 */

function record(over: Partial<ChatTurnRecord> = {}): ChatTurnRecord {
  return {
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'claude',
    model: 'sonnet',
    tokensIn: 1000,
    tokensOut: 200,
    costUsd: null,
    seconds: 4,
    outcome: 'answered',
    conversation: 'tab-1',
    title: 'PLAN_a_turn.md',
    ...over,
  };
}

test('a vendor that reports per-turn numbers is recorded as it reported', () => {
  assert.deepStrictEqual(
    turnTokens('claude', { tokensIn: 1000, tokensOut: 200, costUsd: null }, { tokensIn: 900, tokensOut: 100 }),
    { tokensIn: 1000, tokensOut: 200 },
    'a per-turn reporter must not be differenced against anything',
  );
});

test('a vendor that reports a CUMULATIVE total is differenced against what it last said', () => {
  // The defect the plan would have shipped, caught on its round. `codex` reports a cumulative maximum
  // for the thread — the plan says so in its own limitations — and the plan then went on to record
  // and sum those numbers per turn. Turn one at 1000 and turn two at 1200 would have been written
  // down as 2200, and every conversation on that vendor would have been over-billed in the log,
  // increasingly, the longer it ran. (gemini, the plan round, Blocking.)
  assert.deepStrictEqual(
    turnTokens('codex', { tokensIn: 1200, tokensOut: 300, costUsd: null }, { tokensIn: 1000, tokensOut: 200 }),
    { tokensIn: 200, tokensOut: 100 },
    'the second turn cost the DIFFERENCE, not the running total',
  );
});

test('the first turn of a cumulative vendor has nothing to difference against', () => {
  assert.deepStrictEqual(
    turnTokens('codex', { tokensIn: 1000, tokensOut: 200, costUsd: null }, undefined),
    { tokensIn: 1000, tokensOut: 200 },
  );
});

test('a cumulative total that goes BACKWARDS starts again rather than going negative', () => {
  // A new thread, a resumed conversation whose earlier turns this process never saw, or a vendor that
  // changed its mind. A negative number of tokens is not a thing that can be true.
  assert.deepStrictEqual(
    turnTokens('codex', { tokensIn: 50, tokensOut: 10, costUsd: null }, { tokensIn: 1000, tokensOut: 200 }),
    { tokensIn: 0, tokensOut: 0 },
  );
});

test('which vendors report cumulatively is something a caller can ask', () => {
  assert.strictEqual(reportsCumulative('codex'), true);
  assert.strictEqual(reportsCumulative('claude'), false);
  assert.strictEqual(reportsCumulative('antigravity'), false);
});

test('a record survives a round trip through the file', () => {
  const one = record({ costUsd: 0.0042, outcome: 'answered' });

  assert.deepStrictEqual(parseChatUsage(chatUsageLine(one)), [one]);
});

test('a torn line costs itself and nothing else', () => {
  // The file is appended to while it is read, so the last line can be half-written. Exactly the rule
  // `usage.ts` follows for the server's file.
  const good = record();
  const text = `${chatUsageLine(good)}{"utc":"2026-09-09T20:00:01.00\n${chatUsageLine(record({ tokensIn: 5 }))}`;

  assert.strictEqual(parseChatUsage(text).length, 2, 'the two whole lines must survive the torn one');
});

test('a line with no timestamp is not a record', () => {
  assert.strictEqual(parseChatUsageLine('{"provider":"claude"}'), undefined);
  assert.strictEqual(parseChatUsageLine('not json at all'), undefined);
  assert.strictEqual(parseChatUsageLine('null'), undefined);
});

test('a cost the vendor never gave stays null, and never becomes a zero', () => {
  // A zero says the turn was free. `null` says nobody told us. They are different claims and only one
  // of them is usually true.
  const parsed = parseChatUsageLine(chatUsageLine(record({ costUsd: null })));

  assert.strictEqual(parsed?.costUsd, null);
});

test('a garbled number is read as nothing rather than as NaN', () => {
  const parsed = parseChatUsageLine('{"utc":"2026-09-09T20:00:00.000Z","tokensIn":"lots","tokensOut":-4}');

  assert.strictEqual(parsed?.tokensIn, 0);
  assert.strictEqual(parsed?.tokensOut, 0);
});

test('a conversation total sums only its own turns', () => {
  const total = conversationTotal(
    [
      record({ conversation: 'tab-1', tokensIn: 100, tokensOut: 10 }),
      record({ conversation: 'tab-2', tokensIn: 999, tokensOut: 99 }),
      record({ conversation: 'tab-1', tokensIn: 200, tokensOut: 20 }),
    ],
    'tab-1',
  );

  assert.strictEqual(total.tokensIn, 300);
  assert.strictEqual(total.tokensOut, 30);
});

test('a total is an ESTIMATE if any turn in it went unbilled', () => {
  // A sum that is part bill and part estimate is an estimate, and the tilde marks the whole of it.
  const mixed = conversationTotal(
    [record({ costUsd: 0.01 }), record({ costUsd: null })],
    'tab-1',
  );
  assert.strictEqual(mixed.estimated, true);
  assert.strictEqual(mixed.costUsd, 0.01, 'what WAS billed is still summed');

  const billed = conversationTotal([record({ costUsd: 0.01 }), record({ costUsd: 0.02 })], 'tab-1');
  assert.strictEqual(billed.estimated, false);
  assert.strictEqual(Number(billed.costUsd?.toFixed(4)), 0.03);
});

test('a conversation nobody billed at all has no cost rather than a cost of zero', () => {
  const total = conversationTotal([record({ costUsd: null })], 'tab-1');

  assert.strictEqual(total.costUsd, null, 'null is "nobody said", which is not "it was free"');
  assert.strictEqual(total.estimated, true);
});

test('a turn that was STOPPED is still recorded, because it still cost tokens', () => {
  // The reviewer's point, and it matters more now that stopping a turn is a shipped feature: the
  // vendor billed for what it had generated before the kill. A ledger that only records successes
  // under-reports every conversation somebody changed their mind in.
  const stopped = record({ outcome: 'stopped', tokensIn: 800, tokensOut: 40 });
  const total = conversationTotal([stopped], 'tab-1');

  assert.strictEqual(total.tokensIn, 800);
  assert.strictEqual(parseChatUsage(chatUsageLine(stopped))[0]?.outcome, 'stopped');
});

test('what makes numbers cumulative is the RUNTIME, never the vendor row a person named', () => {
  // The defect this test exists for: the rule was first keyed on the ledger's `provider`, which is a
  // vendor ROW id — a person's own text, editable in the panel. Somebody running two Codex accounts
  // as `codex-work` and `codex-home` would have had neither of them differenced, and both
  // conversations would have been over-billed in the log, increasingly, the longer they ran. That is
  // the exact defect `turnTokens` was written to prevent, reintroduced through its key.
  assert.deepStrictEqual(
    turnTokens('codex', { tokensIn: 1200, tokensOut: 300, costUsd: null }, { tokensIn: 1000, tokensOut: 200 }),
    { tokensIn: 200, tokensOut: 100 },
    'the runtime is what the rule is keyed on',
  );
  assert.strictEqual(reportsCumulative('codex-work'), false, 'a ROW id is not a runtime and must not match');

  // And the whole way through, which is what a caller actually depends on: a record built for a row
  // called `codex-work` running the `codex` runtime is differenced.
  const built = chatTurnRecord({
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'codex-work',
    runtime: 'codex',
    model: 'gpt-5',
    conversation: 'tab-1',
    title: 'PLAN_x.md',
    seconds: 9,
    outcome: 'answered',
    reported: { tokensIn: 1200, tokensOut: 300, costUsd: null },
    previous: { tokensIn: 1000, tokensOut: 200, costUsd: null },
  });

  assert.strictEqual(built.tokensIn, 200, 'a renamed Codex row was not differenced');
  assert.strictEqual(built.provider, 'codex-work', 'the row is still what the ledger is priced by');
});

test('money is differenced by the same rule as tokens, so one cannot drift from the other', () => {
  // Dead code today — the one cumulative vendor reports no money at all — and written anyway. Leaving
  // money un-differenced beside tokens that are is a trap for whoever adds the next cumulative vendor:
  // the tokens would be right and the bill would grow with the length of the conversation.
  assert.strictEqual(turnCost('codex', { tokensIn: 0, tokensOut: 0, costUsd: 1.5 }, { costUsd: 1.2 }), 0.3);
  assert.strictEqual(turnCost('claude', { tokensIn: 0, tokensOut: 0, costUsd: 1.5 }, { costUsd: 1.2 }), 1.5);
  assert.strictEqual(
    turnCost('codex', { tokensIn: 0, tokensOut: 0, costUsd: null }, { costUsd: 1.2 }),
    null,
    'a vendor that billed nothing must not be handed the previous turn\u2019s bill',
  );
  assert.strictEqual(
    turnCost('codex', { tokensIn: 0, tokensOut: 0, costUsd: 0.4 }, { costUsd: null }),
    0.4,
    'nothing to difference against is the first turn, not a free one',
  );
});

test('a turn nobody reported numbers for is STILL a record, with zeroes and its outcome', () => {
  // The accounting hole the gate raised against the plan: it recorded answers, so a turn that was
  // stopped or that fell over left no line at all — and those are exactly the ones somebody hunting
  // for waste is looking for. Zeroes here are not a claim that it was free; `costUsd: null` says
  // nobody told us, and the log page turns a wholly silent turn into a dash rather than a `0`.
  const built = chatTurnRecord({
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'claude',
    runtime: 'claude',
    model: 'sonnet',
    conversation: 'tab-1',
    title: 'PLAN_x.md',
    seconds: 3,
    outcome: 'stopped',
    reported: undefined,
    previous: undefined,
  });

  assert.deepStrictEqual(
    built,
    {
      utc: '2026-09-09T20:00:00.000Z',
      provider: 'claude',
      model: 'sonnet',
      tokensIn: 0,
      tokensOut: 0,
      costUsd: null,
      seconds: 3,
      outcome: 'stopped',
      conversation: 'tab-1',
      title: 'PLAN_x.md',
    },
    'a stopped turn must still be written down',
  );
});

test('the runtime is not written to disk, because it answers no question the log page asks', () => {
  // By the time a record exists the differencing has already happened. A field carried into the file
  // and never read is a field that will be trusted one day by somebody who should not have.
  const built = chatTurnRecord({
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'codex-work',
    runtime: 'codex',
    model: 'gpt-5',
    conversation: 'tab-1',
    title: 'PLAN_x.md',
    seconds: 1,
    outcome: 'answered',
    reported: { tokensIn: 10, tokensOut: 2, costUsd: null },
    previous: undefined,
  });

  assert.ok(!Object.keys(built).includes('runtime'), 'the runtime leaked into the ledger');
});

test('a turn nobody reported numbers for gets NO usage key at all, not an undefined one', () => {
  // `exactOptionalPropertyTypes` makes the difference visible: writing the key with an undefined
  // value puts the KEY there, and every test that compares a whole event or result would then see a
  // shape it did not have before. There were two copies of this rule; there is one now.
  assert.deepStrictEqual(spent(undefined), {});
  assert.ok(!Object.keys(spent(undefined)).includes('usage'));
  assert.deepStrictEqual(
    spent({ tokensIn: 1, tokensOut: 2, costUsd: null }),
    { usage: { tokensIn: 1, tokensOut: 2, costUsd: null } },
  );
});
