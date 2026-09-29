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
   * Appends one write and hands back ITS outcome — for a caller that must report its own failure. The queue
   * still runs the next write after a failed one, exactly as {@link enqueue} does.
   *
   * <p>Why it exists: a text-control press reads the setting, adds a step and writes it back. Started one
   * after another without waiting, two quick presses both read the same value and one of them is lost
   * (CodeRabbit on PR #615).</p>
   */
  run(work: () => Promise<void>): Promise<void> {
    const outcome = this.queued.then(work, work);
    this.queued = outcome.catch(() => undefined);

    return outcome;
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
