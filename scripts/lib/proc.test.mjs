// node --test scripts/lib/*.test.mjs — the Linux halves of the Kimi tree-hold probe (plan §5, E1): the process listing from /proc
// (pid, parent, comm, and the NAME the product's listing reads) and the lock probe that asks flock(1) whether an EXCLUSIVE taker would
// get in. The parser and the Kimi rule run everywhere over a fake /proc; the flock case needs util-linux and runs on the Linux CI job and in WSL.
// Makes folders only under this user's own temp folder, and removes them; ends only the pid it started. Runs no real kimi.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { FLOCK, isKimi, linuxProcesses, lockState, parseProcStat, parseStartTime, waitForNoOtherKimi } from './proc.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-proc-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

test('a /proc/<pid>/stat line gives pid, parent and comm — comm is cut at the LAST ")" because a name may hold one', () => {
  assert.deepEqual(parseProcStat('4242 (kimi) S 17 4242 4242 0 -1 4194560 7 0'), { pid: 4242, ppid: 17, comm: 'kimi' });
  assert.deepEqual(parseProcStat('7 (a) b (c)) R 1 7 7'), { pid: 7, ppid: 1, comm: 'a) b (c)' });
  assert.equal(parseProcStat(''), null);
  assert.equal(parseProcStat('not a stat line'), null);
});

test('the listing reads every numeric /proc entry and skips the rest — over a fake /proc on disk, with the name read the product\'s way', () => {
  const proc = join(base, 'proc');
  const own = process.getuid?.() ?? 0;
  const entry = (pid, line, argv0) => {
    mkdirSync(join(proc, String(pid)), { recursive: true });
    writeFileSync(join(proc, String(pid), 'stat'), `${line}\n`);
    writeFileSync(join(proc, String(pid), 'status'), `Name:\tx\nUid:\t${own}\t${own}\t0\t0\n`);
    writeFileSync(join(proc, String(pid), 'cmdline'), `${argv0}\0--flag\0`);
  };
  entry(1, '1 (init) S 0 1 1', '/sbin/init');
  entry(300, '300 (coai-mcp) S 1 300 300', '/usr/local/bin/coai-mcp');
  entry(301, '301 (MainThread) S 300 301 300', '/root/.kimi-code/bin/kimi'); // no exe link on disk: argv[0] names it
  mkdirSync(join(proc, 'self'), { recursive: true });
  mkdirSync(join(proc, '999'), { recursive: true }); // a process that ended between the listing and the read: no stat
  writeFileSync(join(proc, 'uptime'), '1 1');

  const all = linuxProcesses(proc, undefined, { uid: own }).sort((a, b) => a.pid - b.pid);

  assert.deepEqual(all, [
    { pid: 1, ppid: 0, comm: 'init', name: 'init' },
    { pid: 300, ppid: 1, comm: 'coai-mcp', name: 'coai-mcp' },
    { pid: 301, ppid: 300, comm: 'MainThread', name: 'kimi' },
  ]);
});

// ------------------------------------------------------------------------------------------ parity with the product's Kimi rule
// The probes' only Kimi test is lib/proc.mjs's isKimi, a PORT of ForeignKimiWatch.IsKimi (src_mcp/runners/Kimi/ForeignKimi.cs:51-54, :115) over
// the name LinuxProcessListing reads (LinuxProcessListing.cs:100-134). Each case below is one of the product's own, by name — in
// src_mcp/tests/ForeignKimiTests.cs and src_mcp/tests/LinuxHiddenExeTests.cs — built over an in-memory /proc (the model is the product's
// FakeProcReads), so a drift of the port from the product is a red test, not a probe that quietly misses a running kimi.

const denied = (what) => Object.assign(new Error(`EACCES: ${what}`), { code: 'EACCES' });
const missing = (what) => Object.assign(new Error(`ENOENT: ${what}`), { code: 'ENOENT' });
const OWN = 1000;

/** An in-memory /proc as lib/proc.mjs's reads: `running` (a readable exe), `hidden` (exe denied, cmdline readable), `with` (set up by hand). */
function fakeProc() {
  const rows = new Map();
  const put = (pid, row) => rows.set(pid, { state: 'S', uid: OWN, comm: 'x', exe: () => '', cmdline: () => '', ...row });
  const row = (pid) => {
    if (!rows.has(pid)) throw missing(`/proc/${pid}`);
    return rows.get(pid);
  };
  const fake = {
    running(pid, ppid, exe, comm = '') {
      put(pid, { ppid, comm: comm || exe.split('/').pop(), exe: () => exe, cmdline: () => `${exe}\0` });
      return fake;
    },
    hidden(pid, ppid, argv, comm = '') {
      put(pid, { ppid, comm: comm || argv[0].split('/').pop(), exe: () => { throw denied(`/proc/${pid}/exe`); }, cmdline: () => `${argv.join('\0')}\0` });
      return fake;
    },
    with(pid, state, ppid, patch = {}) {
      put(pid, { ppid, state, ...patch });
      return fake;
    },
    patch(pid, patch) {
      rows.set(pid, { ...rows.get(pid), ...patch });
      return fake;
    },
    listed: undefined,
    reads: {
      list: () => (fake.listed ? fake.listed() : [...rows.keys()].map(String)),
      stat: (pid) => `${pid} (${row(Number(pid)).comm}) ${row(Number(pid)).state} ${row(Number(pid)).ppid} ${pid} ${pid} 0 -1`,
      status: (pid) => `Name:\t${row(Number(pid)).comm}\nUid:\t${row(Number(pid)).uid}\t${row(Number(pid)).uid}\t0\t0\n`,
      exe: (pid) => row(Number(pid)).exe(),
      cmdline: (pid) => row(Number(pid)).cmdline(),
    },
  };
  return fake;
}

const KIMI_EXE = '/root/.kimi-code/bin/kimi';
const listed = (fake) => linuxProcesses('/fake', fake.reads, { uid: OWN });
const kimiPids = (fake) => listed(fake).filter(isKimi).map((e) => e.pid);
/** A terminal's kimi, as ForeignKimiTests.ATerminalsKimi: pid 4242, comm kimi, exe the installed binary. */
const aTerminalsKimi = () => fakeProc().running(1, 0, '/sbin/init', 'init').running(4242, 1, KIMI_EXE, 'kimi');

test('S-N1: a running kimi acp (comm MainThread) whose exe link is the kimi binary is Kimi — the executable is the witness', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').running(4301, 1, KIMI_EXE, 'MainThread');

  const kimi = listed(fake).find((e) => e.pid === 4301);

  assert.equal(isKimi(kimi), true, 'a running kimi acp (comm MainThread) was not seen as Kimi — the guard would let a second refresher start');
  assert.deepEqual([kimi.comm, kimi.name], ['MainThread', 'kimi']);
});

test('S-N1: a comm of MainThread ALONE is not Kimi — the executable decides, exactly as in the product', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').running(4302, 1, '/usr/bin/some-python', 'MainThread');

  assert.deepEqual(kimiPids(fake), []);
});

test('ATuiWhoseCommIsKimi_IsKimiByItsSecondWitness (ForeignKimiTests.cs:238)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').running(500, 1, '/usr/bin/wrapped-binary', 'kimi');

  assert.deepEqual(kimiPids(fake), [500], 'a process whose comm is kimi is the TUI as the first scout saw it');
});

test('ADeletedKimiBinary_IsStillKimi_AndAnUnrelatedZombie_IsSkipped (ForeignKimiTests.cs:298)', () => {
  const fake = aTerminalsKimi().patch(4242, { exe: () => `${KIMI_EXE} (deleted)` })
    .with(999, 'Z', 1, { exe: () => { throw denied('a zombie exe cannot be read'); }, cmdline: () => { throw denied('a zombie cmdline'); } });

  const all = listed(fake);

  assert.equal(all.find((e) => e.pid === 4242).name, 'kimi', 'a running kimi whose binary was replaced was not seen');
  assert.deepEqual(kimiPids(fake), [4242]);
  assert.equal(all.some((e) => e.pid === 999), false, 'an unrelated zombie is skipped, never a reason to stop');
});

test('AProcessThatLeftBetweenTheListingAndTheRead_IsSkipped_AndOnlyOneDeletedSuffixIsStripped (ForeignKimiTests.cs:313)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').running(2, 1, '/x/kimi (deleted) (deleted)').running(100, 1, '/usr/local/bin/coai-mcp', 'coai-mcp');
  fake.listed = () => ['1', '2', '100', '42']; // pid 42 left between the listing and the read

  const all = listed(fake);

  assert.equal(all.some((e) => e.pid === 42), false);
  assert.equal(all.find((e) => e.pid === 2).name, 'kimi (deleted)', 'ONE trailing " (deleted)" is stripped, not every one');
  assert.equal(isKimi(all.find((e) => e.pid === 2)), false, 'what is left is not the name kimi');
});

test('AHiddenExe_WhoseCmdlineNamesSystemd_IsNotKimi_AndDoesNotMakeTheListingUnreadable (LinuxHiddenExeTests.cs:27)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').hidden(700, 1, ['/usr/lib/systemd/systemd', '--user'], 'systemd');

  const systemd = listed(fake).find((e) => e.pid === 700);

  assert.equal(systemd.name, 'systemd', 'the identity is the basename of argv[0] when the exe is denied');
  assert.deepEqual(kimiPids(fake), [], 'systemd --user is not a Kimi');
});

for (const argv0 of ['kimi', '/home/me/.local/bin/kimi']) {
  test(`AHiddenExe_WhoseCmdlineNamesKimi_IsKimi_ByItsArgvZero (LinuxHiddenExeTests.cs:49) — ${argv0}`, () => {
    const fake = fakeProc().running(1, 0, '/sbin/init', 'init').hidden(4242, 1, [argv0, 'acp']);

    assert.deepEqual(kimiPids(fake), [4242], 'a Kimi whose exe is hidden was not seen');
  });
}

test('AHiddenExe_WhoseCommIsKimi_IsKimi_AlthoughArgvZeroSaysNode (LinuxHiddenExeTests.cs:60)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').hidden(4243, 1, ['node', 'app.js'], 'kimi');

  assert.deepEqual(kimiPids(fake), [4243]);
});

test('ADeletedSuffixIsStrippedFromTheExeOnly_NeverFromArgvZero (LinuxHiddenExeTests.cs:101)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init').hidden(891, 1, ['/opt/kimi (deleted)']);

  assert.equal(listed(fake).find((e) => e.pid === 891).name, 'kimi (deleted)', 'the kernel appends " (deleted)" to an exe link, never to a process\'s own argv[0]');
});

test('the rest of the product\'s hidden-exe shapes: an exe that fails any other way falls back to argv[0]; an empty cmdline is listed by comm alone; a process gone before its cmdline is skipped', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init')
    .with(890, 'S', 1, { exe: () => { throw Object.assign(new Error('EIO'), { code: 'EIO' }); }, cmdline: () => '/opt/kimi/kimi\0acp\0' })
    .with(892, 'S', 1, { comm: 'worker', exe: () => { throw denied('exe'); }, cmdline: () => '' })
    .with(889, 'S', 1, { exe: () => { throw denied('exe'); }, cmdline: () => { throw missing('cmdline'); } });

  const all = listed(fake);

  assert.deepEqual(kimiPids(fake), [890]);
  assert.deepEqual([all.find((e) => e.pid === 892).name, all.find((e) => e.pid === 892).comm], ['', 'worker']);
  assert.equal(all.some((e) => e.pid === 889), false);
});

test('WhenBothTheExeAndTheCmdlineAreUnreadable_TheListingIsUnreadable_NamingThePidAndWhatFailed (LinuxHiddenExeTests.cs)', () => {
  const fake = fakeProc().running(1, 0, '/sbin/init', 'init')
    .with(888, 'S', 1, { exe: () => { throw denied('/proc/888/exe'); }, cmdline: () => { throw denied('/proc/888/cmdline'); } });

  assert.throws(() => listed(fake), (e) => /888/u.test(e.message) && /exe/u.test(e.message) && /cmdline/u.test(e.message),
    'a process that could be a Kimi, and cannot be identified, was read as "not a Kimi"');
});

test('AnotherAccountsProcess_IsSkipped_NotCountedUnreadable (ForeignKimiTests.cs)', () => {
  const fake = aTerminalsKimi().with(1001, 'S', 1, { uid: 0, exe: () => { throw denied('exe'); }, cmdline: () => { throw denied('cmdline'); } });

  assert.deepEqual(listed(fake).map((e) => e.pid).sort((a, b) => a - b), [1, 4242], 'another account\'s process cannot refresh this person\'s 0700 folder');
});

test('the Windows image name is the name: kimi.exe, in any case, is Kimi; a bare entry with no comm is read by name alone', () => {
  assert.equal(isKimi({ pid: 5, ppid: 1, name: 'KIMI.EXE' }), true);
  assert.equal(isKimi({ pid: 6, ppid: 1, name: 'node.exe' }), false);
  assert.equal(isKimi({ pid: 7, ppid: 1, name: '' }), false);
});

// ------------------------------------------------------------------------------------------ the shared "no other kimi" guard

/** A listing source that answers the given listings in order, then the last one for ever; the clock and the sleep are the test's. */
function guardRig(listings, { waitMs = 600_000, pollMs = 10_000, except = [], startTimeOf = () => '' } = {}) {
  let asked = 0;
  let clock = 0;
  const log = [];
  return {
    log,
    get asked() { return asked; },
    run: () => waitForNoOtherKimi('cell-1', {
      list: () => listings[Math.min(asked++, listings.length - 1)],
      sleep: async (ms) => { clock += ms; },
      now: () => clock,
      log: (line) => log.push(line),
      waitMs,
      pollMs,
      except,
      startTimeOf,
    }),
  };
}

const KIMI = { pid: 77, ppid: 1, comm: 'MainThread', name: 'kimi' };
const NOT_KIMI = { pid: 78, ppid: 1, comm: 'node', name: 'node' };

test('the guard passes at once with no other kimi, and waits (saying so) while one runs until it ends', async () => {
  const quiet = guardRig([[NOT_KIMI]]);
  assert.equal(await quiet.run(), true);
  assert.equal(quiet.asked, 1);

  const busy = guardRig([[KIMI], [KIMI], [NOT_KIMI]]);
  assert.equal(await busy.run(), true);
  assert.equal(busy.asked, 3);
  assert.equal(busy.log.filter((l) => /waiting/u.test(l)).length, 2);
  assert.match(busy.log[0], /pids 77/u);
});

test('the guard gives up after its wait and says the run is SKIPPED — two refreshers could sign the person out', async () => {
  const stuck = guardRig([[KIMI]], { waitMs: 30_000, pollMs: 10_000 });

  assert.equal(await stuck.run(), false);
  assert.match(stuck.log.at(-1), /SKIPPED.*another kimi still runs/u);
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

// ------------------------------------------------------------------------------------------ the live-survivor exception (killed-hard-live)
// N-K2's live variant launches the next turn while the killed arm's OWN orphan still runs, to see the product's gate refuse. The probe's guard is
// bypassed for exactly that orphan — its pid AND its start time (a reused pid is a stranger), never its name — and every other kimi still blocks.

const ORPHAN = { pid: 100, ppid: 1, comm: 'MainThread', name: 'kimi' };
const STRANGER = { pid: 300, ppid: 1, comm: 'MainThread', name: 'kimi' };
const startTimes = (table) => (pid) => table[pid] ?? '';

test('a stat line\'s start time is field 22, cut after the LAST ")"; a short or odd line gives ""', () => {
  assert.equal(parseStartTime('100 (kimi) S 1 100 100 0 -1 4194560 7 0 0 0 1 1 0 0 20 0 3 0 98765 123 4'), '98765');
  assert.equal(parseStartTime('100 (a) b (c)) S 1 100 100 0 -1 4194560 7 0 0 0 1 1 0 0 20 0 3 0 4242 123 4'), '4242');
  assert.equal(parseStartTime('100 (kimi) S 1 100'), '');
  assert.equal(parseStartTime('garbage'), '');
});

test('the guard lets the named orphan through by pid AND start time, and says nothing is waited for', async () => {
  const rig = guardRig([[ORPHAN]], { except: [{ pid: 100, name: 'kimi', startTime: 'T1' }], startTimeOf: startTimes({ 100: 'T1' }) });

  assert.equal(await rig.run(), true, 'the killed arm\'s own orphan was waited for — the live variant could never launch');
  assert.equal(rig.log.length, 0);
});

test('every OTHER kimi still blocks — a second kimi with the same NAME is not the orphan', async () => {
  const rig = guardRig([[ORPHAN, STRANGER]], { except: [{ pid: 100, name: 'kimi', startTime: 'T1' }], startTimeOf: startTimes({ 100: 'T1', 300: 'T7' }), waitMs: 20_000, pollMs: 10_000 });

  assert.equal(await rig.run(), false, 'a kimi that is not the orphan was let through — two refreshers could race');
  assert.match(rig.log.at(-1), /pids 300\)/u, 'the refusal must name the stranger and not the orphan');
});

test('a reused pid (same number, another start time) and an unreadable start time are not the orphan — the exception fails closed', async () => {
  const reused = guardRig([[ORPHAN]], { except: [{ pid: 100, name: 'kimi', startTime: 'T1' }], startTimeOf: startTimes({ 100: 'T9' }), waitMs: 0 });
  const unreadable = guardRig([[ORPHAN]], { except: [{ pid: 100, name: 'kimi', startTime: '' }], startTimeOf: startTimes({ 100: '' }), waitMs: 0 });

  assert.equal(await reused.run(), false);
  assert.equal(await unreadable.run(), false);
});
