import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatDoorRecord, chatDoorRecord } from '../chatDoors';
import { CHAT_DOORS_FILE, chatDoorsPath, readChatDoors, recordChatDoor } from '../chatDoorsFile';
import { flushChatUsage } from '../chatUsageFile';

/**
 * The door ledger's file, against a real directory.
 *
 * <p>`chatDoors.ts` decides what a record MEANS and is tested without a disk; `chatWiring.test.ts`
 * checks that every command writes one. Neither of them calls the two functions that touch the
 * filesystem, so a regression in the path, in the shared appender or in the read could pass both
 * suites while the counts on the page stayed at zero. (CodeRabbit, PR #209.)</p>
 *
 * <p>The flush is `flushChatUsage` on purpose: the queue in `jsonlLedger.ts` is SHARED by both chat
 * ledgers, which is what lets `deactivate` drain them with one await. A test that waited on a queue
 * of its own would be testing a thing that does not exist.</p>
 */

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-doors-'));
}

const NOON = new Date('2026-09-12T12:00:00.000Z');

test('a door written to a real directory can be read back', async () => {
  const dir = home();
  try {
    await recordChatDoor(dir, chatDoorRecord('take', 'preset-4', 'gemini-3.8-flash', NOON, 'antigravity'));
    const back = await readChatDoors(dir);

    assert.deepStrictEqual([...back], [{
      utc: '2026-09-12T12:00:00.000Z',
      door: 'take',
      provider: 'preset-4',
      model: 'gemini-3.8-flash',
      vendor: 'antigravity',
    }]);
    assert.strictEqual(chatDoorsPath(dir), join(dir, CHAT_DOORS_FILE), 'the ledger is not where the path says');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a second invocation is APPENDED, never written over the first', async () => {
  // The whole point of a ledger. A truncating write would lose a day of counts to one keypress.
  const dir = home();
  try {
    await recordChatDoor(dir, chatDoorRecord('take', 'preset-4', 'g', NOON, 'antigravity'));
    await recordChatDoor(dir, chatDoorRecord('add', 'preset-4', 'g', NOON, 'antigravity'));
    await flushChatUsage();

    assert.deepStrictEqual((await readChatDoors(dir)).map((one) => one.door), ['take', 'add']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('queued writes land in the order they were asked for', async () => {
  // Two invocations can be issued in either order by a promise chain that is not one; the shared
  // queue is what makes the file's order the person's order.
  const dir = home();
  try {
    const doors: readonly ChatDoorRecord[] = ['key', 'default', 'choose', 'take', 'add']
      .map((one) => chatDoorRecord(one as 'key', 'preset-4', 'g', NOON, 'antigravity'));
    for (const one of doors) {
      void recordChatDoor(dir, one);
    }
    await flushChatUsage();

    assert.deepStrictEqual(
      (await readChatDoors(dir)).map((one) => one.door),
      ['key', 'default', 'choose', 'take', 'add'],
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a directory that does not exist yet is made rather than refused', async () => {
  // On a machine where nobody has run a review round, a chat is the FIRST thing to write anything
  // under the coai data directory.
  const dir = join(home(), 'never', 'been', 'here');
  try {
    await recordChatDoor(dir, chatDoorRecord('key', '', '', NOON));

    assert.strictEqual((await readChatDoors(dir)).length, 1, 'the first invocation on a fresh machine was lost');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a half-written tail costs itself and nothing else', async () => {
  // A ledger is appended to by a process that can be killed mid-write. One torn line must not take
  // the count with it.
  const dir = home();
  try {
    await recordChatDoor(dir, chatDoorRecord('take', 'preset-4', 'g', NOON, 'antigravity'));
    await flushChatUsage();
    const path = chatDoorsPath(dir);
    writeFileSync(path, `${readFileSync(path, 'utf8')}{"utc":"2026-09-12T12:00:01.000Z","doo`, 'utf8');

    assert.deepStrictEqual((await readChatDoors(dir)).map((one) => one.door), ['take']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger that has never been written is no invocations, not a failure', async () => {
  const dir = home();
  try {
    assert.deepStrictEqual([...(await readChatDoors(dir))], [], 'a machine with no history read as an error');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
