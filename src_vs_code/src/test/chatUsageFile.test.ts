import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatTurnRecord } from '../chatUsage';
import { CHAT_USAGE_FILE, chatUsagePath, flushChatUsage, readChatUsage, recordChatTurn } from '../chatUsageFile';

/**
 * The chat ledger's file, against a real directory.
 *
 * <p>`chatUsage.ts` decides what a record MEANS and is tested without a disk. This is the other half:
 * that the line reaches the file, that the file can be read back, and that none of the ways it can go
 * wrong reaches the person whose answer is on screen.</p>
 *
 * <p>What is NOT tested here is several processes appending at once — a unit test cannot fork four
 * writers and mean anything by it. That question was measured instead: `npm run measure:append`.</p>
 */

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-ledger-'));
}

function record(over: Partial<ChatTurnRecord> = {}): ChatTurnRecord {
  return {
    utc: '2026-09-09T20:00:00.000Z',
    provider: 'claude',
    model: 'sonnet',
    tokensIn: 1000,
    tokensOut: 200,
    costUsd: 0.107958,
    seconds: 4,
    outcome: 'answered',
    conversation: 'tab-1',
    title: 'PLAN_a_turn.md',
    ...over,
  };
}

test('a turn written down comes back exactly as it went in', async () => {
  const dir = home();
  try {
    const one = record();
    await recordChatTurn(dir, one);

    assert.deepStrictEqual(await readChatUsage(dir), [one]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the ledger APPENDS — a second turn does not replace the first', async () => {
  // The defect this guards against is a plain write, which truncates: a conversation would then have
  // cost whatever its last turn cost, for ever.
  const dir = home();
  try {
    await recordChatTurn(dir, record({ utc: '2026-09-09T20:00:00.000Z', tokensIn: 100 }));
    await recordChatTurn(dir, record({ utc: '2026-09-09T20:00:05.000Z', tokensIn: 200 }));
    const back = await readChatUsage(dir);

    assert.strictEqual(back.length, 2);
    assert.deepStrictEqual(back.map((row) => row.tokensIn), [100, 200], 'in the order they happened');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('turns queued together land in the order they were asked, not the order they resolved', async () => {
  // Two `await`ed appends from one process can be issued in either order. The file's order is the
  // conversation's order because the writes are chained, and this is what says so.
  const dir = home();
  try {
    const many = [1, 2, 3, 4, 5].map((n) => record({ utc: `2026-09-09T20:00:0${n}.000Z`, tokensIn: n }));
    await Promise.all(many.map((one) => recordChatTurn(dir, one)));

    assert.deepStrictEqual(
      (await readChatUsage(dir)).map((row) => row.tokensIn),
      [1, 2, 3, 4, 5],
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a data directory that does not exist yet is created rather than refused', async () => {
  // On a machine where nobody has run a review round, the chat is the FIRST thing to write anything
  // under the coai data directory.
  const dir = join(home(), 'not', 'there', 'yet');
  try {
    await recordChatTurn(dir, record());

    assert.strictEqual((await readChatUsage(dir)).length, 1);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger nobody has written is empty, which is not an error', async () => {
  const dir = home();
  try {
    assert.deepStrictEqual(await readChatUsage(dir), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a half-written last line costs itself and nothing else', async () => {
  // The file is appended to while it is read, so its tail can be a fragment. Losing the newest turn
  // until the next tick is the bargain `usage.ts` already makes for the server's ledger.
  const dir = home();
  try {
    await recordChatTurn(dir, record({ tokensIn: 111 }));
    writeFileSync(chatUsagePath(dir), '{"utc":"2026-09-09T20:00:0', { flag: 'a' });
    const back = await readChatUsage(dir);

    assert.strictEqual(back.length, 1);
    assert.strictEqual(back[0]?.tokensIn, 111);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger that cannot be written costs the record and never the answer', async () => {
  // A person's answer must not fail because a disk was full or a path was refused. The call resolves,
  // it does not reject, and nothing is left behind — `chatUsageFile.ts` reports it on the console.
  const dir = home();
  try {
    // A FILE where the directory should be: `mkdir` then fails on every write, on every platform.
    const blocked = join(dir, 'blocked');
    writeFileSync(blocked, 'this is a file, not a directory');

    await assert.doesNotReject(() => recordChatTurn(join(blocked, 'inside'), record()));
    assert.deepStrictEqual(await readChatUsage(join(blocked, 'inside')), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('one line per turn, newline-terminated, so the file stays appendable', async () => {
  const dir = home();
  try {
    await recordChatTurn(dir, record());
    await recordChatTurn(dir, record({ utc: '2026-09-09T20:00:09.000Z' }));
    const text = readFileSync(chatUsagePath(dir), 'utf8');

    assert.ok(text.endsWith('\n'), 'a file not ending in a newline tears the NEXT write');
    assert.strictEqual(text.split('\n').filter((line) => line.length > 0).length, 2);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the ledger is NOT the server\u2019s usage.jsonl', () => {
  // The whole reason this module exists. The server appends to its own file while it runs a round,
  // and it ships on its own days; a file with two writers that release separately is a file whose
  // format is a contract nobody wrote down.
  assert.strictEqual(CHAT_USAGE_FILE, 'chat-usage.jsonl');
  assert.notStrictEqual(CHAT_USAGE_FILE, 'usage.jsonl');
  assert.strictEqual(chatUsagePath('D:/data'), join('D:/data', 'chat-usage.jsonl'));
});


test('a flush waits for every queued write, so a closing window does not drop the last turn', async () => {
  // The turn path deliberately does NOT wait for the disk — a person's answer must not — which
  // means a host closed the instant a turn ends can take the record with it. `deactivate` returns
  // this, and VS Code awaits what `deactivate` returns. (codex and the local reviewer, the code
  // round.)
  const dir = home();
  try {
    for (let n = 1; n <= 5; n += 1) {
      void recordChatTurn(dir, record({ utc: `2026-09-10T20:00:0${n}.000Z`, tokensIn: n }));
    }
    await flushChatUsage();

    assert.deepStrictEqual(
      (await readChatUsage(dir)).map((row) => row.tokensIn),
      [1, 2, 3, 4, 5],
      'a write that had been queued was still in flight after the flush',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a write that failed does not stall the writes queued behind it', async () => {
  // The queue is one chain, so a rejection on it would poison every later turn. The catch is INSIDE
  // the link, which is what keeps the chain resolving. (the local reviewer, the code round — it
  // asked for the guarantee the code already had, and this is what says so.)
  const dir = home();
  try {
    const blocked = join(dir, 'blocked');
    writeFileSync(blocked, 'a file where a directory should be');

    void recordChatTurn(join(blocked, 'inside'), record({ tokensIn: 111 }));
    await recordChatTurn(dir, record({ tokensIn: 222 }));

    assert.deepStrictEqual(
      (await readChatUsage(dir)).map((row) => row.tokensIn),
      [222],
      'a turn behind a failed write was lost',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
