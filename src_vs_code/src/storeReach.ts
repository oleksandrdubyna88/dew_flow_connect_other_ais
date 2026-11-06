import { join } from 'node:path';

import { DATABASE_FILE } from './dataDir';

/**
 * Whether the other installations' stores are THERE — a cached answer the render reads, refreshed in the background (E6.S4 of
 * todo/PLAN_the_round_counter_tail_E6.md, the render path of its risk paragraph).
 *
 * <p>The panel used to ask `existsSync(<dir>/coai.db)` while it drew. Against a share that is not answering (WSL stopped, a
 * NAS unplugged) a synchronous probe blocks the extension host for the SMB timeout, and the 8 s spawn cap of the counts cannot
 * bound a call that is not a spawn. So the render only {@link current reads} what is known; {@link refresh} asks in the
 * background through the panel's off-loop `reachable` probe, and a probe that has not answered within the deadline loses the
 * race — the store reads *not answering*, which the Rounds block says and does not count.</p>
 *
 * <p><b>One probe in flight per directory.</b> A probe still pending at its deadline is KEPT, not abandoned, and a refresh
 * neither waits for it again nor starts another against a share that is down: an UNSETTLED probe is skipped. Once it has settled
 * (the share came back, or the OS gave up) the next refresh consumes its answer at once, whatever the age of the cache. Only
 * after that does the cache age decide when a fresh probe is started. Only the existence of `coai.db` is asked; nothing is
 * opened.</p>
 */

/** How long a probe may take before the store reads *not answering* — well inside the 8 s spawn cap. */
export const STORE_REACH_DEADLINE_MS = 2_000;

/** How long an answer is held before it is asked again. */
const HELD_MS = 60_000;

/** `here`: the store file is there. `absent`: the directory holds no store. `not-answering`: the probe lost the race. */
export type Reach = 'here' | 'absent' | 'not-answering';

export interface Reached {
  readonly directory: string;
  readonly reach: Reach;
}

export interface ReachOptions {
  readonly deadlineMs?: number;
  readonly maxAgeMs?: number;
  readonly clock?: () => number;
}

interface Known {
  readonly reach: Reach;
  readonly atMs: number;
}

/** A probe in flight: `settled` flips when it answers (or throws), however long after its deadline that is. */
interface Probe {
  readonly read: Promise<boolean>;
  settled: boolean;
}

/**
 * What the Rounds block is drawn from, refreshed TOGETHER: the round counts and the reach of the other stores. Every surface that
 * draws the block asks through here, so none of them can refresh the counts and leave the reach cache empty — the block would then
 * miss another installation's store and a store that is not answering. True when either changed, so the panel should repaint.
 */
export async function refreshCountsAndReach(
  counts: { refresh(): Promise<boolean> },
  reach: StoreReach,
  directories: readonly string[],
): Promise<boolean> {
  const [counted, reached] = await Promise.all([counts.refresh(), reach.refresh(directories)]);

  return counted || reached;
}

/**
 * The Rounds PAGE's refresh: waits for the counts only, and lets the reach probe run behind the draw. The page draws from the reach
 * cache as it stands; `repaint` is called once if the probe, when it settles, changed that cache. A store on a dead share would
 * otherwise hold the page back for the probe's whole deadline. Resolves with whether the COUNTS changed.
 */
export async function refreshCountsThenReach(
  counts: { refresh(): Promise<boolean> },
  reach: StoreReach,
  directories: readonly string[],
  repaint: () => void,
): Promise<boolean> {
  // Started first so the probe runs beside the counts; never awaited here. `refresh` does not throw (a probe that throws reads as
  // absent), and a throwing `repaint` is logged rather than lost as an unhandled rejection.
  void reach.refresh(directories)
    .then((changed) => { if (changed) { repaint(); } })
    .catch((reason: unknown) => { console.error('ConnectOtherAIs: the store reach refresh could not repaint the Rounds page', reason); });

  return counts.refresh();
}

export class StoreReach {
  private readonly known = new Map<string, Known>();

  private readonly pending = new Map<string, Probe>();

  private readonly settings: Required<ReachOptions>;

  /** @param probe whether a path is there — off the event loop, and never throws (the panel's `reachable`). */
  constructor(private readonly probe: (path: string) => Promise<boolean>, options: ReachOptions = {}) {
    this.settings = { deadlineMs: STORE_REACH_DEADLINE_MS, maxAgeMs: HELD_MS, clock: Date.now, ...options };
  }

  /** What is known of the given directories, in their order — synchronous, a directory nobody has probed yet is left out. */
  current(directories: readonly string[]): readonly Reached[] {
    return directories.flatMap((directory) => {
      const known = this.known.get(directory);

      return known === undefined ? [] : [{ directory, reach: known.reach }];
    });
  }

  /** Asks again what is due, in parallel; true when any directory's reach changed, so the panel should repaint. */
  async refresh(directories: readonly string[]): Promise<boolean> {
    const changes = await Promise.all(directories.filter((one) => this.due(one)).map((one) => this.check(one)));

    return changes.includes(true);
  }

  private due(directory: string): boolean {
    const probe = this.pending.get(directory);
    if (probe !== undefined) {
      return probe.settled;
    }
    const known = this.known.get(directory);

    return known === undefined || this.settings.clock() - known.atMs >= this.settings.maxAgeMs;
  }

  private async check(directory: string): Promise<boolean> {
    const probe = this.pending.get(directory) ?? this.start(directory);
    this.pending.set(directory, probe);
    const reach = await this.raced(probe.read);
    if (reach !== 'not-answering') {
      this.pending.delete(directory);
    }
    const changed = this.known.get(directory)?.reach !== reach;
    this.known.set(directory, { reach, atMs: this.settings.clock() });

    return changed;
  }

  /** A probe that throws is a store that is not there, like a path `access` refuses. */
  private start(directory: string): Probe {
    const probe: Probe = {
      settled: false,
      read: this.probe(join(directory, DATABASE_FILE)).catch(() => false).then((here) => { probe.settled = true; return here; }),
    };

    return probe;
  }

  private async raced(read: Promise<boolean>): Promise<Reach> {
    let timer: NodeJS.Timeout | undefined = undefined;
    const late = new Promise<Reach>((resolve) => {
      timer = setTimeout(() => resolve('not-answering'), this.settings.deadlineMs);
    });
    try {
      return await Promise.race([read.then((here): Reach => (here ? 'here' : 'absent')), late]);
    } finally {
      clearTimeout(timer);
    }
  }
}
