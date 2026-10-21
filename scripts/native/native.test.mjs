// node --test "scripts/**/*.test.mjs" — the scripts that run a measurement SERIES on the native Linux host safely (E1.S1 of
// todo/PLAN_kimi_and_vibe_on_native_linux_E1.md; scenario S-N15 of the parent plan, DN11): scripts/native/run-template.sh (rendered by
// scripts/lib/native-run.mjs), make-series.sh, abort.sh, remove-series.sh.
//
// The scripts are bash over /proc and setsid, so every behavioural case here is "[Linux only …]" in its NAME and SKIPS elsewhere
// with that reason — it never passes silently. They run on the Linux CI job (`ubuntu-latest`, step "Probe script tests") and in WSL;
// only the parity case runs everywhere. Each case makes its series folder under a fresh `testsToDelete-native-test-…` root (the
// scripts' root is /tmp unless COAI_NATIVE_SERIES_ROOT names another — this test's seam), starts DUMMY workloads only (a node probe
// that spawns a TERM-resistant child and a setsid child), keeps an unrelated SENTINEL `sleep` that nothing may end, and ends what it
// started by pid + start time. No real sign-in is ever touched: the fake home holds fake files.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { chmodSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { SERIES_SHAPE, renderRunScript } from '../lib/native-run.mjs';

const NATIVE = dirname(fileURLToPath(import.meta.url));
const LINUX = process.platform === 'linux';
const WHY = 'needs bash, /proc and setsid: runs on the Linux CI job and in WSL';
const linuxTest = (name, fn) => test(`[Linux only: bash + /proc + setsid; skipped elsewhere] ${name}`, { skip: LINUX ? false : WHY }, fn);

const ROOT = LINUX ? mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-native-test-')) : '';
const NODE = realpathSync.native(process.execPath);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ---------------------------------------------------------------- what the test started, so it can end exactly that
const started = [];
const procStat = (pid) => {
  try {
    const line = readFileSync(`/proc/${pid}/stat`, 'utf8');
    const rest = line.slice(line.lastIndexOf(') ') + 2).split(' ');
    return { state: rest[0], ppid: Number(rest[1]), pgrp: Number(rest[2]), start: rest[19] };
  } catch {
    return null;
  }
};
const identity = (pid) => ({ pid, start: procStat(pid)?.start ?? '' });
const alive = ({ pid, start }) => {
  const s = procStat(pid);
  return s !== null && s.start === start && s.state !== 'Z';
};
const track = (pid) => {
  const id = identity(pid);
  started.push(id);
  return id;
};
const endOwn = (id) => {
  if (alive(id)) {
    try {
      process.kill(id.pid, 'SIGKILL');
    } catch {
      // already gone
    }
  }
};
/** Ends the members of a handle's group that started at or after the leader — the test's own cleanup, independent of abort.sh. */
function endGroup(series, phase) {
  const file = join(series, 'logs', `${phase}.handle`);
  if (!existsSync(file)) return;
  const [, pgid, start] = readFileSync(file, 'utf8').trim().split(' ').map(Number);
  for (const name of readdirSync('/proc').filter((n) => /^\d+$/u.test(n))) {
    const s = procStat(Number(name));
    if (s !== null && s.pgrp === pgid && Number(s.start) >= start) endOwn({ pid: Number(name), start: s.start });
  }
}
const seriesInUse = [];
after(() => {
  if (!LINUX) return;
  for (const { series, phase } of seriesInUse) endGroup(series, phase);
  for (const id of started) endOwn(id);
  rmSync(ROOT, { recursive: true, force: true });
});

async function waitFor(what, ready, ms = 30_000) {
  const until = Date.now() + ms;
  while (Date.now() < until) {
    if (ready()) return;
    await sleep(50);
  }
  assert.fail(what);
}

// ---------------------------------------------------------------- a series folder, provisioned the way make-series.sh leaves it
const DUMMY = `
import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
const args = process.argv.slice(2);
const mode = (args.find((a) => a.startsWith('--mode=')) ?? '--mode=idle').slice(7);
const scratch = args[args.indexOf('--scratch') + 1];
if (mode.startsWith('exit')) process.exit(Number(mode.slice(4)));
const info = { execPath: process.execPath, path: process.env.PATH, pid: process.pid };
// node resets every signal disposition at start-up, so left alone it dies of the signal and the run script ends through wait —
// the script's own traps would never be what the test sees. In idle mode the probe swallows the three, so only the script's traps can end the run.
if (mode === 'idle') for (const signal of ['SIGTERM', 'SIGINT', 'SIGHUP']) process.on(signal, () => {});
if (mode === 'workloads') {
  // a child that ignores TERM (the disposition is inherited through exec), and a child that leaves the group
  info.term = spawn('/bin/sh', ['-c', "trap '' TERM; exec /bin/sleep 600"], { stdio: 'ignore' }).pid;
  info.setsid = spawn('/usr/bin/setsid', ['/bin/sleep', '600'], { stdio: 'ignore' }).pid;
}
writeFileSync(join(scratch, 'ready.json'), JSON.stringify(info));
setInterval(() => {}, 1000);
`;

let counter = 0;
function bareSeries(kind = 'vibe') {
  counter += 1;
  const dir = join(ROOT, `testsToDelete-native-${kind}-20261021-${randomBytes(2).toString('hex')}${String(counter).padStart(2, '0')}`);
  mkdirSync(dir, { mode: 0o700 });
  chmodSync(dir, 0o700);
  return dir;
}

function provisioned(phase = 'n-v2', { abortText = '' } = {}) {
  const series = bareSeries();
  for (const sub of ['checkout/scripts', 'tmp', 'run', 'logs', 'bin']) mkdirSync(join(series, sub), { recursive: true, mode: 0o700 });
  writeFileSync(join(series, 'SERIES_PATH'), `${series}\n`);
  writeFileSync(join(series, 'run', 'node.txt'), `${NODE}\n`);
  writeFileSync(join(series, 'run', 'path.txt'), '/usr/bin:/bin\n');
  writeFileSync(join(series, 'checkout', 'scripts', 'dummy-probe.mjs'), DUMMY);
  for (const file of ['abort.sh', 'remove-series.sh']) copyFileSync(join(NATIVE, file), join(series, 'run', file));
  if (abortText) writeFileSync(join(series, 'run', 'abort.sh'), abortText);
  seriesInUse.push({ series, phase });
  return series;
}

const render = (series, phase, mode, mutate = (text) => text) => {
  const text = mutate(renderRunScript({ series, phase, probe: 'dummy-probe.mjs', args: [`--mode=${mode}`], root: ROOT }));
  writeFileSync(join(series, 'run', `${phase}.sh`), text, { mode: 0o700 });
  return text;
};

const fakeHome = () => {
  const home = join(ROOT, `home-${randomBytes(3).toString('hex')}`);
  mkdirSync(join(home, '.vibe'), { recursive: true });
  mkdirSync(join(home, '.kimi-code', 'credentials'), { recursive: true });
  writeFileSync(join(home, '.vibe', '.env'), 'FAKE_KEY=not-a-secret\n');
  writeFileSync(join(home, '.kimi-code', 'credentials', 'kimi-code-0123456789abcdef.json'), '{"fake":true}');
  return home;
};

const scriptEnv = (extra = {}) => ({ PATH: '/usr/bin:/bin', HOME: ROOT, COAI_NATIVE_SERIES_ROOT: ROOT, COAI_NATIVE_ABORT_GRACE: '2', ...extra });

/** Starts a rendered run script the way the host does, minus ssh: setsid + bash by path (with nohup when asked). */
function launch(series, phase, { nohup = false } = {}) {
  const script = join(series, 'run', `${phase}.sh`);
  const argv = nohup ? ['/usr/bin/nohup', '/usr/bin/setsid', '/bin/bash', '--', script] : ['/usr/bin/setsid', '/bin/bash', '--', script];
  const child = spawn(argv[0], argv.slice(1), { env: scriptEnv(), stdio: 'ignore' });
  child.on('error', () => {});
  return child;
}

const logs = (series, phase, ext) => join(series, 'logs', `${phase}.${ext}`);
const handleOf = (series, phase) => {
  const [pid, pgid, start] = readFileSync(logs(series, phase, 'handle'), 'utf8').trim().split(' ');
  return { pid: Number(pid), pgid: Number(pgid), start };
};
const readyOf = (series, phase) => JSON.parse(readFileSync(join(series, phase, 'ready.json'), 'utf8'));
const hasFile = (path) => () => existsSync(path);

async function running(series, phase, mode, options = {}) {
  render(series, phase, mode, options.mutate);
  launch(series, phase, options);
  await waitFor(`the dummy probe (${mode}) never reported ready — the run script did not reach it: ${tail(series, phase)}`, hasFile(join(series, phase, 'ready.json')));
  return { handle: handleOf(series, phase), ready: readyOf(series, phase) };
}
const tail = (series, phase) => (existsSync(logs(series, phase, 'log')) ? readFileSync(logs(series, phase, 'log'), 'utf8').slice(-400) : '(no log)');

// ---------------------------------------------------------------- the cases
test('the series shape is ONE literal: native-run.mjs and all three bash scripts carry exactly the same text', () => {
  for (const file of ['make-series.sh', 'abort.sh', 'remove-series.sh']) {
    assert.ok(readFileSync(join(NATIVE, file), 'utf8').includes(SERIES_SHAPE), `${file} does not carry the series shape ${SERIES_SHAPE}`);
  }
});

test('every script has LF line endings and a bash shebang — a CRLF shebang is "bad interpreter" on the host', () => {
  for (const file of ['make-series.sh', 'abort.sh', 'remove-series.sh', 'run-template.sh']) {
    const text = readFileSync(join(NATIVE, file), 'utf8');
    assert.equal(text.includes('\r'), false, `${file} holds a carriage return`);
    assert.ok(text.startsWith('#!/bin/bash\n'), `${file} does not start with #!/bin/bash`);
  }
});

linuxTest('a probe that exits 3 leaves .exit 3, .done and a .handle of three integers whose pid equals its pgid', async () => {
  const series = provisioned();
  render(series, 'n-v2', 'exit3');
  launch(series, 'n-v2');
  await waitFor('a run script that exited 3 wrote no .done — the poll would wait for ever', hasFile(logs(series, 'n-v2', 'done')));
  assert.equal(readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim(), '3');
  const handle = readFileSync(logs(series, 'n-v2', 'handle'), 'utf8').trim().split(' ');
  assert.equal(handle.length, 3);
  assert.ok(handle.every((field) => /^\d+$/u.test(field)), `the handle is not three integers: ${handle.join(' ')}`);
  assert.equal(handle[0], handle[1], 'setsid made the script its own group leader, so pid equals pgid');
  assert.equal(existsSync(logs(series, 'n-v2', 'log')), true);
});

for (const [signal, code] of [['SIGTERM', 143], ['SIGINT', 130], ['SIGHUP', 129]]) {
  linuxTest(`a ${signal} to the group mid-probe still writes .done, and .exit holds ${code}`, async () => {
    const series = provisioned();
    const { handle } = await running(series, 'n-v2', 'idle');
    process.kill(-handle.pgid, signal);
    await waitFor(`a run script killed mid-probe wrote no .done — the poll would wait for ever (${signal})`, hasFile(logs(series, 'n-v2', 'done')));
    assert.equal(readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim(), String(code));
    assert.ok(existsSync(logs(series, 'n-v2', 'handle')));
    endGroup(series, 'n-v2'); // an async job ignores SIGINT, so the probe may still run: the test ends it, as its own cleanup
  });
}

linuxTest('started through nohup (SIGHUP ignored on entry) a HUP never ends the run WITHOUT .done — it is ignored or trapped to 129, and the outcome is printed — and a TERM still ends it with .done 143', async (t) => {
  const series = provisioned();
  const { handle } = await running(series, 'n-v2', 'idle', { nohup: true });
  process.kill(-handle.pgid, 'SIGHUP');
  await sleep(1500);
  const ended = !alive({ pid: handle.pid, start: handle.start });
  t.diagnostic(ended ? 'nohup + bash: the HUP trap RAN (the run ended)' : 'nohup + bash: the HUP was IGNORED (the run went on)');
  if (ended) {
    assert.equal(existsSync(logs(series, 'n-v2', 'done')), true, 'a HUP ended a nohup-started run without .done — the poll would wait for ever');
    assert.equal(readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim(), '129');
  } else {
    process.kill(-handle.pgid, 'SIGTERM');
    await waitFor('a TERM did not end a run script started through nohup', hasFile(logs(series, 'n-v2', 'done')));
    assert.equal(readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim(), '143');
  }
  endGroup(series, 'n-v2');
});

/** Replaces `from` by `to` in a script's text; a mutation that no longer applies is a failure of the TEETH test, never a silent no-op. */
function mutated(text, from, to) {
  assert.ok(text.includes(from), `the mutation no longer applies — the script has no line: ${from}`);
  return text.split(from).join(to);
}

// One scenario, three claims: the abort over a TERM-resistant child, a setsid child and an unrelated sentinel.
let realAbortScenario;
const abortOverWorkloads = () => (realAbortScenario ??= workloadsThenAbort());

/** The abort scenario: workloads started, a sentinel apart, then abort.sh (the real one, or a mutant's text) run over them. */
async function workloadsThenAbort(abortText = '') {
  const series = provisioned('n-v2', { abortText });
  const sentinel = spawn('/bin/sleep', ['600'], { detached: true, stdio: 'ignore' });
  sentinel.on('error', () => {});
  const sentinelId = track(sentinel.pid);
  const { handle, ready } = await running(series, 'n-v2', 'workloads');
  const term = track(ready.term);
  const setsid = track(ready.setsid);
  const leader = { pid: handle.pid, start: handle.start };
  const before = { term: alive(term), setsid: alive(setsid), leader: alive(leader), sentinel: alive(sentinelId), sameGroup: procStat(ready.term)?.pgrp === handle.pgid, otherGroup: procStat(ready.setsid)?.pgrp !== handle.pgid };
  const result = spawnSync('/bin/bash', ['--', join(series, 'run', 'abort.sh'), 'n-v2'], { env: scriptEnv(), encoding: 'utf8', timeout: 90_000 });
  return { series, before, result, term, setsid, leader, sentinelId };
}

linuxTest('the dummy workloads are what the abort scenario says: a TERM-resistant child IN the group, a setsid child OUT of it, a sentinel apart', async () => {
  const { before } = await abortOverWorkloads();
  assert.deepEqual(before, { term: true, setsid: true, leader: true, sentinel: true, sameGroup: true, otherGroup: true }, 'the scenario did not start the workloads it claims — the abort cases below would prove nothing');
});

linuxTest('abort.sh ends the TERM-resistant child by escalating on the GROUP — "the abort left the TERM-resistant child alive — it escalated on the leader, not the group"', async () => {
  const { result, term } = await abortOverWorkloads();
  assert.equal(alive(term), false, 'the abort left the TERM-resistant child alive — it escalated on the leader, not the group');
  assert.match(result.stdout, /KILL to group/u, `the group was never KILLed: ${result.stdout}`);
});

linuxTest('abort.sh ends the setsid child through its snapshot — "the abort left the setsid child alive — it left the group and nothing else found it"', async () => {
  const { result, setsid } = await abortOverWorkloads();
  assert.equal(alive(setsid), false, 'the abort left the setsid child alive — it left the group and nothing else found it');
  assert.match(result.stdout, /outside the group/u, `no owned pid outside the group was signalled: ${result.stdout}`);
});

linuxTest('abort.sh leaves the unrelated sentinel alone, reports "survived: none", and the phase still has .handle, .exit and .done', async () => {
  const { series, result, sentinelId, leader } = await abortOverWorkloads();
  assert.equal(alive(sentinelId), true, 'the abort ended a process that was not its own — the sentinel');
  assert.equal(result.status, 0, `abort.sh exited ${result.status}: ${result.stdout}${result.stderr}`);
  assert.match(result.stdout, /survived: none/u);
  assert.equal(alive(leader), false);
  for (const ext of ['handle', 'exit', 'done']) assert.equal(existsSync(logs(series, 'n-v2', ext)), true, `the abort left no .${ext}`);
  assert.equal(readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim(), '143');
});

linuxTest('a .handle whose start time was rewritten: abort.sh signals NOTHING and says so; the dummy still lives', async () => {
  const series = provisioned();
  const { handle, ready } = await running(series, 'n-v2', 'workloads');
  const term = track(ready.term);
  track(ready.setsid);
  const file = logs(series, 'n-v2', 'handle');
  writeFileSync(file, `${handle.pid} ${handle.pgid} ${Number(handle.start) + 1}\n`);
  const result = spawnSync('/bin/bash', ['--', join(series, 'run', 'abort.sh'), 'n-v2'], { env: scriptEnv(), encoding: 'utf8', timeout: 60_000 });
  assert.equal(result.status, 4, `${result.stdout}${result.stderr}`);
  assert.match(result.stdout, /nothing signalled/u);
  assert.ok(alive({ pid: handle.pid, start: handle.start }), 'the run script was signalled although its start time did not match');
  assert.ok(alive(term), 'the child was signalled although the handle did not match');
  assert.equal(existsSync(logs(series, 'n-v2', 'done')), false);
  writeFileSync(file, `${handle.pid} ${handle.pgid} ${handle.start}\n`); // the test ends it by its own, true handle
  endGroup(series, 'n-v2');
});

linuxTest('abort.sh refuses a phase that is not [a-z0-9-]{1,32} and a series whose SERIES_PATH names another folder — nothing is read or signalled', () => {
  const series = provisioned();
  const run = (phase) => spawnSync('/bin/bash', ['--', join(series, 'run', 'abort.sh'), phase], { env: scriptEnv(), encoding: 'utf8', timeout: 30_000 });
  assert.equal(run('n-v2;rm').status, 65);
  assert.equal(run('../x').status, 65);
  assert.equal(run('').status, 65);
  writeFileSync(join(series, 'SERIES_PATH'), `${bareSeries()}\n`);
  assert.equal(run('n-v2').status, 78);
});

linuxTest('a hostile path.txt is DATA: the rendered text holds none of it, nothing in it runs, and the decoy node earlier on that PATH is never run — node.txt\'s absolute path is', async () => {
  const series = provisioned();
  const decoyDir = join(ROOT, `decoy-${randomBytes(2).toString('hex')}`);
  const touched = join(ROOT, `touched-${randomBytes(2).toString('hex')}`);
  mkdirSync(decoyDir);
  writeFileSync(join(decoyDir, 'node'), `#!/bin/sh\ntouch '${touched}.decoy'\n`, { mode: 0o755 });
  const before = render(series, 'n-v2', 'idle');
  const hostile = `${decoyDir}:/usr/bin:/bin;$(touch ${touched}.sub)\`touch ${touched}.tick\``;
  writeFileSync(join(series, 'run', 'path.txt'), `${hostile}\n`);
  const after = render(series, 'n-v2', 'idle');
  assert.equal(after, before, 'the rendered script depends on path.txt');
  for (const piece of [touched, decoyDir, '$(touch', 'touch ' + touched]) assert.equal(after.includes(piece), false, `the script text holds ${piece} — path.txt reached the text`);
  launch(series, 'n-v2');
  await waitFor('the dummy probe did not start', hasFile(join(series, 'n-v2', 'ready.json')));
  const info = readyOf(series, 'n-v2');
  assert.equal(info.path, hostile, 'the probe did not receive the host PATH verbatim, as data');
  assert.equal(info.execPath, NODE, "the run script ran something other than node.txt's absolute path");
  for (const suffix of ['decoy', 'sub', 'tick']) assert.equal(existsSync(`${touched}.${suffix}`), false, `a ${suffix} marker was written — host data was executed`);
  endGroup(series, 'n-v2');
});

// ---------------------------------------------------------------- make-series.sh
/** A process whose exe lies under <home>/.vscode-server/ and whose environment holds the given PATH — the "VS Code server". */
function fakeServer(home, pathValue) {
  const dir = join(home, '.vscode-server', 'bin');
  mkdirSync(dir, { recursive: true });
  const exe = join(dir, 'server-node');
  copyFileSync('/bin/sleep', exe);
  chmodSync(exe, 0o755);
  const child = spawn(exe, ['600'], { env: { PATH: pathValue, SECRET_OTHER: 'must-not-be-kept' }, stdio: 'ignore' });
  child.on('error', () => {});
  return track(child.pid);
}

function makeSeries(series, home, text = readFileSync(join(NATIVE, 'make-series.sh'), 'utf8')) {
  writeFileSync(join(series, 'make-series.sh'), text);
  return spawnSync('/bin/bash', ['--', join(series, 'make-series.sh')], { env: scriptEnv({ HOME: home }), encoding: 'utf8', timeout: 60_000 });
}

/** A server whose PATH is `pathValue`, then make-series.sh over a fresh series: the result of spawnSync. */
function withPath(pathValue, text) {
  const series = bareSeries();
  const home = fakeHome();
  fakeServer(home, pathValue);
  return makeSeries(series, home, text);
}

function pathDirs() {
  const clean = join(ROOT, `clean-${randomBytes(2).toString('hex')}`);
  const wide = join(ROOT, `wide-${randomBytes(2).toString('hex')}`);
  mkdirSync(clean);
  chmodSync(clean, 0o755);
  mkdirSync(wide);
  chmodSync(wide, 0o777);
  symlinkSync(NODE, join(clean, 'node'));
  return { clean, wide };
}

linuxTest('make-series.sh with a clean PATH: a private tree, SERIES_PATH, ONLY the PATH value in path.txt, node.txt an absolute realpath', () => {
  const series = bareSeries();
  const home = fakeHome();
  const { clean } = pathDirs();
  const serverPath = `${clean}:/usr/bin:/bin`;
  fakeServer(home, serverPath);
  const r = makeSeries(series, home);
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  assert.equal(readFileSync(join(series, 'SERIES_PATH'), 'utf8'), `${series}\n`);
  assert.equal(readFileSync(join(series, 'run', 'path.txt'), 'utf8'), `${serverPath}\n`, 'path.txt is the PATH value alone');
  assert.equal(readFileSync(join(series, 'run', 'node.txt'), 'utf8'), `${NODE}\n`);
  for (const sub of ['checkout', 'tmp', 'run', 'logs', 'bin']) assert.equal((statSync(join(series, sub)).mode & 0o777).toString(8), '700', sub);
  assert.equal(`${r.stdout}${r.stderr}`.includes('must-not-be-kept'), false, 'another variable of the server leaked into the output');
  assert.equal(readFileSync(join(series, 'run', 'path.txt'), 'utf8').includes('must-not-be-kept'), false);
});

for (const [name, entryOf, mention] of [
  ['an empty entry', () => '', /PATH entry 0 '' is an empty entry (= the current directory)/u],
  ['the entry "."', () => '.', /PATH entry 0 \. is a relative entry/u],
  ['a relative entry', () => 'rel/bin', /PATH entry 0 rel\/bin is a relative entry/u],
  ['a mode-777 directory', (dirs) => dirs.wide, /world-writable \(mode 777\)/u],
]) {
  linuxTest(`make-series.sh refuses with 77, naming the entry, a PATH with ${name} before node's directory`, () => {
    const series = bareSeries();
    const home = fakeHome();
    const dirs = pathDirs();
    fakeServer(home, `${entryOf(dirs)}:${dirs.clean}:/usr/bin:/bin`);
    const r = makeSeries(series, home);
    assert.equal(r.status, 77, `a relative, empty or world-writable PATH entry before node's was accepted: ${r.stdout}${r.stderr}`);
    assert.match(r.stderr, mention);
    assert.equal(existsSync(join(series, 'run', 'node.txt')), false, 'node.txt was written for a PATH that was refused');
  });
}

linuxTest('make-series.sh ignores a bad entry AFTER the directory every used tool resolved to — only the entries a root-run step could resolve through are judged', () => {
  const series = bareSeries();
  const home = fakeHome();
  const { clean, wide } = pathDirs();
  fakeServer(home, `${clean}:${wide}::`);
  const r = makeSeries(series, home);
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
});

linuxTest('make-series.sh without a VS Code server process exits 69 and says Remote-SSH must be connected', () => {
  const series = bareSeries();
  const r = makeSeries(series, fakeHome());
  assert.equal(r.status, 69);
  assert.match(r.stderr, /Remote-SSH must be connected/u);
});

linuxTest('make-series.sh refuses a place that is wrong: a name off the shape, a mode other than 700, a folder outside the root, a symlinked folder', () => {
  const home = fakeHome();
  const offShape = join(ROOT, 'testsToDelete-native-other-20261021-a1b2c3');
  mkdirSync(offShape, { mode: 0o700 });
  assert.equal(makeSeries(offShape, home).status, 78);

  const wideMode = bareSeries();
  chmodSync(wideMode, 0o755);
  assert.equal(makeSeries(wideMode, home).status, 78);

  const elsewhere = bareSeries();
  copyFileSync(join(NATIVE, 'make-series.sh'), join(elsewhere, 'make-series.sh'));
  const outside = spawnSync('/bin/bash', ['--', join(elsewhere, 'make-series.sh')], { env: scriptEnv({ HOME: home, COAI_NATIVE_SERIES_ROOT: join(ROOT, 'not-the-root') }), encoding: 'utf8', timeout: 30_000 });
  assert.equal(outside.status, 78);

  const target = bareSeries();
  copyFileSync(join(NATIVE, 'make-series.sh'), join(target, 'make-series.sh'));
  const link = join(ROOT, `link-${randomBytes(2).toString('hex')}`);
  symlinkSync(target, link);
  const viaLink = spawnSync('/bin/bash', ['--', join(link, 'make-series.sh')], { env: scriptEnv({ HOME: home }), encoding: 'utf8', timeout: 30_000 });
  assert.equal(viaLink.status === 0, false, 'a series reached through a symlink was prepared');
});

// ---------------------------------------------------------------- remove-series.sh
const removeSeries = (series, arg, home) => spawnSync('/bin/bash', ['--', join(series, 'run', 'remove-series.sh'), arg], { env: scriptEnv({ HOME: home }), encoding: 'utf8', timeout: 30_000 });
const signIns = (home) => ['.vibe/.env', '.kimi-code/credentials/kimi-code-0123456789abcdef.json'].map((p) => { const s = statSync(join(home, p)); return [p, s.size, s.mode, s.mtimeMs]; });

linuxTest('remove-series.sh refuses a SERIES_PATH that names another folder or does not match the shape — nothing is removed', () => {
  const home = fakeHome();
  const series = provisioned();
  const other = bareSeries();
  writeFileSync(join(series, 'SERIES_PATH'), `${other}\n`);
  let r = removeSeries(series, series, home);
  assert.equal(r.status, 78, `${r.stdout}`);
  assert.equal(existsSync(series), true);
  assert.equal(existsSync(other), true);
  writeFileSync(join(series, 'SERIES_PATH'), '/tmp/anything\n');
  r = removeSeries(series, series, home);
  assert.equal(r.status, 78);
  writeFileSync(join(series, 'SERIES_PATH'), `${series}/..\n`);
  assert.equal(removeSeries(series, series, home).status, 78);
  writeFileSync(join(series, 'SERIES_PATH'), `${series}\n`);
  assert.equal(removeSeries(series, other, home).status, 78, 'the folder given differs from the one SERIES_PATH names');
  assert.equal(existsSync(series), true);
  assert.equal(existsSync(other), true);
});

linuxTest('remove-series.sh removes the folder AND its own file, still prints step 5, lists the symlinks, and leaves the sign-ins stat-identical', () => {
  const home = fakeHome();
  const series = provisioned();
  symlinkSync('/nonexistent-target', join(series, 'run', 'a-link'));
  const before = signIns(home);
  const r = removeSeries(series, series, home);
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  assert.equal(existsSync(series), false, 'the folder is still there');
  assert.match(r.stdout, /a-link -> \/nonexistent-target/u, 'step 2 did not list the symlink');
  assert.match(r.stdout, /step 4: removed/u);
  assert.match(r.stdout, /step 5: the folder is gone/u, "the script stopped at step 4 — step 5, after the rm, never ran");
  assert.match(r.stdout, /step 5: the sign-in facts are identical/u);
  assert.equal(r.stdout.includes('0123456789abcdef'), false, "the credential slot's hex reached the output");
  assert.deepEqual(signIns(home), before);
});

// ---------------------------------------------------------------- TEETH — the break-its of E1.S1 (B1, B2, B3, B5), kept as tests
// The plan asks for each guarantee to be seen red: change the PRODUCTION script, watch the case fail, restore. Here the change is made
// to a COPY of the script's text, in the test, every run — so the red is never a claim in a commit message but a case that passes only
// while the mutant is broken, and a refactor that moves the mutated line fails loudly ("the mutation no longer applies"). Each asserts
// the very condition the matching case above would print as its failure.
const nativeText = (file) => readFileSync(join(NATIVE, file), 'utf8');

linuxTest('TEETH (B1): a run script without the TERM, INT and HUP traps records a TERMed run as exit 0, not 143 — bash still runs the EXIT trap on a fatal signal, but with the status of the last command', async (t) => {
  const series = provisioned();
  const dropTraps = (text) => mutated(text, "trap 'exit 143' TERM; trap 'exit 130' INT; trap 'exit 129' HUP", ':');
  const { handle } = await running(series, 'n-v2', 'idle', { mutate: dropTraps });
  process.kill(-handle.pgid, 'SIGTERM');
  await waitFor('the run script did not end at all on a group TERM', () => !alive({ pid: handle.pid, start: handle.start }));
  await sleep(500);
  const recorded = existsSync(logs(series, 'n-v2', 'exit')) ? readFileSync(logs(series, 'n-v2', 'exit'), 'utf8').trim() : '(no .exit)';
  t.diagnostic(`without the signal traps a group TERM left .exit = ${recorded}, .done = ${existsSync(logs(series, 'n-v2', 'done'))}`);
  assert.notEqual(recorded, '143', 'the mutant recorded 143 anyway, so the signal cases above (which assert .exit 143 / 130 / 129) cannot go red');
  endGroup(series, 'n-v2');
});

linuxTest('TEETH (B2): an abort that escalates while the LEADER exists, not the group, leaves the TERM-resistant child alive — "the abort left the TERM-resistant child alive — it escalated on the leader, not the group"', async () => {
  const leaderOnly = mutated(nativeText('abort.sh'), 'pending() { scan; [ -n "$(group_members)" ] || [ -n "$(owned_alive)" ]; }', 'pending() { scan; [ -n "${START_OF[$PID]:-}" ] && [ "${STATE_OF[$PID]:-Z}" != Z ]; }');
  const { term, before } = await workloadsThenAbort(leaderOnly);
  assert.equal(before.term, true);
  assert.equal(alive(term), true, 'the mutant ended the TERM-resistant child anyway, so the group-escalation case above cannot go red');
});

linuxTest('TEETH (B3): an abort that takes no snapshot of descendants leaves the setsid child alive — "the abort left the setsid child alive — it left the group and nothing else found it"', async () => {
  const noDescendants = mutated(nativeText('abort.sh'), 'while [ "$changed" = 1 ]; do', 'while [ "$changed" = 1 ] && false; do');
  const { setsid, before } = await workloadsThenAbort(noDescendants);
  assert.equal(before.setsid, true);
  assert.equal(alive(setsid), true, 'the mutant ended the setsid child anyway, so the snapshot case above cannot go red');
});

linuxTest('TEETH (B5): a make-series.sh that does not judge a relative PATH entry accepts it — "a relative, empty or world-writable PATH entry before the node directory was accepted"', () => {
  const blind = mutated(nativeText('make-series.sh'), 'elif [ "${ENTRY#/}" = "$ENTRY" ]; then\n    WHY="a relative entry"\n  ', '');
  const series = bareSeries();
  const home = fakeHome();
  const dirs = pathDirs();
  fakeServer(home, `rel/bin:${dirs.clean}:/usr/bin:/bin`);
  const r = makeSeries(series, home, blind);
  assert.equal(r.status, 0, `the mutant still refused (${r.status}), so the relative-entry cases above cannot go red: ${r.stderr}`);
});

// ---------------------------------------------------------------- the PATH audit, second round (code gate G3, G4, G13; judge J6)
const spacedNodeDir = () => {
  const dir = join(ROOT, `node dir ${randomBytes(2).toString('hex')}`);
  mkdirSync(dir, { mode: 0o755 });
  chmodSync(dir, 0o755);
  symlinkSync(NODE, join(dir, 'node'));
  return dir;
};

/** A folder holding an executable `name`; `mode` is the folder's own mode. */
function dirWith(name, mode = 0o755) {
  const dir = join(ROOT, `with-${name}-${randomBytes(2).toString('hex')}`);
  mkdirSync(dir);
  chmodSync(dir, mode);
  writeFileSync(join(dir, name), '#!/bin/sh\nexit 0\n', { mode: 0o755 });
  return dir;
}

/** A folder `mode` holding a symlink `link` to `target`; returns the symlink's path. */
function linkIn(mode, target) {
  const folder = join(ROOT, `holder-${randomBytes(2).toString('hex')}`);
  mkdirSync(folder);
  chmodSync(folder, mode);
  const link = join(folder, 'link');
  symlinkSync(target, link);
  return link;
}

linuxTest('G3: a node directory with a SPACE in its name works end to end — make-series.sh resolves it, node.txt holds the absolute realpath, and the run script runs exactly that', async () => {
  const series = bareSeries();
  const home = fakeHome();
  const spaced = spacedNodeDir();
  fakeServer(home, `${spaced}:/usr/bin:/bin`);
  const r = makeSeries(series, home);
  assert.equal(r.status, 0, `a node directory with a space was refused or broke the script: ${r.stdout}${r.stderr}`);
  assert.equal(readFileSync(join(series, 'run', 'node.txt'), 'utf8'), `${NODE}\n`);

  const run = provisioned();
  writeFileSync(join(run, 'run', 'node.txt'), `${join(spaced, 'node')}\n`); // the path WITH the space, as a rendered script reads it
  await running(run, 'n-v2', 'idle');
  assert.equal(readyOf(run, 'n-v2').execPath, NODE, 'the run script did not run the node whose path holds a space');
  endGroup(run, 'n-v2');
});

linuxTest('G4: an EMPTY entry is named "empty (= the current directory)" — not "missing" — and refused; a missing absolute entry is refused as missing', () => {
  const dirs = pathDirs();
  const empty = withPath(`:${dirs.clean}:/usr/bin:/bin`);
  assert.equal(empty.status, 77, `${empty.stdout}${empty.stderr}`);
  assert.match(empty.stderr, /PATH entry 0 '' is an empty entry \(= the current directory\)/u);
  assert.doesNotMatch(empty.stderr, /missing/u, 'an empty entry was reported as a missing folder');

  const gone = `${ROOT}/no-such-${randomBytes(2).toString('hex')}`;
  const missing = withPath(`${gone}:${dirs.clean}:/usr/bin:/bin`);
  assert.equal(missing.status, 77, `${missing.stdout}${missing.stderr}`);
  assert.match(missing.stderr, new RegExp(`PATH entry 0 ${gone} is missing`, 'u'));
});

linuxTest('G4: only the entries up to the folder a used tool resolves to are judged — a missing and an empty entry AFTER node\'s folder are ignored', () => {
  const dirs = pathDirs();
  const r = withPath(`${dirs.clean}:${ROOT}/no-such-after::`);
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
});

linuxTest('J6: a group-writable folder before node\'s is refused (the group is not root\'s)', () => {
  const dirs = pathDirs();
  const group = join(ROOT, `group-${randomBytes(2).toString('hex')}`);
  mkdirSync(group);
  chmodSync(group, 0o775);
  const r = withPath(`${group}:${dirs.clean}:/usr/bin:/bin`);
  assert.equal(r.status, 77, `${r.stdout}${r.stderr}`);
  assert.match(r.stderr, /group-writable \(mode 775, group \d+\)/u);
});

for (const tool of ['git', 'python3']) {
  linuxTest(`J6: ${tool} is a USED tool — a world-writable folder before the one ${tool} resolves to is refused, and ignored when ${tool} is nowhere on the PATH`, () => {
    const { clean, wide } = pathDirs();
    const refused = withPath(`${clean}:${wide}:${dirWith(tool)}`);
    assert.equal(refused.status, 77, `${tool} is not in the audited tool list, so the cut-off stopped before the world-writable folder: ${refused.stdout}${refused.stderr}`);
    assert.match(refused.stderr, /world-writable \(mode 777\)/u);
    const other = withPath(`${clean}:${wide}:${dirWith('not-a-tool')}`);
    assert.equal(other.status, 0, `the control: no ${tool} anywhere, so nothing reaches the world-writable folder: ${other.stdout}${other.stderr}`);
  });
}

linuxTest('G13: a symlinked entry whose FOLDER is mode 777 is refused naming the entry — the link can be repointed by anyone — though its target is clean', () => {
  const { clean } = pathDirs();
  const link = linkIn(0o777, clean);
  const r = withPath(`${link}:/usr/bin:/bin`);
  assert.equal(r.status, 77, `a symlink in a world-writable folder was accepted: ${r.stdout}${r.stderr}`);
  assert.match(r.stderr, new RegExp(`PATH entry 0 ${link} is a link whose folder .* is world-writable \\(mode 777\\)`, 'u'));
});

linuxTest('G13: a symlinked entry in a SAFE folder whose target is safe is accepted (Ubuntu\'s /bin -> usr/bin is one); one whose TARGET is world-writable is refused', () => {
  const { clean, wide } = pathDirs();
  const fine = withPath(`${linkIn(0o755, clean)}:/usr/bin:/bin`);
  assert.equal(fine.status, 0, `a symlink in a safe folder with a safe target was refused: ${fine.stdout}${fine.stderr}`);
  const link = linkIn(0o755, wide);
  const bad = withPath(`${link}:${clean}:/usr/bin:/bin`);
  assert.equal(bad.status, 77, `${bad.stdout}${bad.stderr}`);
  assert.match(bad.stderr, new RegExp(`PATH entry 0 ${link} is a link to ${wide}, which is world-writable \\(mode 777\\)`, 'u'));
});

linuxTest('TEETH (G13): a make-series.sh that judges only the link\'s TARGET (not the folder that holds the link) accepts a symlink in a mode-777 folder', () => {
  const blind = mutated(nativeText('make-series.sh'), 'if [ -n "$HOLDER" ]; then', 'if false; then');
  const { clean } = pathDirs();
  const r = withPath(`${linkIn(0o777, clean)}:/usr/bin:/bin`, blind);
  assert.equal(r.status, 0, `the mutant still refused (${r.status}), so the symlink-folder case above cannot go red: ${r.stderr}`);
});

linuxTest('TEETH (J6): a make-series.sh whose tool list lacks git and python3 stops auditing before the world-writable folder', () => {
  const old = mutated(nativeText('make-series.sh'), 'for TOOL in node vibe kimi rg fd git python3; do', 'for TOOL in node vibe kimi rg fd; do');
  const { clean, wide } = pathDirs();
  const r = withPath(`${clean}:${wide}:${dirWith('git')}`, old);
  assert.equal(r.status, 0, `the mutant still refused (${r.status}), so the git case above cannot go red: ${r.stderr}`);
});

// ---------------------------------------------------------------- abort.sh, second round (code gate G5; judge J3)
const abortOf = (series) => spawnSync('/bin/bash', ['--', join(series, 'run', 'abort.sh'), 'n-v2'], { env: scriptEnv(), encoding: 'utf8', timeout: 60_000 });
const writeHandle = (series, pid, pgid, start) => writeFileSync(logs(series, 'n-v2', 'handle'), `${pid} ${pgid} ${start}\n`);
/** A process the test owns, apart from every group of the run: its own session and group, so its pid is its pgid. */
function bystander() {
  const child = spawn('/bin/sleep', ['600'], { detached: true, stdio: 'ignore' });
  child.on('error', () => {});
  return track(child.pid);
}

linuxTest('J3: a handle whose pid is NOT its process-group id signals nothing — a live leader plus another group\'s id would otherwise end that whole group', async () => {
  const series = provisioned();
  const { handle } = await running(series, 'n-v2', 'idle');
  const other = bystander();
  writeHandle(series, handle.pid, other.pid, handle.start); // the leader is real and its start time matches; the group id is somebody else's
  const r = abortOf(series);
  assert.equal(r.status, 4, `a handle with pid != pgid was acted on — it signalled a group the run script never owned: ${r.stdout}${r.stderr}`);
  assert.match(r.stdout + r.stderr, /nothing signalled/u);
  assert.ok(alive(other), 'the bystander in the named group was ended');
  assert.ok(alive({ pid: handle.pid, start: handle.start }), 'the run script was ended by a refused abort');
  writeHandle(series, handle.pid, handle.pgid, handle.start); // the test ends it by its own, true handle
  endGroup(series, 'n-v2');
});

linuxTest('J3: a process-group id of 1 or 0 — even with pid equal to it — is never ours: nothing is signalled', async () => {
  const series = provisioned();
  const { handle } = await running(series, 'n-v2', 'idle');
  for (const id of [0, 1]) {
    writeHandle(series, id, id, handle.start);
    const r = abortOf(series);
    assert.equal(r.status, 4, `a handle naming pgid ${id} was acted on: ${r.stdout}${r.stderr}`);
  }
  assert.ok(alive({ pid: handle.pid, start: handle.start }));
  writeHandle(series, handle.pid, handle.pgid, handle.start);
  endGroup(series, 'n-v2');
});

linuxTest('J3: the leader alive with a matching start time but NOT leading the named group (its current pgrp differs) — nothing is signalled', () => {
  const series = provisioned();
  const sleeper = spawn('/bin/sleep', ['600'], { stdio: 'ignore' }); // same group as the test runner: its pid is not its pgrp
  sleeper.on('error', () => {});
  const id = track(sleeper.pid);
  assert.notEqual(procStat(id.pid)?.pgrp, id.pid, 'the scenario needs a process that leads no group');
  writeHandle(series, id.pid, id.pid, id.start);
  const r = abortOf(series);
  assert.equal(r.status, 4, `a leader that no longer leads the group was acted on: ${r.stdout}${r.stderr}`);
  assert.ok(alive(id), 'the process was ended although it was not the group leader the handle claimed');
});

linuxTest('J3: the leader gone and a group member that STARTED EARLIER than the recorded leader (a reused group id) — nothing is signalled, exit 4', async () => {
  const series = provisioned();
  const leader = spawn('/usr/bin/setsid', ['/bin/sh', '-c', '/bin/sleep 600 & exit 0'], { stdio: 'ignore' });
  leader.on('error', () => {});
  await new Promise((resolve) => leader.once('exit', resolve));
  const member = readdirSync('/proc').filter((n) => /^\d+$/u.test(n)).map(Number).find((pid) => procStat(pid)?.pgrp === leader.pid);
  assert.ok(member !== undefined, 'the scenario did not leave a member in the dead leader\'s group');
  const id = track(member);
  writeHandle(series, leader.pid, leader.pid, Number(id.start) + 100_000); // a "leader" that started long AFTER the member
  const r = abortOf(series);
  assert.equal(r.status, 4, `${r.stdout}${r.stderr}`);
  assert.match(r.stdout, /started before the recorded leader/u);
  assert.ok(alive(id), 'a member older than the recorded leader was signalled — the group id had been reused');
});

test('G5: no table-key expansion of abort.sh is a bare "${!TABLE[@]}" — an EMPTY table would trip `set -u` on a bash older than 4.4; they all go through keys_of', () => {
  const text = nativeText('abort.sh');
  const bare = text.split('\n').filter((line) => line.includes('"${!') && !line.includes('printf'));
  assert.deepEqual(bare, [], 'a bare "${!TABLE[@]}" expansion of a possibly empty associative array');
  assert.match(text, /keys_of\(\)/u);
});

linuxTest('TEETH (J3): an abort.sh that does not require pid == pgid kills the bystander group a forged handle names', async () => {
  const lax = mutated(nativeText('abort.sh'), 'if [ "$PID" != "$PGID" ]; then', 'if false; then');
  const series = provisioned('n-v2', { abortText: lax });
  const { handle } = await running(series, 'n-v2', 'idle');
  const other = bystander();
  writeHandle(series, handle.pid, other.pid, handle.start);
  const r = abortOf(series);
  assert.equal(alive(other), false, `the mutant left the bystander alone (exit ${r.status}), so the pid == pgid case above cannot go red: ${r.stdout}`);
  writeHandle(series, handle.pid, handle.pgid, handle.start);
  endGroup(series, 'n-v2');
});
