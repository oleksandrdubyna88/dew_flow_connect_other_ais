import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isMain, loadOwned } from './run-tests.mjs';
import { parseMeasureArgs, stepsTable, summarize, summaryLine } from './coldstart-lib.mjs';
import { writeResult } from './coldstart-output.mjs';

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
// Windows has no socket path (the editor uses a named pipe), so nothing there is measured: the limit is unbounded.
const PLATFORM_SOCKET_LIMIT = process.platform === 'win32' ? Number.POSITIVE_INFINITY : process.platform === 'darwin' ? 103 : 107;

/** How many bytes are left under `limit` (the platform's by default) for the editor's socket path under a profile folder `profile`; negative when it would not fit. */
export function socketRoom(profile, limit = PLATFORM_SOCKET_LIMIT) {
  return limit - Buffer.byteLength(`${profile}${SOCKET_LEAF}`);
}

/** The runner's own default launch: `@vscode/test-electron` is loaded when a launch happens, not when this module is imported. */
const runInTheEditor = async (options) => (await import('@vscode/test-electron')).runTests(options);

/** BUILT FIRST, every time. The host loads `dist/extension.js`, so a run against a stale bundle is a run against yesterday's code. */
const bundleTheExtension = () => execFileSync('npm', ['run', 'bundle'], { cwd: ROOT, stdio: 'inherit', shell: process.platform === 'win32' });

/**
 * The run folder of one invocation and everything that must happen around it, shared by the scenario run (`main`) and the cold-start measurement
 * (`measure`): the poisoned variables removed, the run folder made (78 when it cannot be), `body` run with the folders' helpers, every failure it
 * collects reported together, and the folders removed BEFORE the exit code is returned. `body` gets `{ owned, profileFolder, failures }`:
 * `owned(what)` makes an owned folder inside the run folder, `profileFolder()` makes an editor user-data folder short enough for the socket path, `failures` collects what
 * made the run fail.
 */
async function inRunFolder({ env, root, onExit, log, error, importOwned, socketLimit, title }, body) {
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
    // The editor's IPC socket lives in the user-data directory and a socket path has a byte limit: the profile folder keeps its own name unless that would pass it.
    const profileFolder = () => {
      const room = socketRoom(join(run.folder, `${wrap.owned.ownedPrefix('host-profile')}abcdef`), socketLimit);
      const profile = owned(room >= 0 ? 'host-profile' : 'hp');
      if (socketRoom(profile, socketLimit) < 0) {
        log(`run-host: the editor's IPC socket path under ${profile} is ${-socketRoom(profile, socketLimit)} bytes over the platform's limit; point TMPDIR at a shorter folder if the editor reports it`);
      }

      return profile;
    };
    await body({ owned, profileFolder, failures });
  } catch (reason) {
    failures.push(reason instanceof Error ? reason.message : String(reason));
  } finally {
    // BEFORE the process can exit: the owned folders and their claims, then the run folder and its claim.
    await scope.close();
    run.end();
  }
  if (failures.length > 0) {
    error(`${title}: FAILED`);
    error(failures.join('\n'));
    return 1;
  }
  log(`${title}: passed`);
  return 0;
}

/** One launch of the editor, bounded; a failure is pushed onto `failures` instead of thrown, so the next launch still runs. */
async function launchBounded(launch, failures, label, options) {
  try {
    await Promise.race([launch(options), bounded(LAUNCH_MS, label)]);
    return true;
  } catch (reason) {
    failures.push(`${label}: ${reason instanceof Error ? reason.message : String(reason)}`);
    return false;
  }
}

/**
 * Runs both launches and answers the exit code: 0 when every scenario passed, 1 otherwise, 78 when the run folder could not be made. `launch` (test-electron's `runTests`),
 * `bundle`, `env` (the process's own by default: the editor inherits it), `root` (the temp root), `onExit`, `log`, `error`, `importOwned` and `socketLimit` are the seams of its test.
 */
export async function main({
  launch = runInTheEditor, bundle = bundleTheExtension, env = process.env, root, onExit, log = console.log, error = console.error, importOwned, socketLimit = PLATFORM_SOCKET_LIMIT,
} = {}) {
  return inRunFolder({ env, root, onExit, log, error, importOwned, socketLimit, title: 'extension-host scenarios' }, async ({ owned, profileFolder, failures }) => {
    const workspace = owned('host-ws');
    const dataHome = owned('host-data');
    const profile = profileFolder();
    bundle();

    // Two launches, BOTH run whatever the first one says: a scenario that fails in the first (the clipboard ones do, on
    // some machines) must not hide the second launch's answer, which is about something else entirely. The failures are
    // collected and reported together, so the exit code is still non-zero for either.
    const run1 = (label, extensionTestsEnv) => launchBounded(launch, failures, label, {
      extensionDevelopmentPath: ROOT,
      extensionTestsPath: join(ROOT, 'out', 'test', 'host', 'scenarios.js'),
      // `--disable-extensions` so a developer's own installed extensions cannot change the answer,
      // and a user-data dir of this run's own so neither can their settings.
      launchArgs: [workspace, '--disable-extensions', `--user-data-dir=${profile}`],
      // The host process's environment, which is what the extension AND the scenarios read.
      extensionTestsEnv,
    });

    await run1('the extension host', { COAI_DATA_DIR: dataHome, COAI_DATA_SIDE: '' });

    // A SECOND launch (research/PLAN_paths_per_side.md E5.7): its data directory is a folder that is NOT there, so the one
    // scenario that can prove "nothing creates a missing named root" runs with the real activation, the real mirror and the
    // real notice — and none of the ordinary scenarios, which would write into it. `missingDataRootScenario.ts` says what
    // it drives; the environment variable is what tells the runner which list this launch is.
    await run1('the extension host, second launch',
      { COAI_DATA_DIR: join(dataHome, 'named-but-missing'), COAI_DATA_SIDE: '', COAI_HOST_MISSING_DATA_ROOT: '1' });
  });
}

/**
 * The cold-start measurement (v2 extension.md § 2; `node scripts/run-host.mjs measure [flags]`, flags in `coldstart-lib.mjs`'s `parseMeasureArgs`).
 *
 * <p>Each of the samples is its OWN editor launch with a user-data directory, an extensions directory, a workspace and a data directory made
 * fresh for that launch, so a restored view or a warm profile cannot have started the extension before the scenario's clock
 * (`measureScenario.ts` asserts `isActive === false` and starts the clock itself). The launch sets COAI_MEASURE=1 so the extension registers
 * `coai.__measure`, and COAI_MEASURE_OUT, the file this sample's raw record goes to. A launch that fails, or leaves no record, is a failed sample:
 * the others still run, and the verdict says which one failed.</p>
 *
 * <p>The verdict (`coldstart-lib.mjs`): the MAXIMUM M2 over the samples at most 2000 ms on Linux and 3000 ms on Windows (the platform is the one the editor's own records name, `host.platform`; owner 2026-11-23), M1 at most 100 ms, a fresh `menuReady` (the real sidebar menu shown and usable) in every sample.
 * `--record-only` is the v1 baseline: no limits, and the extension may have been active. `--json PATH` writes the result; `--row` appends a row to
 * `research/RESULTS_v2_start.md`. `measure` answers the same codes as `main`: 0 pass, 1 fail, 78 no run folder.</p>
 */
export async function measure({
  argv = [], launch = runInTheEditor, bundle = bundleTheExtension, env = process.env, root, onExit, log = console.log, error = console.error, importOwned,
  socketLimit = PLATFORM_SOCKET_LIMIT, output = {},
} = {}) {
  let options;
  try {
    options = parseMeasureArgs(argv);
  } catch (reason) {
    error(`run-host measure: ${reason instanceof Error ? reason.message : String(reason)}`);
    return 64;
  }
  let result;
  const code = await inRunFolder({ env, root, onExit, log, error, importOwned, socketLimit, title: 'cold-start measurement' }, async ({ owned, profileFolder, failures }) => {
    bundle();
    const raws = [];
    for (let sample = 1; sample <= options.samples; sample += 1) {
      // Everything of this sample is new: the editor's user data, its extensions folder, the workspace and the extension's data directory.
      const profile = profileFolder();
      const extensions = owned('host-ext');
      const workspace = owned('host-ws');
      const dataHome = owned('host-data');
      const record = join(owned('host-out'), 'sample.json');
      await launchBounded(launch, failures, `sample ${sample}`, {
        extensionDevelopmentPath: ROOT,
        extensionTestsPath: join(ROOT, 'out', 'test', 'host', 'measureScenario.js'),
        launchArgs: [workspace, '--disable-extensions', `--user-data-dir=${profile}`, `--extensions-dir=${extensions}`],
        extensionTestsEnv: {
          COAI_MEASURE: '1', COAI_MEASURE_OUT: record, COAI_MEASURE_SAMPLE: String(sample), COAI_MEASURE_RECORD_ONLY: options.recordOnly ? '1' : '0',
          COAI_DATA_DIR: dataHome, COAI_DATA_SIDE: '',
        },
      });
      // A launch that failed may still have left a record (the scenario writes it before its last assertion): it is read so the verdict names the number.
      if (existsSync(record)) {
        raws.push(JSON.parse(readFileSync(record, 'utf8')));
      } else {
        failures.push(`sample ${sample}: the launch left no record, so there is nothing to measure`);
      }
    }
    result = summarize(raws, { expected: options.samples, recordOnly: options.recordOnly });
    for (const problem of result.problems) {
      failures.push(problem);
    }
    log(summaryLine(result));
    // Where the time goes: every mark, its time since the clock and since the step above (the old activation and the first sidebar render name their phases).
    for (const line of stepsTable(result)) {
      log(line);
    }
  });
  if (result !== undefined) {
    await writeResult(result, options, output);
  }

  return code;
}

// Importable for its own test; still a script when node is pointed at it.
if (isMain(import.meta.url)) {
  process.exit(process.argv[2] === 'measure' ? await measure({ argv: process.argv.slice(3) }) : await main());
}
