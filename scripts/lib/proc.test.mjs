// node --test scripts/lib/*.test.mjs — the Linux halves of the Kimi tree-hold probe (plan §5, E1): the process listing from /proc
// (pid, parent, comm — the probe never ends a process it lists) and the lock probe that asks flock(1) whether an EXCLUSIVE taker would
// get in. The parser runs everywhere over a fake /proc; the flock case needs util-linux and runs on the Linux CI job and in WSL.
// Makes folders only under this user's own temp folder, and removes them; ends only the pid it started.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { FLOCK, linuxProcesses, lockState, parseProcStat } from './proc.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-proc-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

test('a /proc/<pid>/stat line gives pid, parent and comm — comm is cut at the LAST ")" because a name may hold one', () => {
  assert.deepEqual(parseProcStat('4242 (kimi) S 17 4242 4242 0 -1 4194560 7 0'), { pid: 4242, ppid: 17, name: 'kimi' });
  assert.deepEqual(parseProcStat('7 (a) b (c)) R 1 7 7'), { pid: 7, ppid: 1, name: 'a) b (c)' });
  assert.equal(parseProcStat(''), null);
  assert.equal(parseProcStat('not a stat line'), null);
});

test('the listing reads every numeric /proc entry and skips the rest — over a fake /proc', () => {
  const proc = join(base, 'proc');
  const entry = (pid, line) => {
    mkdirSync(join(proc, String(pid)), { recursive: true });
    writeFileSync(join(proc, String(pid), 'stat'), `${line}\n`);
  };
  entry(1, '1 (init) S 0 1 1');
  entry(300, '300 (coai-mcp) S 1 300 300');
  entry(301, '301 (kimi) S 300 301 300');
  mkdirSync(join(proc, 'self'), { recursive: true });
  mkdirSync(join(proc, '999'), { recursive: true }); // a process that ended between the listing and the read: no stat
  writeFileSync(join(proc, 'uptime'), '1 1');

  const all = linuxProcesses(proc).sort((a, b) => a.pid - b.pid);
  assert.deepEqual(all, [{ pid: 1, ppid: 0, name: 'init' }, { pid: 300, ppid: 1, name: 'coai-mcp' }, { pid: 301, ppid: 300, name: 'kimi' }]);
});

test('the lock probe says Held while another process holds an exclusive flock, Free after it lets go, Absent with no file', { skip: (WINDOWS || !existsSync(FLOCK)) && 'flock(1) is util-linux — the Linux CI job and WSL' }, async () => {
  const file = join(base, 'tree.lock');
  writeFileSync(file, '');
  assert.equal(lockState(join(base, 'no-such.lock')), 'Absent');
  assert.equal(lockState(file), 'Free');

  const holder = spawn(FLOCK, [file, 'sleep', '2'], { stdio: 'ignore' });
  try {
    let state = '';
    const until = Date.now() + 1500;
    while (state !== 'Held' && Date.now() < until) {
      state = lockState(file);
      if (state !== 'Held') await new Promise((done) => setTimeout(done, 25));
    }
    assert.equal(state, 'Held');
  } finally {
    await new Promise((done) => { if (holder.exitCode !== null) done(); else holder.on('exit', done); });
  }
  assert.equal(lockState(file), 'Free');
});
