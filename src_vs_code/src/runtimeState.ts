import { randomBytes } from 'node:crypto';
import type { NotificationRecord } from './notifications';
import type { PathFinding } from './pathFindings';
import { type Suppressor, suppressor } from './suppression';
import { type Gap, NO_GAP } from './writeGap';

/**
 * The state that must exist ONCE per extension host (v2 E4, story s4-chat-carried, task t1; extension.md § 10 row 7).
 *
 * <p>The shell loads each feature as its own bundle, and an esbuild bundle that imports `notify.ts` carries its OWN copy of that file's
 * module-level variables. Two copies of the notify suppressor are two budgets and two run ids; two copies of the data root are two answers to
 * "where does this window keep its data"; two copies of the findings board are a path watch the other bundle's writers never hear from. So the
 * state is not module-level in any bundle that uses it: it lives in the object {@link createRuntime} makes, and the `runtime` module
 * (`shell/modules/runtime.ts`, `dist/modules/runtime.js`) makes exactly one, which the shell's loader hands to every bundle by reference
 * (`hostRuntime.ts`). This file and `shell/modules/runtime.ts` are therefore forbidden inputs of every other bundle (`scripts/bundle-entries.json`,
 * `forbidInputs`): the type imports above are erased, and nothing here is bundled twice.</p>
 *
 * <p>Plain data and one factory. The functions that read and change these fields stay where they were (`notify.ts`, `dataDir.ts`,
 * `findingsBoard.ts`), so no decision moved.</p>
 */

/**
 * The shapes below are spelled here, not imported from `dataDir.ts` and `findingsBoard.ts`, which import `hostRuntime.ts` (which imports this file's types):
 * a type import is erased at run time, but `importCycles.test.mjs` counts it, and a ring of three is a ring. Structural typing keeps them one type.
 */

/** `dataDir.ts`'s `StorageChoice`: a root and a side. */
export interface StorageChoice {
  readonly directory: string;
  readonly side: string;
}

/** `findingsBoard.ts`'s `HeldNotice`: the setting key a notice is about and the record the ledger did not take. */
export interface HeldNotice {
  readonly key: string;
  readonly record: NotificationRecord;
}

/** What `notify.ts` keeps: which run this is, what bounds this run's share of the ledger, and the write gap. */
export interface NotifyState {
  /** Minted once per host start, and NOT the pid (pids are reused): a restart is a new observer. */
  readonly run: string;
  /** What bounds this run's share of the ledger. It bounds what is WRITTEN and never what is shown (`suppression.ts`). */
  readonly bounds: Suppressor;
  /** What this run could not write, and over what window. Never a notification. */
  gap: Gap;
  /** One gap record in flight at a time. */
  flushing: boolean;
}

/** The two settings layers this window read for itself (`dataDir.ts`): the ONE answer to "where does this window keep its data". */
export interface DataRootState {
  installed: { readonly thisSide: StorageChoice; readonly shared: StorageChoice };
}

/** What `findingsBoard.ts` keeps: the findings the host published, the held notices, and the data-root gate. */
export interface BoardState {
  published: readonly PathFinding[];
  held: readonly HeldNotice[];
  listeners: readonly (() => void)[];
  settling: Promise<unknown>;
  known: boolean;
  absentRoot: PathFinding | undefined;
  backListeners: readonly (() => void)[];
}

/** Everything that exists once per host. */
export interface HostRuntime {
  readonly notify: NotifyState;
  readonly dataRoot: DataRootState;
  readonly board: BoardState;
  /**
   * A host singleton by key: `make` runs the first time any bundle asks, and every later call, from any bundle, gets that same object. The path
   * watch lives here — the chat module and the old activation both start it, and it must be one watch.
   */
  readonly once: <T>(key: string, make: () => T) => T;
}

const NOTHING_CHOSEN: StorageChoice = { directory: '', side: '' };

/** A fresh runtime. The `runtime` module calls this once; a plain `node` run with no shell calls it once per process (`hostRuntime.ts`). */
export function createRuntime(): HostRuntime {
  const run = randomBytes(6).toString('hex');
  const singletons = new Map<string, unknown>();

  return {
    notify: { run, bounds: suppressor(run, process.pid), gap: NO_GAP, flushing: false },
    dataRoot: { installed: { thisSide: NOTHING_CHOSEN, shared: NOTHING_CHOSEN } },
    board: { published: [], held: [], listeners: [], settling: Promise.resolve(), known: true, absentRoot: undefined, backListeners: [] },
    once: <T>(key: string, make: () => T): T => {
      if (!singletons.has(key)) {
        singletons.set(key, make());
      }

      return singletons.get(key) as T;
    },
  };
}
