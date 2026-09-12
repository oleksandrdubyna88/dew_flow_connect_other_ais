import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatDoorRecord, DOORS, asking, chatDoorLine, parseChatDoorLine, parseChatDoors } from '../chatDoors';
import { parseChatUsageLine } from '../chatUsage';

/**
 * How often the chat was reached for — the count a turn ledger cannot answer.
 *
 * <p>The assertions about the OTHER ledger are the load-bearing ones. This record was going to share
 * `chat-usage.jsonl` until the plan round showed what that file's parser actually requires, and the
 * two tests below are that finding kept where it can fail.</p>
 */

const DOOR: ChatDoorRecord = {
  utc: '2026-09-12T10:00:00.000Z',
  door: 'take',
  provider: 'antigravity',
  model: 'gemini-3.8-flash',
};

test('a door record survives the trip to disk and back', () => {
  const back = parseChatDoorLine(chatDoorLine(DOOR));

  assert.deepStrictEqual(back, DOOR);
});

test('the line ends in a newline, because a ledger is appended to', () => {
  // Without it two invocations in the same second are one unreadable line.
  assert.strictEqual(chatDoorLine(DOOR).endsWith('\n'), true);
  assert.strictEqual(chatDoorLine(DOOR).trimEnd().includes('\n'), false, 'the record was written over several lines');
});

test('a turn line is not a door record', () => {
  // The direction that can actually happen: a reader pointed at the wrong file. A turn carries no
  // door, and a count of invocations built out of answers would be a different number wearing this
  // one's name.
  const turn = '{"utc":"2026-09-12T10:00:00.000Z","provider":"antigravity","model":"g","tokensIn":10,'
    + '"tokensOut":2,"costUsd":null,"seconds":3,"outcome":"answered","conversation":"c1","title":"t"}';

  assert.strictEqual(parseChatDoorLine(turn), undefined, 'a finished turn was counted as an invocation');
});

test('a door line would be read as a TURN by the ledger it does not live in', () => {
  // THE REASON THESE RECORDS HAVE A FILE OF THEIR OWN, as a test rather than as a paragraph.
  // `parseChatUsageLine` requires a non-empty `utc` and nothing else, so a door line in that file is
  // a conversation that cost nothing — a phantom row in the rounds table, and in any older build
  // somebody rolls back to. Asserted here so that moving these records back into that file breaks a
  // test instead of a page. (codex and gemini, the plan round, independently.)
  const asTurn = parseChatUsageLine(chatDoorLine(DOOR));

  assert.notStrictEqual(asTurn, undefined, 'the premise changed: the turn parser now rejects a door line');
  assert.strictEqual(asTurn?.tokensIn, 0, 'the premise changed: a door line no longer reads as an empty turn');
});

test('a torn line is dropped rather than taking the history with it', () => {
  const text = [
    chatDoorLine(DOOR).trimEnd(),
    '{"utc":"2026-09-12T10:00:01.000Z","door":"ad',
    '',
    'not json at all',
    '{"door":"add"}',
    '{"utc":"2026-09-12T10:00:02.000Z"}',
    chatDoorLine({ ...DOOR, door: 'add' }).trimEnd(),
  ].join('\n');

  const read = parseChatDoors(text);

  assert.deepStrictEqual(read.map((one) => one.door), ['take', 'add'],
    'a torn line took readable records with it, or a record with no door or no instant was kept');
});

test('a door this version has never heard of still counts as an opening', () => {
  // Strict where it is WRITTEN, tolerant where it is READ. A sixth door added later must not vanish
  // from the count in a build that predates it — it is still the chat being reached for.
  const later = parseChatDoorLine('{"utc":"2026-09-12T10:00:00.000Z","door":"whatever-comes-next"}');

  assert.strictEqual(later?.door, 'whatever-comes-next', 'a later version of this ledger was thrown away');
  assert.strictEqual(asking(later?.door ?? ''), false, 'an unknown door was counted as a question');
});

test('Asked is take and add, and nothing else', () => {
  assert.deepStrictEqual(DOORS.filter((door) => asking(door)), ['take', 'add']);
  assert.deepStrictEqual(DOORS.filter((door) => !asking(door)), ['key', 'default', 'choose']);
});

test('a door that resolved no model keeps the empty strings rather than inventing them', () => {
  // Nothing was configured, which is a real state: the row it lands in says so instead of naming a
  // vendor that was never chosen.
  const nowhere = parseChatDoorLine('{"utc":"2026-09-12T10:00:00.000Z","door":"choose"}');

  assert.deepStrictEqual(nowhere, { utc: '2026-09-12T10:00:00.000Z', door: 'choose', provider: '', model: '' });
});
