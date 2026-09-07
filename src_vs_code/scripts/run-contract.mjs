#!/usr/bin/env node
/**
 * Runs the client/server contract tests against a REAL coai-server.
 *
 * <p>Two ways in, and the tests cannot tell them apart:</p>
 *
 * <ul>
 *   <li><b>Point it at one you already have</b> — set `COAI_CONTRACT_URL` and `COAI_CONTRACT_KEY`
 *       (and `COAI_CONTRACT_DOMAIN` if it is not `contract.test`). That is the `docker compose up`
 *       story: nothing here starts or stops anything.</li>
 *   <li><b>Let it start one</b> — the default. It runs the server this repository just built, on a
 *       free loopback port, with a throwaway data directory and a freshly generated signing key,
 *       and stops it afterwards.</li>
 * </ul>
 *
 * <p>Starting a plain `dotnet` process rather than building the container is a deliberate CI
 * choice: `src_server/Dockerfile` publishes Native AOT (clang, a full link) and would add minutes
 * to every pull request, while `build · test · family checks` has already compiled this server
 * seconds earlier. The container remains the better LOCAL story, which is why the address is an
 * input rather than something this script owns.</p>
 */
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const DOMAIN = process.env.COAI_CONTRACT_DOMAIN ?? 'contract.test';
const READY_TIMEOUT_MS = 60_000;

/** The compiled contract tests, listed rather than globbed — see scripts/run-tests.mjs on why. */
function contractTests() {
  const dir = join('out', 'contract');
  if (!existsSync(dir)) {
    fail(`no compiled contract tests in ${dir} — did the compile step run?`);
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
 * <p>`coai-server.dll`, not `CoaiServer.dll` — the project sets `AssemblyName`, and the file is
 * named for the binary people run rather than for the csproj.</p>
 */
function serverAssembly() {
  const candidates = ['Release', 'Debug'].map((c) =>
    resolve('..', 'src_server', 'src', 'bin', c, 'net10.0', 'coai-server.dll'));
  const found = candidates.find((p) => existsSync(p));
  if (found === undefined) {
    fail(
      'the Team server is not built. Run `dotnet build dew_flow_connect_other_ais.slnx -c Release` '
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
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      fail(`the server exited with code ${child.exitCode} before it became healthy`);
    }
    try {
      const response = await fetch(`${url}/api/health`);
      if (response.ok) {
        return;
      }
    } catch {
      // Not up yet. The deadline is the only thing that ends this loop unhappily.
    }
    await new Promise((r) => setTimeout(r, 250));
  }

  fail(`the server did not answer ${url}/api/health within ${READY_TIMEOUT_MS / 1000}s`);
}

async function startServer() {
  const port = await freePort();
  const url = `http://127.0.0.1:${port}`;
  // At least 32 bytes or the server refuses to start, deliberately (src_server/src/Startup.cs).
  const key = randomBytes(48).toString('base64');
  const data = mkdtempSync(join(tmpdir(), 'coai-contract-'));

  const child = spawn('dotnet', [serverAssembly()], {
    stdio: ['ignore', 'inherit', 'inherit'],
    env: {
      ...process.env,
      ASPNETCORE_URLS: url,
      // Logs follow the data directory (`CoaiLogPath.RootFor`), so the throwaway directory takes
      // them too and nothing is written next to the extension's sources.
      Coai__DataDir: data,
      Coai__AllowedDomains: DOMAIN,
      // Loopback with no proxy in front of it: the forwarded-proto gate would refuse every
      // request, and it is a deployment concern rather than a contract one.
      Coai__RequireForwardedHttps: 'false',
      Auth__Local__SigningKey: key,
    },
  });

  await waitForHealth(url, child);

  return { url, key, stop: () => stopServer(child, data) };
}

function stopServer(child, data) {
  child.kill();
  try {
    rmSync(data, { recursive: true, force: true });
  } catch {
    // A leftover temp directory is not worth failing a green run over.
  }
}

function runTests(env) {
  return new Promise((ok) => {
    const child = spawn(process.execPath, ['--test', ...contractTests()], {
      stdio: 'inherit',
      env: { ...process.env, ...env },
    });
    child.on('exit', (code) => ok(code ?? 1));
  });
}

function fail(message) {
  console.error(`run-contract: ${message}`);
  process.exit(1);
}

const given = process.env.COAI_CONTRACT_URL ?? '';
if (given.length > 0) {
  if ((process.env.COAI_CONTRACT_KEY ?? '').length === 0) {
    fail('COAI_CONTRACT_URL is set but COAI_CONTRACT_KEY is not — the tests cannot mint a token.');
  }
  console.log(`run-contract: using the server at ${given}`);
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
  server.stop();
}
process.exit(status);
