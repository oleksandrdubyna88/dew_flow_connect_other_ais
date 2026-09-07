/**
 * Whether a lock left on disk should be broken.
 *
 * <p>Its own module, and pure, because the interesting half of a lock is not taking it — it is
 * deciding that somebody else's is dead. A window killed between taking the lock and releasing it
 * would otherwise wedge every other window on the machine forever, and the settings file would stop
 * being written by anybody. That failure is worse than the race the lock is there to close, so the
 * rule that governs it is a function with tests rather than a line inside an I/O path nothing can
 * reach.</p>
 *
 * <p>The window is generous on purpose. What is held is a read, a comparison and a write of a file
 * of a few hundred bytes — microseconds — so ten seconds is four orders of magnitude of slack and
 * still short enough that nobody waits for it. A clock that jumps BACKWARDS (a laptop resuming, an
 * NTP correction) makes the age negative, and a negative age is not stale: it means this machine
 * cannot currently tell how old the lock is, and guessing "old" there is how two windows both decide
 * to break one lock and write at once.</p>
 */
export const LOCK_STALE_AFTER_MS = 10_000;

export function lockIsStale(writtenAtMs: number, nowMs: number, staleAfterMs = LOCK_STALE_AFTER_MS): boolean {
  const age = nowMs - writtenAtMs;

  return age >= 0 && age > staleAfterMs;
}
