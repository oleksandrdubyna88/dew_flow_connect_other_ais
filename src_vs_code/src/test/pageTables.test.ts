import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PAGE_SIZE, asInstant, compareRows } from '../pageTables';

/**
 * The three mechanics two pages share, pinned so that sharing them cannot change them.
 *
 * <p><b>A characterisation suite, not a feature suite.</b> `compareRows` and `asInstant` came out
 * of `roundsLog.ts` so the notifications page could use them rather than grow a second copy, and
 * the risk in that move is not that it fails to compile — it is that a genericised comparator
 * sorts <i>almost</i> the same and the rounds log quietly changes under a reader who was not told.
 * The gate's plan round named the three behaviours most likely to be lost, and they are the three
 * tests below. Every existing `roundsLog.test.ts` assertion passing unchanged is the other half of
 * the guard.</p>
 *
 * <p>The page's own copy is checked elsewhere and differently: `roundsLog.test.ts` asserts the
 * rendered script contains `var compareRows = ` followed by this function's exact source, so the
 * sort these tests run is the sort a reader's browser runs. That is what makes a unit test of a
 * function that executes in a webview worth anything at all.</p>
 */

interface Row {
  readonly name: string;
  readonly size: number | null;
  readonly note: string;
}

const ROWS: readonly Row[] = [
  { name: 'beta', size: 20, note: 'x' },
  { name: 'alpha', size: 10, note: 'x' },
  { name: 'gamma', size: 30, note: 'x' },
];

const sorted = (rows: readonly Row[], key: keyof Row & string, dir: 'asc' | 'desc'): Row[] =>
  [...rows].sort((a, b) => compareRows(a, b, key, dir));

test('a blank sorts last in BOTH directions, because a missing number is not a small one', () => {
  // The behaviour a genericisation loses first, and the one that matters most here: the
  // notifications page has a Rate column that is deliberately absent for a single occurrence, so
  // blanks-first would put every singleton at the top of the one column the storm feature is for.
  const withGaps: readonly Row[] = [
    { name: 'has', size: 5, note: 'x' },
    { name: 'none', size: null, note: 'x' },
    { name: 'empty', size: 7, note: '' },
  ];

  assert.deepEqual(sorted(withGaps, 'size', 'asc').map((r) => r.name), ['has', 'empty', 'none']);
  assert.deepEqual(sorted(withGaps, 'size', 'desc').map((r) => r.name), ['empty', 'has', 'none']);
  assert.deepEqual(sorted(withGaps, 'note', 'asc').map((r) => r.name), ['has', 'none', 'empty']);
  assert.deepEqual(sorted(withGaps, 'note', 'desc').map((r) => r.name), ['has', 'none', 'empty']);
});

test('the direction flips real values and nothing else', () => {
  assert.deepEqual(sorted(ROWS, 'name', 'asc').map((r) => r.name), ['alpha', 'beta', 'gamma']);
  assert.deepEqual(sorted(ROWS, 'name', 'desc').map((r) => r.name), ['gamma', 'beta', 'alpha']);
  assert.deepEqual(sorted(ROWS, 'size', 'asc').map((r) => r.size), [10, 20, 30]);
  assert.deepEqual(sorted(ROWS, 'size', 'desc').map((r) => r.size), [30, 20, 10]);
});

test('equal keys keep the order they arrived in, in both directions', () => {
  // The one the rounds log never asserted. `Array.prototype.sort` is stable, but only while the
  // comparator answers 0 for equal keys — and "0 for equal" is exactly what an optimisation like
  // `return x < y ? -1 : 1` throws away. A page that reshuffles equal rows on every re-sort looks
  // like a page that lost data.
  const same: readonly Row[] = [
    { name: 'first', size: 1, note: 'same' },
    { name: 'second', size: 1, note: 'same' },
    { name: 'third', size: 1, note: 'same' },
  ];

  assert.equal(compareRows(same[0] as Row, same[1] as Row, 'note', 'asc'), 0);
  assert.deepEqual(sorted(same, 'note', 'asc').map((r) => r.name), ['first', 'second', 'third']);
  assert.deepEqual(sorted(same, 'note', 'desc').map((r) => r.name), ['first', 'second', 'third']);
  assert.deepEqual(sorted(same, 'size', 'desc').map((r) => r.name), ['first', 'second', 'third']);
});

test('numbers compare as numbers and text as text', () => {
  // `String(10) < String(9)`, so a comparator that stringified everything would order 10 before 9
  // and a Repeats column would read as nonsense exactly when the numbers got interesting.
  const counts: readonly Row[] = [
    { name: 'ten', size: 10, note: 'x' },
    { name: 'nine', size: 9, note: 'x' },
  ];

  assert.deepEqual(sorted(counts, 'size', 'asc').map((r) => r.size), [9, 10]);
});

test('a wall-clock bound becomes an instant, and the named minute is inside it', () => {
  const from = asInstant('2026-09-17T09:30', false);
  const to = asInstant('2026-09-17T09:30', true);

  assert.equal(new Date(to).getTime() - new Date(from).getTime(), 59_999, 'the whole minute');
  assert.ok(from.endsWith('Z') && to.endsWith('Z'), 'both are UTC instants');
});

test('a bound that is empty or unreadable is empty, not an instant at the epoch', () => {
  // A filter that silently became "since 1970" would show everything and look like it was off.
  assert.equal(asInstant('', false), '');
  assert.equal(asInstant('not a date', false), '');
  assert.equal(asInstant('not a date', true), '');
});

test('the page size is the operator ruling, and one number', () => {
  assert.equal(PAGE_SIZE, 200);
});
