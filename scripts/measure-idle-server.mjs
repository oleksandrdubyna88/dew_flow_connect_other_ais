#!/usr/bin/env node
// Measures what one coai-mcp costs when nobody uses it, and how long it takes to answer `initialize`
// (research/RESULTS_idle_cpu_and_slow_start.md, 2026-10-06).
//
//   node scripts/measure-idle-server.mjs <coai-mcp binary> <data-dir snapshot> [--env <file>] [--settle <s>] [--idle <s>]
//
// The snapshot is COPIED to a fresh temp directory per run, so the measured server never touches it and two arms
// start from the same bytes. `--env` is a file of NAME=value lines (a live instance's COAI_* environment) put into the
// child's environment; every other COAI_* variable of this shell is removed. The copy is deleted when the run ends,
// however it ends. The child's CPU is its own user+kernel time over the idle window: /proc/<pid>/stat on Linux,
// Get-Process on Windows. Only the child this script started is read or killed. Prints one JSON line.
//
// Run it on a machine you are not also loading, or say what else ran: on a CPU-saturated WSL the process start alone
// took 5–9 s (with `nice -n 19`), whatever the data.
import { spawn, execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [binary, snapshot, ...rest] = process.argv.slice(2);
if (!binary || !snapshot) {
  console.error('usage: measure-idle-server.mjs <binary> <snapshot> [--env <file>] [--settle <s>] [--idle <s>]');
  process.exit(65);
}
const option = (name, fallback) => {
  const at = rest.indexOf(name);
  return at >= 0 && at + 1 < rest.length ? rest[at + 1] : fallback;
};
const settle = Number(option('--settle', '30'));
const idle = Number(option('--idle', '120'));

const data = mkdtempSync(join(tmpdir(), 'coai-idle-arm-'));
cpSync(snapshot, data, { recursive: true });
const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => !name.startsWith('COAI_')));
const envFile = option('--env', '');
if (envFile) {
  for (const line of readFileSync(envFile, 'utf8').split(/\r?\n/)) {
    const at = line.indexOf('=');
    if (at > 0) env[line.slice(0, at)] = line.slice(at + 1);
  }
}
env.COAI_DATA_DIR = data;

// Both helpers by ABSOLUTE path, never by a PATH lookup a writable directory could shadow (Sonar S4036 on #690).
const POWERSHELL = join(process.env.SystemRoot ?? 'C:/Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
const GETCONF = '/usr/bin/getconf';
const cpuSeconds = (pid) => {
  if (process.platform === 'win32') {
    const said = execFileSync(POWERSHELL, ['-NoProfile', '-Command', `(Get-Process -Id ${pid}).TotalProcessorTime.TotalSeconds`]);
    return Number(String(said).trim().replace(',', '.'));
  }
  const stat = readFileSync(`/proc/${pid}/stat`, 'utf8');
  const fields = stat.slice(stat.lastIndexOf(') ') + 2).split(' '); // the name field may itself hold ') '
  const ticks = Number(execFileSync(GETCONF, ['CLK_TCK']));
  return (Number(fields[11]) + Number(fields[12])) / ticks;
};
const sleep = (seconds) => new Promise((done) => setTimeout(done, seconds * 1000));

let answered = 0;
let exited = false;
const child = spawn(binary, [], { env, stdio: ['pipe', 'pipe', 'ignore'] });
const ended = new Promise((done) => child.on('exit', () => { exited = true; done(); }));
child.on('error', () => { exited = true; });
child.stdout.on('data', () => { answered ||= Date.now(); });

// The answer is an event on stdout, so the wait for it is a short poll with a deadline.
const untilAnswered = (deadline) => new Promise((done) => {
  const tick = setInterval(() => {
    if (answered || exited || Date.now() >= deadline) {
      clearInterval(tick);
      done();
    }
  }, 10);
});

const measure = async () => {
  const started = Date.now();
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'measure-idle-server', version: '0' } } }) + '\n');
  await untilAnswered(started + 60_000);
  const initializeSeconds = answered ? (answered - started) / 1000 : null;
  child.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');
  await sleep(settle);
  if (exited) {
    return { binary, exitedBeforeTheIdleWindow: true, initializeSeconds };
  }
  const before = cpuSeconds(child.pid);
  const from = Date.now();
  await sleep(idle);
  const after = cpuSeconds(child.pid);
  const window = (Date.now() - from) / 1000;
  return {
    binary,
    sessions: existsSync(join(snapshot, 'sessions'))
      ? readdirSync(join(snapshot, 'sessions')).filter((name) => /^session-.*\.json$/.test(name)).length
      : 0,
    initializeSeconds,
    initializeAnsweredWithin60s: Boolean(answered) && answered - started <= 60_000,
    idleCpuPercentOfOneCore: Math.round(((after - before) / window) * 10000) / 100,
    idleCpuSeconds: Math.round((after - before) * 100) / 100,
    idleWindowSeconds: Math.round(window * 10) / 10,
  };
};

let outcome = 1;
try {
  const result = await measure();
  console.log(JSON.stringify(result));
  outcome = result.exitedBeforeTheIdleWindow ? 1 : 0;
} finally {
  // The copy holds a person's sessions and settings: it does not outlive the run, however the run ends — and it is
  // removed only once the child that had it open has exited (CodeRabbit on #690).
  if (!exited) child.kill();
  await Promise.race([ended, sleep(10)]);
  rmSync(data, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
process.exit(outcome);
