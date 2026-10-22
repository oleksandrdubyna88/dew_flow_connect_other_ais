import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Writable } from 'node:stream';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { runTests } from '@vscode/test-electron';
import { HOST_STARTED_PATTERN, LAUNCH_MS, runHostScenarios } from '../../scripts/run-host.mjs';

/**
 * The real-editor harness, driven against a STAND-IN editor — never a real VS Code, never a download.
 *
 * <p><b>What it is for (C22, S23).</b> The job `extension · a real editor` failed once on a docs-only
 * branch: the editor's main process logged `CodeWindow: detected unresponsive` and exited 1 before the
 * extension host ran anything, and the harness reported `the extension host: Test run failed with code
 * 1` — a sentence that fits a failed scenario just as well as an editor that never started. The rerun
 * passed. So the harness now says which of the two it was, starts the editor once more when the host
 * provably never ran, and says it did, in a `::warning::` annotation the streak step counts.</p>
 *
 * <p><b>How.</b> `scripts/fixtures/stubEditor.mjs` is run by `node` where VS Code would be: the real
 * `runTests` of `@vscode/test-electron` spawns it, so the output capture and the non-zero exit are the
 * real ones. `STUB_MODE` says which situation it plays; a log file it appends to counts the launches
 * without trusting anything the harness reports.</p>
 *
 * <p><b>The wall-clock figures are the harness's own budgets, made small.</b> They are inputs, not
 * waits: a case that expects a download NOT to be charged to the launch budget gives the launch budget
 * 1 s and the download 1.2 s, and a stub that starts in tens of milliseconds.</p>
 */

const STUB = fileURLToPath(new URL('../../scripts/fixtures/stubEditor.mjs', import.meta.url));
const NO_OUTPUT = new Writable({ write(_chunk, _encoding, done) { done(); } });
const BOTH_LINES = '[main 2026-10-22T10:00:00.000Z] CodeWindow: detected unresponsive';
const EXITED = 'the editor exited (code 1) before the extension host ran a scenario';

/** Drive the harness against the stub in `mode`; answer what it reported and how often the stub ran. */
async function drive({ mode, download, budgetMs, downloadBudgetMs, launches }) {
  const dir = mkdtempSync(join(tmpdir(), 'coai-runhost-'));
  const log = join(dir, 'launches.log');
  const said = [];
  try {
    const result = await runHostScenarios({
      download: download ?? (async () => process.execPath),
      runTests,
      baseOptions: { extensionDevelopmentPath: dir, extensionTestsPath: join(dir, 'unused.js'), launchArgs: [STUB] },
      launches: launches ? launches(log) : [{ label: 'the extension host', env: { STUB_MODE: mode, STUB_LOG: log }, retry: true }],
      budgetMs,
      downloadBudgetMs,
      say: (line) => { said.push(line); },
      out: NO_OUTPUT,
    });
    let launched = 0;
    try {
      launched = readFileSync(log, 'utf8').split('\n').filter(Boolean).length;
    } catch {
      launched = 0;
    }

    return { ...result, said, launched };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

const warningsIn = (said) => said.filter((line) => line.startsWith('::warning::'));
const after = (ms) => new Promise((done) => { setTimeout(done, ms); });

// ---- S23 (a): an editor that exits before the host ran a scenario ------------------------------------------

const exitedEarly = drive({ mode: 'exit-before-host' });

test('S23(a): the failure names an editor that exited before the host ran, with the last [main] line', async () => {
  const { failures } = await exitedEarly;

  assert.equal(failures.length, 1, `one failure expected, got ${JSON.stringify(failures)}`);
  assert.ok(
    failures[0].includes(`${EXITED} — VS Code's last [main] line: "${BOTH_LINES}"`),
    `expected the sentence to name an editor that exited before the host ran a scenario, got "${failures[0]}"`,
  );
});

test('S23(a): the sentence quotes the LAST [main] line, not the first nor the last line of all', async () => {
  const { failures } = await exitedEarly;

  assert.ok(!failures[0].includes('CodeWindow: created'), `quoted the first [main] line: "${failures[0]}"`);
  assert.ok(!failures[0].includes('the window did not answer'), `quoted a line that is not [main]: "${failures[0]}"`);
});

test('S23(a): the launch is retried ONCE, and the sentence says the second start failed too', async () => {
  const { launched, failures } = await exitedEarly;

  assert.equal(launched, 2, `expected 2 launches, got ${launched}`);
  assert.ok(failures[0].includes('did not start the second time either'),
    `the sentence does not name the retry: "${failures[0]}"`);
});

test('S23(a): a ::warning:: annotation names the exit and the line, whatever happens next', async () => {
  const { said } = await exitedEarly;

  const warnings = warningsIn(said);
  assert.equal(warnings.length, 1, `expected a ::warning:: line, got ${JSON.stringify(said)}`);
  assert.equal(warnings[0], `::warning::${EXITED} — VS Code's last [main] line: "${BOTH_LINES}"`);
});

test('S23(a): an editor that printed no [main] line at all is said so, not quoted as empty', async () => {
  const { failures } = await drive({ mode: 'exit-silent' });

  assert.ok(failures[0].includes(`${EXITED} — VS Code printed no [main] line`),
    `expected the no-[main]-line wording, got "${failures[0]}"`);
});

// ---- S23 (b): an editor that exited AFTER the host started is a scenario failure, never retried ------------

test('S23(b): an exit after the host-started line is launched ONCE and keeps today\'s sentence', async () => {
  const { launched, failures, said } = await drive({ mode: 'host-then-fail' });

  assert.equal(launched, 1, `expected 1 launch, got ${launched}`);
  assert.deepEqual(failures, ['the extension host: Test run failed with code 1']);
  assert.deepEqual(warningsIn(said), [], 'a scenario failure is not a first-start warning');
});

test('a first start that failed and a second that ran: no failure, and the run says it needed the second', async () => {
  const { launched, failures, said } = await drive({ mode: 'fail-first-then-ok' });

  assert.equal(launched, 2);
  assert.deepEqual(failures, []);
  assert.ok(said.includes('the editor did not start the first time; started on the second'),
    `the retry was not said: ${JSON.stringify(said)}`);
  assert.equal(warningsIn(said).length, 1, 'the first start must never be silent, even when the second one passed');
});

test('a launch that is not marked for a retry is never started twice', async () => {
  const { launched, failures } = await drive({
    launches: (log) => [{ label: 'the extension host, second launch', env: { STUB_MODE: 'exit-before-host', STUB_LOG: log } }],
  });

  assert.equal(launched, 1, `expected 1 launch, got ${launched}`);
  assert.equal(failures.length, 1);
  assert.ok(failures[0].includes(EXITED), `the sentence still names the exit: "${failures[0]}"`);
  assert.ok(!failures[0].includes('second time'), `a launch with no retry must not claim one: "${failures[0]}"`);
});

// ---- the budgets: the download leaves the launch budget ------------------------------------------------------

test('a slow download is NOT charged to the launch budget', async () => {
  const { failures, launched } = await drive({
    mode: 'ok',
    budgetMs: 1000,
    download: async () => { await after(1200); return process.execPath; },
  });

  assert.deepEqual(failures, [], 'a download that outlasted the LAUNCH budget failed the launch');
  assert.equal(launched, 1);
});

test('a download that fails names the download, not the launch', async () => {
  const { failures, launched } = await drive({
    mode: 'ok',
    budgetMs: 1000,
    download: async () => { await after(1200); throw new Error('connection reset'); },
  });

  assert.deepEqual(failures, ['the editor download failed: connection reset']);
  assert.equal(launched, 0, 'the editor must not be launched when it could not be downloaded');
});

test('a download that never finishes is cut by its own budget, with its own sentence', async () => {
  const { failures } = await drive({
    mode: 'ok',
    budgetMs: 3000,
    downloadBudgetMs: 150,
    download: () => new Promise(() => {}),
  });

  assert.deepEqual(failures, ['the editor download did not finish within 150ms']);
});

test('a launch that outlasts its budget keeps its sentence, and is not retried', async () => {
  const { failures, launched } = await drive({ mode: 'hang', budgetMs: 400 });

  // Today's sentence, label doubled and all: the budgets and the words of a hang are NOT part of this change.
  assert.deepEqual(failures, ['the extension host: the extension host did not finish within 400ms']);
  assert.equal(launched, 1, 'a hung editor has not EXITED; only an exit before the host is retried');
});

test('the job\'s launch budget is the ten minutes it has always been', () => {
  assert.equal(LAUNCH_MS, 10 * 60 * 1000);
});

// ---- the host-started line: what the harness looks for is what the host prints -------------------------------

test('the host-started line pattern matches the line and nothing a scenario prints', () => {
  assert.ok(HOST_STARTED_PATTERN.test('  host started: 3 scenarios\n'));
  assert.ok(!HOST_STARTED_PATTERN.test('  ok  host started: 3 scenarios\n'), 'a scenario named like the line must not count');
  assert.ok(!HOST_STARTED_PATTERN.test('  3/3 scenarios passed\n'));
});

test('scenarios.ts prints the host-started line BEFORE the first scenario runs', () => {
  const source = readFileSync(fileURLToPath(new URL('./host/scenarios.ts', import.meta.url)), 'utf8');
  const printed = source.indexOf('console.log(`  host started: ${scenarios.length} scenarios`);');
  const loop = source.indexOf('for (const scenario of scenarios)');

  assert.ok(printed > 0, 'scenarios.ts does not print "  host started: N scenarios"');
  assert.ok(printed < loop, 'the host-started line is printed after the scenarios begin, so it proves nothing');
});
