import assert from 'node:assert';
import { test } from 'node:test';
import { Notice } from '../notice';
import { NotificationRecord, parseNotificationLine, notificationLine } from '../notifications';
import {
  CEILING,
  FAULT_RESERVE,
  KEYS_REMEMBERED,
  RUN_BUDGET,
  STORM_AT,
  Suppressor,
  suppressor,
} from '../suppression';

/**
 * What the ledger keeps when something goes wrong a hundred thousand times.
 *
 * <p>The module under test is the one place in this feature where being WRONG is silent: a bound
 * that is too tight drops the records somebody needed and a bound that is too loose fills a disk,
 * and neither shows up in a screenshot. So the assertions here are about the file that results, not
 * about the internal counters that produce it.</p>
 */

const START = Date.UTC(2026, 8, 17, 9, 0, 0);

function complaint(over: Partial<Notice> = {}): Notice {
  return {
    as: 'warning',
    class: 'failure',
    source: 'serverSettingsSync',
    code: 'the-mirror-stood-down',
    title: 'The settings mirror stood down',
    ...over,
  };
}

/**
 * What one run would actually write, as a list of records.
 *
 * <p>The verdicts are turned back into the ledger the funnel would produce — the occurrence when it
 * is admitted, plus any meta-alert — because every promise this module makes is a promise about the
 * FILE. A test asserting on `verdict.write` would pass on a build that never wrote the storm.</p>
 */
function ledgerOf(
  bounds: Suppressor,
  notice: Notice,
  times: number,
  everyMs = 1_000,
  fromMs = START,
): NotificationRecord[] {
  const kept: NotificationRecord[] = [];
  for (let n = 0; n < times; n += 1) {
    const at = new Date(fromMs + n * everyMs);
    const verdict = bounds.admit(notice, at);
    if (verdict.write) {
      kept.push({
        utc: at.toISOString(),
        class: notice.class,
        source: notice.source,
        code: notice.code,
        ...(notice.subject === undefined ? {} : { subject: notice.subject }),
        seq: verdict.seq,
      });
    }
    if (verdict.storm !== undefined) {
      kept.push(verdict.storm);
    }
  }

  return kept;
}

const occurrences = (kept: readonly NotificationRecord[]): NotificationRecord[] =>
  kept.filter((record) => record.class !== 'storm');

const storms = (kept: readonly NotificationRecord[]): NotificationRecord[] =>
  kept.filter((record) => record.class === 'storm');

test('ten occurrences and ninety-nine do not produce the same ledger', () => {
  // The sentence that killed the sampled design on consultation, as an assertion. The first draft
  // wrote the 1st, 10th, 100th and so on, and claimed a reader could "see exactly how far it went";
  // under that rule these two files are byte-identical apart from timestamps, and the claim is
  // false. Exact below the bound is the whole reason the ceiling is as high as it is.
  const ten = occurrences(ledgerOf(suppressor('r1', 1), complaint(), 10));
  const ninetyNine = occurrences(ledgerOf(suppressor('r2', 1), complaint(), 99));

  assert.equal(ten.length, 10);
  assert.equal(ninetyNine.length, 99);
});

test('every occurrence is kept up to the ceiling, and none past it', () => {
  const kept = ledgerOf(suppressor('r', 1), complaint(), CEILING + 500);

  assert.equal(occurrences(kept).length, CEILING, 'exact below the bound');
  assert.equal(
    occurrences(kept).at(-1)?.seq,
    CEILING,
    'the last one kept is the one that reached the ceiling, not the one before it',
  );
});

test('the storm fires at the threshold and at the ceiling, and only there', () => {
  const kept = storms(ledgerOf(suppressor('r', 1), complaint(), CEILING + 500));

  assert.deepEqual(kept.map((record) => record.bound), [STORM_AT, CEILING]);
  assert.equal(kept.length, 2, 'at most two per key per run — the growth budget says so');
});

test('a storm title carries no number, because a number in a title is a new key every time', () => {
  // The defect that made this plan stop keying repeats on rendered titles: the Role2 complaint
  // named its round, so eleven rounds minted eleven keys, counted one each, and reached no
  // threshold. A storm whose own title said "100 times" would repeat that on itself.
  // EVERY storm, not the first one: the first draft of this test read `storms(...)[0]` and a number
  // planted in the CEILING title sailed straight through it. Two titles, two chances to get it
  // wrong, and a test that checks one of them is a test that passes on half a feature.
  const all = storms(ledgerOf(suppressor('r', 1), complaint(), CEILING + 1));

  assert.equal(all.length, 2, 'both crossings, so both titles are under assertion');
  for (const alert of all) {
    assert.ok(!/\d/u.test(alert.title ?? ''), `a number reached a title: ${alert.title ?? ''}`);
    assert.match(alert.detail ?? '', /\d+ since /u, 'the count and the rate live in detail');
    assert.match(alert.detail ?? '', /a minute/u, 'the rate is what says how bad it is');
  }
});

test('a rate that could not be measured is absent rather than zero', () => {
  // `coding-style.md`: a measurement not taken must not render as 0. Every occurrence at the same
  // instant is a zero-length window, and "0.0 a minute" for a hundred in one tick would be a lie
  // about the one column the storm feature exists for.
  const [first] = storms(ledgerOf(suppressor('r', 1), complaint(), STORM_AT, 0));

  assert.ok(first !== undefined);
  assert.ok(!(first.detail ?? '').includes('a minute'), first.detail ?? '');
});

test('eviction cannot corrupt a count: the ledger still holds every occurrence', () => {
  // Three for one subject, enough other subjects to push it out of the LRU, two more. A count taken
  // from the in-memory map would read two; the page counts ROWS, and there are five.
  const bounds = suppressor('r', 1);
  const mine = complaint({ subject: 'V:/connectOtherAis' });
  const kept = [...ledgerOf(bounds, mine, 3)];
  for (let n = 0; n < KEYS_REMEMBERED + 1; n += 1) {
    kept.push(...ledgerOf(bounds, complaint({ subject: `other-${n}` }), 1));
  }
  kept.push(...ledgerOf(bounds, mine, 2));

  assert.equal(
    occurrences(kept).filter((record) => record.subject === 'V:/connectOtherAis').length,
    5,
  );
});

test('a loop churning distinct subjects is stopped by the run budget, which eviction cannot reset', () => {
  // The hole the plan named and did not close. Its budget charged repeats of a `(code, subject)`;
  // an evicted key returns looking like a first occurrence, a first occurrence is never charged, so
  // the loop wrote for ever. The budget is charged against each CODE's first instead — a finite set
  // that is never evicted.
  const bounds = suppressor('r', 1);
  const kept: NotificationRecord[] = [];
  for (let n = 0; n < RUN_BUDGET * 3; n += 1) {
    kept.push(...ledgerOf(bounds, complaint({ class: 'refusal', subject: `path-${n}` }), 1));
  }

  assert.equal(occurrences(kept).length, RUN_BUDGET + 1, 'the budget, plus the code\u2019s free first');
  assert.equal(storms(kept).length, 1, 'said once, not once per refused record');
  assert.equal(storms(kept)[0]?.bound, RUN_BUDGET);
});

test('a failure arriving after the budget is spent is still written, out of the reserve', () => {
  // The whole reason the reserve exists: a churn loop must not cost the settings refusal that
  // arrives after it. The log would then be protecting storage at the price of the record it was
  // built for.
  const bounds = suppressor('r', 1);
  for (let n = 0; n < RUN_BUDGET + 10; n += 1) {
    bounds.admit(complaint({ class: 'refusal', subject: `path-${n}` }), new Date(START));
  }
  const after = occurrences(ledgerOf(bounds, complaint({ class: 'failure', subject: 'the-mirror' }), 3));
  const ordinary = occurrences(ledgerOf(bounds, complaint({ class: 'outcome', code: 'a-copy-landed' }), 3));

  assert.equal(after.length, 3, 'failure keeps its allowance');
  assert.equal(ordinary.length, 1, 'and an ordinary class gets only its code\u2019s free first');
});

test('the reserve is finite too — it is a reserve, not an exemption', () => {
  const bounds = suppressor('r', 1);
  for (let n = 0; n < RUN_BUDGET + FAULT_RESERVE + 50; n += 1) {
    bounds.admit(complaint({ class: 'failure', subject: `s-${n}` }), new Date(START));
  }

  assert.equal(
    occurrences(ledgerOf(bounds, complaint({ class: 'failure', subject: 'one-more' }), 1)).length,
    0,
  );
});

test('a condition that ended makes its next occurrence news again', () => {
  // `serverSettingsSync.ts:171` clears its own guard on a successful write, because "the situation
  // is over; a stand-down after this is news, not a repeat". A clock window destroys exactly this.
  const bounds = suppressor('r', 1);
  const mine = complaint({ subject: 'V:/connectOtherAis' });
  ledgerOf(bounds, mine, 5);
  bounds.resolved(mine.code, mine.subject);

  assert.equal(bounds.admit(mine, new Date(START)).seq, 1, 'a first occurrence, not the sixth');
});

test('recovering one subject does not reset another', () => {
  // One call site can fail for two things at once — two Team servers, two prompt files — and a
  // `code` alone is the call site rather than the fault.
  const bounds = suppressor('r', 1);
  ledgerOf(bounds, complaint({ subject: 'one' }), 4);
  ledgerOf(bounds, complaint({ subject: 'two' }), 4);
  bounds.resolved('the-mirror-stood-down', 'one');

  assert.equal(bounds.admit(complaint({ subject: 'one' }), new Date(START)).seq, 1);
  assert.equal(bounds.admit(complaint({ subject: 'two' }), new Date(START)).seq, 5);
});

test('a restart is a new run: the ceiling starts again and the runs stay apart', () => {
  // Deliberate, and asked about by two reviewers on the plan round. A new host is a new observer,
  // and a ceiling that persisted would need durable state whose loss is itself a defect. What makes
  // it honest rather than a silent reset is that the records carry different `run` ids, so the page
  // shows "it happened again after a restart" instead of one number quietly growing.
  const first = ledgerOf(suppressor('run-a', 11), complaint(), CEILING + 5);
  const second = ledgerOf(suppressor('run-b', 22), complaint(), CEILING + 5);

  assert.equal(occurrences(first).length, CEILING);
  assert.equal(occurrences(second).length, CEILING);
  assert.deepEqual(
    [...new Set(storms([...first, ...second]).map((record) => record.run))],
    ['run-a', 'run-b'],
    'and the meta-alerts say which run each belongs to',
  );
});

test('a storm record survives the round trip, so read-time grouping can reach its bound', () => {
  // The once-only promise is a READ-time invariant: two windows crossing the same threshold each
  // write one, and the page groups (code, subject, bound) into a single row. That grouping is only
  // possible if `bound` is a field rather than a sentence inside the detail text.
  const [alert] = storms(ledgerOf(suppressor('r', 1), complaint({ subject: 'V:/x' }), STORM_AT));
  assert.ok(alert !== undefined);
  const back = parseNotificationLine(notificationLine(alert).trim());

  assert.equal(back?.bound, STORM_AT);
  assert.equal(back?.code, 'the-mirror-stood-down');
  assert.equal(back?.subject, 'V:/x');
  assert.equal(back?.class, 'storm');
});
