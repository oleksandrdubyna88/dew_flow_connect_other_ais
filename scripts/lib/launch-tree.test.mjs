// node --test scripts/lib/launch-tree.test.mjs — runTree: a child started with a timeout whose expiry ends its WHOLE process tree
// (on Windows spawnSync's timeout ends only the direct child and its grandchildren live on). The children are `node -e` scripts
// this test starts; only the pids they report are ever ended, never an image name.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runTree } from './launch-tree.mjs';

const NODE = process.execPath;
const alive = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
};
const until = async (done, ms) => {
  const end = Date.now() + ms;
  while (Date.now() < end && !done()) await new Promise((r) => setTimeout(r, 100));
  return done();
};

test('a child that finishes: its stdin was delivered, stdout and stderr are captured, the exit code is kept, nothing timed out', async () => {
  const script = "let t='';process.stdin.on('data',c=>t+=c).on('end',()=>{process.stdout.write('got:'+t);process.stderr.write('warn');process.exit(3);});";
  const r = await runTree(NODE, ['-e', script], { input: 'hello', timeoutMs: 20_000, maxBuffer: 1024 * 1024 });
  assert.equal(r.status, 3);
  assert.equal(r.stdout, 'got:hello');
  assert.equal(r.stderr, 'warn');
  assert.equal(r.timedOut, false);
  assert.equal(r.error, '');
});

test('a timeout ends the grandchild too, and what the tree printed before it ended is kept', async () => {
  // On Windows the grandchild breaks away from the job Node puts its children in (as a CLI's helper can), so ending only the
  // direct child leaves it running; elsewhere it stays in the child's process group, which the group kill reaches.
  const script = "const {spawn}=require('node:child_process');"
    + "const g=spawn(process.execPath,['-e','setTimeout(()=>{},120000)'],{stdio:'ignore',windowsHide:true,detached:process.platform==='win32'});"
    + "process.stdout.write('GRANDCHILD '+g.pid+'\\n');setTimeout(()=>{},120000);";
  const r = await runTree(NODE, ['-e', script], { input: '', timeoutMs: 4_000, maxBuffer: 1024 * 1024 });
  const pid = Number(/GRANDCHILD (\d+)/.exec(r.stdout)?.[1]);
  try {
    assert.equal(r.timedOut, true);
    assert.ok(Number.isInteger(pid) && pid > 0, `the child reported a grandchild pid (stdout: ${JSON.stringify(r.stdout)})`);
    assert.equal(await until(() => !alive(pid), 10_000), true, `grandchild ${pid} is still running after the timeout`);
  } finally {
    if (alive(pid)) process.kill(pid);
  }
});

test('a program that cannot be started is a result with the error code, not a throw', async () => {
  const r = await runTree(`${NODE}.does-not-exist`, [], { input: '', timeoutMs: 5_000, maxBuffer: 1024 });
  assert.equal(r.status, null);
  assert.equal(r.timedOut, false);
  assert.match(r.error, /ENOENT/);
});

test('output past the buffer ends the tree, the grandchild too, and says so', async () => {
  const script = "const {spawn}=require('node:child_process');"
    + "const g=spawn(process.execPath,['-e','setTimeout(()=>{},120000)'],{stdio:'ignore',windowsHide:true,detached:process.platform==='win32'});"
    + "process.stdout.write('GRANDCHILD '+g.pid+'\\n');setInterval(()=>process.stdout.write('x'.repeat(4096)),10);";
  const r = await runTree(NODE, ['-e', script], { input: '', timeoutMs: 20_000, maxBuffer: 64 * 1024 });
  const pid = Number(/GRANDCHILD (\d+)/.exec(r.stdout)?.[1]);
  try {
    assert.equal(r.error, 'maxBuffer');
    assert.equal(r.timedOut, false);
    assert.ok(r.stdout.length >= 64 * 1024);
    assert.ok(Number.isInteger(pid) && pid > 0, 'the child reported a grandchild pid');
    assert.equal(await until(() => !alive(pid), 10_000), true, `grandchild ${pid} is still running after the overflow`);
  } finally {
    if (alive(pid)) process.kill(pid);
  }
});

test('what is kept stops at the buffer: a helper that outlives the kill and keeps writing cannot grow it', async () => {
  // The grandchild leads its own group, so the group kill misses it, and it holds the child's stdout.
  const script = "const {spawn}=require('node:child_process');"
    + "const g=spawn(process.execPath,['-e',\"setInterval(()=>process.stdout.write('y'.repeat(65536)),1)\"],{stdio:['ignore','inherit','inherit'],windowsHide:true,detached:true});"
    + "process.stdout.write('GRANDCHILD '+g.pid+'\\n');setInterval(()=>{},1000);";
  const maxBuffer = 64 * 1024;
  const r = await runTree(NODE, ['-e', script], { input: '', timeoutMs: 20_000, maxBuffer });
  const pid = Number(/GRANDCHILD (\d+)/.exec(r.stdout)?.[1]);
  try {
    assert.equal(r.error, 'maxBuffer');
    assert.ok(r.stdout.length + r.stderr.length <= maxBuffer + 4 * 65536, `kept ${r.stdout.length + r.stderr.length} chars`);
  } finally {
    if (alive(pid)) process.kill(pid);
  }
});

test('a helper still holding the pipes after the program ended is named, ended where it can be, and the result comes back', async () => {
  const script = "const {spawn}=require('node:child_process');"
    + "const g=spawn(process.execPath,['-e','setTimeout(()=>{},120000)'],{stdio:['ignore','inherit','inherit'],windowsHide:true});"
    + "process.stdout.write('GRANDCHILD '+g.pid+'\\n');process.exit(0);";
  const r = await runTree(NODE, ['-e', script], { input: '', timeoutMs: 20_000, maxBuffer: 1024 * 1024 });
  const pid = Number(/GRANDCHILD (\d+)/.exec(r.stdout)?.[1]);
  try {
    assert.equal(r.status, 0);
    assert.equal(r.error, 'heldPipes');
    assert.equal(r.timedOut, false);
    // POSIX: its group outlives the leader. Windows: a tree kill by an exited parent cannot reach it, which is why the probe stops.
    if (process.platform !== 'win32') assert.equal(await until(() => !alive(pid), 10_000), true, `helper ${pid} is still running`);
  } finally {
    if (alive(pid)) process.kill(pid);
  }
});

test('nothing is appended once the result has been given', async () => {
  const script = "const {spawn}=require('node:child_process');"
    + "const g=spawn(process.execPath,['-e',\"setInterval(()=>process.stdout.write('z'),20)\"],{stdio:['ignore','inherit','inherit'],windowsHide:true,detached:true});"
    + "process.stdout.write('GRANDCHILD '+g.pid+'\\n');process.exit(0);";
  const r = await runTree(NODE, ['-e', script], { input: '', timeoutMs: 20_000, maxBuffer: 1024 * 1024 });
  const pid = Number(/GRANDCHILD (\d+)/.exec(r.stdout)?.[1]);
  try {
    const kept = r.stdout;
    await new Promise((done) => setTimeout(done, 300));
    assert.equal(r.stdout, kept);
  } finally {
    if (alive(pid)) process.kill(pid);
  }
});
