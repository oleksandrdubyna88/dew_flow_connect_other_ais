import { overlayKeyOf } from './coaiInstall';
import { storageChoiceFrom } from './dataDir';
import { type PathFamily } from './pathFamily';
import { sidesIn, type SideHere, type SideReader } from './sidesSeen';

/**
 * The OTHER side's data directory, read-only (research/PLAN_paths_per_side.md E3.4; decided with the operator, D2).
 *
 * <p><b>Where it is.</b> `coai.dataDirectory` is always per side (`ALWAYS_PER_SIDE`): each window writes its choice into
 * its own side's overlay in `globalState` — the client's store, which every window of the profile shares (measured,
 * `research/RESULTS_side_registry_reaches_the_other_window.md`). So the other side's choice is already here, under that
 * side's `overlayKey`; what was missing is WHICH sides exist, and the side registry (`sidesSeen.ts`, E2) records exactly
 * that, keyed by the same `sideKey`.</p>
 *
 * <p><b>Read-only, by type.</b> The store is handed in as a {@link SideReader} — `keys` and `get`, no `update` — so this
 * module cannot write another side's overlay: that side's own window changes it (*"Change it from a window on that
 * side."*). Its value is never `stat`ed from here either: `\\wsl.localhost\…` wakes a stopped distro, and `/mnt/<drive>`
 * may not be mounted. E4.3 draws it.</p>
 */

/** One other side, and the data directory it chose for itself — empty when it chose none here. */
export interface OtherSideDataDirectory {
  readonly side: string;
  readonly label: string;
  readonly family: PathFamily;
  readonly lastSeen: number;
  /** The directory the other side's own overlay names — empty: it uses its environment, the shared setting or the default. */
  readonly directory: string;
  /** The side folder it partitions that directory with (`coai.dataSide`), empty when none. */
  readonly dataSide: string;
}

/**
 * Every side of the OTHER family seen within the registry's window, newest first, with the data directory its own
 * overlay names. A second distro is listed as a second row (D1: one WSL / Linux family, but each distro chooses its own
 * folder); a side that chose nothing is listed with an empty directory, never dropped.
 */
export function otherSideDataDirectories(store: SideReader, here: SideHere, now: number): readonly OtherSideDataDirectory[] {
  return sidesIn(store, now)
    .filter((seen) => seen.family !== here.family)
    .map((seen) => {
      const choice = storageChoiceFrom((section) => overlayOf(store, seen.side)[section]);

      return { side: seen.side, label: seen.label, family: seen.family, lastSeen: seen.lastSeen, directory: choice.directory.trim(), dataSide: choice.side.trim() };
    });
}

/** A side's overlay as a record of its settings, or none — a value that is no record names nothing. */
function overlayOf(store: SideReader, side: string): Readonly<Record<string, unknown>> {
  const stored = store.get(overlayKeyOf(side));

  return typeof stored === 'object' && stored !== null && !Array.isArray(stored) ? stored as Readonly<Record<string, unknown>> : {};
}
