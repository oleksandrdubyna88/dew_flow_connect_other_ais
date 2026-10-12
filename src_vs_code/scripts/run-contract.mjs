#!/usr/bin/env node
/**
 * Runs the client/server contract tests against a REAL coai-server.
 *
 * <p>Two ways in, and the tests cannot tell them apart:</p>
 *
 * <ul>
 *   <li><b>Point it at one you already have</b> — set COAI_CONTRACT_URL and COAI_CONTRACT_KEY (and
 *       COAI_CONTRACT_DOMAIN if it is not contract.test). That is the docker compose story:
 *       nothing here starts or stops anything.</li>
 *   <li><b>Let it start one</b> — the default. It runs the server this repository just built, on a
 *       free loopback port, with a throwaway data directory and a freshly generated signing key,
 *       and stops it afterwards.</li>
 * </ul>
 *
 * <p>Starting a plain dotnet process rather than building the container is a deliberate CI choice:
 * src_server/Dockerfile publishes Native AOT (clang, a full link) and would add minutes to every
 * pull request, while "build · test · family checks" has already compiled this server seconds
 * earlier. The container remains the better LOCAL story, which is why the address is an input
 * rather than something this script owns.</p>
 *
 * <p><b>A remote address is refused unless you say so.</b> These tests mint sessions and probe a
 * refusal; pointed at a real deployment they would create real session state on it, and this
 * machine keeps coai.remsoft.dev one environment variable away. Loopback is allowed silently,
 * anything else needs COAI_CONTRACT_ALLOW_REMOTE=1 — the point being that it must be typed rather
 * than inherited.</p>
 *
 * <p><b>Nothing may outlive this process.</b> Every deadline here exists because the alternative
 * is a job that hangs rather than fails, and every exit path goes through one teardown: a server
 * left holding a port is the next run's mystery.</p>
 */
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
// Lowers this runner, the server it starts and the tests it runs (common.local-cpu-budget).
import './lower-priority.mjs';
import { nodeTestArgs } from './run-tests.mjs';
// How the real server is started, waited for and stopped — shared with the seam's usage-counts leg.
import { kill, startTeamServer } from './team-server-child.mjs';

const DOMAIN = process.env.COAI_CONTRACT_DOMAIN ?? 'contract.test';
// The admin persona the Team server tab's contract needs (teamUsage.contract.ts): an email in Coai:Admins. Pointed at
// a server you already run, set COAI_CONTRACT_ADMIN to one in ITS list.
const ADMIN = process.env.COAI_CONTRACT_ADMIN ?? `admin@${DOMAIN}`;
const TEST_TIMEOUT_MS = 60_000;
// The whole test child, not one test: a hang during module loading never reaches --test-timeout.
const TEST_RUN_TIMEOUT_MS = 180_000;

/**
 * Whatever must be torn down if this process ends, for ANY reason.
 *
 * <p>Reached from the signal handlers and from {@link fail} as well as from the happy path: three
 * reviewers independently pointed out that "the script stops it afterwards" only holds when the
 * script gets to its afterwards. A killed run used to leave a server holding a port.</p>
 */
let cleanUp = () => {};
let cleaned = false;

function runCleanUp() {
  if (cleaned) {
    return;
  }
  cleaned = true;
  cleanUp();
}

process.on('exit', runCleanUp);
for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
  process.on(signal, () => {
    // Not process.exit() here: that would run in the same tick and cancel the SIGKILL escalation
    // the teardown just scheduled, which is the orphan it exists to prevent. Letting the handler
    // return leaves the loop alive until the timer fires and the child's exit unrefs everything.
    runCleanUp();
  });
}
process.on('uncaughtException', (error) => {
  console.error(error);
  runCleanUp();
  process.exitCode = 1;
});

function fail(message) {
  console.error(`run-contract: ${message}`);
  runCleanUp();
  process.exit(1);
}

/** The compiled contract tests: *.contract.js beside the unit tests, which are *.test.js. */
function contractTests() {
  const dir = join('out', 'test');
  if (!existsSync(dir)) {
    fail(`no compiled tests in ${dir} — did the compile step run?`);
  }

  const files = readdirSync(dir)
    .filter((f) => f.endsWith('.contract.js'))
    .map((f) => join(dir, f));
  if (files.length === 0) {
    fail(`no *.contract.js in ${dir}`);
  }

  return files;
}

/**
 * Spending for the Team server tab's contract to read (todo/PLAN_team_usage_by_person.md, 3.4).
 *
 * <p>A fresh server's ledger is empty, so a company answer has no vendors, no models and thirty empty days, and every
 * assertion about them would pass vacuously. Three lines, in the shape the server's own ledger writer uses: two people,
 * one named model and one the line left empty ("" — unknown), one vendor id written with a capital the server must
 * lower-case, today and yesterday so the 30-day series has two days to show. `COAI_CONTRACT_SEEDED` tells the tests
 * the lines are there; pointed at a server you run, they are not, and the tests that need them say so and skip.</p>
 */
const SEEDED = [
  { email: `alice@${DOMAIN}`, provider: 'Codex', model: 'gpt-5.6-sol', daysAgo: 0, tokensIn: 1000, tokensOut: 100 },
  { email: `bob@${DOMAIN}`, provider: 'codex', model: 'gpt-5.6-sol', daysAgo: 1, tokensIn: 2000, tokensOut: 200 },
  { email: `bob@${DOMAIN}`, provider: 'gemini', model: '', daysAgo: 0, tokensIn: 50, tokensOut: 5 },
];

function seedLedger(dir) {
  const lines = SEEDED.map((one) => JSON.stringify({
    utc: new Date(Date.now() - one.daysAgo * 24 * 3600 * 1000 - 60_000).toISOString(),
    provider: one.provider, model: one.model, role: 'Architecture', stage: 'CodeReview', seconds: 3,
    tokensIn: one.tokensIn, tokensOut: one.tokensOut, costUsd: null, outcome: 'ok', email: one.email, kind: 'review',
  }));
  writeFileSync(join(dir, 'usage.jsonl'), `${lines.join('\n')}\n`, 'utf8');
}

/**
 * The server this repository just built, over a throwaway seeded data directory — started, waited for and stopped by
 * `team-server-child.mjs`, the one copy of how that is done. The teardown is registered BEFORE the start so a failure
 * half way still takes the directory with it.
 */
async function startServer() {
  const data = mkdtempSync(join(tmpdir(), 'coai-contract-'));
  seedLedger(data);
  cleanUp = () => removeData(data);
  try {
    const server = await startTeamServer({
      extensionRoot: resolve(import.meta.dirname, '..'),
      dataDir: data,
      domain: DOMAIN,
      admins: ADMIN,
      say: (line) => console.log(`run-contract: ${line}`),
    });
    cleanUp = () => {
      server.stop();
      removeData(data);
    };

    return { url: server.url, key: server.key };
  } catch (error) {
    fail(error instanceof Error ? error.message : String(error));
  }
}

/** The temp directory, after the server: a leftover one is not worth failing a green run over. */
function removeData(data) {
  try {
    rmSync(data, { recursive: true, force: true });
  } catch {
    // Not worth failing for.
  }
}

function runTests(env) {
  return new Promise((ok) => {
    const child = spawn(
      process.execPath,
      nodeTestArgs(contractTests(), [`--test-timeout=${TEST_TIMEOUT_MS}`]),
      { stdio: 'inherit', env: { ...process.env, ...env } });

    // A whole-run deadline as well as --test-timeout: a child that hangs before any test starts —
    // resolving an import, say — never reaches the per-test one, and the job would sit there.
    const deadline = setTimeout(() => {
      console.error(`run-contract: the tests did not finish within ${TEST_RUN_TIMEOUT_MS / 1000}s`);
      kill(child, 'SIGKILL');
    }, TEST_RUN_TIMEOUT_MS);

    // `error` rather than `exit` fires when the child cannot be spawned at all; without this the
    // promise would never settle and the command would look frozen.
    child.on('error', (error) => {
      clearTimeout(deadline);
      console.error(`run-contract: the test runner could not be started: ${error.message}`);
      ok(1);
    });
    child.on('exit', (code) => {
      clearTimeout(deadline);
      ok(code ?? 1);
    });
  });
}

/** Loopback, or an address somebody typed on purpose. Never one that merely happened to be set. */
function isLoopback(url) {
  try {
    const { hostname } = new URL(url);

    return ['127.0.0.1', 'localhost', '::1', '[::1]'].includes(hostname);
  } catch {
    return false;
  }
}

/** The origin alone: a pasted URL can carry a token in its query or credentials in its userinfo. */
function safeToPrint(url) {
  try {
    return new URL(url).origin;
  } catch {
    return '(unparseable)';
  }
}

const given = process.env.COAI_CONTRACT_URL ?? '';
if (given.length > 0) {
  if ((process.env.COAI_CONTRACT_KEY ?? '').length === 0) {
    fail(
      'COAI_CONTRACT_URL is set but COAI_CONTRACT_KEY is not. The tests mint their own identity '
      + 'token and it has to be signed with the key THAT server was started with, so this script '
      + 'will not generate one: a key it invented would not match, and every assertion would fail '
      + 'as a 401 that looks like a contract break. Pass the key you gave the server.');
  }
  if (!isLoopback(given) && process.env.COAI_CONTRACT_ALLOW_REMOTE !== '1') {
    fail(
      `${safeToPrint(given)} is not loopback. These tests mint sessions and probe a refusal, so `
      + 'pointed at a real deployment they would leave real session state on it. Set '
      + 'COAI_CONTRACT_ALLOW_REMOTE=1 if that is genuinely what you want.');
  }
  console.log(`run-contract: using the server at ${safeToPrint(given)}`);
  process.exit(await runTests({ COAI_CONTRACT_DOMAIN: DOMAIN, COAI_CONTRACT_ADMIN: ADMIN }));
}

const server = await startServer();
console.log(`run-contract: started a server at ${server.url}`);
let status = 1;
try {
  status = await runTests({
    COAI_CONTRACT_URL: server.url,
    COAI_CONTRACT_KEY: server.key,
    COAI_CONTRACT_DOMAIN: DOMAIN,
    COAI_CONTRACT_ADMIN: ADMIN,
    COAI_CONTRACT_SEEDED: '1',
  });
} finally {
  runCleanUp();
}
process.exit(status);
