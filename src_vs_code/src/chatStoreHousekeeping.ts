import { ConversationIndex } from './chatStoreCache';
import { ChatStoreFile } from './chatStoreFile';
import { ConversationHeartbeat, HeartbeatTimers } from './chatStoreHeartbeat';
import { ChatStoreKeeper, runSweep } from './chatStoreKeeper';
import { SweepReport, describeSweep } from './chatStoreSweep';

/**
 * Starting the store's housekeeping for one window, in the order that loses nothing.
 *
 * <p>Three things start here and the ORDER between them is the whole point — which is why it is a
 * module a test can drive against a real directory rather than a paragraph in `activate`:</p>
 *
 * <ol>
 *   <li><b>The heartbeat beats from the first moment</b>, before anything is waited for, so this
 *   window is announced while its tabs are still being restored; the ids it names are read live from the
 *   registry on every beat, and `chatCommand.ts` pulses it whenever that set changes.</li>
 *   <li><b>The sweep waits for what it is told to</b> — the migration — because a conversation still in
 *   the memento and not yet on disk must not be read as a directory with nothing to protect. <b>And it
 *   waits for the heartbeat to have LANDED</b>: a first beat that did not land is tried once more when
 *   the wait is over, and if it still has not landed the sweep does not run at all — a window that
 *   cannot say what it is holding has no business deleting anything (codex, the blocking finding: the
 *   first draft awaited the boolean and discarded it). It skips every id a live heartbeat names and
 *   every id this window holds, re-checks each expiry under the conversation's lock, and does nothing
 *   while the store is `unavailable`.</li>
 *   <li><b>The index is published after the sweep</b>, atomically, or the picker would hold a row for a
 *   record the sweep is removing. Its survey is the same availability question the sweep asked, so
 *   neither runs against a store that will not answer. It is built whether or not the sweep ran — it
 *   deletes nothing, and a picker over a full store must not say "none".</li>
 * </ol>
 *
 * <p><b>One root.</b> The keeper, the heartbeat and the sweep are bound to the store's own directory,
 * read from the store; a `dir` handed in beside the store was two roots a future change could let
 * drift, the sweep surveying one while deleting from another. (The code round.)</p>
 *
 * <p>Nothing here throws: the sweep and the index answer in outcomes, each outcome is said on the
 * console, and a defect that rejects anyway is caught at the end of {@link Housekeeping.ready} and said
 * too — the outer edge of a detached call. No `vscode`.</p>
 */

/** What starting housekeeping needs of the world. */
export interface HousekeepingDeps {
  /** The store — and, through its directory, the ONE root everything here is bound to. */
  readonly store: ChatStoreFile;
  /** What this window holds open, asked at every beat and at every decision. */
  readonly held: () => readonly string[];
  /** What must finish first: the migration. */
  readonly after: Promise<unknown>;
  readonly pid?: number;
  /** The clock, pinned by a test. Every age the sweep measures, and the index's load instant, come from it. */
  readonly clock?: () => number;
  /** The heartbeat's timers, injected by a test. */
  readonly timers?: HeartbeatTimers;
}

export interface Housekeeping {
  readonly index: ConversationIndex;
  readonly heartbeat: ConversationHeartbeat;
  /**
   * Resolves once the sweep has run — or said why not — and the index has been published once, with
   * the sweep's report; `undefined` for a defect, which has been said on the console. For a test, and
   * for a picker that wants to know whether the index is there yet.
   */
  readonly ready: Promise<SweepReport | undefined>;
  /** Stop the heartbeat's timer. Its file stays — `chatStoreHeartbeat.ts` says why. */
  dispose(): void;
}

export function startHousekeeping(deps: HousekeepingDeps): Housekeeping {
  const pid = deps.pid ?? process.pid;
  const clock = deps.clock ?? Date.now;
  const keeper = new ChatStoreKeeper(deps.store.dir);
  const index = new ConversationIndex(keeper, deps.store);
  const heartbeat = new ConversationHeartbeat(keeper, deps.held, pid, deps.timers, clock);
  heartbeat.start();
  const announced = heartbeat.beat();
  const ready = deps.after
    .then(() => announced)
    .then(async (landed): Promise<SweepReport> => {
      // Branched on, not discarded: a first beat that did not land is tried once more now that the
      // wait is over, and a window that still cannot announce what it holds does not sweep.
      if (!landed && !await heartbeat.beat()) {
        return { kind: 'skipped', why: 'unannounced', reason: '' };
      }

      return runSweep({ store: deps.store, keeper, held: () => new Set(deps.held()), pid, now: clock(), clock });
    })
    .then(async (report): Promise<SweepReport | undefined> => {
      console.info(describeSweep(report));
      const built = await index.refresh(clock());
      if (built.kind === 'unavailable') {
        console.warn(`ConnectOtherAIs: the conversation index could not be built — ${built.reason}; the picker will say so`);
      }

      return report;
    })
    .catch((reason: unknown): undefined => {
      console.error('ConnectOtherAIs: the conversation sweep or index threw', reason);

      return undefined;
    });

  return {
    index,
    heartbeat,
    ready,
    dispose: () => {
      heartbeat.dispose();
    },
  };
}
