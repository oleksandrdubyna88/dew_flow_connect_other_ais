#!/usr/bin/env node
// Runs the whole `.http` suite against a stack this script starts, and reports the verdict as an
// exit code: 0 pass · 1 CONTRACT regression · 3 environment · 4 configuration.
//
// The lifecycle is here rather than in a README because a suite whose setup is prose is a suite that
// runs on one machine. Everything it needs — the build, a free port, a throwaway data directory, the
// three personas, the readiness wait and the teardown — is in this file.

import { spawn, spawnSync } from 'node:child_process';
import { createHmac, randomBytes } from 'node:crypto';
import { closeSync, existsSync, mkdtempSync, openSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');

/** Exit codes, so a caller can tell a broken contract from a broken machine. */
const PASS = 0;
const CONTRACT = 1;
const ENVIRONMENT = 3;
const CONFIG = 4;

/**
 * A fresh HMAC key for THIS run, never a constant.
 *
 * 32+ bytes because the server refuses a shorter one at startup. Generated rather than written down:
 * a literal key in a repository is a credential by every scanner's definition and by most people's,
 * even when the server it opens lives for six seconds on loopback — and there is no reason to have
 * one, since nothing outside this process needs to know it.
 */
const SIGNING_KEY = randomBytes(32).toString('base64url');
const DOMAIN = 'example.com';
const ADMIN = `boss@${DOMAIN}`;
const DEVELOPER = `dev@${DOMAIN}`;

/** A valid signature from OUTSIDE the company — the 403 that keeps another company off these accounts. */
const OUTSIDER = 'someone@other-company.test';

function fail(code, message) {
  console.error(`\n  ${message}\n`);
  process.exit(code);
}

/** A JWT the server's `Local` scheme accepts. Its issuer is fixed; there is no audience. */
function mintToken(email) {
  const b64 = (value) =>
    Buffer.from(JSON.stringify(value)).toString('base64url');
  const header = b64({ alg: 'HS256', typ: 'JWT' });
  const payload = b64({
    iss: 'coai-local',
    email,
    exp: Math.floor(Date.now() / 1000) + 3600,
  });
  const signature = createHmac('sha256', SIGNING_KEY)
    .update(`${header}.${payload}`)
    .digest('base64url');

  return `${header}.${payload}.${signature}`;
}

/** A port nothing else holds. Asking the OS beats picking a number and hoping. */
async function freePort() {
  return new Promise((resolve, reject) => {
    const probe = createServer();
    probe.on('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address();
      probe.close(() => resolve(port));
    });
  });
}

/**
 * How the health probe went: still starting, answering, or answering WRONG.
 *
 * The third is the one worth separating. A startup or routing defect makes every route answer 500 —
 * that is how the missing JSON resolver was found — and treating a 500 like a refused connection
 * meant polling for the full forty seconds and then reporting ENVIRONMENT, "the requests were never
 * sent", for a server that was up and broken. A running server that answers wrongly is a contract
 * failure and should say so at once. (gemini, code round.)
 */
async function probe(baseUrl) {
  try {
    const response = await fetch(`${baseUrl}/api/health`);

    return response.ok ? 'ready' : `broken:${response.status}`;
  } catch {
    return 'starting';
  }
}

async function waitForReady(baseUrl, child, seconds) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      return 'exited';
    }

    const answer = await probe(baseUrl);
    if (answer !== 'starting') {
      return answer;
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  return 'timeout';
}

// The server is BUILT by whoever runs this, not by this.
//
// It used to run `dotnet build` itself, which meant resolving `dotnet` through PATH — the thing this
// script is otherwise careful to remove, and a real one: a process started by name is whichever
// binary happens to be first on the path. Building is the caller's job in both places that call it,
// since CI builds the solution before it runs anything and a developer has just built to get here.
// What is left is a clear refusal when the binary is not there.

if (!existsSync(exe)) {
  fail(ENVIRONMENT, `${exe} is not there. Build it first: dotnet build src_server/src/CoaiServer.csproj`);
}
const port = await freePort();
const baseUrl = `http://127.0.0.1:${port}`;
// A throwaway data directory per run: no vendors, no accounts, no sessions. That emptiness is
// deliberate — see the README on why no review ever reaches a vendor.
const dataDir = mkdtempSync(path.join(tmpdir(), 'coai-contracts-'));


// ONE vendor in the allowlist, and no account signed in for it.
//
// That combination is what lets the suite exercise the whole submit / poll / cancel contract without
// a vendor ever being launched: the allowlist accepts the request, the job is queued, and the runner
// refuses to start it because no account has been signed in. 202, 200 and 204 are real answers from
// real state, and nothing is spent. Without the file every submission is a 400 and those three
// statuses are never seen at all — a regression in queue acceptance or cancellation could ship with
// the suite still green. (codex, code round.)
writeFileSync(
  path.join(dataDir, 'vendors.json'),
  JSON.stringify([{ id: 'codex', runtime: 'codex', models: ['gpt-5.6-luna'], slots: ['a'] }]),
);

const exe = path.join(
  ROOT, 'src_server', 'src', 'bin', 'Debug', 'net10.0',
  process.platform === 'win32' ? 'coai-server.exe' : 'coai-server');

// The server's output goes to a FILE, never to a pipe this script holds.
//
// This is the whole reason the suite used to hang. `spawnSync` blocks Node's event loop for the
// duration of the run, so nothing drains a piped stdout; the server logs a line per request, filled
// the pipe, and then BLOCKED writing to it — so it stopped answering, httpyac's remaining requests
// timed out, and the run wedged with every assertion already passed. It only appeared once a vendor
// fixture made the server log more, which is why it looked like the fixture's fault.
const serverLog = path.join(dataDir, 'server.log');
const serverLogFd = openSync(serverLog, 'a');

// The child's environment, built as a value so PATH can be REMOVED rather than blanked.
//
// /api/catalog probes each configured vendor by LAUNCHING its CLI to ask its version. With the
// developer's real PATH the suite starts `codex`, which on a signed-in machine waits for a human and
// hangs the run. The suite asserts that `cliFound` is a BOOLEAN, not that it is true, so a probe
// that resolves nothing is exactly as good a test and is the same on every machine.
//
// Deleted rather than set to an empty string, and rather than set to a temporary directory: a search
// path made of somewhere writable is a place to drop an executable that then gets run, and "no PATH
// at all" is what is actually meant here. The server needs none — it is started by absolute path.
const childEnv = {
  ...process.env,
  ASPNETCORE_URLS: baseUrl,
  Coai__DataDir: dataDir,
  Coai__AllowedDomains: DOMAIN,
  Coai__Admins: ADMIN,
  // The proxy that would set X-Forwarded-Proto is not in front of anything here.
  Coai__RequireForwardedHttps: 'false',
  Auth__Local__SigningKey: SIGNING_KEY,
  Auth__Microsoft__Tenant: '',
  Auth__Microsoft__Audiences: '',
  Auth__Google__Enabled: 'false',
};
delete childEnv.PATH;
delete childEnv.Path;

const server = spawn(exe, [], {
  env: childEnv,
  stdio: ['ignore', serverLogFd, serverLogFd],
});

/** What the server said, for a failure message. Read from the file rather than held in memory. */
function serverSaid() {
  try {
    return readFileSync(serverLog, 'utf8').slice(-4000);
  } catch {
    return '(the server wrote nothing)';
  }
}
function stop() {
  if (server.exitCode === null) {
    server.kill();
  }

  try {
    closeSync(serverLogFd);
  } catch {
    // Already closed, or never opened. Nothing to do and nothing to say.
  }

  rmSync(dataDir, { recursive: true, force: true });
}

process.on('exit', stop);
process.on('SIGINT', () => { stop(); process.exit(ENVIRONMENT); });

const readiness = await waitForReady(baseUrl, server, 40);
if (readiness.startsWith('broken:')) {
  console.error(serverSaid());
  stop();
  const status = readiness.slice(7);
  fail(CONTRACT, `/api/health answered ${status}. The server is up and every route is broken — `
    + 'that is a regression, not an environment problem.');
}

if (readiness !== 'ready') {
  console.error(serverSaid());
  stop();
  fail(ENVIRONMENT, 'coai-server never answered /api/health — the requests were never sent.');
}

// Written through a temporary file and renamed, and named after THIS run's port. Two runs at once
// (a developer and a CI job on one machine) would otherwise overwrite each other's tokens and base
// URL, and each would send requests to the other's server. (local, code round.)
const envPath = path.join(HERE, '.env');
const envTemp = `${envPath}.${port}.tmp`;
writeFileSync(
  envTemp,
  [
    `baseUrl=${baseUrl}`,
    `token=${mintToken(DEVELOPER)}`,
    `adminToken=${mintToken(ADMIN)}`,
    `outsiderToken=${mintToken(OUTSIDER)}`,
    '',
  ].join('\n'),
);
renameSync(envTemp, envPath);

// httpyac is a local devDependency, spawned as plain Node with NO shell.
//
// It used to be `npx --yes httpyac@…` with `shell: true`, and on Windows that puts a cmd.exe
// between this script and the tool. The tool finished every request, printed its summary, and the
// shell never exited — so the run reported all assertions passed and then hung, which is the worst
// possible failure for a check somebody is supposed to trust. Spawning the JS entry point directly
// removes the shell from the chain, and pinning it in http/package.json removes the download.
const tool = path.join(HERE, 'node_modules', 'httpyac', 'bin', 'httpyac.js');
if (!existsSync(tool)) {
  stop();
  fail(CONFIG, 'httpyac is not installed — run `npm install` in http/ (it is a pinned devDependency).');
}

let httpyac;
try {
  httpyac = spawnSync(
    process.execPath,
    // A RELATIVE glob, resolved against `cwd` below: pointed at the folder, httpyac tries to parse
    // this very script as a request file.
    [tool, 'send', '--all', '**/*.http'],
    {
      stdio: 'inherit',
      cwd: HERE,
      // A backstop, not the plan: the requests themselves take about five seconds, so a run that has
      // not finished in three minutes is wedged rather than slow.
      timeout: 180_000,
    },
  );
} finally {
  // Every path, including a throw from spawnSync itself. A server left holding a port and a temp
  // directory left on disk are what make the NEXT run fail for a reason that has nothing to do with
  // the API. (Four findings, first code round.)
  stop();
}
if (httpyac.error) {
  fail(CONFIG, `httpyac could not be started: ${httpyac.error.message}`);
}

// The rule reserves exit 1 for a CONTRACT regression — an API that answered something other than
// what its file says. Everything else is the machine: a missing binary, a bad argument, a run that
// produced no report at all. Reporting those as 1 tells whoever reads the exit code that the server
// is broken when it is the harness that is.
if (httpyac.status === null) {
  fail(ENVIRONMENT, 'httpyac was killed before it finished — no verdict was produced.');
}

// httpyac answers 0 for a clean run and 1 when a request failed its assertions. Anything ELSE is
// the harness — bad arguments, a crash, a version that does not understand the flags — and reporting
// those as 1 tells whoever reads the exit code that the API is broken when it is the suite that is.
if (httpyac.status === 1) {
  fail(CONTRACT, 'a contract request failed — the API did not answer what its .http file says it does.');
}

if (httpyac.status !== 0) {
  const code = httpyac.status;
  fail(CONFIG, `httpyac exited ${code}, which is neither pass nor a failed assertion — `
    + 'the suite is misconfigured, not the API.');
}

console.log('\n  contracts: every request answered what its file says.\n');
process.exit(PASS);
