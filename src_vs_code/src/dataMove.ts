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
  /**
   * How many entries each moved DIRECTORY holds, by name.
   *
   * <p>Three counts could not see an edited prompt, a replaced picture or an audit record — they
   * cover the database, the sessions and the ledger, and the inventory moves sixteen things (codex,
   * code round). This sees anything added to or removed from the rest of them.</p>
   *
   * <p>What it deliberately does NOT see is a file edited in place without changing the count. The
   * cure for that is hashing every byte of a copy that is on a network drive by definition, and the
   * price is not worth the last increment — so it is said here rather than implied.</p>
   *
   * <p>Optional, because a record written before this existed has none, and a comparison that cannot
   * be made must not read as one that passed.</p>
   */
  readonly entries?: Readonly<Record<string, number>>;
}

/** What is known about the last move, kept so the delete can be offered later — or refused. */
export interface MoveRecord {
  readonly from: string;
  readonly to: string;
  readonly verified: boolean;
  /**
   * What the SOURCE held when the copy was taken.
   *
   * <p>Optional because a record written by an older build has none — and that is treated as
   * "cannot compare", never as "nothing changed". See {@link sourceChangedSince}.</p>
   */
  readonly held?: StorageFingerprint;
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
/**
 * Why this destination cannot be the one, on the grounds of WHERE it is, or empty.
 *
 * <p><b>The worst finding of the code round</b> (codex, Blocking). `C:\coai` moved into
 * `C:\coai\new` passes every other check — the destination is empty, nothing is running, the copy
 * succeeds and verifies. Then deleting the source takes the copy inside it, and the configuration is
 * left pointing at a directory that no longer exists.</p>
 *
 * <p>Both paths are compared after canonicalising, and a separator is appended before the prefix
 * test so that `C:\coai2` is not read as living inside `C:\coai`.</p>
 *
 * @param within Whether the second path is the first, or sits inside it. Injected so the rule is
 *   testable without a filesystem, and so the caller supplies the platform's own comparison.
 */
export function destinationPlaceRefusal(source: string, destination: string, within: (outer: string, inner: string) => boolean): string {
  if (within(source, destination)) {
    return `${destination} is inside ${source}, which is the folder being copied. The copy would `
      + 'land inside its own source, and deleting the old folder afterwards would take the copy with '
      + 'it. Choose a folder outside this one.';
  }

  if (within(destination, source)) {
    return `${destination} contains ${source}, the folder being copied. Choose a folder that is not `
      + 'above this one.';
  }

  return '';
}

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

  return '';
}

/**
 * What is worth saying about the source before copying it, or empty.
 *
 * <p><b>A write-ahead log used to refuse the move, and that was wrong twice over</b> (codex, plan
 * round). It made the feature unreachable for exactly the installations most likely to want it: an
 * unclean stop leaves a sidecar behind and nothing removes it by itself, so those installations
 * could never move at all. And it contradicted the inventory, which copies BOTH sidecars precisely
 * because they carry committed rounds — with nothing running, copying the three files together IS a
 * consistent snapshot.</p>
 *
 * <p>So it is said rather than enforced. What the extension genuinely cannot know is whether the MCP
 * client has a server attached, because the server opens the database per write and closes it: an
 * absent sidecar proves nothing either. The defence that actually holds is
 * {@link sourceChangedSince}, read immediately before the delete.</p>
 */
export function sourceWarning(activity: SourceActivity): string {
  if (activity.sidecars.length === 0) {
    return '';
  }

  return `${activity.sidecars.join(' and ')} ${activity.sidecars.length === 1 ? 'is' : 'are'} beside `
    + 'the database, which means something has it open or was stopped while it did. They are copied '
    + 'with it, so the rounds in them travel too — but if your MCP client is running, stop it first: '
    + 'a database copied while it is being written to arrives missing whatever was written during the '
    + 'copy.';
}

/**
 * Why the old folder must not be deleted after all, or empty.
 *
 * <p><b>The one defence available against a writer nobody can enumerate.</b> The extension cannot
 * stop the MCP client's server, and cannot detect one attached — so a move can copy, verify, and
 * then have the server append another round to the SOURCE before anybody presses delete. Both codex
 * and gemini raised it, from different directions.</p>
 *
 * <p>Reading the source again immediately before deleting it turns that silent loss into a refusal.
 * A record with no fingerprint — written by an older build — cannot be compared, and "cannot
 * compare" is not "nothing changed": it refuses, because the alternative is deleting a directory
 * nobody checked.</p>
 */
export function sourceChangedSince(record: MoveRecord, now: StorageFingerprint): string {
  if (record.held === undefined) {
    return `${record.from} was moved by an older version of this extension, which did not record what `
      + 'it held. There is nothing to compare it against, so it is not deleted — remove it yourself '
      + 'once you have seen your history in the new folder.';
  }

  const moved = differences(record.held, now);

  return moved.length === 0
    ? ''
    : `${record.from} has been written to since it was copied — ${moved}. Something is still using `
      + 'it, which is almost always an MCP client that has not been restarted with the new folder. '
      + 'Nothing has been deleted.';
}

/** The counts that differ, as a person reads them — or empty when none do. */
function differences(before: StorageFingerprint, after: StorageFingerprint): string {
  const counted: (readonly [string, number, number])[] = [
    ['rounds', before.rounds, after.rounds],
    ['sessions', before.sessions, after.sessions],
    ['ledger lines', before.usageLines, after.usageLines],
  ];

  // Compared only when BOTH sides have them. One side without is a comparison that cannot be made,
  // and reporting "0 before, 40 after" for a record that simply predates this field would refuse
  // every move made by an older build.
  if (before.entries !== undefined && after.entries !== undefined) {
    const named = new Set([...Object.keys(before.entries), ...Object.keys(after.entries)]);
    for (const name of [...named].sort((a, b) => a.localeCompare(b))) {
      counted.push([name, before.entries[name] ?? 0, after.entries[name] ?? 0]);
    }
  }

  return counted
    .filter(([, from, to]) => from !== to)
    .map(([what, from, to]) => `${what}: ${from} before, ${to} after`)
    .join('; ');
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
  const said = differences(before, after);

  return said.length === 0
    ? ''
    : `The new folder does not read back what the old one held — ${said}. Nothing has been deleted, `
      + 'and the old folder is exactly as it was; check what is missing before using either.';
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
