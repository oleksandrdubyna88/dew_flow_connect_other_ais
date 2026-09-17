import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Gap, NO_GAP, settled, widened } from '../writeGap';

/**
 * The ledger's own hole, counted.
 *
 * <p>This is the arithmetic `notify.ts` runs while the disk is refusing writes, extracted so that it
 * can be RUN — that file imports `vscode` and no test here can import it, and the rules below are
 * each a way of getting the number wrong rather than obvious properties of a counter.</p>
 */

const at = (minute: number, second: number): string =>
  new Date(Date.UTC(2026, 8, 17, 9, minute, second)).toISOString();

const hole = (lost: number, since: string, until: string): Gap => ({ lost, since, until });

test('the first loss opens the hole, and later ones only move its far end', () => {
  const one = widened(NO_GAP, at(12, 44));

  assert.deepEqual(one, hole(1, at(12, 44), at(12, 44)), 'one loss reads as a hole of no width');

  const three = widened(widened(one, at(20, 0)), at(30, 58));

  assert.deepEqual(three, hole(3, at(12, 44), at(30, 58)), 'the beginning never moves');
});

test('what was written down is SUBTRACTED, never zeroed', () => {
  // The rule this module exists for. Writing the gap record is an append and an append is awaited,
  // so a notice can be lost while the earlier losses are being reported. Clearing the counter
  // outright would swallow exactly those — the losses that happened during the reporting of the
  // others, which is a hole inside the report of a hole.
  const now = hole(14, at(12, 44), at(31, 30));
  const written = hole(12, at(12, 44), at(30, 58));

  assert.deepEqual(
    settled(now, written),
    hole(2, at(30, 58), at(31, 30)),
    'two are still missing, and they begin where the written record ended',
  );
});

test('a hole entirely written down leaves nothing behind, including its instants', () => {
  const now = hole(14, at(12, 44), at(30, 58));

  assert.deepEqual(settled(now, now), NO_GAP);
  assert.equal(settled(now, now).since, '', 'a stale instant would date the next hole wrongly');
});

test('a report larger than the hole cannot drive the count below zero', () => {
  // Reachable if `forgetTheGap` runs — the data directory moved — while a flush is in flight.
  assert.deepEqual(settled(hole(1, at(12, 44), at(12, 44)), hole(9, at(0, 0), at(1, 0))), NO_GAP);
  assert.deepEqual(settled(NO_GAP, hole(3, at(0, 0), at(1, 0))), NO_GAP);
});
