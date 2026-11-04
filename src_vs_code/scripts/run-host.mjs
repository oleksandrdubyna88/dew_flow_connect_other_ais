import { execFileSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadOwned } from './run-tests.mjs';

/**
 * Run the scenarios inside a REAL VS Code, and exit non-zero unless they actually ran.
 *
 * <p><b>Why this exists.</b> `research/module_tests.md:175` has said for months that there is no
 * extension-host harness here, that `@vscode/test-electron` is what closes it, and that this is the
 * largest single gap in the repository. Twelve rows of that file end "NOT covered … needs an
 * extension host".</p>
 *
 * <p><b>`test-electron`, not `@vscode/test-cli`.</b> The friendlier wrapper brings mocha, and this
 * repository runs `node --test` across two hundred test files. A second framework beside that is the
 * duplicate the reuse rule calls a defect from the moment it compiles — two runners, two reporters,
 * two ways to read a failure in CI. This package only downloads a build and launches it; what runs
 * inside is ours.</p>
 *
 * <p><b>The exit code is the whole point.</b> A harness that exits zero because the editor never
 * started, or because it found nothing to run, is a green tick over nothing — which is exactly the
 * shape of the rate-limited review check that reported `pass` while no review happened. So: a
 * failure to launch is non-zero, a scenario failure is non-zero, and an empty scenario list throws
 * inside the host rather than passing.</p>
 *
 * <p>Two budgets, not one, because "VS Code never started" and "a scenario hung" are different
 * failures that want different sentences.</p>
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..');

/** How long the editor may take to download and launch before that is the failure. */
const LAUNCH_MS = 10 * 60 * 1000;

const bounded = (ms, what) => new Promise((_, no) => {
  setTimeout(() => { no(new Error(`${what} did not finish within ${Math.round(ms / 1000)}s`)); }, ms).unref();
});

/**
 * Variables a run STARTED FROM INSIDE VS Code inherits, and which break the child editor.
 *
 * <p>`ELECTRON_RUN_AS_NODE=1` is the one that matters: with it set, the `Code.exe` this launches
 * behaves as plain Node, tries to run the first argument as a script, and rejects the rest with
 * Node's own wording — `bad option: --disable-extensions`. That message names VS Code's binary and
 * says nothing about the variable, so it reads as a broken launcher rather than a poisoned
 * environment. Measured here: a terminal inside VS Code exports it along with ten `VSCODE_*`
 * siblings, so this suite cannot be developed from the editor it tests without stripping them.</p>
 */
const POISONED = ['ELECTRON_RUN_AS_NODE', 'VSCODE_PID', 'VSCODE_CWD', 'VSCODE_IPC_HOOK', 'VSCODE_NLS_CONFIG',
  'VSCODE_CODE_CACHE_PATH', 'VSCODE_ESM_ENTRYPOINT', 'VSCODE_L10N_BUNDLE_LOCATION',
  'VSCODE_HANDLES_UNCAUGHT_ERRORS', 'VSCODE_CRASH_REPORTER_PROCESS_TYPE'];

/**
 * The OWNED folders of this run (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 16 D-c; must-not-copy #14). The run has ONE run folder of the wrap (`what` `host`, TEMP, TMP,
 * TMPDIR and the marker moved there), and INSIDE it three owned folders of the wrap, each with its claim:
 *
 * <ul>
 *   <li>`host-ws` — a workspace of its own, per run, never the folder somebody is working in;</li>
 *   <li>`host-data` — a DATA directory of its own: the settings-mirror scenario asserts that the extension really writes `settings.json`, and the only way to assert that without
 *       reading somebody's own installation is to tell this host where its data lives (`COAI_DATA_DIR` is what both halves of the product read);</li>
 *   <li>`host-profile` — the editor's USER-DATA directory (`--user-data-dir`), so neither a developer's settings nor the product's temp files land anywhere shared.</li>
 * </ul>
 *
 * Everything is removed BEFORE the process exits: `main` removes them in a `finally` and RETURNS the exit code, and only the command line ends the process. The old
 * `main().then(exit).finally(rm)` ended the process first and removed nothing (#14).
 *
 * <p><b>The IPC socket.</b> The editor puts its IPC socket (`<user-data-dir>/<version>-main.sock`) in the user-data directory, and a socket path has a byte limit (107 on Linux, 103 on
 * macOS; creds E2.1 met the macOS one). The profile folder keeps its own name `host-profile` unless that would pass the limit, and then it is named `hp` (ten characters
 * shorter); `socketRoom` is the measurement and `main` says one line when even that is too long.</p>
 */
const SOCKET_LEAF = '/1.999.9-main.sock';
const PLATFORM_SOCKET_LIMIT = process.platform === 'darwin' ? 103 : 107;

/** How many bytes are left under `limit` (the platform's by default) for the editor's socket path under a profile folder `profile`; negative when it would not fit. */
export function socketRoom(profile, limit = PLATFORM_SOCKET_LIMIT) {
  return limit - Buffer.byteLength(`${profile}${SOCKET_LEAF}`);
}

/** The runner's own default launch: `@vscode/test-electron` is loaded when a launch happens, not when this module is imported. */
const runInTheEditor = async (options) => (await import('@vscode/test-electron')).runTests(options);

/** BUILT FIRST, every time. The host loads `dist/extension.js`, so a run against a stale bundle is a run against yesterday's code. */
const bundleTheExtension = () => execFileSync('npm', ['run', 'bundle'], { cwd: ROOT, stdio: 'inherit', shell: process.platform === 'win32' });

/**
 * Runs both launches and answers the exit code: 0 when every scenario passed, 1 otherwise, 78 when the run folder could not be made. `launch` (test-electron's `runTests`),
 * `bundle`, `env` (the process's own by default: the editor inherits it), `root` (the temp root), `onExit`, `log`, `error`, `importOwned` and `socketLimit` are the seams of its test.
 */
export async function main({
  launch = runInTheEditor, bundle = bundleTheExtension, env = process.env, root, onExit, log = console.log, error = console.error, importOwned, socketLimit = PLATFORM_SOCKET_LIMIT,
} = {}) {
  const wrap = await loadOwned({ ...(importOwned === undefined ? {} : { importOwned }), error, prefix: 'run-host' });
  for (const name of POISONED) {
    delete env[name];
  }
  let run;
  try {
    run = wrap.beginOwnedRun({ what: 'host', env, ...(root === undefined ? {} : { root }), ...(onExit === undefined ? {} : { onExit }) });
  } catch (caught) {
    if (caught?.name !== 'RunFolderRefused') throw caught;
    error(`run-host: nothing ran — ${caught.message}. Make that folder writable, or point TMPDIR, TEMP and TMP at a folder you can write.`);
    return 78;
  }
  const scope = wrap.owned.cleanupScope();
  const failures = [];
  try {
    const owned = (what) => wrap.owned.ownedDir(scope, what, { parent: run.folder });
    const workspace = owned('host-ws');
    const dataHome = owned('host-data');
    const room = socketRoom(join(run.folder, `${wrap.owned.ownedPrefix('host-profile')}abcdef`), socketLimit);
    const profile = owned(room >= 0 ? 'host-profile' : 'hp');
    if (socketRoom(profile, socketLimit) < 0) {
      log(`run-host: the editor's IPC socket path under ${profile} is ${-socketRoom(profile, socketLimit)} bytes over the platform's limit; point TMPDIR at a shorter folder if the editor reports it`);
    }
    bundle();

    // Two launches, BOTH run whatever the first one says: a scenario that fails in the first (the clipboard ones do, on
    // some machines) must not hide the second launch's answer, which is about something else entirely. The failures are
    // collected and reported together, so the exit code is still non-zero for either.
    const run1 = async (label, extensionTestsEnv) => {
      try {
        await Promise.race([
          launch({
            extensionDevelopmentPath: ROOT,
            extensionTestsPath: join(ROOT, 'out', 'test', 'host', 'scenarios.js'),
            // `--disable-extensions` so a developer's own installed extensions cannot change the answer,
            // and a user-data dir of this run's own so neither can their settings.
            launchArgs: [workspace, '--disable-extensions', `--user-data-dir=${profile}`],
            // The host process's environment, which is what the extension AND the scenarios read.
            extensionTestsEnv,
          }),
          bounded(LAUNCH_MS, label),
        ]);
      } catch (reason) {
        failures.push(`${label}: ${reason instanceof Error ? reason.message : String(reason)}`);
      }
    };

    await run1('the extension host', { COAI_DATA_DIR: dataHome, COAI_DATA_SIDE: '' });

    // A SECOND launch (research/PLAN_paths_per_side.md E5.7): its data directory is a folder that is NOT there, so the one
    // scenario that can prove "nothing creates a missing named root" runs with the real activation, the real mirror and the
    // real notice — and none of the ordinary scenarios, which would write into it. `missingDataRootScenario.ts` says what
    // it drives; the environment variable is what tells the runner which list this launch is.
    await run1('the extension host, second launch',
      { COAI_DATA_DIR: join(dataHome, 'named-but-missing'), COAI_DATA_SIDE: '', COAI_HOST_MISSING_DATA_ROOT: '1' });
  } catch (reason) {
    failures.push(reason instanceof Error ? reason.message : String(reason));
  } finally {
    // BEFORE the process can exit: the three folders and their claims, then the run folder and its claim.
    await scope.close();
    run.end();
  }
  if (failures.length > 0) {
    error('extension-host scenarios: FAILED');
    error(failures.join('\n'));
    return 1;
  }
  log('extension-host scenarios: passed');
  return 0;
}

// Importable for its own test; still a script when node is pointed at it.
if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
