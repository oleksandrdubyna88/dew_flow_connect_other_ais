/**
 * The first statement of every entry script (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 6.4): `const run = await startOwnedRun({ what: '<what>' });`.
 * It loads the wrap (`owned.mjs`, the ONE module that binds the token) inside a `try` — a mount that is not there is ONE stderr line and exit 2 before anything is made —
 * and begins the script's owned run folder: TEMP, TMP and TMPDIR name it from then on, so everything the script and its children make in temp lands inside and is removed
 * with it. A folder that cannot be made is ONE line and exit 78. No static import of the mount and no token here.
 */
import { chmodSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { loadOwned } from './run-tests.mjs';

/** A probe's owned SCRATCH instead (TEMP untouched): `startOwnedScratch({ what })` → `{ path, end }`. */
export async function startOwnedScratch({ what, prefix = what, importOwned, error = console.error, exit = process.exit, ...rest } = {}) {
  return start({ prefix, importOwned, error, exit, make: (owned) => owned.ownedScratch(what, rest) });
}

/** The run folder of an entry script: `{ folder, root, end }`. `prefix` names the script in the one line a refusal is; `importOwned`, `error` and `exit` are test seams. */
export async function startOwnedRun({ what, prefix = what, importOwned, error = console.error, exit = process.exit, ...rest } = {}) {
  return start({ prefix, importOwned, error, exit, make: (owned) => owned.beginOwnedRun({ what, ...rest }) });
}

/**
 * The DEFAULT scratch of a person-run probe that used to keep it for a later `--reparse` or `--from` (D-b, the rollout's epoch 5): an owned folder of the wrap in the real temp
 * folder, mode 0700 on POSIX (a copied sign-in may live there until the scrub), removed at exit — and ONE stderr line at exit says so and how to keep it:
 * `<prefix>: scratch was <path>, removed at exit — pass --scratch <dir> to keep it`. `--scratch <dir>` stays a folder the person names and is never removed.
 * Call it only when no `--scratch` was given. Answers `{ path, end }`.
 */
export async function startDefaultScratch({ what, prefix, importOwned, error = console.error, exit = process.exit, ...rest } = {}) {
  // The real temp root is read INSIDE `start`'s try: a root that is not there is one line and exit 78, not a stack.
  const scratch = await start({
    prefix, importOwned, error, exit,
    make: (owned) => {
      let root;
      try {
        // testsToDelete-exempt: raw-temp — reads the root and allocates nothing; the run folder is made through the wrap, not here
        root = realpathSync.native(tmpdir());
      } catch (cause) {
        // testsToDelete-exempt: raw-temp — reads the root and allocates nothing; the run folder is made through the wrap, not here
        throw new owned.RunFolderRefused(`the temp folder '${tmpdir()}' cannot be read (${String(cause.message).split('\n')[0]})`, { cause });
      }
      return owned.ownedScratch(what, { root, ...rest });
    },
  });
  if (scratch === undefined) return undefined;
  if (process.platform !== 'win32') chmodSync(scratch.path, 0o700);
  process.on('exit', () => process.stderr.write(`${prefix}: scratch was ${scratch.path}, removed at exit — pass --scratch <dir> to keep it\n`));
  return scratch;
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
