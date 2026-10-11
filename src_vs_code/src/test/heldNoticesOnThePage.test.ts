import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { NotificationRecord } from '../notifications';
import { NOTIFICATIONS_FILE } from '../notificationsFile';
import { heldArrival, snapshotOf, type LedgerRead } from '../notificationsSnapshot';
import { SETTING_PATH_MISSING } from '../pathFindings';

/**
 * A notice held in memory (todo/PLAN_paths_per_side.md E5.0) reaches the notifications page for this window run: it was
 * written to no ledger — a write would create the missing data folder it reports — so the page is the one place it can
 * be read after the toast has gone, and it says there that it was never written down.
 */

const ledger: LedgerRead = { records: [], start: 0, end: 0, readable: true };

const held: NotificationRecord = {
  utc: '2026-10-10T12:00:00.000Z', class: 'failure', source: 'pathWatch', code: SETTING_PATH_MISSING,
  subject: 'coai.dataDirectory|Z:\\coai', title: "This window's data folder Z:\\coai was not found on this machine (Windows) just now — nothing is written there until it exists.",
};

test('a held notice is a row of the page, on the extension\'s side, never read, saying it is held in memory', () => {
  const snapshot = snapshotOf({ dataDir: 'Z:\\coai', mine: ledger, theirs: ledger, seen: [], generation: 1, notice: '', waitedSeconds: 5, held: [held] });

  const row = snapshot.state.rows.find((one) => one.code === SETTING_PATH_MISSING);
  assert.ok(row !== undefined, 'the held notice is not on the page — the toast was its only surface');
  assert.equal(row.ledger, 'extension');
  assert.equal(row.read, false, 'a notice nobody could write down cannot have been acknowledged');
  assert.equal(snapshot.state.loaded, 1);
  assert.deepEqual(snapshot.loaded.get(NOTIFICATIONS_FILE), { from: 0, to: 0 }, 'a held row claims no byte of the ledger as read');
  assert.match(heldArrival(held).record.detail ?? '', /Held in this window's memory, written to no ledger/u);
  assert.equal(heldArrival(held).at, -1);
});

test('a draw with nothing held is the draw it always was', () => {
  const snapshot = snapshotOf({ dataDir: 'Z:\\coai', mine: ledger, theirs: ledger, seen: [], generation: 1, notice: '', waitedSeconds: 5 });

  assert.deepEqual(snapshot.state.rows, []);
});
