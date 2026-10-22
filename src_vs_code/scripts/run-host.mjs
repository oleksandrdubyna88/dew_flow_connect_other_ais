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
 * failures that want different sentences. (Three since C22: the download has its own.)</p>
 *
 * <p><b>The editor that exits before the host ran (C22, 2026-10-22).</b> On a cold first start of a
 * freshly unpacked VS Code under `xvfb`, the renderer once failed Electron's hung-renderer check and
 * the editor exited 1 before our extension host ran anything; the harness said `Test run failed with
 * code 1`, which fits a failed scenario just as well. So the harness now (1) says which it was — the
 * editor's exit, and the last `[main …]` line it printed — (2) keeps the download out of the launch
 * budget, (3) starts the FIRST launch once more, and only on proof that the host never started (the
 * `host started: N scenarios` line is absent), and (4) writes a `::warning::` annotation whatever the
 * second start does, which `host-job-streak.mjs` counts. A budget is never lengthened, and an exit
 * AFTER the host started is a scenario failure and is never retried.</p>
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..');

/** How long the editor may take to launch and run its scenarios before that is the failure. */
export const LAUNCH_MS = 10 * 60 * 1000;

/**
 * How long the editor may take to DOWNLOAD before that is the failure — its own budget, because a slow download is not
 * a slow launch and the two want different sentences. The number is the old shared one: until C22 the download ran
 * inside `LAUNCH_MS`, so ten minutes is exactly what it was allowed before.
 */
export const DOWNLOAD_MS = 10 * 60 * 1000;

/**
 * The line `scenarios.ts` prints once the host has started, before its first scenario. It is the harness's only proof
 * that the editor got as far as our code: an editor that exits WITHOUT it never ran a scenario.
 */
export const HOST_STARTED_PATTERN = /^\s*host started: \d+ scenarios\s*$/m;

/** What the run says when the first start failed and the second did not. */
export const RETRIED_NOTE = 'the editor did not start the first time; started on the second';

/**
 * The words every "the editor never reached the host" sentence shares — what `host-job-streak.mjs` looks for in an
 * annotation, so the writer and the counter cannot drift apart.
 */
export const BEFORE_THE_HOST = 'before the extension host ran a scenario';

/** A budget as a sentence says it: whole seconds from one up, milliseconds below (a test's budget is small). */
export const spoken = (ms) => (ms >= 1000 ? `${Math.round(ms / 1000)}s` : `${ms}ms`);

/** A budget that ran out — told apart from any other failure, because it gets its own words and is never retried. */
class BudgetExceeded extends Error {}

/** `work()` against a clock: its answer, or a `BudgetExceeded` naming `what`. The timer is cleared whichever wins. */
async function within(ms, what, work) {
  let timer;
  const clock = new Promise((_, no) => {
    timer = setTimeout(() => { no(new BudgetExceeded(`${what} did not finish within ${spoken(ms)}`)); }, ms);
  });
  try {
    return await Promise.race([work(), clock]);
  } finally {
    clearTimeout(timer);
  }
}

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

/** The last `[main …]` line an editor printed — its main process's own voice — or '' when it printed none. */
export function lastMainLine(printed) {
  return printed.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.startsWith('[main ')).at(-1) ?? '';
}

/**
 * How an editor's run ended, from what `runTests` rejected with: `code N` or `signal S` when the editor PROCESS ended
 * non-zero (`TestRunFailedError` carries both), '' for anything else — a budget, a spawn that never happened.
 */
function exitOf(reason) {
  if (!(reason instanceof Error) || reason instanceof BudgetExceeded || !('code' in reason) || !('signal' in reason)) {
    return '';
  }

  return reason.signal ? `signal ${reason.signal}` : `code ${reason.code}`;
}

/** What VS Code's main process last said, as a clause of the sentence. */
function mainLineClause(printed) {
  const line = lastMainLine(printed);

  return line === '' ? 'VS Code printed no [main] line' : `VS Code's last [main] line: "${line}"`;
}

/** The sentence for an editor that exited before the host ran a scenario: the exit, and the line it died on. */
export const exitedBeforeHost = (exited, printed) =>
  `the editor exited (${exited}) ${BEFORE_THE_HOST} — ${mainLineClause(printed)}`;

/** A workflow command's text: GitHub reads `%`, CR and LF in it as escapes, so they are escaped, not dropped. */
const annotated = (text) => text.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A');

/** One editor run: what it printed (kept, and still passed through to the CI log) and how it ended. */
async function attempt({ run, label, budgetMs, out }) {
  let printed = '';
  const tee = new Writable({
    write(chunk, _encoding, done) {
      printed += chunk.toString();
      out.write(chunk);
      done();
    },
  });
  try {
    await within(budgetMs, label, () => run({ stdout: tee, stderr: tee }));

    return { printed, failure: '', exited: '' };
  } catch (reason) {
    return {
      printed,
      failure: reason instanceof Error ? reason.message : String(reason),
      exited: exitOf(reason),
    };
  }
}

/** True when the editor exited and the host never printed its started line: nothing of ours ran. */
const neverStarted = (ended) => ended.exited !== '' && !HOST_STARTED_PATTERN.test(ended.printed);

/** The failure of a launch whose first start never reached the host, and whose second start (if any) is `second`. */
function failureAfterNoStart(first, second) {
  const sentence = exitedBeforeHost(first.exited, first.printed);
  if (second === undefined) {
    return sentence;
  }
  if (neverStarted(second)) {
    return `${sentence} — and it did not start the second time either (${exitedBeforeHost(second.exited, second.printed)})`;
  }

  return `${second.failure} (the first start had failed: ${sentence})`;
}

/**
 * One launch of the editor, with the ONE retry C22 allows: only when the editor exited and the host never started.
 *
 * @returns {Promise<{failure: string, printed: string[]}>} `failure` is '' when the launch passed.
 */
async function launchOnce({ label, env, retry }, shared) {
  const run = (streams) => shared.runTests({ ...streams, extensionTestsEnv: env });
  const first = await attempt({ ...shared, run, label });
  if (first.failure === '') {
    return { failure: '', printed: [first.printed] };
  }
  if (!neverStarted(first)) {
    return { failure: `${label}: ${first.failure}`, printed: [first.printed] };
  }
  if (!retry) {
    return { failure: `${label}: ${failureAfterNoStart(first)}`, printed: [first.printed] };
  }
  // NEVER SILENT: the annotation is written the moment the first start fails, whatever the second one does.
  shared.say(`::warning::${annotated(exitedBeforeHost(first.exited, first.printed))}`);
  const second = await attempt({ ...shared, run, label });
  const printed = [first.printed, second.printed];
  if (second.failure === '') {
    shared.say(RETRIED_NOTE);

    return { failure: '', printed };
  }

  return { failure: `${label}: ${failureAfterNoStart(first, second)}`, printed };
}

/**
 * Download the editor, then run the launches — ALL of them, whatever the first one says: a scenario that fails in the
 * first (the clipboard ones do, on some machines) must not hide the second launch's answer, which is about something
 * else entirely. The failures are collected and returned together, so the exit code is still non-zero for either.
 *
 * <p><b>The download is not part of the launch.</b> It runs first, under its own budget and its own sentence, and its
 * path is what every launch is given as `vscodeExecutablePath`. It used to run INSIDE `runTests`, so the launch budget
 * measured "download plus start plus every scenario" and a slow mirror read as a slow editor. (C22 (2).)</p>
 *
 * @param {{download: () => Promise<string>, launches: {label: string, env: object, retry?: boolean}[],
 *   baseOptions: object, runTests: Function, budgetMs?: number, downloadBudgetMs?: number,
 *   say?: (line: string) => void, out?: {write: Function}}} what
 * @returns {Promise<{failures: string[], output: string[]}>} `output` is what each editor start printed.
 */
export async function runHostScenarios({ download, launches, baseOptions, runTests, budgetMs = LAUNCH_MS,
  downloadBudgetMs = DOWNLOAD_MS, say = (line) => { console.log(line); }, out = process.stdout }) {
  let executable;
  try {
    executable = await within(downloadBudgetMs, 'the editor download', () => download());
  } catch (reason) {
    const said = reason instanceof BudgetExceeded ? reason.message
      : `the editor download failed: ${reason instanceof Error ? reason.message : String(reason)}`;

    return { failures: [said], output: [] };
  }

  const shared = {
    runTests: (options) => runTests({ ...baseOptions, vscodeExecutablePath: executable, ...options }),
    budgetMs,
    out,
    say,
  };
  const failures = [];
  const output = [];
  for (const spec of launches) {
    const { failure, printed } = await launchOnce(spec, shared);
    output.push(...printed);
    if (failure !== '') {
      failures.push(failure);
    }
  }

  return { failures, output };
}

async function main() {
  // A workspace of its own, per run, deleted after — never the folder somebody is working in.
  const workspace = mkdtempSync(join(tmpdir(), 'coai-host-ws-'));
  // A DATA directory of its own, for the same reason and one more: the settings-mirror scenario asserts that the
  // extension really writes `settings.json`, and the only way to assert that without reading somebody's own
  // installation is to tell this host where its data lives. `COAI_DATA_DIR` is what both halves of the product read,
  // so it is also what the scenario asks rather than guessing a path.
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
        // The FIRST launch is the one that may be the editor's cold start, so it is the one that may be retried.
        { label: 'the extension host', env: { COAI_DATA_DIR: dataHome, COAI_DATA_SIDE: '' }, retry: true },
        // A SECOND launch (research/PLAN_paths_per_side.md E5.7): its data directory is a folder that is NOT there, so
        // the one scenario that can prove "nothing creates a missing named root" runs with the real activation, the
        // real mirror and the real notice — and none of the ordinary scenarios, which would write into it.
        // `missingDataRootScenario.ts` says what it drives; the environment variable is what tells the runner which
        // list this launch is. The editor has started once by now, so a failure here is not a cold start.
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

// Only when RUN, never when imported by a test — a test drives `runHostScenarios` against a stand-in editor.
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
