import assert from 'node:assert/strict';
import {
  appendFileSync,
  closeSync,
  mkdtempSync,
  openSync,
  rmSync,
  statSync,
  writeFileSync,
  writeSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { COUNT_CAP, NOT_LOOKED, countSentence, openLabel } from '../notificationsCount';
import { glanceAtLedgers } from '../notificationsGlance';
import {
  NOTIFICATIONS_FILE,
  SERVER_NOTICES_FILE,
  notificationsPath,
  serverNoticesPath,
} from '../notificationsFile';
import { SEEN_FILE, seenLine, tailBegins } from '../notificationsSeen';
import { forgetSeen, readSoFarCheaply } from '../notificationsSeenCache';

/**
 * The cheap look, and the sentence it becomes.
 *
 * <p>Two promises, pulling against each other. The panel re-renders every window every five seconds
 * whatever happened, so this path must not parse a record — and it must still never render a
 * broken read as a clean zero, which is the whole reason the feature exists.</p>
 */

const NEWLINE = String.fromCharCode(10);

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-glance-'));
}

/** `many` records in one ledger. Not valid JSON: nothing on this path may parse one. */
function ledger(path: string, many: number): void {
  writeFileSync(path, Array.from({ length: many }, (_, n) => `record ${n}`).join(NEWLINE) + NEWLINE);
}

function acknowledged(dir: string, ledgerName: string, from: number, to: number): void {
  appendFileSync(join(dir, SEEN_FILE), seenLine({ utc: 'u', ledger: ledgerName, from, to }));
}

test('nothing written down yet is not the same sentence as nothing new', () => {
  assert.equal(countSentence(NOT_LOOKED), 'Nothing has been written down yet.');
  assert.equal(
    countSentence({ ...NOT_LOOKED, readable: false }),
    'The notifications could not be read.',
    'and neither of them is a zero',
  );
  assert.equal(countSentence({ ...NOT_LOOKED, anyRecords: true }), 'Nothing new.');
  assert.equal(
    countSentence({ ...NOT_LOOKED, anyRecords: true, older: true }),
    'Nothing new. Some older ones have not been opened.',
  );
});

test('a count that stopped at its cap says so rather than naming a number it did not earn', () => {
  assert.equal(countSentence({ ...NOT_LOOKED, anyRecords: true, unread: 1 }), '1 new notification.');
  assert.equal(countSentence({ ...NOT_LOOKED, anyRecords: true, unread: 4 }), '4 new notifications.');
  assert.equal(
    countSentence({ ...NOT_LOOKED, anyRecords: true, unread: 3000, more: true }),
    '3000+ new notifications.',
  );
  assert.equal(openLabel({ ...NOT_LOOKED, unread: 2 }), 'Read them');
  assert.equal(openLabel(NOT_LOOKED), 'Open notifications');
});

test('a first run — no files at all — is readable and empty, never a failure', async () => {
  const dir = home();
  forgetSeen();
  try {
    assert.deepEqual(await glanceAtLedgers(dir), {
      readable: true, anyRecords: false, unread: 0, more: false, older: false,
    });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('both ledgers are counted, and what has been acknowledged is not', async () => {
  const dir = home();
  forgetSeen();
  try {
    ledger(notificationsPath(dir), 10);
    ledger(serverNoticesPath(dir), 4);
    // The first three records of this side's ledger, by byte: three lines of 'record N' at nine
    // bytes each, eight for the text and one for the newline.
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 27);

    const glance = await glanceAtLedgers(dir);

    assert.equal(glance.readable, true);
    assert.equal(glance.anyRecords, true);
    assert.equal(glance.unread, 11, 'seven of mine and four of theirs');
    assert.equal(glance.more, false);
    assert.equal(glance.older, false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the two ledgers share ONE count budget, and cannot together exceed the cap', async () => {
  // Two caps of 3000 would walk 6000 newlines and report "6000 new" — a number above the cap the
  // sentence documents, earned by twice the work the cap exists to bound. (codex, the S5 round.)
  const dir = home();
  forgetSeen();
  try {
    ledger(notificationsPath(dir), COUNT_CAP - 500);
    ledger(serverNoticesPath(dir), 1000);

    const glance = await glanceAtLedgers(dir);

    assert.equal(glance.unread, COUNT_CAP, 'the budget is spent, not doubled');
    assert.equal(glance.more, true, 'and the sentence says "+" rather than a figure it did not earn');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

/*
 * What is NOT tested here, said plainly: no test in this file produces a genuine unreadable ledger.
 * The filesystem will not do it on demand — a directory in a ledger's place stats as ZERO BYTES, so
 * the backwards walk never runs and "readable, nothing to count" is an honest answer; on Windows
 * `open` on a directory succeeds and fails at the first read; and a path inside a file answers
 * ENOENT rather than ENOTDIR. All three were measured. So the decision itself — missing versus
 * broken — is pinned directly in `notificationsSeen.test.ts` against real error shapes, and every
 * caller here routes through that one function.
 */


test('the acknowledgements are read once and then only where they GREW', async () => {
  const dir = home();
  forgetSeen();
  try {
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 100);
    const first = await readSoFarCheaply(dir);

    assert.equal(tailBegins(first?.get(NOTIFICATIONS_FILE) ?? []), 100);

    acknowledged(dir, NOTIFICATIONS_FILE, 100, 250);
    const second = await readSoFarCheaply(dir);

    assert.equal(tailBegins(second?.get(NOTIFICATIONS_FILE) ?? []), 250, 'the appended line arrived');
    assert.deepEqual(
      [...(second?.get(NOTIFICATIONS_FILE) ?? [])],
      [{ from: 0, to: 250 }],
      'and merged with what was already held, rather than sitting beside it',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the bytes already consumed are never read again — observed, not assumed', async () => {
  // A PROBE, and it does something the product never does: it overwrites bytes the file has already
  // had. `notifications-seen.jsonl` is append-only, so this cannot happen in life — which is exactly
  // what makes it a way to SEE whether the prefix was re-read. An incremental reader still reports
  // the first range because it parsed it once; a reader that re-read the file would lose it. Without
  // this, the "only where it grew" test passes against an implementation that reads everything every
  // time, and the finding it answers would be unfixed with a green suite.
  const dir = home();
  forgetSeen();
  try {
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 100);
    const path = join(dir, SEEN_FILE);
    const held = await readSoFarCheaply(dir);

    assert.equal(tailBegins(held?.get(NOTIFICATIONS_FILE) ?? []), 100);

    const wasThere = statSync(path).size;
    const fd = openSync(path, 'r+');
    try {
      const rubbish = Buffer.alloc(wasThere - 1, 0x58);
      writeSync(fd, rubbish, 0, rubbish.length, 0);
    } finally {
      closeSync(fd);
    }
    acknowledged(dir, NOTIFICATIONS_FILE, 100, 250);
    const after = await readSoFarCheaply(dir);

    assert.deepEqual(
      [...(after?.get(NOTIFICATIONS_FILE) ?? [])],
      [{ from: 0, to: 250 }],
      'the first range survives, so its bytes were not re-read',
    );
    forgetSeen();
    const cold = await readSoFarCheaply(dir);

    assert.deepEqual(
      [...(cold?.get(NOTIFICATIONS_FILE) ?? [])],
      [{ from: 100, to: 250 }],
      'and the control: a cold read of that same file really has lost the first line',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger that got SHORTER is not this ledger any more, and is read from the start', async () => {
  // Rotated, restored from a backup, replaced by hand. Keeping the old offsets would mark records
  // read that this file never held — and the person would have no way to know why it is empty.
  const dir = home();
  forgetSeen();
  try {
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 400);
    acknowledged(dir, NOTIFICATIONS_FILE, 400, 900);
    assert.equal(tailBegins((await readSoFarCheaply(dir))?.get(NOTIFICATIONS_FILE) ?? []), 900);

    writeFileSync(join(dir, SEEN_FILE), seenLine({ utc: 'u', ledger: NOTIFICATIONS_FILE, from: 0, to: 40 }));
    const after = await readSoFarCheaply(dir);

    assert.deepEqual([...(after?.get(NOTIFICATIONS_FILE) ?? [])], [{ from: 0, to: 40 }], 'only what the new file says');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a half-written last line is left for next time rather than parsed in halves', async () => {
  const dir = home();
  forgetSeen();
  try {
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 100);
    await readSoFarCheaply(dir);

    appendFileSync(join(dir, SEEN_FILE), '{"utc":"u","ledger":"notifications.jsonl","from":100,"to');
    const torn = await readSoFarCheaply(dir);

    assert.equal(tailBegins(torn?.get(NOTIFICATIONS_FILE) ?? []), 100, 'the half line is not a range');

    appendFileSync(join(dir, SEEN_FILE), `":900}${NEWLINE}`);
    const whole = await readSoFarCheaply(dir);

    assert.equal(tailBegins(whole?.get(NOTIFICATIONS_FILE) ?? []), 900, 'and it counts once it is complete');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the two ledgers keep their own ranges through the cache as well', async () => {
  const dir = home();
  forgetSeen();
  try {
    acknowledged(dir, NOTIFICATIONS_FILE, 0, 5000);
    acknowledged(dir, SERVER_NOTICES_FILE, 0, 120);
    const spans = await readSoFarCheaply(dir);

    assert.equal(tailBegins(spans?.get(NOTIFICATIONS_FILE) ?? []), 5000);
    assert.equal(tailBegins(spans?.get(SERVER_NOTICES_FILE) ?? []), 120, 'and the small one is not swallowed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
