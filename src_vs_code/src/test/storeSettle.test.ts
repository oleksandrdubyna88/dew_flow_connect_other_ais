import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SETTLE, foundQuiet, putBackUntilHeld } from '../storeSettle';

/**
 * What a real-editor scenario uses to put the profile back and to find it quiet (`storeSettle.ts`): pure, over a fake clock that only a pause moves.
 * The fact behind it is the editor's, not ours: it keeps one `globalState` value for each extension and replaces the window's copy with the news of an
 * older write, so a write made a moment later can be taken away (`research/PLAN_auto_never_asks_a_person_E2_S2_spec.md`, V1 to V3).
 */

const LIMITS = { stepMs: 100, quietMs: 500, maxMs: 5_000 };

class Clock {
  now = 0;
  readonly pause = async (ms: number): Promise<void> => { this.now += ms; };
}

test('SETTLE: the quiet time is longer than the stall of the editor that took a write away, and the loop ends', () => {
  assert.ok(SETTLE.quietMs > 130, 'the quiet time is not above the 130 ms stall that was seen');
  assert.ok(SETTLE.quietMs > SETTLE.stepMs && SETTLE.maxMs > SETTLE.quietMs);
});

test('putBackUntilHeld: a state that already stands holds after the quiet time, with no write', async () => {
  const clock = new Clock();
  let writes = 0;
  const held = await putBackUntilHeld('want', { read: async () => 'want', putBack: async () => { writes += 1; }, pause: clock.pause, now: () => clock.now }, LIMITS);
  assert.equal(held, true);
  assert.equal(writes, 0);
  assert.ok(clock.now >= LIMITS.quietMs, 'it did not wait the quiet time');
});

test('putBackUntilHeld: a write taken away by the late news of an older one is made again, and the quiet time starts again', async () => {
  const clock = new Clock();
  // The first read shows the older state (the editor replaced the copy), the write puts it back, a second late news takes it away once more.
  const reads = ['older', 'want', 'want', 'older', 'want'];
  let writes = 0;
  const seen: number[] = [];
  const held = await putBackUntilHeld('want', {
    read: async () => { seen.push(clock.now); return reads.shift() ?? 'want'; },
    putBack: async () => { writes += 1; },
    pause: clock.pause,
    now: () => clock.now,
  }, LIMITS);
  assert.equal(held, true);
  assert.equal(writes, 2, 'the write was not made again for each time it was taken away');
  assert.ok(clock.now >= 400 + LIMITS.quietMs, 'the quiet time did not start again after the last write');
});

test('putBackUntilHeld: a state that keeps changing under the writes ends at the limit and says it did not hold', async () => {
  const clock = new Clock();
  let writes = 0;
  const held = await putBackUntilHeld('want', { read: async () => 'other', putBack: async () => { writes += 1; }, pause: clock.pause, now: () => clock.now }, LIMITS);
  assert.equal(held, false);
  assert.ok(writes > 1 && clock.now >= LIMITS.maxMs);
});

test('foundQuiet: the state found is the one a later read agrees with, not the first read', async () => {
  const clock = new Clock();
  const reads = ['late news of the last scenario', 'its second news', 'settled', 'settled'];
  const found = await foundQuiet({ read: async () => reads.shift() ?? 'settled', same: (a, b) => a === b, pause: clock.pause, now: () => clock.now }, LIMITS);
  assert.equal(found, 'settled');
  assert.equal(reads.length, 0);
});

test('foundQuiet: a state that never settles is read up to the limit, and the last read is the answer', async () => {
  const clock = new Clock();
  let n = 0;
  const found = await foundQuiet({ read: async () => { n += 1; return n; }, same: (a, b) => a === b, pause: clock.pause, now: () => clock.now }, LIMITS);
  assert.ok(clock.now >= LIMITS.maxMs);
  assert.equal(found, n);
});
