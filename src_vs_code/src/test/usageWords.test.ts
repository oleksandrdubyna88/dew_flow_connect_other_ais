import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { claudeLine, codexLine, minutesLeft, part, percentLeft, resetText, windowLabel } from '../usageWords';

/**
 * The words of the usage list (E1.S4, A4-T1 … A4-T3): the mod's three rules (`percentLeft`, `minutesLeft`, `resetText`), copied once into
 * TypeScript and held to the mod's own cases by a table, and the window names. The mod is a `.tsx` of another runtime that this extension cannot
 * import, so a table of the mod's cases is what keeps the two copies the same.
 */

const MINUTE = 60_000;

test('A4-T1: percentLeft is the mod\'s rule — 100 minus used, rounded, clamped, and null for anything that is not a finite number', () => {
  const cases: readonly (readonly [unknown, number | null])[] = [
    [13, 87], [33.4, 67], [100, 0], [0, 100], [130, 0], [-5, 100], [Number.POSITIVE_INFINITY, null], [Number.NEGATIVE_INFINITY, null],
    [null, null], ['13', null], [undefined, null], [Number.NaN, null], [{}, null], [66.5, 34],
  ];
  for (const [used, left] of cases) {
    assert.equal(percentLeft(used), left, `${String(used)} used is not ${String(left)} left`);
  }
});

test('A4-T1: resetText is the mod\'s rule under a day — 1h 0m, 59m, 1m, <1m, and a dash when the time is not known', () => {
  const cases: readonly (readonly [number | null, string])[] = [[60, '1h 0m'], [59, '59m'], [1, '1m'], [0, '<1m'], [null, '—'], [61, '1h 1m'], [90, '1h 30m'], [1439, '23h 59m']];
  for (const [minutes, said] of cases) {
    assert.equal(resetText(minutes), said, `${String(minutes)} minutes`);
  }
});

test('A4-T1: minutesLeft is the mod\'s rule — whole minutes, null while unknown and once the reset has come', () => {
  const now = 1_000_000;
  assert.equal(minutesLeft(now + (90 * MINUTE) + 59_000, now), 90, 'whole minutes, rounded down');
  assert.equal(minutesLeft(now + MINUTE, now), 1);
  assert.equal(minutesLeft(now + 1, now), 0, 'under a minute is 0, which says <1m');
  assert.equal(minutesLeft(now, now), null, 'at the reset it has come');
  assert.equal(minutesLeft(now - 1, now), null, 'a reset in the past');
  assert.equal(minutesLeft(null, now), null);
  assert.equal(minutesLeft(now + MINUTE, null), null, 'a clock that is not known');
  assert.equal(minutesLeft(now + MINUTE, Number.NaN), null);
});

test('A4-T1: part says "<label> <n>% left", or "<label> —" with no "% left" for a number that is not there', () => {
  assert.equal(part('5h', 56), '5h 44% left');
  assert.equal(part('week', 100), 'week 0% left', 'a real 0 left is said');
  assert.equal(part('Fable', null), 'Fable —');
  assert.equal(part('Fable', undefined), 'Fable —');
  assert.equal(part('week', 'x'), 'week —');
  assert.ok(!part('week', null).includes('left'), 'a dash that still says "left"');
});

test('A4-T1: a Claude line with every value absent is the mod\'s dashes, and a Codex line never is a row of dashes', () => {
  assert.equal(claudeLine([], 1_000_000), '5h — · resets in — · week — · Fable —');
  assert.equal(claudeLine([{ name: '5h', usedPercent: null, resetsAtMs: null }, { name: 'week', usedPercent: null, resetsAtMs: null }], 1_000_000), '5h — · resets in — · week — · Fable —');
  assert.equal(codexLine([{ minutes: 10_080, usedPercent: null, resetsAtMs: null }], 1_000_000), 'week — · resets in —');
});

test('A4-T2: from a day on, the reset is said in days and hours', () => {
  assert.equal(resetText(1439), '23h 59m');
  assert.equal(resetText(1440), '1d 0h');
  assert.equal(resetText(3790), '2d 15h', 'the week\'s reset is said in hours, not in days');
  assert.equal(resetText(7139), '4d 22h');
  assert.equal(resetText(10_080), '7d 0h');
});

test('A4-T3: a window is named by its length, never by the slot it came in', () => {
  assert.equal(windowLabel({ name: '5h' }), '5h');
  assert.equal(windowLabel({ name: 'week' }), 'week');
  assert.equal(windowLabel({ name: { model: 'Fable' } }), 'Fable');
  assert.equal(windowLabel({ minutes: 300 }), '5h');
  assert.equal(windowLabel({ minutes: 10_080 }), 'week', 'a weekly window was not called week');
  assert.equal(windowLabel({ minutes: 120 }), '2h');
  assert.equal(windowLabel({ minutes: 90 }), '1.5h');
  assert.equal(windowLabel({ minutes: 45 }), '0.8h');
  assert.equal(windowLabel({ minutes: 1440 }), '24h', 'a day-long window that is neither 5h nor week is said in hours');
});
