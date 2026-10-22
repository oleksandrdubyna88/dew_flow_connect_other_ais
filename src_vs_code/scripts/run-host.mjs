import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { Writable } from 'node:stream';
import { fileURLToPath } from 'node:url';

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
export const LAUNCH_MS = 10 * 60 * 1000;

/** The line `scenarios.ts` prints once the host has started, before its first scenario. */
export const HOST_STARTED_PATTERN = /^\s*host started: \d+ scenarios\s*$/m;

/** A budget as a sentence says it: whole seconds from one up, milliseconds below (a test's budget is small). */
export const spoken = (ms) => (ms >= 1000 ? `${Math.round(ms / 1000)}s` : `${ms}ms`);

const bounded = (ms, what) => new Promise((_, no) => {
  setTimeout(() => { no(new Error(`${what} did not finish within ${spoken(ms)}`)); }, ms).unref();
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
 * Run the launches, BOTH whatever the first one says: a scenario that fails in the first (the clipboard ones do, on
 * some machines) must not hide the second launch's answer, which is about something else entirely. The failures are
 * collected and returned together, so the exit code is still non-zero for either.
 *
 * @param {{download: () => Promise<string>, launches: {label: string, env: object}[], baseOptions: object,
 *   runTests: Function, budgetMs?: number, say?: (line: string) => void, out?: {write: Function}}} what
 * @returns {Promise<{failures: string[], output: string[]}>} `output` is what each launch's editor printed.
 */
export async function runHostScenarios({ download, launches, baseOptions, runTests, budgetMs = LAUNCH_MS,
  say = (line) => { console.log(line); }, out = process.stdout }) {
  const failures = [];
  const output = [];
  const launch = async (label, extensionTestsEnv) => {
    let printed = '';
    const tee = new Writable({
      write(chunk, _encoding, done) {
        printed += chunk.toString();
        out.write(chunk);
        done();
      },
    });
    try {
      await Promise.race([
        (async () => runTests({
          ...baseOptions,
          vscodeExecutablePath: await download(),
          extensionTestsEnv,
          stdout: tee,
          stderr: tee,
        }))(),
        bounded(budgetMs, label),
      ]);
    } catch (reason) {
      failures.push(`${label}: ${reason instanceof Error ? reason.message : String(reason)}`);
    }
    output.push(printed);
  };
  for (const { label, env } of launches) {
    await launch(label, env);
  }

  return { failures, output };
}

async function main() {
  // A workspace of its own, per run, deleted after — never the folder somebody is working in.
  const workspace = mkdtempSync(join(tmpdir(), 'coai-host-ws-'));
  // A DATA directory of its own, for the same reason and one more: the settings-mirror scenario asserts that the
  // extension really writes `settings.json`, and the only way to assert that without reading somebody's own
  // installation is to tell this host where its data lives. `COAI_DATA_DIR` is what both halves of the product read.
  const dataHome = mkdtempSync(join(tmpdir(), 'coai-host-data-'));
  try {
    for (const name of POISONED) {
      delete process.env[name];
    }
    // BUILT FIRST, every time. The host loads `dist/extension.js`, so a run against a stale bundle is a
    // run against yesterday's code — the defect `theBundleLoads.test.mjs` was written after hitting.
    execFileSync('npm', ['run', 'bundle'], { cwd: ROOT, stdio: 'inherit', shell: process.platform === 'win32' });

    const { runTests, downloadAndUnzipVSCode } = await import('@vscode/test-electron');
    const { failures } = await runHostScenarios({
      download: () => downloadAndUnzipVSCode(),
      runTests,
      baseOptions: {
        extensionDevelopmentPath: ROOT,
        extensionTestsPath: join(ROOT, 'out', 'test', 'host', 'scenarios.js'),
        // `--disable-extensions` so a developer's own installed extensions cannot change the answer,
        // and a throwaway user-data dir so neither can their settings.
        launchArgs: [workspace, '--disable-extensions'],
      },
      launches: [
        { label: 'the extension host', env: { COAI_DATA_DIR: dataHome, COAI_DATA_SIDE: '' } },
        // A SECOND launch (research/PLAN_paths_per_side.md E5.7): its data directory is a folder that is NOT there, so
        // the one scenario that can prove "nothing creates a missing named root" runs with the real activation, the
        // real mirror and the real notice — and none of the ordinary scenarios, which would write into it.
        // `missingDataRootScenario.ts` says what it drives; the environment variable is what tells the runner which
        // list this launch is.
        { label: 'the extension host, second launch',
          env: { COAI_DATA_DIR: join(dataHome, 'named-but-missing'), COAI_DATA_SIDE: '', COAI_HOST_MISSING_DATA_ROOT: '1' } },
      ],
    });
    if (failures.length > 0) {
      throw new Error(failures.join('\n'));
    }
  } finally {
    rmSync(workspace, { recursive: true, force: true });
    rmSync(dataHome, { recursive: true, force: true });
  }
}

// Only when RUN, never when imported by a test.
if (process.argv[1]?.endsWith('run-host.mjs')) {
  main()
    .then(() => {
      console.log('extension-host scenarios: passed');
      process.exit(0);
    })
    .catch((reason) => {
      console.error('extension-host scenarios: FAILED');
      console.error(reason instanceof Error ? reason.message : String(reason));
      process.exit(1);
    });
}
