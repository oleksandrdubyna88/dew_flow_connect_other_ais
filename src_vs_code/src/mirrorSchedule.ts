import { SyncOutcome } from './serverSettingsSync';

/**
 * How many times the settings mirror tries, and what it says when it stops.
 *
 * <p><b>The silence this replaces.</b> `mirrorSettings` retried a `busy` sync exactly once and then
 * dropped it, and a `failed` one not at all — so a configuration somebody had just changed could
 * simply not reach the server, with no surface anywhere saying so. That is the 2026-09-16 incident
 * from its other side: the mirror declining to write, quietly.</p>
 *
 * <p>Everything it needs is a PARAMETER — the sync, the timer, its cancel, and the two reporters —
 * so the whole schedule is run by tests rather than read. A schedule tested by sleeping is a test
 * that is slow when it passes and flaky when it does not.</p>
 */

/** The outcomes worth trying again: another window was writing, or this one could not. */
export type Retryable = Extract<SyncOutcome, 'busy' | 'failed'>;

/**
 * The waits BETWEEN attempts, in order, measured from the moment an attempt answered.
 *
 * <p>Three attempts counting the first, so the whole schedule is over in about eight seconds. That
 * shortness is the point rather than an accident: reporting only on exhaustion keeps a share that
 * hiccupped for 200 ms from raising anything, and a schedule any longer would leave somebody who
 * has genuinely made their settings directory read-only waiting to be told. Measured from the
 * ANSWER, not from the start, so a slow lock does not eat the next wait.</p>
 */
export const WAITS_MS: readonly number[] = [2_000, 6_000];

/** One attempt per wait, plus the attempt that starts it. */
export const ATTEMPTS = WAITS_MS.length + 1;

/** Whatever the host's `setTimeout` hands back. Never inspected here, only handed back to `stop`. */
export type Pending = unknown;

export interface Clock {
  readonly later: (work: () => void, ms: number) => Pending;
  readonly stop: (pending: Pending) => void;
}

/** Told when the mirror has given up, and on which of the two conditions. */
export type ReportExhausted = (outcome: Retryable) => void;

/**
 * Told when a write LANDS after something was reported, and only then.
 *
 * <p>A person who presses *Try again* and is told nothing cannot tell a retry that worked from one
 * that never fired. It is silent until something has been said, because announcing every ordinary
 * write would be the churn the run budget exists to stop. (gemini, the code round.)</p>
 */
export type ReportRecovered = () => void;

export class MirrorSchedule {
  private pending: Pending | undefined;

  /**
   * Which run is the live one.
   *
   * <p><b>This is what makes superseding real.</b> `cancel()` can drop a pending TIMER, and could
   * never drop an attempt that was already inside `await sync()` — so a settings change arriving
   * while a slow lock was being waited on left the old attempt to come back afterwards, arm a timer
   * of its own and report against a schedule that had already recovered. Two timers then shared one
   * counter. Every continuation carries the run it belongs to and does nothing at all if that run
   * is no longer this one, which also stops a schedule cancelled by a closing window from waking up
   * inside a host that has gone. (Five reviewers, from four roles, the code round.)</p>
   */
  private run = 0;

  /**
   * Which conditions have already been reported, cleared by a write that lands.
   *
   * <p>Once per CONDITION rather than once per attempt, and the two are kept apart: a lock another
   * window holds and a directory this one cannot write are different facts with different cures, and
   * one counter for both would let recovering from either hide the other. The same rule the
   * notifications ledger applies to `(code, subject)` and `reportRefusal` applies to the setting.</p>
   *
   * <p>REPLACED rather than mutated, per `coding-style.md`. (codex and gemini, the code round.)</p>
   */
  private said: ReadonlySet<Retryable> = new Set();

  constructor(
    private readonly sync: () => Promise<SyncOutcome>,
    private readonly clock: Clock,
    private readonly report: ReportExhausted,
    private readonly recovered: ReportRecovered,
  ) {}

  /**
   * A settings change, an activation, or somebody pressing *Try again*.
   *
   * <p><b>It SUPERSEDES whatever was pending</b> rather than running beside it. The half that is
   * safe by construction is worth saying too: every attempt calls `sync()`, which re-reads the
   * configuration, so a retry cannot write a stale payload over a newer one — it writes what the
   * settings say at the moment it runs. (Two reviewers, the plan round, from opposite
   * directions.)</p>
   */
  start(): void {
    this.cancel();
    void this.once(this.run, 1);
  }

  /**
   * Drops the pending attempt AND disowns any attempt already in flight.
   *
   * <p>The host calls this on `deactivate`; a restart re-derives everything from the file.</p>
   */
  cancel(): void {
    this.run += 1;
    if (this.pending !== undefined) {
      this.clock.stop(this.pending);
      this.pending = undefined;
    }
  }

  /**
   * One call to the sync, with an unexpected throw read as the failure it is.
   *
   * <p>`sync()` is typed to answer an outcome, and a module whose whole subject is silence may not
   * assume that. Without this an unhandled rejection left the schedule stuck on an incremented
   * counter with nothing armed — silent, which is the defect. (gemini, the code round.)</p>
   */
  private async tried(): Promise<SyncOutcome> {
    try {
      return await this.sync();
    } catch (reason) {
      console.error('ConnectOtherAIs: the settings mirror threw rather than answering', reason);

      return 'failed';
    }
  }

  private async once(run: number, attempt: number): Promise<void> {
    if (run !== this.run) {
      return;
    }
    const outcome = await this.tried();
    if (run !== this.run) {
      return;
    }
    if (outcome === 'written' || outcome === 'unchanged') {
      // The configuration IS what the server reads, either because this attempt wrote it or because
      // it was already there. The conditions below are over, and the next one is news.
      this.landed();

      return;
    }
    if (outcome !== 'busy' && outcome !== 'failed') {
      // `stood-down`: a newer build owns the file. Trying again cannot help — only a reload can, and
      // `serverSettingsSync` offers exactly that, once per version. It is emphatically NOT a write,
      // so nothing is cleared: a failure reported a moment ago is still true. (Three reviewers, the
      // code round, and the only finding all three vendors raised.)
      return;
    }
    this.next(run, attempt, outcome);
  }

  private next(run: number, attempt: number, outcome: Retryable): void {
    const wait = WAITS_MS[attempt - 1];
    if (wait === undefined) {
      this.exhausted(outcome);

      return;
    }
    this.pending = this.clock.later(() => {
      this.pending = undefined;
      void this.once(run, attempt + 1);
    }, wait);
  }

  private landed(): void {
    const had = this.said;
    this.said = new Set();
    if (had.size > 0) {
      this.recovered();
    }
  }

  private exhausted(outcome: Retryable): void {
    if (this.said.has(outcome)) {
      return;
    }
    this.said = new Set([...this.said, outcome]);
    this.report(outcome);
  }
}
