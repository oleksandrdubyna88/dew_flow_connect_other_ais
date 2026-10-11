import { isPosixAbsolute, isWindowsAbsolute, otherSideHere, type Existence } from './pathFamily';

/**
 * A list of folders a Windows window and a WSL window share — `coai.qconsultRoots`, `coai.alsoWatchDataDirectories` —
 * split into the groups the page draws (research/PLAN_paths_per_side.md E3.3, design (b) O1: the spelling IS the side).
 *
 * <p><b>Three groups, never two</b> (the cadence consultation for epics 4-6, finding c, verified): `spelledForTheOtherOs`
 * is false on BOTH platforms for `//server/share/x` and for a relative path, so a two-way split put them in THIS window's
 * group on each side. So:</p>
 *
 * <ul>
 *   <li><b>this side's</b> — spelled for this OS, or spelled for the other one and a folder HERE (on Windows `/work` is
 *       `C:\work` on the system drive; the host asked, `existingHere`);</li>
 *   <li><b>the other side's</b> — spelled for the other OS and the disk here said no such folder (`otherSideHere`);</li>
 *   <li><b>both sides / unclear</b> — spelled for neither OS (`//server/share`, a relative or drive-relative name, still
 *       refused by `rootRefusal`, its refusal shown in that group), or spelled for the other OS and the disk here could
 *       not tell (`unknown`). Never classified as the other side's on a guess, and never hidden.</li>
 * </ul>
 *
 * <p><b>The disk it asks.</b> Only THIS host's, at this machine's own spelling of an entry (`existingHere` →
 * `qualified`: a root-relative path on the system drive on Windows, the literal name in WSL) — the disk this side's
 * server reads. Never across to the other side's (`\\wsl.localhost\…` wakes a stopped distro, `/mnt/<drive>` may not be
 * mounted), which is what E4's "never stat the other side" forbids. The answer is handed in, so this is pure: the group
 * is THIS window's verdict, and the two windows may group a root-relative `/work` differently — each right for the
 * server on its own side.</p>
 */

/** Which group an entry lands in. */
export type PathGroup = 'thisSide' | 'otherSide' | 'unclear';

/** A list split into the three groups, each in the list's own order. */
export interface GroupedPaths {
  readonly thisSide: readonly string[];
  readonly otherSide: readonly string[];
  readonly unclear: readonly string[];
}

/** No disk answer at all — what a list decided by its spelling alone (the watched folders) is grouped against. */
export const NOTHING_ASKED: Existence = { existing: [], unknown: [] };

/**
 * The group of one entry, as THIS window sees it.
 *
 * @param windows whether this host is Windows
 * @param existence what this host's disk said about the list's other-OS spellings (`existingHere`), the entries as stored
 */
export function groupOf(entry: string, windows: boolean, existence: Existence): PathGroup {
  if (spelledForNeither(entry) || existence.unknown.includes(entry)) {
    return 'unclear';
  }

  return otherSideHere(entry, windows, existence.existing.includes(entry)) ? 'otherSide' : 'thisSide';
}

/** Neither OS's absolute spelling — a `//server/share` (a UNC share on Windows, a path from `/` on POSIX), a relative or drive-relative name. */
function spelledForNeither(entry: string): boolean {
  return !isPosixAbsolute(entry) && !isWindowsAbsolute(entry);
}

/** Every entry in its group; nothing is dropped, nothing is moved between lists. */
export function groupedPaths(entries: readonly string[], windows: boolean, existence: Existence): GroupedPaths {
  const of = (group: PathGroup): readonly string[] => entries.filter((entry) => groupOf(entry, windows, existence) === group);

  return { thisSide: of('thisSide'), otherSide: of('otherSide'), unclear: of('unclear') };
}
