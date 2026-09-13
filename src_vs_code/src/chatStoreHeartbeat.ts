import { ChatStoreKeeper } from './chatStoreKeeper';
import { HEARTBEAT_EVERY_MS } from './chatStoreSweep';

/**
 * This window's heartbeat: the file that tells every other window which conversations are open
 * HERE, so that none of them sweeps one of these by its age.
 *
 * <p>Why a window has to say so is `chatStoreSweep.ts`'s header; this is the writer. It beats on a
 * timer every {@link HEARTBEAT_EVERY_MS}, and at once — well, on the next tick — whenever the set of
 * open conversations changes: `chatCommand.ts` pulses it when a thread is registered, restored, closed
 * or re-minted under a new id, and the tick is what lets the registry finish registering the entry the
 * pulse is about. A pulse writes only when the ids actually differ from what was last announced; the
 * timer writes regardless, because the refresh IS the point.</p>
 *
 * <p><b>The file is never removed by this class</b> — not on dispose, not on deactivation. A reload
 * runs `deactivate` and then a fresh `activate` in a new host with a new pid, and the tabs the new host
 * is still restoring are protected in the meantime by exactly this file, aged out by the sweep after
 * the stale window. Removing it on the way out would open the gap the heartbeat exists to close.</p>
 *
 * <p>Writes are chained, so two beats cannot race each other's temporary; the keeper's write is
 * atomic and never rejects, so the chain cannot poison. No `vscode`: the timer is injectable, and a
 * test drives it by hand.</p>
 */

/** The two timer calls, injectable so a test can drive the interval without waiting a minute. */
export interface HeartbeatTimers {
  readonly every: (run: () => void, ms: number) => unknown;
  readonly stop: (handle: unknown) => void;
}

const REAL_TIMERS: HeartbeatTimers = {
  every: (run, ms) => {
    const handle = setInterval(run, ms);
    // Never the thing that keeps a host alive: a heartbeat with nobody to hear it is nothing.
    handle.unref();

    return handle;
  },
  stop: (handle) => {
    clearInterval(handle as NodeJS.Timeout);
  },
};

export class ConversationHeartbeat {
  private handle: unknown;

  private queued: ReturnType<typeof setTimeout> | undefined;

  /** The ids last written, joined — so a pulse that changes nothing writes nothing. Unset before the first write. */
  private announced: string | undefined;

  private writes: Promise<unknown> = Promise.resolve();

  /**
   * @param held what this window holds open, asked on every beat — never cached here
   * @param pid the writer's pid, in the file's name and its body
   */
  public constructor(
    private readonly keeper: ChatStoreKeeper,
    private readonly held: () => readonly string[],
    private readonly pid: number = process.pid,
    private readonly timers: HeartbeatTimers = REAL_TIMERS,
    private readonly clock: () => number = Date.now,
  ) {}

  /** Start the timer. A second start is a no-op; one interval per window. */
  public start(everyMs: number = HEARTBEAT_EVERY_MS): void {
    if (this.handle !== undefined) {
      return;
    }
    this.handle = this.timers.every(() => {
      this.beat().catch((reason: unknown) => {
        // The outer edge of a detached call: the keeper answers in a boolean and never rejects, so
        // anything arriving here is a defect worth a line rather than a silence.
        console.error('ConnectOtherAIs: a heartbeat threw', reason);
      });
    }, everyMs);
  }

  /** The set of open conversations may have changed: announce it on the next tick, if it did. */
  public pulse(): void {
    if (this.queued !== undefined) {
      return;
    }
    this.queued = setTimeout(() => {
      this.queued = undefined;
      this.beatIfChanged().catch((reason: unknown) => {
        console.error('ConnectOtherAIs: a heartbeat threw', reason);
      });
    }, 0);
  }

  /** Write the heartbeat now, whatever it last said. `true` when it landed. */
  public beat(): Promise<boolean> {
    return this.write(this.snapshot());
  }

  /** Write the heartbeat now if the ids differ from the last announcement. `false` when nothing needed saying. */
  public beatIfChanged(): Promise<boolean> {
    const ids = this.snapshot();

    return ids.join(' ') === this.announced ? Promise.resolve(false) : this.write(ids);
  }

  /** For a test: wait for a queued pulse and every write so far. */
  public async settled(): Promise<void> {
    if (this.queued !== undefined) {
      await new Promise<void>((resolve) => {
        setTimeout(resolve, 1);
      });
    }
    await this.writes;
  }

  /** Stop the timer. The file stays — see the header. */
  public dispose(): void {
    if (this.handle !== undefined) {
      this.timers.stop(this.handle);
      this.handle = undefined;
    }
    if (this.queued !== undefined) {
      clearTimeout(this.queued);
      this.queued = undefined;
    }
  }

  /** What is held, once each, in one order — so the same set always announces as the same text. */
  private snapshot(): readonly string[] {
    // A NAMED comparator, not the default: the default sorts by UTF-16 code unit, which is the one
    // "sort" in JavaScript that does something different from what its call site reads like. The
    // order only has to be STABLE — the text is compared against this window's own previous
    // announcement — and `localeCompare` is what every other ordering in this feature uses.
    return [...new Set(this.held())].sort((left, right) => left.localeCompare(right));
  }

  private write(ids: readonly string[]): Promise<boolean> {
    const step = async (): Promise<boolean> => {
      const landed = await this.keeper.beat(this.pid, ids, this.clock());
      if (landed) {
        this.announced = ids.join(' ');
      }

      return landed;
    };
    const next = this.writes.then(step, step);
    this.writes = next;

    return next;
  }
}
