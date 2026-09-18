import { SyncOutcome } from './serverSettingsSync';

/**
 * How many times the settings mirror tries, and what it says when it stops.
 *
 * <p><b>The silence this replaces.</b> `mirrorSettings` retried a `busy` sync exactly once and then
 * dropped it, and a `failed` one not at all — so a configuration somebody had just changed could
 * simply not reach the server, with no surface anywhere saying so. That is the 2026-09-16 incident
 * from its other side: the mirror declining to write, quietly.</p>
 *
 * <p>Everything it needs is a PARAMETER — the sync, the timer, its cancel, and the reporter — so the
 * whole schedule is run by tests rather than read. A schedule tested by sleeping is a test that is
 * slow when it passes and flaky when it does not.</p>
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

export class MirrorSchedule {
  private pending: Pending | undefined;

  private attempt = 0;

  /**
   * Which conditions have already been reported, cleared by a write that lands.
   *
   * <p>Once per CONDITION rather than once per attempt, and the two are kept apart: a lock another
   * window holds and a directory this one cannot write are different facts with different cures, and
   * one counter for both would let recovering from either hide the other. The same rule the
   * notifications ledger applies to `(code, subject)` and `reportRefusal` applies to the setting.</p>
   */
  private readonly said = new Set<Retryable>();

  constructor(
    private readonly sync: () => Promise<SyncOutcome>,
    private readonly clock: Clock,
    private readonly report: ReportExhausted,
  ) {}

  /**
   * A settings change, an activation, or somebody pressing *Try again*.
   *
   * <p><b>It SUPERSEDES whatever was pending</b> rather than running beside it. Two schedules would
   * race each other's attempt counters, and the one that lost would report a condition the other had
   * already recovered from. The half that is safe by construction is worth saying too: every attempt
   * calls `sync()`, which re-reads the configuration, so a retry cannot write a stale payload over a
   * newer one — it writes what the settings say at the moment it runs. (Two reviewers, the plan
   * round, from opposite directions.)</p>
   */
  start(): void {
    this.cancel();
    this.attempt = 0;
    void this.once();
  }

  /** Drops the pending attempt. The host calls this on `deactivate`; a restart re-derives. */
  cancel(): void {
    if (this.pending !== undefined) {
      this.clock.stop(this.pending);
      this.pending = undefined;
    }
  }

  private async once(): Promise<void> {
    this.attempt += 1;
    const outcome = await this.sync();
    if (outcome !== 'busy' && outcome !== 'failed') {
      // It wrote, or there was nothing to write. Either way the conditions below are over, and the
      // next one that happens is news rather than a repeat.
      this.said.clear();

      return;
    }

    const wait = WAITS_MS[this.attempt - 1];
    if (wait === undefined) {
      this.exhausted(outcome);

      return;
    }
    this.pending = this.clock.later(() => {
      this.pending = undefined;
      void this.once();
    }, wait);
  }

  private exhausted(outcome: Retryable): void {
    if (this.said.has(outcome)) {
      return;
    }
    this.said.add(outcome);
    this.report(outcome);
  }
}
