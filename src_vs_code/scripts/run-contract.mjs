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
import { randomBytes } from 'node:crypto';
import { existsSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const DOMAIN = process.env.COAI_CONTRACT_DOMAIN ?? 'contract.test';
// Startup is a build this job just compiled, not a download: 30s is generous, and a shorter
// deadline is what turns a hang into a diagnosis instead of a coffee break.
const READY_TIMEOUT_MS = 30_000;
// One health request, so a server that accepts the connection and then says nothing cannot park
// the loop past its own deadline.
const HEALTH_REQUEST_TIMEOUT_MS = 5_000;
const TEST_TIMEOUT_MS = 60_000;
// The whole test child, not one test: a hang during module loading never reaches --test-timeout.
const TEST_RUN_TIMEOUT_MS = 180_000;
// Long enough for a graceful ASP.NET shutdown (its default drain is five seconds), short enough
// that a server ignoring SIGTERM does not become the next run's port conflict.
const STOP_GRACE_MS = 5_000;

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
 * The server this repository just built. Release first: that is what CI compiles.
 *
 * <p>coai-server.dll, not CoaiServer.dll — the project sets AssemblyName, and the file is named
 * for the binary people run rather than for the csproj.</p>
 */
function serverAssembly() {
  const candidates = ['Release', 'Debug'].map((c) =>
    resolve('..', 'src_server', 'src', 'bin', c, 'net10.0', 'coai-server.dll'));
  const found = candidates.find((p) => existsSync(p));
  if (found === undefined) {
    fail(
      'the Team server is not built. Run "dotnet build dew_flow_connect_other_ais.slnx -c Release" '
      + 'from the repository root first, or set COAI_CONTRACT_URL to point at a server you are '
      + `already running. Looked in:\n  ${candidates.join('\n  ')}`);
  }

  return found;
}

function freePort() {
  return new Promise((ok, no) => {
    const probe = createServer();
    probe.on('error', no);
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address();
      probe.close(() => ok(port));
    });
  });
}

/** Polls the server's own anonymous health route — the one thing it answers before any credential. */
async function waitForHealth(url, child) {
  console.log(`run-contract: waiting for ${url}/api/health`);
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      fail(`the server exited with code ${child.exitCode} before it became healthy — its output is above`);
    }
    try {
      const response = await fetch(`${url}/api/health`, {
        signal: AbortSignal.timeout(HEALTH_REQUEST_TIMEOUT_MS),
      });
      if (response.ok) {
        return;
      }
    } catch {
      // Not up yet, or it accepted and stalled — either way the deadline is what ends this loop.
    }
    await new Promise((r) => setTimeout(r, 250));
  }

  fail(`the server did not answer ${url}/api/health within ${READY_TIMEOUT_MS / 1000}s`);
}

async function startServer() {
  const port = await freePort();
  const url = `http://127.0.0.1:${port}`;
  // At least 32 bytes or the server refuses to start, deliberately (src_server/src/Startup.cs);
  // 48 because a key that costs nothing to make may as well be well over the floor.
  const key = randomBytes(48).toString('base64');
  const data = mkdtempSync(join(tmpdir(), 'coai-contract-'));

  const child = spawn('dotnet', [serverAssembly()], {
    // Inherited rather than captured: when startup fails, its own log lines are the diagnosis, and
    // in CI they belong in the job output where somebody will actually read them.
    stdio: ['ignore', 'inherit', 'inherit'],
    // Its own process group on POSIX, so teardown can take the group rather than one pid.
    detached: process.platform !== 'win32',
    env: {
      ...process.env,
      ASPNETCORE_URLS: url,
      // Logs follow the data directory (CoaiLogPath.RootFor), so the throwaway directory takes
      // them too and nothing is written next to the extension's sources.
      Coai__DataDir: data,
      Coai__AllowedDomains: DOMAIN,
      // Pinned, not defaulted: an inherited Coai__AllowAnyDomain=true would turn the
      // outside-the-company assertion from a refusal into an acceptance, and the suite would go
      // green while proving the opposite of what it says.
      Coai__AllowAnyDomain: 'false',
      // Loopback with no proxy in front of it: the forwarded-proto gate would refuse every
      // request, and it is a deployment concern rather than a contract one.
      Coai__RequireForwardedHttps: 'false',
      Auth__Local__SigningKey: key,
      // The server refuses to start with a local key beside a real provider, and this process
      // inherits whatever the shell has — so both are cleared for the child rather than trusted.
      Auth__Microsoft__Tenant: '',
      Auth__Google__Enabled: 'false',
    },
  });

  cleanUp = () => stopServer(child, data);
  child.on('error', (error) => fail(`the server could not be started: ${error.message}`));
  await waitForHealth(url, child);

  return { url, key };
}

/** SIGTERM, then the whole tree, then the temp directory — in that order and never conditionally. */
function stopServer(child, data) {
  if (child.exitCode === null && child.pid !== undefined) {
    kill(child, 'SIGTERM');
    // A server that ignores the polite signal still has to release the port before the next run.
    // NOT unref'd: this timer is the escalation, and a timer nothing waits for is a timer that
    // never fires when the process is on its way out.
    const hard = setTimeout(() => kill(child, 'SIGKILL'), STOP_GRACE_MS);
    child.on('exit', () => clearTimeout(hard));
  }
  try {
    rmSync(data, { recursive: true, force: true });
  } catch {
    // A leftover temp directory is not worth failing a green run over.
  }
}

/**
 * The process TREE, not the process.
 *
 * <p>`security.md` requires a timeout to kill the whole tree, and `dotnet <dll>` is a host that
 * may have children of its own. On POSIX the child owns a process group (`detached`), so the
 * negative pid takes the group; on Windows `taskkill /T` is the equivalent.</p>
 */
function kill(child, signal) {
  try {
    if (process.platform === 'win32') {
      spawn('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });

      return;
    }
    process.kill(-child.pid, signal);
  } catch {
    // Already gone, or never had a group. Nothing left to stop either way.
  }
}

function runTests(env) {
  return new Promise((ok) => {
    const child = spawn(
      process.execPath,
      ['--test', `--test-timeout=${TEST_TIMEOUT_MS}`, ...contractTests()],
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
  process.exit(await runTests({ COAI_CONTRACT_DOMAIN: DOMAIN }));
}

const server = await startServer();
console.log(`run-contract: started a server at ${server.url}`);
let status = 1;
try {
  status = await runTests({
    COAI_CONTRACT_URL: server.url,
    COAI_CONTRACT_KEY: server.key,
    COAI_CONTRACT_DOMAIN: DOMAIN,
  });
} finally {
  runCleanUp();
}
process.exit(status);
