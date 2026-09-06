#!/usr/bin/env node
// Runs the whole `.http` suite against a stack this script starts, and reports the verdict as an
// exit code: 0 pass · 1 CONTRACT regression · 3 environment · 4 configuration.
//
// The lifecycle is here rather than in a README because a suite whose setup is prose is a suite that
// runs on one machine. Everything it needs — the build, a free port, a throwaway data directory, the
// three personas, the readiness wait and the teardown — is in this file.

import { spawn, spawnSync } from 'node:child_process';
import { createHmac } from 'node:crypto';
import { mkdtempSync, renameSync, rmSync, writeFileSync } from 'node:fs';
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

/** 32+ bytes: the server refuses a shorter HMAC key at startup, and so does HMAC-SHA256. */
const SIGNING_KEY = 'coai-contract-suite-signing-key!!';
const DOMAIN = 'example.com';
const ADMIN = `boss@${DOMAIN}`;

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

/** True once the server answers its own health route. */
async function isReady(baseUrl) {
  try {
    const response = await fetch(`${baseUrl}/api/health`);

    return response.ok;
  } catch {
    return false;
  }
}

async function waitForReady(baseUrl, child, seconds) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      return false;
    }

    if (await isReady(baseUrl)) {
      return true;
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  return false;
}

const build = spawnSync(
  'dotnet',
  ['build', path.join(ROOT, 'src_server', 'src', 'CoaiServer.csproj'), '-c', 'Debug'],
  { stdio: 'inherit', shell: process.platform === 'win32' },
);
if (build.status !== 0) {
  fail(ENVIRONMENT, 'coai-server did not build — nothing to send requests to.');
}

const port = await freePort();
const baseUrl = `http://127.0.0.1:${port}`;
// A throwaway data directory per run: no vendors, no accounts, no sessions. That emptiness is
// deliberate — see the README on why no review ever reaches a vendor.
const dataDir = mkdtempSync(path.join(tmpdir(), 'coai-contracts-'));

const exe = path.join(
  ROOT, 'src_server', 'src', 'bin', 'Debug', 'net10.0',
  process.platform === 'win32' ? 'coai-server.exe' : 'coai-server');

const server = spawn(exe, [], {
  env: {
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
  },
  stdio: ['ignore', 'pipe', 'pipe'],
});

let serverOutput = '';
server.stdout.on('data', (chunk) => { serverOutput += chunk; });
server.stderr.on('data', (chunk) => { serverOutput += chunk; });

function stop() {
  if (server.exitCode === null) {
    server.kill();
  }

  rmSync(dataDir, { recursive: true, force: true });
}

process.on('exit', stop);
process.on('SIGINT', () => { stop(); process.exit(ENVIRONMENT); });

if (!(await waitForReady(baseUrl, server, 40))) {
  console.error(serverOutput);
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
    `token=${mintToken(`dev@${DOMAIN}`)}`,
    `adminToken=${mintToken(ADMIN)}`,
    `outsiderToken=${mintToken('someone@other-company.test')}`,
    '',
  ].join('\n'),
);
renameSync(envTemp, envPath);

let httpyac;
try {
  httpyac = spawnSync(
    'npx',
    // A GLOB, not the directory: pointed at the folder, httpyac tries to parse this very script as a
    // request file and reports `Invalid URL: import { spawn ... }`.
    ['--yes', 'httpyac@6.16.7', 'send', '--all', path.join(HERE, '**', '*.http')],
    { stdio: 'inherit', cwd: HERE, shell: process.platform === 'win32' },
  );
} finally {
  // Every path, including a throw from spawnSync itself. The `exit` handler is a backstop rather
  // than the plan: a server left holding a port and a temp directory left on disk are what make the
  // NEXT run fail for a reason that has nothing to do with the API. (Four findings, code round.)
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

if (httpyac.status === 2) {
  fail(CONFIG, 'httpyac refused its own arguments — the suite is misconfigured, not the API.');
}

if (httpyac.status !== 0) {
  fail(CONTRACT, 'a contract request failed — the API did not answer what its .http file says it does.');
}

console.log('\n  contracts: every request answered what its file says.\n');
process.exit(PASS);
