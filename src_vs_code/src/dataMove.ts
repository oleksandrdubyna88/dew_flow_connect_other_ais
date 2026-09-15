import { DATA_TO_MOVE } from './dataDir';

/**
 * Whether a data directory may be moved, and whether the move that happened worked.
 *
 * <p>Free of `vscode` so every one of these decisions is a test rather than something checked by
 * clicking. The dialogs, the folder picker and the copy itself are `extension.ts`'s; what is here is
 * every judgement they make, because a judgement behind a dialog is a judgement nobody checks.</p>
 *
 * <p><b>Read this next to `dataChoice.ts`, which holds the opposite rule about the same folder.</b>
 * Adopting a folder at install time is reading a history that is already there: a non-empty folder
 * is the whole point. Moving is writing where nothing is: a non-empty destination is refused,
 * because copying a database over a side's own history destroys it — and destroys it before the
 * "check your history before deleting anything" step could possibly notice.</p>
 */

/** What is running, or left behind by something that was. */
export interface SourceActivity {
  /** `coai.db-wal` / `coai.db-shm`, if they are there. */
  readonly sidecars: readonly string[];
  /** Reviewer processes this data directory says are alive. */
  readonly livePids: number;
}

/** What a directory holds, counted rather than described, so two of them can be compared. */
export interface StorageFingerprint {
  /** Rounds, counted in SQL by the server rather than over a page of results. */
  readonly rounds: number;
  /** Session files. */
  readonly sessions: number;
  /** Lines of the spending ledger. */
  readonly usageLines: number;
}

/** What is known about the last move, kept so the delete can be offered later — or refused. */
export interface MoveRecord {
  readonly from: string;
  readonly to: string;
  readonly verified: boolean;
}

/**
 * Why this destination cannot be moved into, or empty.
 *
 * <p>Anything that CARRIES history refuses it, not only the database. A destination holding
 * `sessions/` and no `coai.db` is somebody's half-finished attempt, and copying into it merges two
 * histories into a third that belongs to neither — the same loss, arrived at by a route the
 * obvious check does not cover.</p>
 *
 * <p>What is NOT moved is not consulted: a `logs/` or a `servers/` in the destination was written by
 * whatever already runs there, carries nothing this move would overwrite, and refusing on it would
 * refuse most of the real destinations somebody would pick.</p>
 */
export function destinationRefusal(holds: readonly string[]): string {
  const clashes = holds.filter((entry) => DATA_TO_MOVE.includes(entry));
  if (clashes.length === 0) {
    return '';
  }

  return `That folder already holds ${clashes.join(', ')}, which means it has a history of its own. `
    + 'Copying into it would write over that history before anything could check it. Choose an empty '
    + 'folder, or a side name inside this one that is not in use.';
}

/**
 * Why this directory cannot be copied FROM right now, or empty.
 *
 * <p>A database being written while it is read copies as a file missing its newest transactions —
 * and because the journal mode is WAL, those transactions are sitting in a sidecar this move would
 * have to take as well. A sidecar that exists means either that something has the database open, or
 * that something was killed while it did; neither is a state to copy from.</p>
 *
 * <p>The extension cannot stop the server: the MCP client owns that process. So it refuses and says
 * what to stop, which is the only honest thing available to it.</p>
 */
export function sourceRefusal(activity: SourceActivity): string {
  if (activity.livePids > 0) {
    return `${activity.livePids} reviewer ${activity.livePids === 1 ? 'process is' : 'processes are'} `
      + 'still running against this folder. Let them finish, or stop them, before moving what they '
      + 'are writing to.';
  }

  if (activity.sidecars.length > 0) {
    return `${activity.sidecars.join(' and ')} ${activity.sidecars.length === 1 ? 'is' : 'are'} beside `
      + 'the database, which means something has it open or was stopped while it did — and the rounds '
      + 'committed most recently are in there rather than in coai.db. Stop the server (your MCP client '
      + 'restarting it is what releases the database) and try again.';
  }

  return '';
}

/**
 * Why a completed move cannot be called verified, or empty.
 *
 * <p>Three counts rather than one, because they fail differently: the database is a single file and
 * nearly always arrives whole, while the sessions are a hundred small ones and are exactly what a
 * partial copy loses. A verification that counted only rounds would call that move a success.</p>
 *
 * <p><b>More is a failure too.</b> A destination that reads back more than the source is not a
 * destination this move filled — it held a history the refusal should have caught, or two moves are
 * racing. "At least as many" would pass both.</p>
 */
export function verificationFailure(before: StorageFingerprint, after: StorageFingerprint): string {
  const differences = [
    ['rounds', before.rounds, after.rounds] as const,
    ['sessions', before.sessions, after.sessions] as const,
    ['ledger lines', before.usageLines, after.usageLines] as const,
  ].filter(([, from, to]) => from !== to);

  if (differences.length === 0) {
    return '';
  }

  const said = differences.map(([what, from, to]) => `${what}: ${from} before, ${to} after`).join('; ');

  return `The new folder does not read back what the old one held — ${said}. Nothing has been `
    + 'deleted, and the old folder is exactly as it was; check what is missing before using either.';
}

/**
 * Whether the old copy of a moved directory may be deleted.
 *
 * <p>Only after a move that VERIFIED. A copy that did not verify is a copy that may have lost
 * something, and the old folder is then the only place that something still exists — which is why
 * the delete is a separate action rather than the tail of the same click.</p>
 *
 * <p>The same-directory case cannot arise from the flow and is refused all the same: a record left
 * by an older build, or a hand-edited setting, would otherwise delete the directory in use. A guard
 * against a state that "cannot happen" costs one comparison.</p>
 */
export function mayDeleteTheOldCopy(record: MoveRecord | undefined): boolean {
  return record !== undefined && record.verified && record.from !== record.to;
}
