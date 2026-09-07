import assert from 'node:assert/strict';
import { test } from 'node:test';
import { LOCK_STALE_AFTER_MS, lockIsStale } from '../settingsLock';

/**
 * Breaking somebody else's lock is the decision worth testing.
 *
 * <p>Taking a lock is a rename that either works or does not. What can go wrong is the other half:
 * break too eagerly and two windows write at once, which is the race the lock exists to close;
 * never break at all and one window killed at the wrong instant wedges every other window on the
 * machine forever, and nobody's settings reach the server again. The second failure is the worse
 * one, and it is silent.</p>
 */

test('a lock taken a moment ago is held, not dead', () => {
  assert.equal(lockIsStale(1_000, 1_000), false, 'this instant');
  assert.equal(lockIsStale(1_000, 1_000 + LOCK_STALE_AFTER_MS), false, 'exactly at the boundary');
});

test('a lock older than any write could take belonged to a window that died', () => {
  assert.equal(lockIsStale(1_000, 1_000 + LOCK_STALE_AFTER_MS + 1), true);
});

test('the window is orders of magnitude wider than the work it covers', () => {
  // What is held is a read, a comparison and a write of a few hundred bytes. Ten seconds is slack,
  // not a timeout somebody will wait out: a person who hits it is a person whose other window is
  // gone, and the next configuration change writes.
  assert.ok(LOCK_STALE_AFTER_MS >= 5_000, 'too tight and a slow disk looks like a dead window');
  assert.ok(LOCK_STALE_AFTER_MS <= 60_000, 'too wide and a crash costs a minute of settings');
});

test('a clock that went backwards is not evidence that a lock is old', () => {
  // A laptop resuming, or an NTP correction, makes the age negative. Reading that as "very old"
  // is how two windows both decide to break one lock — the exact simultaneous write the lock is
  // there to prevent, produced by the mechanism meant to prevent it.
  assert.equal(lockIsStale(10_000, 1_000), false);
  assert.equal(lockIsStale(Date.now() + 60_000, Date.now()), false);
});
