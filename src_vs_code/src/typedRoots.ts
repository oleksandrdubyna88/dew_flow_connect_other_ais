import { isPosixAbsolute, isWindowsAbsolute, qualified } from './pathFamily';
import { rootRefusal, type RootPlaces } from './qconsultWrite';

/**
 * Text typed into the OTHER side's folder box on the Question consultant (todo/PLAN_paths_per_side.md E4.2, the
 * admission rule of the cadence consultation for epics 4-6, finding c): admitted only when THIS window would group the
 * typed text as the other side's (`pathGroups.groupOf`), so an entry never jumps groups on the next render — an
 * unambiguous absolute path of that OS: a drive letter or `\\server\share` for the Windows box, a single-slash `/…` for
 * the WSL / Linux box. `//server/share/…` is spelled for neither OS and is admitted into *both sides / unclear*.
 *
 * <p>Refused BESIDE the box, with the reason (never a toast — `typedRefusals.ts`):</p>
 * <ul>
 *   <li>a spelling of this OS — *that is this side's — add it above*;</li>
 *   <li>a relative or drive-relative path — `rootRefusal`'s own words;</li>
 *   <li>on Windows, a `/…` whose `C:\…` exists here — *on this machine that is C:\…, this side's folder*; one the disk
 *       could not answer for is refused too, since it would group as unclear, never as the other side's;</li>
 *   <li>in WSL, a root-relative `\…` — it names no drive, so no Windows server can read it;</li>
 *   <li>and whatever `rootRefusal` refuses for every root (a drive root, a credential folder).</li>
 * </ul>
 *
 * <p>The disk asked is THIS host's, at this machine's own spelling of the text (`qualified`) — what the grouping asks —
 * never the other side's. Pure: the host hands in the places with the typed text's existence in them.</p>
 */

/** The key the other side's typed box posts, in the caller slot (`data-caller` names the group it types into). */
export const TYPED_ROOT_KEY = 'qconsultTypedRoot';

/** A verdict on the typed root: a sentence (refused), '' (admitted) — or undefined, when the next rule decides. */
type Verdict = (root: string, places: RootPlaces) => string | undefined;

const VERDICTS: readonly Verdict[] = [
  // `//server/share/…`: neither OS's spelling — admitted into both sides / unclear unless every root's rules refuse it.
  (root, places) => (isForwardShare(root) ? rootRefusal(root, places) : undefined),
  (root, places) => (spelledForThisOs(root, places.windows) ? `'${root}' is spelled for this side — add it above with Add a folder…` : undefined),
  (root, places) => (!places.windows && /^\\(?!\\)/u.test(root) ? `'${root}' names no drive — a Windows folder is spelled from a drive letter (D:\\work) or a share (\\\\server\\share)` : undefined),
  (root, places) => (places.existingHere.includes(root) ? `on this machine that is ${qualified(root, places.windows, places.systemDrive)}, this side's folder — add it above with Add a folder…` : undefined),
  (root, places) => (places.unknownHere.includes(root)
    ? `this window could not tell whether ${qualified(root, places.windows, places.systemDrive)} exists on this machine, so it cannot be called the other side's`
    : undefined),
];

/** Why a root typed into the other side's box is not added — '' when it is. */
export function typedRootRefusal(text: string, places: RootPlaces): string {
  const root = text.trim();
  for (const verdict of VERDICTS) {
    const said = verdict(root, places);
    if (said !== undefined) {
      return said;
    }
  }

  return rootRefusal(root, places);
}

/** `//server/share` — two forward slashes: a UNC share on Windows, a path from `/` on POSIX, neither side's alone. */
function isForwardShare(root: string): boolean {
  return root.startsWith('//');
}

function spelledForThisOs(root: string, windows: boolean): boolean {
  return windows ? isWindowsAbsolute(root) : isPosixAbsolute(root);
}
