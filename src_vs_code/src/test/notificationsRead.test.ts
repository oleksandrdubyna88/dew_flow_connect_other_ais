import assert from 'node:assert/strict';
import { test } from 'node:test';
import { NotificationRecord } from '../notifications';
import {
  Arrival,
  Grouped,
  RUN_CEILING,
  group,
  keyOf,
  ratePerMinute,
  repeatsSaid,
  tabOf,
} from '../notificationsRead';
import { Span } from '../notificationsSeen';

/**
 * The rows the page shows, decided without a disk.
 *
 * <p>Every number here is one a person will act on: how often something happened, how fast, and
 * whether they have already seen it. The page renders them; this decides them, which is why the
 * hard cases live here rather than in a fixture handed to a DOM shim.</p>
 */

const AT = Date.UTC(2026, 8, 17, 9, 0, 0);

function arrival(over: Partial<NotificationRecord> & { at?: number; ms?: number } = {}): Arrival {
  const { at, ms, ...rest } = over;

  return {
    at: at ?? 0,
    ledger: 'extension',
    record: {
      utc: new Date(AT + (ms ?? 0)).toISOString(),
      class: 'failure',
      source: 'serverSettingsSync',
      code: 'the-mirror-stood-down',
      run: 'run-a',
      ...rest,
    },
  };
}

/**
 * The same arrival with no `run` at all - the shape a record written before S1 has.
 *
 * <p>The key is DELETED rather than set to `undefined`: `exactOptionalPropertyTypes` is on, so
 * `run: undefined` is not the same type as an absent `run`, and writing it that way was a compile
 * error rather than a test.</p>
 */
function withoutRun(one: Arrival): Arrival {
  const { run, ...rest } = one.record;

  return { ...one, record: rest };
}

const only = (rows: readonly Grouped[]): Grouped => {
  assert.equal(rows.length, 1, `expected one row, got ${rows.length}`);

  return rows[0] as Grouped;
};

test('a hundred occurrences of one fault are ONE row that says a hundred', () => {
  // The whole reason a row is a group. The ledger already refuses to WRITE a thousand identical
  // records; rendering a thousand identical rows would undo that at the other end.
  const rows = group(
    Array.from({ length: 100 }, (_, n) => arrival({ at: n * 100, ms: n * 60_000 })),
    [],
  );

  assert.equal(only(rows).repeats, 100);
  assert.equal(repeatsSaid(only(rows)), '100');
});

test('one code raised as two classes is two rows, not one that moves between tabs', () => {
  // Tabs are per class, so a row must belong to exactly one. Nothing in the funnel stops one code
  // being a refusal at one call site and a failure at another, and with the class outside the key
  // the single row would sit under whichever tab the NEWEST record happened to fall into - and
  // move, as records arrived.
  const rows = group([
    arrival({ class: 'refusal', at: 0, ms: 0 }),
    arrival({ class: 'failure', at: 100, ms: 1000 }),
  ], []);

  assert.equal(rows.length, 2);
  assert.deepEqual(rows.map((row) => tabOf(row)).sort(), ['failure', 'refusal']);
});

test('a subject separates two faults from one call site', () => {
  const rows = group([
    arrival({ subject: 'V:/one', at: 0 }),
    arrival({ subject: 'V:/two', at: 100 }),
    arrival({ subject: 'V:/one', at: 200, ms: 1000 }),
  ], []);

  assert.equal(rows.length, 2);
  assert.equal(rows.find((row) => row.subject === 'V:/one')?.repeats, 2);
});

test('`when` is the LATEST occurrence and `first` is the earliest, whatever order they arrive in', () => {
  // A column that shows one and sorts by the other is the kind of defect nobody reports and
  // everybody works around.
  const row = only(group([
    arrival({ at: 0, ms: 60_000 }),
    arrival({ at: 100, ms: 0 }),
    arrival({ at: 200, ms: 30_000 }),
  ], []));

  assert.equal(row.when, new Date(AT + 60_000).toISOString());
  assert.equal(row.first, new Date(AT).toISOString());
});

test('the row carries the NEWEST wording, because a sentence can be reworded between releases', () => {
  const row = only(group([
    arrival({ at: 0, ms: 0, title: 'the old words' }),
    arrival({ at: 100, ms: 1000, title: 'the words it says now' }),
  ], []));

  assert.equal(row.title, 'the words it says now');
});

test('three runs that each hit the ceiling are not one incident of three thousand', () => {
  // "1000+ in each of 3 runs", never a bare 3000 and never a bare 1000. Past the ceiling the
  // ledger stops writing, so the total is unknowable and a figure would be a lie with a decimal
  // point in it.
  const arrivals: Arrival[] = [];
  for (const run of ['run-a', 'run-b', 'run-c']) {
    for (let n = 0; n < RUN_CEILING; n += 1) {
      arrivals.push(arrival({ run, at: arrivals.length * 10, ms: arrivals.length * 1000 }));
    }
  }

  const row = only(group(arrivals, []));

  assert.equal(row.runs.length, 3);
  assert.ok(row.runs.every((run) => run.ceilinged));
  assert.equal(repeatsSaid(row), `${RUN_CEILING}+ in each of 3 runs`);
});

test('one run at the ceiling says 1000+, and a modest one says its number', () => {
  const capped = only(group(
    Array.from({ length: RUN_CEILING }, (_, n) => arrival({ at: n * 10, ms: n * 1000 })),
    [],
  ));
  const modest = only(group(
    Array.from({ length: 4 }, (_, n) => arrival({ at: n * 10, ms: n * 1000 })),
    [],
  ));

  assert.equal(repeatsSaid(capped), `${RUN_CEILING}+`);
  assert.equal(repeatsSaid(modest), '4');
});

test('two runs below the ceiling are named as two runs', () => {
  const row = only(group([
    arrival({ run: 'run-a', at: 0, ms: 0 }),
    arrival({ run: 'run-a', at: 10, ms: 1000 }),
    arrival({ run: 'run-b', at: 20, ms: 2000 }),
  ], []));

  assert.equal(repeatsSaid(row), '3 in 2 runs');
});

test('a record written before `run` existed is its own run, named as unknown', () => {
  // The field arrived with S1 and the ledger is kept for ever, so a file will hold records from
  // before it. Folding them into one pseudo-run would invent a session and one day report
  // "1000+ in 1 run" for the sum of a year.
  const row = only(group([
    withoutRun(arrival({ at: 0, ms: 0 })),
    arrival({ run: 'run-a', at: 100, ms: 1000 }),
  ], []));

  assert.equal(row.runs.length, 2);
  assert.ok(row.runs.some((run) => run.run === ''), 'the record with no run is its own');
});

test('a rate needs a window worth measuring, and says nothing otherwise', () => {
  // `coding-style.md`: a measurement that could not be taken must not render as 0. And the other
  // direction, which the plan round added: forty occurrences in 40 ms divides into tens of
  // thousands a minute and sorts straight to the top of the column the storm feature is for.
  assert.equal(ratePerMinute(1, AT, AT + 60_000), undefined, 'one occurrence is not a rate');
  assert.equal(ratePerMinute(40, AT, AT), undefined, 'a zero-length window');
  assert.equal(ratePerMinute(40, AT, AT + 40), undefined, 'forty milliseconds is not a window');
  assert.equal(ratePerMinute(2, AT, AT + 1000), 120, 'two in a second is two a second');
  assert.equal(ratePerMinute(60, AT, AT + 60_000), 60, 'sixty in a minute is sixty a minute');
});

test('a row is read only when EVERY occurrence in it is', () => {
  // One unread repeat of a fault somebody acknowledged last week is news. Marking the row read
  // because its older occurrences were is how an honest watermark starts lying.
  const read: readonly Span[] = [{ from: 0, to: 150 }];
  const half = only(group([arrival({ at: 0 }), arrival({ at: 200, ms: 1000 })], read));
  const whole = only(group([arrival({ at: 0 }), arrival({ at: 100, ms: 1000 })], read));

  assert.equal(half.read, false, 'the newer occurrence is outside the acknowledged range');
  assert.equal(whole.read, true);
});

test('a class this build has never heard of is shown, not dropped', () => {
  // The forward-compatibility bargain the parser already takes: a newer build writing a class this
  // one does not know must not make its records vanish from an older reader.
  const row = only(group([arrival({ class: 'something-new' as never, at: 0 })], []));

  assert.equal(tabOf(row), 'other');
  assert.equal(row.repeats, 1);
});

test('the two ledgers merge into one set of rows, and a row remembers which it came from', () => {
  const rows = group([
    arrival({ at: 0, code: 'from-the-extension' }),
    { ...arrival({ at: 0, code: 'from-the-server' }), ledger: 'server' } as Arrival,
  ], []);

  assert.equal(rows.length, 2);
  assert.equal(rows.find((row) => row.code === 'from-the-server')?.ledger, 'server');
  assert.equal(rows.find((row) => row.code === 'from-the-extension')?.ledger, 'extension');
});

test('a key cannot be forged by a subject that contains a separator', () => {
  assert.notEqual(keyOf('failure', 'a', 'b-c'), keyOf('failure', 'a-b', 'c'));
  assert.notEqual(keyOf('failure', 'a', ''), keyOf('failure', '', 'a'));
});
