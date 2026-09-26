/**
 * The panel's setting writes, one at a time and in order — and the wait a render makes for them.
 *
 * <p>Moved out of `PanelProvider` (2026-09-26) so the one hazard it carries can be RUN in a test: work
 * running inside the queue must never await {@link settled}, because while it runs the queue IS that work,
 * and the wait is a wait on itself. The snap-back of a refused box did exactly that from PR #561 on, and the
 * panel froze until the window was reloaded.</p>
 */
export class WriteQueue {
  private queued: Promise<void> = Promise.resolve();

  /** Appends one write. A failed write never stops the ones after it. */
  enqueue(work: () => Promise<void>): void {
    this.queued = this.queued.then(work, work).catch(() => undefined);
  }

  /**
   * Resolves once the queue is STABLE: a write appended while this waited is waited for too, because it
   * would otherwise be read a moment too late. Bounded — under continuous typing the queue never settles,
   * and a render that waits for silence is a render that never happens.
   */
  async settled(): Promise<void> {
    for (let round = 0; round < 5; round += 1) {
      const seen = this.queued;
      await seen;
      if (seen === this.queued) {
        return;
      }
    }
  }
}
