/**
 * The REAL Team server this repository just built, started on a free loopback port over a data directory the caller
 * owns — and stopped, tree and all.
 *
 * <p>Extracted from `run-contract.mjs` when a second runner needed it (the usage-counts leg of `run-seam.mjs`, story 5.1
 * of todo/PLAN_every_round_is_counted.md): two copies of how to start, wait for and kill a server would drift, and the
 * one that drifted would be the one nobody runs.</p>
 *
 * <p>Every failure THROWS, naming what went wrong; the caller decides how its run ends. A started server is stopped by
 * the `stop` it returns — callers put it on every exit path, because a server left holding a port is the next run's
 * mystery.</p>
 */
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { join, resolve } from 'node:path';

// Startup is a build this job just compiled, not a download: 30s is generous, and a shorter
// deadline is what turns a hang into a diagnosis instead of a coffee break.
const READY_TIMEOUT_MS = 30_000;
// One health request, so a server that accepts the connection and then says nothing cannot park
// the loop past its own deadline.
const HEALTH_REQUEST_TIMEOUT_MS = 5_000;
// Long enough for a graceful ASP.NET shutdown (its default drain is five seconds), short enough
// that a server ignoring SIGTERM does not become the next run's port conflict.
const STOP_GRACE_MS = 5_000;

/**
 * The server this repository just built. Release first: that is what CI compiles.
 *
 * <p>coai-server.dll, not CoaiServer.dll — the project sets AssemblyName, and the file is named
 * for the binary people run rather than for the csproj.</p>
 */
export function serverAssembly(extensionRoot) {
  const candidates = ['Release', 'Debug'].map((c) =>
    resolve(extensionRoot, '..', 'src_server', 'src', 'bin', c, 'net10.0', 'coai-server.dll'));
  const found = candidates.find((p) => existsSync(p));
  if (found === undefined) {
    throw new Error(
      'the Team server is not built. Run "dotnet build dew_flow_connect_other_ais.slnx -c Release" '
      + 'from the repository root first, or set COAI_CONTRACT_URL to point at a server you are '
      + `already running. Looked in:\n  ${candidates.join('\n  ')}`);
  }

  return found;
}

export function freePort() {
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
async function waitForHealth(url, child, say) {
  say(`waiting for ${url}/api/health`);
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`the server exited with code ${child.exitCode} before it became healthy — its output is above`);
    }
    try {
      const response = await fetch(`${url}/api/health`, { signal: AbortSignal.timeout(HEALTH_REQUEST_TIMEOUT_MS) });
      if (response.ok) {
        return;
      }
    } catch {
      // Not up yet, or it accepted and stalled — either way the deadline is what ends this loop.
    }
    await new Promise((r) => setTimeout(r, 250));
  }

  throw new Error(`the server did not answer ${url}/api/health within ${READY_TIMEOUT_MS / 1000}s`);
}

/**
 * Start the server over `dataDir`, with a fresh signing key, `domain` allowed and `admins` in `Coai:Admins`.
 *
 * @returns {Promise<{ url: string, key: string, stop: () => void }>}
 */
export async function startTeamServer({ extensionRoot, dataDir, domain, admins, say = () => {}, stdio = 'inherit' }) {
  const assembly = serverAssembly(extensionRoot);
  const port = await freePort();
  const url = `http://127.0.0.1:${port}`;
  // At least 32 bytes or the server refuses to start, deliberately (src_server/src/Startup.cs);
  // 48 because a key that costs nothing to make may as well be well over the floor.
  const key = randomBytes(48).toString('base64');

  const child = spawn('dotnet', [assembly], {
    // Inherited rather than captured by default: when startup fails, its own log lines are the diagnosis, and in CI
    // they belong in the job output where somebody will actually read them.
    stdio: ['ignore', stdio, stdio],
    // Its own process group on POSIX, so teardown can take the group rather than one pid.
    detached: process.platform !== 'win32',
    env: {
      ...process.env,
      ASPNETCORE_URLS: url,
      // Logs follow the data directory (CoaiLogPath.RootFor), so the throwaway directory takes
      // them too and nothing is written next to the extension's sources.
      Coai__DataDir: dataDir,
      Coai__AllowedDomains: domain,
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
      // Named, so the company view has a caller it answers and a member (anyone else) it refuses. Pinned like the domain
      // above: an inherited list could make every persona an admin and a 403 case pass as a 200.
      Coai__Admins: admins,
    },
  });
  let spawnError;
  child.on('error', (error) => { spawnError = error; });
  const stop = () => stopServer(child);
  try {
    await waitForHealth(url, child, say);
  } catch (error) {
    stop();
    throw spawnError === undefined ? error : new Error(`the server could not be started: ${spawnError.message}`);
  }

  return { url, key, stop };
}

/** SIGTERM, then the whole tree — in that order and never conditionally. */
function stopServer(child) {
  if (child.exitCode === null && child.pid !== undefined) {
    kill(child, 'SIGTERM');
    // A server that ignores the polite signal still has to release the port before the next run.
    // NOT unref'd: this timer is the escalation, and a timer nothing waits for is a timer that
    // never fires when the process is on its way out.
    const hard = setTimeout(() => kill(child, 'SIGKILL'), STOP_GRACE_MS);
    child.on('exit', () => clearTimeout(hard));
  }
}

/**
 * The process TREE, not the process.
 *
 * <p>`security.md` requires a timeout to kill the whole tree, and `dotnet <dll>` is a host that
 * may have children of its own. On POSIX the child owns a process group (`detached`), so the
 * negative pid takes the group; on Windows `taskkill /T` is the equivalent.</p>
 */
export function kill(child, signal) {
  try {
    if (process.platform === 'win32') {
      // The absolute path, not the name: `taskkill` resolved through PATH is a name a writeable
      // directory earlier in PATH can claim, and this call runs with whatever rights the run has.
      const taskkill = join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'taskkill.exe');
      spawn(taskkill, ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });

      return;
    }
    process.kill(-child.pid, signal);
  } catch {
    // Already gone, or never had a group. Nothing left to stop either way.
  }
}
