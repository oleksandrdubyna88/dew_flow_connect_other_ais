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
 * <p><b>One probe in flight per directory.</b> A probe still pending at its deadline is KEPT, not abandoned: the next refresh
 * awaits the same promise instead of starting another against a share that is down, and takes its answer if it has settled by
 * then (the share came back). Only the existence of `coai.db` is asked; nothing is opened.</p>
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

export class StoreReach {
  private readonly known = new Map<string, Known>();

  private readonly pending = new Map<string, Promise<boolean>>();

  private readonly deadlineMs: number;

  private readonly maxAgeMs: number;

  private readonly clock: () => number;

  /** @param probe whether a path is there — off the event loop, and never throws (the panel's `reachable`). */
  constructor(private readonly probe: (path: string) => Promise<boolean>, options: ReachOptions = {}) {
    this.deadlineMs = options.deadlineMs ?? STORE_REACH_DEADLINE_MS;
    this.maxAgeMs = options.maxAgeMs ?? HELD_MS;
    this.clock = options.clock ?? Date.now;
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
    const known = this.known.get(directory);

    return known === undefined || this.pending.has(directory) || this.clock() - known.atMs >= this.maxAgeMs;
  }

  private async check(directory: string): Promise<boolean> {
    const read = this.pending.get(directory) ?? this.start(directory);
    this.pending.set(directory, read);
    const reach = await this.raced(read);
    if (reach !== 'not-answering') {
      this.pending.delete(directory);
    }
    const changed = this.known.get(directory)?.reach !== reach;
    this.known.set(directory, { reach, atMs: this.clock() });

    return changed;
  }

  /** A probe that throws is a store that is not there, like a path `access` refuses. */
  private start(directory: string): Promise<boolean> {
    return this.probe(join(directory, DATABASE_FILE)).catch(() => false);
  }

  private async raced(read: Promise<boolean>): Promise<Reach> {
    let timer: NodeJS.Timeout | undefined = undefined;
    const late = new Promise<Reach>((resolve) => {
      timer = setTimeout(() => resolve('not-answering'), this.deadlineMs);
    });
    try {
      return await Promise.race([read.then((here): Reach => (here ? 'here' : 'absent')), late]);
    } finally {
      clearTimeout(timer);
    }
  }
}
