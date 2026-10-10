import { RoundCountsRead } from './roundCounts';
import { isRunning, SessionFile } from './rounds';
import { UsageEntry } from './usage';

/** How many rounds the session files hold that have stopped running — what {@link RoundCountsCache.noteFinishedRounds} watches. */
export function finishedRounds(sessions: readonly SessionFile[]): number {
  return sessions.flatMap((session) => session.rounds).filter((round) => !isRunning(round)).length;
}

/**
 * The rounds a card line and the Rounds block draw from — read at most once a minute, and sooner when something ended
 * (story 2.2 of todo/PLAN_every_round_is_counted.md).
 *
 * <p><b>Never a spawn per render.</b> A render takes {@link current}, which never reads anything; it then asks
 * {@link refresh}, which reads only when the snapshot is older than its bound or something it counts has ended since,
 * and only one read at a time. A sidebar that repaints twenty times a minute therefore costs one `--round-counts`
 * spawn and one tail read of the ledger a minute.</p>
 *
 * <p><b>Bounded freshness, not "until a round ends".</b> A consultation-only session ends no round, and a trailing
 * window must FALL while nothing runs — a round from five hours and one minute ago is out of the 5 h window whether or
 * not anything happened since. So the bound is an age (60 s), and an ending — a round, a consultation, a question —
 * only makes it come sooner ({@link forget}).</p>
 *
 * <p>`vscode`-free and clock-injected: the loader and the clock are handed in, so the bound, the early refresh and the
 * falling window are fake-clock tests rather than claims.</p>
 */

/** One read: the counts, the last seven days of the ledger, and when it was taken. */
export interface RoundsSnapshot {
  readonly read: RoundCountsRead;
  readonly ledger: readonly UsageEntry[];
  readonly atMs: number;
}

/** What a load answers — the snapshot without its instant, which the cache stamps. */
export type RoundsLoad = (nowMs: number) => Promise<Omit<RoundsSnapshot, 'atMs'>>;

/** The longest a snapshot is drawn from before it is read again. */
export const ROUNDS_MAX_AGE_MS = 60_000;

export class RoundCountsCache {
  private snapshot: RoundsSnapshot | undefined = undefined;

  private stale = true;

  private inFlight: Promise<boolean> | undefined = undefined;

  private finishedSeen = -1;

  constructor(
    private readonly load: RoundsLoad,
    private readonly clock: () => number = Date.now,
    private readonly maxAgeMs: number = ROUNDS_MAX_AGE_MS,
  ) {}

  /** The last snapshot, or nothing before the first read — never a read of its own. */
  current(): RoundsSnapshot | undefined {
    return this.snapshot;
  }

  /** Something it counts has ended — a round, a consultation, a question — so the next {@link refresh} reads again. */
  forget(): void {
    this.stale = true;
  }

  /**
   * How many rounds the session files hold that are no longer running. When it rises a round ended, and the count is
   * read again; the first sighting only remembers it.
   */
  noteFinishedRounds(finished: number): void {
    if (this.finishedSeen >= 0 && finished > this.finishedSeen) {
      this.forget();
    }
    this.finishedSeen = finished;
  }

  /** Read again when due, one read at a time; true when this call's read produced a new snapshot. */
  async refresh(): Promise<boolean> {
    if (this.inFlight !== undefined) {
      return this.inFlight;
    }
    if (!this.due()) {
      return false;
    }
    this.inFlight = this.readNow().finally(() => {
      this.inFlight = undefined;
    });

    return this.inFlight;
  }

  private due(): boolean {
    return this.stale || this.snapshot === undefined || this.clock() - this.snapshot.atMs >= this.maxAgeMs;
  }

  /** A load that throws is a snapshot that says why — stamped like any other, so a failing read is not retried per render. */
  private async readNow(): Promise<boolean> {
    const atMs = this.clock();
    this.stale = false;
    const loaded = await this.load(atMs).catch((reason: unknown) => ({
      read: { state: 'unavailable' as const, why: `the read failed: ${String(reason)}` },
      ledger: [],
    }));
    this.snapshot = { ...loaded, atMs };

    return true;
  }
}
