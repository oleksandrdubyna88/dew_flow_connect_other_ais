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
