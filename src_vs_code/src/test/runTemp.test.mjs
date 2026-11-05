import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { claimText, linuxStartSeconds, makeRunTemp, removeRunTemp } from '../../scripts/run-tests.mjs';

/**
 * The extension's test runner keeps everything its `node --test` children make in "temp" inside ONE folder of its own, and removes it
 * BEFORE it exits (`todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md`, D2/E2, scenarios X1-X3).
 *
 * <p>The folder is per invocation and is never the C# run's: two runners at once each have their own, and removing one leaves the other's.
 * It carries the family's owner claim, `<folder>.owner`, in the C# writer's exact format (`<host>@<os>:<pid>[:<start>]`, `os` being Node's
 * `process.platform`), so one sweeper can judge both.</p>
 *
 * <p>The privacy rule of these tests: nothing here touches the machine's real temp folder. Every folder is made under a PRIVATE root, and
 * the driver starts the runner with `TEMP`/`TMP`/`TMPDIR` pointed at it.</p>
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = path.join(here, '..', '..', 'scripts');
const repoRoot = path.join(here, '..', '..', '..');
const claimShape = /^[^@\s]+@(win32|linux|darwin):\d+(:\d+)?$/;
const prefix = 'testsToDelete-coai-run-';

function privateRoot() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'testsToDelete-runtemp-'));
}

function runFoldersIn(root) {
  return fs.readdirSync(root).filter((name) => name.startsWith(prefix));
}

test('X1 two runners get two folders and two claims, and removing one leaves the other', () => {
  const root = privateRoot();
  try {
    const a = makeRunTemp(root);
    const b = makeRunTemp(root);

    assert.notEqual(a, b, 'one folder per invocation');
    for (const folder of [a, b]) {
      assert.equal(path.dirname(folder), root);
      assert.match(path.basename(folder), /^testsToDelete-coai-run-[0-9a-f]{6}$/);
      assert.ok(fs.statSync(folder).isDirectory());
      assert.match(fs.readFileSync(`${folder}.owner`, 'utf8'), claimShape, 'the claim is the family\'s one-line format');
    }

    removeRunTemp(a);

    assert.equal(fs.existsSync(a), false, 'the removed run\'s folder is gone');
    assert.equal(fs.existsSync(`${a}.owner`), false, 'and its claim');
    assert.ok(fs.existsSync(b), 'the other runner\'s folder is untouched');
    assert.ok(fs.existsSync(`${b}.owner`), 'and its claim');

    assert.doesNotThrow(() => removeRunTemp(a), 'removing twice is not an error');
    assert.doesNotThrow(() => removeRunTemp(''), 'removing nothing is not an error');
    removeRunTemp(b);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('X1 the claim names this process, and carries a start only on Linux', () => {
  const root = privateRoot();
  try {
    const folder = makeRunTemp(root);
    const text = fs.readFileSync(`${folder}.owner`, 'utf8');
    const [, rest] = text.split('@');
    const [platform, pid, start] = rest.split(':');

    assert.equal(platform, process.platform, 'Node\'s own spelling: win32 / linux / darwin');
    assert.equal(pid, String(process.pid));
    assert.equal(start === undefined, process.platform !== 'linux', 'the start field is Linux\'s alone');
    assert.equal(text.includes('\n'), false, 'one line, no trailing newline');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('X1 the claim text and the Linux start are pure functions of their inputs', () => {
  assert.equal(claimText({ host: 'h', platform: 'linux', pid: 7, startSeconds: 1000 }), 'h@linux:7:1000');
  assert.equal(claimText({ host: 'h', platform: 'win32', pid: 7 }), 'h@win32:7');
  assert.equal(claimText({ host: 'a b@c:d', platform: 'darwin', pid: 7 }), 'a-b-c-d@darwin:7', 'a host name cannot hold the separators or whitespace');

  // /proc/self/stat: "<pid> (<comm>) <state> ..." — starttime is the 22nd field, in ticks of 1/100 s; the command may hold spaces and ")".
  const stat = `42 (a ) b) S ${Array.from({ length: 18 }, (_, i) => i).join(' ')} 12345 0 0`;
  assert.equal(linuxStartSeconds(stat, 'cpu 1 2 3\nbtime 1700000000\nprocs_running 1'), 1700000000 + 123);
  assert.equal(linuxStartSeconds('garbage', 'btime 1'), undefined, 'a stat that does not parse gives no start, never a wrong one');
  assert.equal(linuxStartSeconds(stat, 'no boot time here'), undefined);
});

/** A fixture tree for the runner: one passing and one failing compiled test, each printing its temp folder and a grandchild's. */
function fixtureTree(root) {
  const ext = path.join(root, 'ext');
  fs.mkdirSync(path.join(ext, 'out', 'test'), { recursive: true });
  fs.mkdirSync(path.join(ext, 'src', 'test'), { recursive: true });
  fs.mkdirSync(path.join(root, 'shared'), { recursive: true });
  fs.copyFileSync(path.join(repoRoot, 'shared', 'temp-sweep.json'), path.join(root, 'shared', 'temp-sweep.json'));
  const body = (outcome) => `
const fs = require('node:fs'); const os = require('node:os'); const { spawnSync } = require('node:child_process');
const test = require('node:test');
const note = (who) => fs.appendFileSync(process.env.SEEN, who + '|' + os.tmpdir() + '|' + fs.existsSync(os.tmpdir() + '.owner') + '\\n');
test('${outcome}', () => {
  note('child');
  spawnSync(process.execPath, ['-e', "require('node:fs').appendFileSync(process.env.SEEN, 'grandchild|' + require('node:os').tmpdir() + '\\\\n')"], { stdio: 'inherit' });
  ${outcome === 'fails' ? 'throw new Error("on purpose");' : ''}
});
`;
  fs.writeFileSync(path.join(ext, 'out', 'test', 'passes.test.js'), body('passes'));
  fs.writeFileSync(path.join(ext, 'out', 'test', 'fails.test.js'), body('fails'));
  return ext;
}

/** The runner as a CHILD of this test: `NODE_TEST_CONTEXT` is dropped, or its own `node --test` would see itself as nested and skip every file. */
function runTheRunner(cwd, machinesTemp, seen) {
  const env = { ...process.env };
      delete env.NODE_TEST_CONTEXT;
  return spawnSync(process.execPath, [path.join(scripts, 'run-tests.mjs')], {
    cwd,
    encoding: 'utf8',
    env: { ...env, TEMP: machinesTemp, TMP: machinesTemp, TMPDIR: machinesTemp, SEEN: seen },
    timeout: 120_000,
  });
}

test('X2 the runner\'s children and grandchildren share its folder, and it is gone, with its claim, when the runner exits — pass or fail', () => {
  const root = privateRoot();
  try {
    const ext = fixtureTree(root);
    const machinesTemp = path.join(root, 'machines-temp');
    fs.mkdirSync(machinesTemp);
    const seen = path.join(root, 'seen.txt');

    const result = runTheRunner(ext, machinesTemp, seen);

    assert.notEqual(result.status, 0, `one test fails, so the runner fails: ${result.stdout}${result.stderr}`);
    const lines = fs.readFileSync(seen, 'utf8').trim().split('\n').map((l) => l.split('|'));
    assert.equal(lines.filter(([who]) => who === 'child').length, 2, 'both tests ran');
    assert.equal(lines.filter(([who]) => who === 'grandchild').length, 2, 'both grandchildren ran');
    const folders = new Set(lines.map(([, tmp]) => tmp));
    assert.equal(folders.size, 1, `every process saw the same folder: ${[...folders].join(', ')}`);
    const [folder] = folders;
    assert.equal(path.dirname(folder), machinesTemp, 'inside the machine\'s temp (here: the private stand-in)');
    assert.match(path.basename(folder), /^testsToDelete-coai-run-[0-9a-f]{6}$/);
    assert.ok(lines.filter(([who]) => who === 'child').every(([, , claim]) => claim === 'true'), 'the claim existed while the children ran');

    assert.deepEqual(fs.readdirSync(machinesTemp).filter((n) => n.startsWith('testsToDelete-')), [], 'the folder and its claim are gone: removed BEFORE process.exit');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('X3 a runner that stops at once (no compiled tests) leaves no folder and no claim', () => {
  const root = privateRoot();
  try {
    const ext = path.join(root, 'ext');
    fs.mkdirSync(path.join(ext, 'src', 'test'), { recursive: true });
    fs.mkdirSync(path.join(root, 'shared'), { recursive: true });
    fs.copyFileSync(path.join(repoRoot, 'shared', 'temp-sweep.json'), path.join(root, 'shared', 'temp-sweep.json'));
    const machinesTemp = path.join(root, 'machines-temp');
    fs.mkdirSync(machinesTemp);

    const result = runTheRunner(ext, machinesTemp, path.join(root, 'seen.txt'));

    assert.equal(result.status, 1, `no compiled tests is the runner's own refusal: ${result.stdout}${result.stderr}`);
    assert.match(result.stderr, /no compiled test files/);
    assert.deepEqual(runFoldersIn(machinesTemp), [], 'the early exit still removes what the run made');
    assert.deepEqual(fs.readdirSync(machinesTemp), [], 'folder and claim');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// The code gate and judge of epic E2 (2026-10-20): one format, two writers, one future reader — and a folder is only ever the run's own.
// ---------------------------------------------------------------------------------------------
const vectors = JSON.parse(fs.readFileSync(path.join(repoRoot, 'shared', 'owner-claim-vectors.json'), 'utf8'));

test('G0/G1 the claim text is exactly what the shared golden vectors say (the C# writer reads the same file)', () => {
  for (const row of vectors.format) {
    assert.equal(
      claimText({ host: row.host, platform: row.os, pid: row.pid, startSeconds: row.start }),
      row.text,
      `the vector for ${row.os} host ${JSON.stringify(row.host)}`,
    );
  }
});

test('J2 the Linux start is the formula of the shared vectors (the C# side reads the same file)', () => {
  for (const row of vectors.linuxStart) {
    assert.equal(linuxStartSeconds(row.stat, row.procStat), row.startSeconds ?? undefined, row.stat);
  }
});

test('J1 this machine\'s host in the claim is os.hostname(), the spelling the sweeper compares', () => {
  const root = privateRoot();
  try {
    const folder = makeRunTemp(root);
    const host = fs.readFileSync(`${folder}.owner`, 'utf8').split('@')[0];

    assert.equal(host, claimText({ host: os.hostname(), platform: 'x', pid: 0 }).split('@')[0]);
    removeRunTemp(folder);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('G2/J4 a name that already exists is never shared: a new suffix is drawn, and the existing folder is left alone', () => {
  const root = privateRoot();
  try {
    const taken = path.join(root, `${prefix}aaaaaa`);
    fs.mkdirSync(taken);
    const draws = ['aaaaaa', 'aaaaaa', 'bbbbbb'];

    const folder = makeRunTemp(root, { suffix: () => draws.shift() });

    assert.equal(path.basename(folder), `${prefix}bbbbbb`, 'the colliding draws were skipped');
    assert.ok(fs.existsSync(`${folder}.owner`), 'the new folder is claimed');
    assert.equal(fs.existsSync(`${taken}.owner`), false, 'the folder that was already there is not claimed by this run');
    assert.deepEqual(draws, [], 'all three draws were used');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('G2/J4 when every draw collides the runner gives up with an error, and shares nothing', () => {
  const root = privateRoot();
  try {
    fs.mkdirSync(path.join(root, `${prefix}aaaaaa`));

    assert.throws(() => makeRunTemp(root, { suffix: () => 'aaaaaa', tries: 3 }), /free name|EEXIST/u);
    assert.equal(fs.existsSync(path.join(root, `${prefix}aaaaaa.owner`)), false);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('J3 a claim is removed only when its folder is gone: a folder that could not be removed keeps its claim', () => {
  const root = privateRoot();
  try {
    const folder = makeRunTemp(root);
    const failing = { ...fs, rmSync: (target, options) => { if (target === folder) { throw new Error('held by a scanner'); } return fs.rmSync(target, options); } };

    assert.doesNotThrow(() => removeRunTemp(folder, failing));

    assert.ok(fs.existsSync(folder), 'the folder is still there');
    assert.ok(fs.existsSync(`${folder}.owner`), 'so its claim stays: a claimless folder could only ever be collected by age');
    removeRunTemp(folder);
    assert.equal(fs.existsSync(`${folder}.owner`), false, 'once the folder goes, the claim goes');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
