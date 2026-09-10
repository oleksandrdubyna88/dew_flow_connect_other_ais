import assert from 'node:assert/strict';
import { test } from 'node:test';
import { agyAdapter } from '../agyAdapter';
import { claudeAdapter } from '../claudeAdapter';
import { codexAdapter } from '../codexAdapter';
import {
  ChatTurnRecord,
  chatTurnRecord,
  chatUsageLine,
  conversationTotal,
  parseChatUsage,
  parseChatUsageLine,
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
    turnTokens(false, { tokensIn: 1000, tokensOut: 200, costUsd: null }, { tokensIn: 900, tokensOut: 100 }),
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
    turnTokens(true, { tokensIn: 1200, tokensOut: 300, costUsd: null }, { tokensIn: 1000, tokensOut: 200 }),
    { tokensIn: 200, tokensOut: 100 },
    'the second turn cost the DIFFERENCE, not the running total',
  );
});

test('the first turn of a cumulative vendor has nothing to difference against', () => {
  assert.deepStrictEqual(
    turnTokens(true, { tokensIn: 1000, tokensOut: 200, costUsd: null }, undefined),
    { tokensIn: 1000, tokensOut: 200 },
  );
});

test('a cumulative total that goes BACKWARDS is a fresh baseline, not a free turn', () => {
  // A vendor thread that restarted, compacted its context or evicted a cache reports a number below
  // the last one. This used to clamp the difference to zero — and a zero says the turn was free,
  // which it was not: it cost exactly what the new count says. The function's own comment already
  // said "treated as a fresh start"; the code returned a zero, and the comment was right.
  // (gemini, the code round.)
  assert.deepStrictEqual(
    turnTokens(true, { tokensIn: 50, tokensOut: 10, costUsd: null }, { tokensIn: 1000, tokensOut: 200 }),
    { tokensIn: 50, tokensOut: 10 },
    'a restarted thread’s turn was recorded as free',
  );
  // Money does NOT follow the tokens any more: a cumulative vendor's bill is refused rather than
  // differenced, so a restart is not a special case there — see the test that says so.
  assert.strictEqual(turnCost(true, { tokensIn: 50, tokensOut: 10, costUsd: 0.2 }), null);
});

test('a negative number of tokens is still not a thing that can be true', () => {
  // The clamp that survives: a vendor reporting a negative count is reporting nonsense, and nonsense
  // must not reach a ledger as a debit.
  assert.deepStrictEqual(
    turnTokens(false, { tokensIn: -5, tokensOut: -1, costUsd: null }, undefined),
    { tokensIn: 0, tokensOut: 0 },
  );
});

test('which vendors report cumulatively is declared by the ADAPTER, so it cannot be forgotten', () => {
  // It was a list of runtime NAMES in this module, and a gate reviewer named the flaw: implementing
  // `ChatAdapter` was then not enough to be billed correctly, because an unlisted runtime defaulted
  // silently to per-turn and its conversations would inflate with nothing saying so. A required field
  // on the interface cannot be forgotten — the compiler asks. (gemini, the code round.)
  assert.strictEqual(codexAdapter.cumulative, true);
  assert.strictEqual(claudeAdapter.cumulative, false);
  assert.strictEqual(agyAdapter.cumulative, false);
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

test('the record takes usage that is ALREADY per-turn, and does not difference anything itself', () => {
  // The rule moved to the SESSION, which is the only thing whose life is exactly a vendor thread's
  // life. Before that it lived here and needed a `runtime` and a `previous` handed in from the
  // command that orchestrates the page — the wire protocol's idiosyncrasy leaking two layers up.
  // (gemini, the code round.)
  const built = chatTurnRecord({
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'codex-work',
    model: 'gpt-5',
    conversation: 'tab-1',
    title: 'PLAN_x.md',
    seconds: 9,
    outcome: 'answered',
    usage: { tokensIn: 200, tokensOut: 100, costUsd: null },
  });

  assert.strictEqual(built.tokensIn, 200);
  assert.strictEqual(built.provider, 'codex-work', 'the row is what the ledger is priced by');
  assert.ok(!Object.keys(built).includes('runtime'), 'the runtime has no business in the file');
});

test('a CUMULATIVE vendor reports no per-turn bill, so none is recorded', () => {
  // The operator ruled on 2026-09-10: the money-differencing arithmetic was dead code and goes. What
  // replaces it is not a clamp but a REFUSAL, because the two are not the same when the case finally
  // arrives. A running total is not what one turn cost, so recording it as one would over-report
  // every turn but the first, increasingly, exactly as the token bug did before the gate caught it.
  // `null` says "nobody told us what this turn cost", which is true, and the log page then prices the
  // row from the public list by the model that answered and marks it with a tilde.
  assert.strictEqual(
    turnCost(true, { tokensIn: 0, tokensOut: 0, costUsd: 1.5 }),
    null,
    'a running total was recorded as this turn’s bill',
  );
  // The vendor that DOES bill per turn is untouched, to the cent it actually charged.
  assert.strictEqual(turnCost(false, { tokensIn: 0, tokensOut: 0, costUsd: 0.107958 }), 0.107958);
  assert.strictEqual(turnCost(false, { tokensIn: 0, tokensOut: 0, costUsd: null }), null);
  // And nonsense is not a debit.
  assert.strictEqual(turnCost(false, { tokensIn: 0, tokensOut: 0, costUsd: -3 }), 0);
});

test('the OLD money-differencing rule is gone, and nothing calls it with a previous turn', () => {
  // Guards the deletion itself: `turnCost` took a third argument and subtracted with it. A signature
  // that still accepted one would let the arithmetic creep back in unnoticed.
  assert.strictEqual(turnCost.length, 2, 'turnCost grew back a `previous` parameter');
});

test('a turn nobody reported numbers for is STILL a record, with zeroes and its outcome', () => {
  // The accounting hole the gate raised against the plan: it recorded answers, so a turn that was
  // stopped or that fell over left no line at all — and those are exactly the ones somebody
  // hunting for waste is looking for. Zeroes here are not a claim that it was free; `costUsd: null`
  // says nobody told us, and the log page turns a wholly silent turn into a dash rather than a `0`.
  assert.deepStrictEqual(
    chatTurnRecord({
      utc: '2026-09-09T20:00:00.000Z',
      provider: 'claude',
      model: 'sonnet',
      conversation: 'tab-1',
      title: 'PLAN_x.md',
      seconds: 3,
      outcome: 'stopped',
      usage: undefined,
    }),
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

test('a running total is money, not float noise', () => {
  // $0.10 + $0.20 arrives as 0.30000000000000004, and a total shown to a person must not read that
  // way. `turnCost` already rounded its own subtraction; this is the other end of the same rule.
  // (gemini, the code round.)
  const total = conversationTotal(
    [record({ costUsd: 0.1 }), record({ costUsd: 0.2 })],
    'tab-1',
  );

  assert.strictEqual(total.costUsd, 0.3);
});
