import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { appendLine, flushLedgers } from '../jsonlLedger';
import { NotificationRecord, notificationLine } from '../notifications';
import {
  NOTIFICATIONS_FILE,
  SERVER_NOTICES_FILE,
  notificationsPath,
  readNewest,
  readNewestPlaced,
  readNotifications,
  readServerNotices,
  recordNotification,
  serverNoticesPath,
} from '../notificationsFile';

/**
 * The notifications ledgers against a real directory.
 *
 * <p>`notifications.ts` decides what a record MEANS and is tested without a disk. Neither that
 * suite nor any page test calls the functions that touch the filesystem, so a regression in the
 * path, in the backwards read or in the shared appender could pass everything else while the page
 * stayed empty — which is the gap `chatDoorsFile.test.ts` was written for on the ledger next
 * door.</p>
 */

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-notes-'));
}

function record(over: Partial<NotificationRecord> = {}): NotificationRecord {
  return {
    utc: '2026-09-16T12:00:00.000Z',
    class: 'failure',
    source: 'serverSettingsSync',
    code: 'settings-not-written',
    ...over,
  };
}

test('a notification written to a real directory can be read back', async () => {
  const dir = home();
  try {
    await recordNotification(dir, record({ title: 'The settings were left alone' }));

    const back = await readNotifications(dir);

    assert.equal(back.length, 1);
    assert.equal(back[0]?.code, 'settings-not-written');
    assert.equal(back[0]?.title, 'The settings were left alone');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the two halves write two files, and neither reader sees the other', async () => {
  const dir = home();
  try {
    await recordNotification(dir, record({ code: 'from-the-extension' }));
    writeFileSync(
      serverNoticesPath(dir),
      notificationLine(record({ code: 'from-the-server', source: 'PanelService' })),
    );

    const mine = await readNotifications(dir);
    const theirs = await readServerNotices(dir);

    assert.deepEqual(mine.map((r) => r.code), ['from-the-extension']);
    assert.deepEqual(theirs.map((r) => r.code), ['from-the-server']);
    assert.ok(readFileSync(notificationsPath(dir), 'utf8').includes('from-the-extension'));
    assert.notEqual(NOTIFICATIONS_FILE, SERVER_NOTICES_FILE);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the newest N come back in file order, read from the END of the file', async () => {
  const dir = home();
  try {
    const lines = Array.from({ length: 40 }, (_, i) => notificationLine(record({ code: `c${i}` })));
    writeFileSync(notificationsPath(dir), lines.join(''));

    // A window far smaller than the file, so the backwards walk really loops rather than
    // swallowing everything in one read and passing by accident.
    const back = await readNewest(notificationsPath(dir), 5, 64);

    assert.deepEqual(back.map((r) => r.code), ['c35', 'c36', 'c37', 'c38', 'c39']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a multi-byte character split by a window boundary survives the backwards read', async () => {
  const dir = home();
  try {
    // Prose is what `detail` carries, and prose is not ASCII. Decoding each window on its own
    // would put a replacement character wherever a boundary fell inside one of these.
    const detail = 'наступила ошибка — «стенд» не ответил ✔';
    const lines = Array.from({ length: 12 }, (_, i) =>
      notificationLine(record({ code: `c${i}`, detail })));
    writeFileSync(notificationsPath(dir), lines.join(''));

    for (const window of [7, 13, 31, 64]) {
      const back = await readNewest(notificationsPath(dir), 12, window);
      assert.equal(back.length, 12, `window ${window} lost records`);
      for (const row of back) {
        assert.equal(row.detail, detail, `window ${window} corrupted the text`);
      }
    }
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the newest read reaches the first line when the file is shorter than the window', async () => {
  const dir = home();
  try {
    writeFileSync(notificationsPath(dir), notificationLine(record({ code: 'only' })));

    assert.deepEqual((await readNewest(notificationsPath(dir), 10, 64)).map((r) => r.code), ['only']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a missing ledger is nothing yet, not an error, and a limit of nothing reads nothing', async () => {
  const dir = home();
  try {
    assert.deepEqual([...await readNewest(notificationsPath(dir), 10)], []);
    assert.deepEqual([...await readNotifications(dir)], []);
    writeFileSync(notificationsPath(dir), notificationLine(record()));
    assert.deepEqual([...await readNewest(notificationsPath(dir), 0)], []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a write that cannot land tells its caller, and still does not throw', async () => {
  const dir = home();
  try {
    // A FILE where the ledger's parent directory should be: `mkdir` answers ENOTDIR, and no retry
    // fixes it. This is the disk-failure path in the shape a test can actually produce.
    const inTheWay = join(dir, 'inTheWay');
    writeFileSync(inTheWay, 'not a directory');
    let told: unknown;

    await appendLine(
      join(inTheWay, 'notifications.jsonl'),
      'line\n',
      'a notification',
      { chain: 'notifications', onFailure: (reason) => { told = reason; } },
    );

    assert.ok(told !== undefined, 'the ledger must tell its caller, or the gap counter is decoration');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the flush drains writes queued WHILE it is draining', async () => {
  const dir = home();
  try {
    const first = join(dir, 'first.jsonl');
    const second = join(dir, 'second.jsonl');

    // The shape the old snapshot lost: a write issued from the completion of another one. It joins
    // the chain after `flushLedgers` has taken its snapshot, so a flush that returned that snapshot
    // resolved without it and the host exited.
    void appendLine(first, 'one\n', 'a notification', { chain: 'notifications' })
      .then(() => { void appendLine(second, 'two\n', 'a notification', { chain: 'notifications' }); });

    await flushLedgers();

    assert.equal(readFileSync(first, 'utf8'), 'one\n');
    assert.equal(readFileSync(second, 'utf8'), 'two\n', 'the second write was still queued');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

const NEWLINE = String.fromCharCode(10);

test('a write that never settles does not hold the window open, and says so', async () => {
  // The blocking finding of the code round. The ceiling was checked only AFTER the drain resolved,
  // which bounds a drain that is making progress and does nothing at all about the one case a
  // ceiling exists for: an append pending on a data directory that has stopped answering — the
  // operator's is a NAS. The await never returned, the deadline was never read, and the window
  // would not close until VS Code killed the host, losing the whole tail rather than the part that
  // could not be written. The docstring promised a ceiling the code did not have. (codex, Blocking;
  // the local reviewer found the same thing from the performance side.)
  const dir = home();
  try {
    const slow = join(dir, 'slow.jsonl');
    // A chain that cannot possibly finish inside the deadline. It is a long queue rather than a
    // hung handle because a hung handle is not something a test can produce on demand - but the
    // mechanism under test is the same one, and it is the only one: whether the ceiling is read
    // while the chain is still busy, or only after it has finished.
    for (let n = 0; n < 500; n += 1) {
      void appendLine(slow, `${n}
`, 'a notification', { chain: 'notifications' });
    }

    // The RESULT, not the clock. An earlier version timed the call with Date.now() and asserted it
    // returned inside two seconds, which is a wall-clock read in a test: it flaked once already,
    // under load from its own queue, and CodeRabbit named the pattern. The boolean is the contract
    // and it is deterministic - the unbounded version returned true here, after waiting 13.4s for a
    // queue it had been given 1ms for. What is no longer asserted is HOW LONG it took; the suite's
    // own timeout is what would catch a drain that never returns at all.
    const drained = await flushLedgers(1);

    assert.equal(drained, false, 'it gave up, and said so instead of reporting a clean drain');

    // And nothing is corrupted by giving up: the writes were not cancelled, only un-awaited.
    assert.equal(await flushLedgers(30_000), true, 'the rest lands, and a complete drain says true');
    const lines = readFileSync(slow, 'utf8').split(NEWLINE).filter((line) => line.length > 0);
    assert.equal(lines.length, 500);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a drain with no time left gives up at once rather than starting a wait it cannot finish', async () => {
  assert.equal(await flushLedgers(0), false, 'no time is not a little time');
  assert.equal(await flushLedgers(-5), false);
});

test('every record comes back with the byte offset its own line starts at', async () => {
  // The unit the watermark is written in. An acknowledgement says "bytes [start, end) of this
  // ledger were loaded", and a reader that returned records without saying WHERE they were could
  // only ever acknowledge the whole file - which claims the person read the thousands that were
  // never rendered. A byte offset on an append-only file is also monotone by construction, which a
  // `utc` from each writer's own clock is not.
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    const rows = [
      notificationLine(record({ code: 'one' })).trim(),
      notificationLine(record({ code: 'two', title: 'сообщение об ошибке — многобайтное' })).trim(),
      notificationLine(record({ code: 'three' })).trim(),
    ];
    writeFileSync(path, rows.join(NEWLINE) + NEWLINE);

    const read = await readNewestPlaced(path, 10);

    assert.deepEqual(read.records.map((p) => p.record.code), ['one', 'two', 'three']);
    let at = 0;
    for (const [index, placed] of read.records.entries()) {
      assert.equal(placed.at, at, `record ${index} starts at ${at}`);
      at += Buffer.byteLength(rows[index] as string, 'utf8') + 1;
    }
    assert.equal(read.start, 0, 'the whole file was loaded');
    assert.equal(read.end, at, 'and `end` is the file, which ends on a newline');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a half-written line moves neither the records nor the end offset', async () => {
  // A snapshot can catch another process midway through an append. Acknowledging through a partial
  // line would skip that record for ever once it was finished - it would fall below a watermark
  // that had already passed it. Found by measuring against a real file: the first version of this
  // counted the partial line as complete and put `end` one byte past the file.
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    const whole = notificationLine(record({ code: 'landed' }));
    writeFileSync(path, whole + '{"utc":"2026-09-17T09:00:01.000Z","cla');

    const read = await readNewestPlaced(path, 10);

    assert.deepEqual(read.records.map((p) => p.record.code), ['landed']);
    assert.equal(read.end, Buffer.byteLength(whole, 'utf8'), 'the last COMPLETE newline, not the size');
    assert.ok(
      read.end < readFileSync(path).length,
      'and the file really is longer than that, or this test is not testing anything',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the newest few start where the newest few start, not where the window did', async () => {
  // `start` is the offset of the first record KEPT, not of the 64 KB window the walk stopped in.
  // Acknowledging from the window boundary would cover records that were read off the disk and
  // then dropped for being older than the limit.
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    const rows = Array.from({ length: 8 }, (_, n) => notificationLine(record({ code: `c-${n}` })).trim());
    writeFileSync(path, rows.join(NEWLINE) + NEWLINE);
    const before = rows.slice(0, 5).reduce((total, row) => total + Buffer.byteLength(row, 'utf8') + 1, 0);

    const read = await readNewestPlaced(path, 3);

    assert.deepEqual(read.records.map((p) => p.record.code), ['c-5', 'c-6', 'c-7']);
    assert.equal(read.start, before, 'the first kept record, counted by hand');
    assert.equal(read.records[0]?.at, before);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger that is not there acknowledges an empty range rather than everything', async () => {
  const dir = home();
  try {
    const read = await readNewestPlaced(join(dir, 'absent.jsonl'), 10);

    assert.deepEqual(read.records, []);
    assert.equal(read.start, 0);
    assert.equal(read.end, 0, 'nothing was read, so nothing may be marked read');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
