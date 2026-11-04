/**
 * The first statement of every entry script (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 6.4): `const run = await startOwnedRun({ what: '<what>' });`.
 * It loads the wrap (`owned.mjs`, the ONE module that binds the token) inside a `try` — a mount that is not there is ONE stderr line and exit 2 before anything is made —
 * and begins the script's owned run folder: TEMP, TMP and TMPDIR name it from then on, so everything the script and its children make in temp lands inside and is removed
 * with it. A folder that cannot be made is ONE line and exit 78. No static import of the mount and no token here.
 */
import { loadOwned } from './run-tests.mjs';

/** A probe's owned SCRATCH instead (TEMP untouched): `startOwnedScratch({ what })` → `{ path, end }`. */
export async function startOwnedScratch({ what, prefix = what, importOwned, error = console.error, exit = process.exit, ...rest } = {}) {
  return start({ prefix, importOwned, error, exit, make: (owned) => owned.ownedScratch(what, rest) });
}

/** The run folder of an entry script: `{ folder, root, end }`. `prefix` names the script in the one line a refusal is; `importOwned`, `error` and `exit` are test seams. */
export async function startOwnedRun({ what, prefix = what, importOwned, error = console.error, exit = process.exit, ...rest } = {}) {
  return start({ prefix, importOwned, error, exit, make: (owned) => owned.beginOwnedRun({ what, ...rest }) });
}

async function start({ prefix, importOwned, error, exit, make }) {
  const owned = await loadOwned({ ...(importOwned === undefined ? {} : { importOwned }), error, exit, prefix });
  if (owned === null || typeof owned !== 'object') return undefined;
  try {
    return make(owned);
  } catch (caught) {
    if (caught?.name !== 'RunFolderRefused') throw caught;
    error(`${prefix}: nothing ran — ${caught.message}. Make that folder writable, or point TMPDIR, TEMP and TMP at a folder you can write.`);
    return exit(78);
  }
}
