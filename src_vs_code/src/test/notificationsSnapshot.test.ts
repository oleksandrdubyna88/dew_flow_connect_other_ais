import assert from 'node:assert/strict';
import { test } from 'node:test';
import { NotificationRecord } from '../notifications';
import { NOTIFICATIONS_FILE, PlacedRecord, SERVER_NOTICES_FILE } from '../notificationsFile';
import { SeenRange } from '../notificationsSeen';
import {
  LedgerRead,
  UNREADABLE_LEDGER,
  WhatWasRead,
  snapshotOf,
} from '../notificationsSnapshot';

/**
 * The half-dozen decisions one draw makes, now that they can be RUN.
 *
 * <p>Every one of these used to live inside `notificationsPanel.ts`, which imports `vscode` and can
 * therefore never be imported by a test here — so the rules below were asserted by nothing, in the
 * one place where getting them wrong marks records read that nobody saw. That is the whole reason
 * the function they moved into exists.</p>
 */

const AT = Date.UTC(2026, 8, 17, 9, 0, 0);

function record(over: Partial<NotificationRecord> = {}): NotificationRecord {
  return {
    utc: new Date(AT).toISOString(),
    class: 'failure',
    source: 'serverSettingsSync',
    code: 'the-mirror-stood-down',
    run: 'run-a',
    ...over,
  };
}

const placed = (at: number, over: Partial<NotificationRecord> = {}): PlacedRecord =>
  ({ at, record: record(over) });

function ledger(over: Partial<LedgerRead> = {}): LedgerRead {
  return { records: [], start: 0, end: 0, readable: true, ...over };
}

function read(over: Partial<WhatWasRead> = {}): WhatWasRead {
  return {
    dataDir: 'V:/connectOtherAis',
    mine: ledger(),
    theirs: ledger(),
    seen: [],
    generation: 1,
    notice: '',
    waitedSeconds: 20,
    ...over,
  };
}

const seenRange = (ledgerName: string, from: number, to: number): SeenRange =>
  ({ utc: 'u', ledger: ledgerName, from, to });

test('both ledgers reach the page, each remembering which file it came out of', () => {
  const snapshot = snapshotOf(read({
    mine: ledger({ records: [placed(0, { code: 'from-the-extension' })], end: 90 }),
    theirs: ledger({ records: [placed(0, { code: 'from-the-server' })], end: 40 }),
  }));

  assert.equal(snapshot.state.loaded, 2);
  assert.equal(snapshot.state.rows.find((row) => row.code === 'from-the-server')?.ledger, 'server');
  assert.equal(snapshot.state.rows.find((row) => row.code === 'from-the-extension')?.ledger, 'extension');
  assert.deepEqual(snapshot.loaded.get(NOTIFICATIONS_FILE), { from: 0, to: 90 });
  assert.deepEqual(snapshot.loaded.get(SERVER_NOTICES_FILE), { from: 0, to: 40 });
});

test('a draw that could not read OFFERS NOTHING, and says so rather than rendering empty tabs', () => {
  // The two halves of one decision, which is why they are computed together. A person shown an
  // empty page concludes there is nothing to see; a host that claimed the window anyway would mark
  // three thousand records read on the strength of a read that failed.
  for (const broken of [
    { mine: UNREADABLE_LEDGER },
    { theirs: UNREADABLE_LEDGER },
    { seen: undefined },
  ]) {
    const snapshot = snapshotOf(read({
      mine: ledger({ records: [placed(0)], end: 90 }),
      theirs: ledger({ end: 40 }),
      ...broken,
    }));

    assert.match(snapshot.state.unreadable ?? '', /did not answer within 20 seconds/u, JSON.stringify(broken));
    assert.equal(snapshot.loaded.size, 0, `${JSON.stringify(broken)}: nothing may be claimed`);
  }
});

test('a readable draw says nothing about being unreadable', () => {
  const snapshot = snapshotOf(read({ mine: ledger({ records: [placed(0)], end: 90 }) }));

  assert.equal('unreadable' in snapshot.state, false, 'an absent field must be absent, not empty');
  assert.equal(snapshot.loaded.size, 2);
});

test('a range left over from a ROTATED ledger marks nothing, and does not reach the other one', () => {
  // `[0, 400000)` against a 90-byte replacement is not a claim about this file. Dropping it errs
  // toward UNREAD; keeping it — or cutting it down to 90 — marks every record in the new file read.
  const snapshot = snapshotOf(read({
    mine: ledger({ records: [placed(0)], end: 90 }),
    theirs: ledger({ records: [placed(0, { code: 'theirs' })], end: 40 }),
    seen: [seenRange(NOTIFICATIONS_FILE, 0, 400_000)],
  }));

  assert.deepEqual([...(snapshot.covered.get(NOTIFICATIONS_FILE) ?? [])], []);
  assert.deepEqual([...(snapshot.covered.get(SERVER_NOTICES_FILE) ?? [])], []);
  assert.equal(snapshot.state.rows.every((row) => !row.read), true, 'so every row is unread');
});

test('what the disk already says is read is carried through, per ledger', () => {
  const snapshot = snapshotOf(read({
    mine: ledger({ records: [placed(0)], end: 90 }),
    theirs: ledger({ end: 40 }),
    seen: [seenRange(NOTIFICATIONS_FILE, 0, 90), seenRange(SERVER_NOTICES_FILE, 0, 40)],
  }));

  assert.deepEqual([...(snapshot.covered.get(NOTIFICATIONS_FILE) ?? [])], [{ from: 0, to: 90 }]);
  assert.deepEqual([...(snapshot.covered.get(SERVER_NOTICES_FILE) ?? [])], [{ from: 0, to: 40 }]);
  assert.equal(snapshot.state.rows[0]?.read, true, 'and the row it covers is marked read');
});

test('older records are reported whether they are BELOW the window or below an acknowledgement', () => {
  // Two different facts with one sentence: the window did not reach the start of the file, or it
  // did and something under an acknowledged range was never acknowledged.
  assert.equal(snapshotOf(read()).state.older, false, 'a file read from the start, fully acknowledged');
  assert.equal(
    snapshotOf(read({ mine: ledger({ start: 400, end: 900 }) })).state.older,
    true,
    'the window begins past the start of the file',
  );
  assert.equal(
    snapshotOf(read({ theirs: ledger({ start: 400, end: 900 }) })).state.older,
    true,
    'and the other ledger counts too',
  );
  assert.equal(
    snapshotOf(read({
      mine: ledger({ end: 900 }),
      seen: [seenRange(NOTIFICATIONS_FILE, 500, 900)],
    })).state.older,
    true,
    'a range that does not start at zero leaves records behind it',
  );
});

test('a notice the host is holding travels with the page, and an empty one is not a field', () => {
  assert.equal(snapshotOf(read({ notice: 'it could not be written' })).state.notice, 'it could not be written');
  assert.equal('notice' in snapshotOf(read()).state, false);
  assert.equal(snapshotOf(read({ generation: 42 })).state.generation, 42);
});
