import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * The exit paths of a runner that starts a server (E5 S4 final fix round, items 1-3; `run-contract.mjs` and `team-server-child.mjs`).
 *
 * The run folder holds the server's data, and the wrap removes the run folder when the process exits. So a server must be GONE — told, waited for, or killed — before
 * the process ends by ANY road: the normal `finally`, `fail()`, an uncaught exception, a bare `process.exit`, Ctrl-C followed by the `finally`. The real server needs the
 * .NET build; these cases run the REAL `run-contract.mjs` and `team-server-child.mjs` (copied into a fixture tree) against a FAKE `dotnet`, a node script that answers
 * `/api/health`, can ignore SIGTERM, can keep writing into its data folder, can die of SIGKILL at once, and records its pid.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const SCRIPTS = ['run-contract.mjs', 'team-server-child.mjs', 'lower-priority.mjs', 'run-tests.mjs', 'owned.mjs', 'ownedRun.mjs', 'runFolderGuard.cjs', 'startOfRun.mjs'];

const FAKE_DOTNET = `#!/usr/bin/env node
const http = require('node:http');
const fs = require('node:fs');
const mode = process.env.FAKE_MODE ?? 'ok';
const url = new URL(process.env.ASPNETCORE_URLS);
if (process.env.FAKE_PIDFILE) fs.writeFileSync(process.env.FAKE_PIDFILE, String(process.pid));
if (mode.includes('ignore-term')) process.on('SIGTERM', () => {});
if (mode.includes('kill-self')) process.kill(process.pid, 'SIGKILL');
http.createServer((req, res) => { res.statusCode = mode.includes('unhealthy') ? 500 : 200; res.end('x'); }).listen(Number(url.port), '127.0.0.1');
if (mode.includes('writer')) setInterval(() => { try { fs.writeFileSync(process.env.Coai__DataDir + '/w' + Date.now(), 'x'); } catch {} }, 20);
setInterval(() => {}, 1000);
`;

const PRELOAD = `
// Loaded into every node process of the fixture run; acts only in run-contract.mjs itself.
if (/run-contract\\.mjs$/.test(process.argv[1] ?? '') && process.env.FAKE_ACTION) {
  const [action, ms] = process.env.FAKE_ACTION.split(':');
  setTimeout(() => {
    if (action === 'throw') throw new Error('boom');
    if (action === 'exit') process.exit(7);
    if (action === 'sigint') process.kill(process.pid, 'SIGINT');
  }, Number(ms));
}
`;

/** A fixture tree: the real scripts, a fake server build, a fake dotnet on PATH, one contract test that takes `testMs`. */
function fixture(t, { testMs = 300, withTests = true } = {}) {
  const base = owned.ownedDir(t, 'rcx');
  const tree = path.join(base, 'tree');
  const scripts = path.join(tree, 'src_vs_code', 'scripts');
  fs.mkdirSync(scripts, { recursive: true });
  for (const name of SCRIPTS) fs.copyFileSync(path.join(pkg, 'scripts', name), path.join(scripts, name));
  fs.symlinkSync(path.join(repoRoot, '.agents'), path.join(tree, '.agents'));
  fs.mkdirSync(path.join(tree, 'shared'));
  for (const name of ['leftovers-sweep.json', 'temp-sweep.json']) fs.copyFileSync(path.join(repoRoot, 'shared', name), path.join(tree, 'shared', name));
  const dll = path.join(tree, 'src_server', 'src', 'bin', 'Release', 'net10.0', 'coai-server.dll');
  fs.mkdirSync(path.dirname(dll), { recursive: true });
  fs.writeFileSync(dll, '');
  const bin = path.join(base, 'bin');
  fs.mkdirSync(bin);
  fs.writeFileSync(path.join(bin, 'dotnet'), FAKE_DOTNET, { mode: 0o755 });
  const ext = path.join(tree, 'src_vs_code');
  if (withTests) {
    fs.mkdirSync(path.join(ext, 'out', 'test'), { recursive: true });
    fs.writeFileSync(path.join(ext, 'out', 'test', 'x.contract.js'), `const t = require('node:test'); t('slow', async () => { await new Promise((r) => setTimeout(r, ${testMs})); });\n`);
  }
  const preload = path.join(base, 'preload.mjs');
  fs.writeFileSync(preload, PRELOAD);
  const temp = path.join(base, 'temp');
  fs.mkdirSync(temp);
  return { ext, bin, preload, temp, pidfile: path.join(base, 'server.pid') };
}

function runContract(fx, { mode = 'ok', action = '', signalAfter } = {}) {
  const env = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', 'COAI_TEST_RUN_FOLDER', 'TESTSTODELETE_RUN', 'COAI_RUNTEMP_SELFTEST', 'COAI_CONTRACT_URL', 'CI']) delete env[name];
  const child = spawn(process.execPath, [path.join(fx.ext, 'scripts', 'run-contract.mjs')], {
    cwd: fx.ext, stdio: ['ignore', 'pipe', 'pipe'],
    env: {
      ...env, PATH: `${fx.bin}${path.delimiter}${env.PATH}`, TEMP: fx.temp, TMP: fx.temp, TMPDIR: fx.temp, FAKE_MODE: mode, FAKE_PIDFILE: fx.pidfile, FAKE_ACTION: action,
      NODE_OPTIONS: `--import=${pathToFileURL(fx.preload).href}`,
    },
  });
  let output = '';
  child.stdout.on('data', (chunk) => { output += chunk; });
  child.stderr.on('data', (chunk) => { output += chunk; });
  return new Promise((done) => {
    const watchdog = setTimeout(() => { child.kill('SIGKILL'); }, 60_000);
    child.on('exit', (status, signal) => { clearTimeout(watchdog); done({ status, signal, output, endedAt: Date.now() }); });
    void signalAfter;
  });
}

const alive = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
};
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const serverPid = (fx) => Number(fs.readFileSync(fx.pidfile, 'utf8'));
async function assertServerGoneAndNothingLeft(fx, result, within = 1000) {
  const pid = serverPid(fx);
  const until = Date.now() + within;
  while (alive(pid) && Date.now() < until) await sleep(50);
  assert.equal(alive(pid), false, `the server (pid ${pid}) was still alive ${within} ms after the runner exited:\n${result.output.slice(-600)}`);
  assert.deepEqual(fs.readdirSync(fx.temp), [], 'the run folder and its claim are gone');
}

test('X1 the normal road: the tests\' status is the runner\'s, the server has exited and the run folder is gone', async (t) => {
  const fx = fixture(t);
  const result = await runContract(fx);
  assert.equal(result.status, 0, result.output.slice(-800));
  await assertServerGoneAndNothingLeft(fx, result, 200);
});

test('X2 fail() waits for the cleanup in flight: no tests to run, and a server that ignores SIGTERM is killed before the process exits', async (t) => {
  const fx = fixture(t, { withTests: false });
  const result = await runContract(fx, { mode: 'ignore-term' });
  assert.equal(result.status, 1, result.output.slice(-800));
  assert.match(result.output, /no compiled tests/u);
  await assertServerGoneAndNothingLeft(fx, result, 200);
});

test('X3 an uncaught exception: the cleanup finishes, then exit 1', async (t) => {
  const fx = fixture(t, { testMs: 6000 });
  const result = await runContract(fx, { mode: 'ignore-term', action: 'throw:1500' });
  assert.equal(result.status, 1, result.output.slice(-800));
  await assertServerGoneAndNothingLeft(fx, result, 200);
});

test('X4 a bare process.exit: the exit listener does only synchronous work, and that work kills the server\'s tree and leaves nothing', async (t) => {
  const fx = fixture(t, { testMs: 6000 });
  const result = await runContract(fx, { mode: 'ignore-term', action: 'exit:1500' });
  assert.equal(result.status, 7, result.output.slice(-800));
  await assertServerGoneAndNothingLeft(fx, result, 1000);
});

test('X5 Ctrl-C and then the finally: the second caller waits for the cleanup already in flight, not for nothing', async (t) => {
  const fx = fixture(t, { testMs: 3000 });
  const result = await runContract(fx, { mode: 'ignore-term', action: 'sigint:1000' });
  assert.notEqual(result.signal, 'SIGINT');
  await assertServerGoneAndNothingLeft(fx, result, 200);
});

/** `team-server-child.mjs` of the fixture, with the fake dotnet first on PATH of THIS process for the call. */
async function withFakeServer(fx, env, fn) {
  const saved = {};
  const set = { PATH: `${fx.bin}${path.delimiter}${process.env.PATH}`, FAKE_PIDFILE: fx.pidfile, ...env };
  for (const [name, value] of Object.entries(set)) {
    saved[name] = process.env[name];
    process.env[name] = value;
  }
  try {
    return await fn(await import(pathToFileURL(path.join(fx.ext, 'scripts', 'team-server-child.mjs')).href));
  } finally {
    for (const [name, value] of Object.entries(saved)) {
      if (value === undefined) delete process.env[name];
      else process.env[name] = value;
    }
  }
}
const within = (ms, promise, what) => Promise.race([promise, new Promise((_, no) => setTimeout(() => no(new Error(`${what} did not settle within ${ms} ms`)), ms))]);

test('X6 stop() of a server that died of a SIGNAL settles at once (its exitCode is null, its signalCode is set)', async (t) => {
  const fx = fixture(t);
  await withFakeServer(fx, { FAKE_MODE: 'ok' }, async ({ stopServer }) => {
    const { spawn: start } = await import('node:child_process');
    const child = start(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { detached: process.platform !== 'win32', stdio: 'ignore' });
    await sleep(200);
    process.kill(child.pid, 'SIGKILL');
    await new Promise((gone) => child.once('exit', gone));
    assert.equal(child.exitCode, null);
    assert.equal(child.signalCode, 'SIGKILL');
    await within(1500, stopServer(child), 'stop() of a signal-killed child');
  });
});

test('X7 a server that dies of a signal while starting is reported at once, not after the health deadline', async (t) => {
  const fx = fixture(t);
  await withFakeServer(fx, { FAKE_MODE: 'kill-self' }, async ({ startTeamServer }) => {
    const started = Date.now();
    await assert.rejects(within(8000, startTeamServer({ extensionRoot: path.join(fx.ext), dataDir: fx.temp, domain: 'x.test', admins: 'a@x.test', stdio: 'ignore' }), 'startTeamServer'), /exited|signal/u);
    assert.ok(Date.now() - started < 5000, `reported after ${Date.now() - started} ms`);
  });
});

test('X8 a failed start waits for the server to be gone: one that ignores SIGTERM does not outlive the rejection', async (t) => {
  const fx = fixture(t);
  await withFakeServer(fx, { FAKE_MODE: 'unhealthy-ignore-term' }, async ({ startTeamServer }) => {
    await assert.rejects(within(15_000, startTeamServer({ extensionRoot: fx.ext, dataDir: fx.temp, domain: 'x.test', admins: 'a@x.test', stdio: 'ignore', readyTimeoutMs: 800 }), 'startTeamServer'), /did not answer/u);
    assert.equal(alive(serverPid(fx)), false, 'the server was still alive when the rejection arrived');
  });
});
