/**
 * The panel's renders, numbered — so a write knows when one has carried it (research/PLAN_model_search_and_busy_marks.md
 * §3.9, §3.14 as shipped).
 *
 * <p>A setting's busy mark settles once a render that STARTED after the write was queued has finished: every render
 * awaits the write queue before it reads the configuration (`PanelProvider.renderNow`), so a render numbered after the
 * write's {@link mark} read the written value. That is also how a write shares the configuration listener's own
 * render instead of asking for a second one.</p>
 *
 * <p><b>Renders are NOT serialised.</b> The first version of this module ran them one at a time with a shared trailing
 * run, and the E3 review found what that cost: `renderNow` awaits a fetch to GitHub and the price tables with no
 * timeout of its own, and one render stalled on a slow proxy then held up every paint after it — the reported freeze,
 * made longer by its fix. Renders run side by side exactly as they did before this module existed; it only counts
 * them. Three consequences, each from the final E3 round:</p>
 *
 * <ul>
 * <li>A waiting write is released by ANY render after its mark that finishes well — never bound to the oldest one,
 * which may be the stalled one.</li>
 * <li>A write whose settle finds no render after its mark waits {@link graceMs} for one to start — the configuration
 * listener's, which arrives a turn or two after the write — before starting its own, so a write costs one render, not
 * two.</li>
 * <li>A render that a newer one has already finished is {@link superseded}: its caller skips the paint rather than put
 * the older state back over the newer.</li>
 * </ul>
 *
 * <p>A render that FAILED did not carry anything: a waiting write is rejected only when no other render after its mark
 * is still running. A work that throws before its first `await` is still a failed render — it is started inside a
 * promise, so it can never be recorded as running for good.</p>
 */
export class RenderTracker {
  /** The number of the last render started; 0 before the first. */
  private started = 0;
  /** The highest-numbered render that finished well. Renders can finish out of order; the highest is what matters. */
  private finished = 0;
  /** The numbers of the renders in flight. Replaced on every change, never mutated in place. */
  private running: ReadonlySet<number> = new Set();
  private waiters: readonly Waiter[] = [];

  /**
   * @param work the render itself, told its own number
   * @param graceMs how long a write's settle waits for somebody else's render before starting its own
   */
  constructor(private readonly work: (number: number) => Promise<void>, private readonly graceMs = 0) {}

  /** The number of the last render started — what {@link runAfter} is later asked to see a render beyond. */
  mark(): number {
    return this.started;
  }

  /** Whether a newer render than `number` has already finished — and painted, so this one's paint would be older. */
  superseded(number: number): boolean {
    return this.finished > number;
  }

  /** A new render, started now, beside any already running. */
  run(): Promise<void> {
    return this.start();
  }

  /** Settles once a render numbered above `mark` has finished well — one that already did, one running, or a new one. */
  runAfter(mark: number): Promise<void> {
    if (this.finished > mark) {
      return Promise.resolve();
    }

    return new Promise<void>((resolve, reject) => {
      this.waiters = [...this.waiters, { mark, resolve, reject }];
      if (!this.runningAfter(mark)) {
        this.startUnlessOneComes(mark);
      }
    });
  }

  private runningAfter(mark: number): boolean {
    return [...this.running].some((number) => number > mark);
  }

  /** No render after `mark` is running: give the configuration listener's a moment to start, then start one. */
  private startUnlessOneComes(mark: number): void {
    if (this.graceMs <= 0) {
      void this.start().catch(() => undefined);
      return;
    }
    setTimeout(() => {
      if (this.waiting(mark) && !this.runningAfter(mark) && this.finished <= mark) {
        void this.start().catch(() => undefined);
      }
    }, this.graceMs);
  }

  private waiting(mark: number): boolean {
    return this.waiters.some((waiter) => waiter.mark === mark);
  }

  private start(): Promise<void> {
    this.started += 1;
    const number = this.started;
    this.running = new Set([...this.running, number]);
    // Inside a promise from the first instruction: a work that throws synchronously is a rejected render, not a
    // render that never ended.
    const run = Promise.resolve().then(() => this.work(number));
    run.then(() => { this.finishedWell(number); }, (error: unknown) => { this.finishedBadly(number, error); });

    return run;
  }

  private finishedWell(number: number): void {
    this.running = new Set([...this.running].filter((one) => one !== number));
    this.finished = Math.max(this.finished, number);
    const released = this.waiters.filter((waiter) => waiter.mark < number);
    this.waiters = this.waiters.filter((waiter) => waiter.mark >= number);
    for (const waiter of released) {
      waiter.resolve();
    }
  }

  /** A failed render releases nobody; a waiter with no other render after its mark still running is told it failed. */
  private finishedBadly(number: number, error: unknown): void {
    this.running = new Set([...this.running].filter((one) => one !== number));
    const failed = this.waiters.filter((waiter) => waiter.mark < number && !this.runningAfter(waiter.mark));
    this.waiters = this.waiters.filter((waiter) => !failed.includes(waiter));
    for (const waiter of failed) {
      waiter.reject(error);
    }
  }
}

interface Waiter {
  readonly mark: number;
  readonly resolve: () => void;
  readonly reject: (reason: unknown) => void;
}
