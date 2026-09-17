import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Notice, answered, gapRecord, gapSentence, noticeRecord } from '../notice';

/**
 * What a notice MEANS, decided without a disk and without `vscode`.
 *
 * <p>`notify.ts` imports `vscode` and therefore cannot be imported by any test in this repository,
 * which is exactly why everything worth asserting lives here — the same split that lets
 * `chatDoors.ts` be tested while `chatDoorsFile.ts`'s caller is not.</p>
 */

const NOON = new Date('2026-09-16T12:00:00.000Z');

function notice(over: Partial<Notice> = {}): Notice {
  return {
    as: 'warning',
    class: 'stand-down',
    source: 'serverSettingsSync',
    code: 'settings-stood-down',
    title: 'The server settings were left alone',
    ...over,
  };
}

test('a notice becomes a record carrying the run, the pid and the clock it was given', () => {
  const record = noticeRecord(notice(), 'a1c9f0', 37308, NOON);

  assert.equal(record.utc, '2026-09-16T12:00:00.000Z');
  assert.equal(record.run, 'a1c9f0');
  assert.equal(record.pid, 37308);
  assert.equal(record.class, 'stand-down');
  assert.equal(record.code, 'settings-stood-down');
});

test('only the optional fields that were given reach the record', () => {
  const bare = noticeRecord(notice(), 'r', 1, NOON);
  const full = noticeRecord(
    notice({ detail: 'because 0.47.0 wrote it', cure: 'Reload the window.', action: 'Reload Window' }),
    'r',
    1,
    NOON,
  );

  assert.equal('detail' in bare, false, 'an absent field must be absent, not an empty string');
  assert.equal('cure' in bare, false);
  assert.equal('action' in bare, false);
  assert.equal(full.detail, 'because 0.47.0 wrote it');
  assert.equal(full.action, 'Reload Window');
});

test('an empty string is the same as not saying it', () => {
  const record = noticeRecord(notice({ detail: '', subject: '' }), 'r', 1, NOON);

  assert.equal('detail' in record, false);
  assert.equal('subject' in record, false);
});

test('the answer is a second record, and walking away is an answer', () => {
  const asked = noticeRecord(notice({ action: 'Reload Window' }), 'r', 1, NOON);

  assert.equal(answered(asked, 'Reload Window').answer, 'Reload Window');
  assert.equal(answered(asked, undefined).answer, 'dismissed');
  // The asking is unchanged: nothing in this design is ever updated in place.
  assert.equal('answer' in asked, false);
  assert.equal(answered(asked, 'x').code, asked.code);
});

test('the gap is a state and says nothing when there is no gap', () => {
  assert.equal(gapSentence(0, '12:00:00'), '');
  assert.equal(gapSentence(1, '12:00:00'), '1 record could not be written since 12:00:00');
  assert.equal(gapSentence(7, '12:00:00'), '7 records could not be written since 12:00:00');
});

test('a gap becomes a RECORD, so it survives the window that noticed it', () => {
  // The counter above lives in memory, so a reloaded host reports no gap while the records it lost
  // are still missing — this plan's own thesis, turned on the plan. It cannot be written to a second
  // file in the data directory, because the directory is what failed; it goes into the LEDGER,
  // carried by the next append that lands, where a reader asking "what happened at 09:20" is already
  // looking. (Operator, 2026-09-17, reversing an accepted plan-round finding.)
  const from = new Date(Date.UTC(2026, 8, 17, 9, 12, 44)).toISOString();
  const to = new Date(Date.UTC(2026, 8, 17, 9, 30, 58)).toISOString();
  const gap = gapRecord(14, from, to, 'run-a', 4242, NOON);

  assert.equal(gap.class, 'failure', 'it belongs where a person looks for what went wrong');
  assert.equal(gap.code, 'notifications-write-gap');
  assert.equal(gap.run, 'run-a');
  assert.equal(gap.pid, 4242);
  assert.match(gap.title ?? '', /14 notifications could not be written/u);
  // The WINDOW, in the record. "14 were lost" without saying when is a fact nobody can act on.
  assert.match(gap.detail ?? '', /2026-09-17T09:12:44/u);
  assert.match(gap.detail ?? '', /2026-09-17T09:30:58/u);
  assert.match(gap.cure ?? '', /\S/u, 'and it says what to do, like every other record');
  assert.equal(
    gapRecord(1, from, from, 'r', 1, NOON).title,
    '1 notification could not be written down',
  );
});

test('a context field added to a notice reaches the record without a list being updated', () => {
  // `given()` used to keep a hand-written allowlist of field names, four functions away from
  // `notificationLine`, which redacts by iterating the record's OWN entries for the stated reason
  // that a list would have to be remembered. The same argument applied here and the code did the
  // opposite: a field added to Notice and to NotificationRecord, and supplied at a call site, was
  // dropped in silence. This asserts the inversion — everything that is not presentation travels.
  const record = noticeRecord(
    notice({
      subject: 'V:/connectOtherAis',
      detail: 'the mirror could not be written',
      cure: 'reload the window',
      action: 'Reload Window',
      repo: 'connect_other_ais',
      branch: 'main',
      session: '0ed4ad0e',
      provider: 'codex',
      role: 'Conventions',
    }),
    'r',
    1,
    NOON,
  );

  for (const field of ['subject', 'detail', 'cure', 'action', 'repo', 'branch', 'session', 'provider', 'role']) {
    assert.ok(field in record, `${field} never reached the record`);
  }
});

test('how a notice is PRESENTED does not reach the record', () => {
  // `as` is which toast API to call; `actions` and `modal` are how it is shown. None of them is a
  // fact about what happened, and a record is a fact about what happened. They are also the reason
  // the mapping can be structural at all: the exclusion list is about presentation, so it does not
  // grow when a new fact about an event is added.
  const record = noticeRecord(
    notice({ as: 'error', modal: true, actions: ['Copy', 'Cancel'] }),
    'r',
    1,
    NOON,
  );

  assert.equal('as' in record, false);
  assert.equal('modal' in record, false);
  assert.equal('actions' in record, false);
});

test('the buttons a question offered are on the record, not only the one pressed', () => {
  // The `.wslconfig` modal offers *Copy `wsl --shutdown`* and *Put it back to nat*, and the second
  // changes a machine's networking. The ledger held the answer and no trace of what it was an
  // answer to, which is half an audit. (codex, the code round.)
  const record = noticeRecord(
    notice({ actions: ['Copy `wsl --shutdown`', 'Put it back to nat'], modal: true }),
    'r',
    1,
    NOON,
  );

  assert.equal(record.offered, 'Copy `wsl --shutdown` · Put it back to nat');
  assert.equal(answered(record, 'Put it back to nat').offered, record.offered, 'and the answer keeps it');
});

test('a single button needs no second field, because `action` already says what it was', () => {
  assert.equal(noticeRecord(notice({ action: 'Reload Window' }), 'r', 1, NOON).offered, undefined);
  assert.equal(noticeRecord(notice({ action: 'Reload Window' }), 'r', 1, NOON).action, 'Reload Window');
  assert.equal(noticeRecord(notice(), 'r', 1, NOON).offered, undefined, 'and no buttons, no field');
});
