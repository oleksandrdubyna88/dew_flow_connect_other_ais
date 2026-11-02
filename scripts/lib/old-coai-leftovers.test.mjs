// node --test scripts/lib/old-coai-leftovers.test.mjs — the one-off clean-up of old `coai-*` test leftovers (E5 S2-A, D4;
// todo/PLAN_test_leftovers_E5_S2_spec.md § A.3, S1–S16). Every case runs over a fresh private root, `now` fixed, the claim probe injected. CI runs it
// on Linux ("Probe script tests").
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { listOldLeftovers, removePicked, MOVE_DRAWS } from './old-coai-leftovers.mjs';
import * as lib from './old-coai-leftovers.mjs';
import { processProbe, thisOwner } from '../../.agents/conventions/tools/lib/owned.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const CLI = path.join(here, '..', 'old-coai-leftovers.mjs');
const LIB = path.join(here, 'old-coai-leftovers.mjs');
const NEVER_SWEPT = JSON.parse(fs.readFileSync(path.join(here, '..', '..', 'shared', 'temp-sweep.json'), 'utf8')).neverSwept;
const MINUTE = 60_000;
const now = Date.UTC(2026, 10, 2, 12, 0, 0);
const posix = process.platform !== 'win32';

/** A legacy claim of THIS machine (`<host>@<os>:<pid>[:<start>]`), the grammar the runners wrote. */
const claimOf = (pid, start) => `${thisOwner().host}:${pid}${start === undefined ? '' : `:${start}`}`;
/** The probe of the injected world: pid 99999 has no process, 88888 is alive, anything else cannot be asked. */
const probe = (pid) => (pid === 99999 ? { state: 'dead' } : pid === 88888 ? { state: 'live' } : { state: 'unknown' });
/** A legacy record is `unknown` for every Linux reader (it carries no pid namespace, rule §3); the seam reaches the other platforms' verdicts. */
const world = { probe, claimPlatform: 'win32' };

function privateRoot(t, prefix = 'old-coai-leftovers-') {
  const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), prefix)));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}

/** A directory `name` holding a `sentinel.txt`, last write `minutesAgo` before `now`. */
function make(root, name, minutesAgo, text = `still ${name}`) {
  const dir = path.join(root, name);
  fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'sentinel.txt'), text);
  age(dir, minutesAgo);
  return dir;
}

function age(target, minutesAgo) {
  const then = new Date(now - minutesAgo * MINUTE);
  fs.utimesSync(target, then, then);
}

const list = (root, options = {}) => listOldLeftovers({ tmp: root, neverSwept: NEVER_SWEPT, now, ...world, ...options });
const remove = (root, listing, picks, options = {}) => removePicked({ tmp: root, listing, picks, neverSwept: NEVER_SWEPT, ...world, ...options });
const byName = (results) => Object.fromEntries(results.map((r) => [r.name, r]));
const sentinel = (root, name) => fs.readFileSync(path.join(root, name, 'sentinel.txt'), 'utf8');

/** S1's root: two candidates and everything the listing must leave out. */
function s1Root(t) {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  make(root, 'coai-ctx-b', 3 * 24 * 60);
  make(root, 'coai-answers-x', 60);
  make(root, 'coai-chat-x', 60);
  make(root, 'coai-0.31.0', 60);
  fs.writeFileSync(path.join(root, 'coai-3.png'), 'a file');
  make(root, 'other-dir', 60);
  try {
    fs.symlinkSync(path.join(root, 'coai-panel-a'), path.join(root, 'coai-link'), 'dir');
  } catch { /* a system without the privilege lists no link, which is the case anyway */ }
  return root;
}

test('S1 the listing holds coai-* names with age and identity, and leaves out the product\'s, the cache, files and links', (t) => {
  const root = s1Root(t);

  const listing = list(root);

  assert.equal(listing.tmp, root, 'the listing records the root it read (S2-A gate finding 2)');
  assert.deepEqual(listing.entries.map((e) => e.name).sort(), ['coai-ctx-b', 'coai-panel-a']);
  const entries = byName(listing.entries);
  for (const [name, minutes] of [['coai-panel-a', 60], ['coai-ctx-b', 4320]]) {
    const stat = fs.lstatSync(path.join(root, name), { bigint: true });
    assert.equal(entries[name].kind, 'coai');
    assert.equal(entries[name].ageMinutes, minutes);
    assert.equal(entries[name].dev, String(stat.dev));
    assert.equal(entries[name].ino, String(stat.ino));
  }
});

/** S2's four legacy run folders, each with the claim its state needs. */
function legacyRuns(root) {
  const runs = { gone: 'testsToDelete-coai-run-abc123', live: 'testsToDelete-coai-run-def456', unknown: 'testsToDelete-coai-run-0a0a0a', none: 'testsToDelete-coai-run-112233' };
  const claims = { gone: claimOf(99999, 1700000000), live: claimOf(88888), unknown: 'box@win32:5' };
  for (const [state, name] of Object.entries(runs)) {
    make(root, name, 120);
    if (state !== 'none') fs.writeFileSync(path.join(root, `${name}.owner`), `${claims[state]}\n`);
  }
  return runs;
}

test('S2 a legacy run folder is listed with its claim state', (t) => {
  const root = privateRoot(t);
  const runs = legacyRuns(root);

  const entries = byName(list(root).entries);

  for (const [state, name] of Object.entries(runs)) {
    assert.equal(entries[name].kind, 'legacy-run', name);
    assert.equal(entries[name].claim, state, `${name} is ${state}`);
  }
  assert.equal(list(root, { claimPlatform: process.platform }).entries.find((e) => e.name === runs.gone).claim, posix && process.platform === 'linux' ? 'unknown' : 'gone',
    'on a Linux reader a legacy claim is never gone: it carries no pid namespace');
});

test('S3 a pick that is not in the listing is refused', (t) => {
  const root = s1Root(t);
  const listing = list(root);
  const before = fs.readdirSync(root).sort();

  const [result] = remove(root, listing, ['coai-ctx-c']);

  assert.deepEqual(result, { name: 'coai-ctx-c', outcome: 'refused', check: 'not-listed' });
  assert.deepEqual(fs.readdirSync(root).sort(), before, 'nothing on disk changed');
});

test('S4 a product prefix, a cache and a name of another shape are refused even when picked', (t) => {
  const root = s1Root(t);
  const listing = list(root);
  const identity = (name) => {
    const stat = fs.lstatSync(path.join(root, name), { bigint: true });
    return { name, kind: 'coai', lastWriteMs: now - 60 * MINUTE, ageMinutes: 60, dev: String(stat.dev), ino: String(stat.ino), mtimeNs: String(stat.mtimeNs) };
  };
  const edited = { ...listing, entries: [...listing.entries, identity('coai-chat-x'), identity('coai-0.31.0'), identity('other-dir')] };

  const results = remove(root, edited, ['coai-chat-x', 'coai-0.31.0', 'other-dir']);

  assert.deepEqual(results.map((r) => [r.outcome, r.check]), [['refused', 'protected'], ['refused', 'protected'], ['refused', 'protected']]);
  for (const name of ['coai-chat-x', 'coai-0.31.0', 'other-dir']) assert.equal(sentinel(root, name), `still ${name}`);
});

test('S5 a pick changed after the listing is refused', (t) => {
  const root = s1Root(t);
  const listing = list(root);
  age(path.join(root, 'coai-panel-a'), 5);

  const [result] = remove(root, listing, ['coai-panel-a']);

  assert.deepEqual([result.outcome, result.check], ['refused', 'changed']);
  assert.equal(sentinel(root, 'coai-panel-a'), 'still coai-panel-a');
});

test('S6 a pick swapped after the listing is refused and the swapped-in object keeps its bytes', (t) => {
  for (const variant of ['a new directory', 'a link']) {
    const root = privateRoot(t);
    make(root, 'coai-panel-a', 60);
    const listing = list(root);
    fs.renameSync(path.join(root, 'coai-panel-a'), path.join(root, 'aside'));
    if (variant === 'a new directory') make(root, 'coai-panel-a', 60, 'the swapped-in one');
    else fs.symlinkSync(path.join(root, 'aside'), path.join(root, 'coai-panel-a'), 'dir');
    let renamed = false;

    const [result] = remove(root, listing, ['coai-panel-a'], { hooks: { beforeRename: () => { renamed = true; } } });

    assert.deepEqual([result.outcome, result.check], ['refused', variant === 'a link' ? 'not-a-directory' : 'changed'], variant);
    assert.equal(renamed, false, `nothing was moved (${variant})`);
    assert.equal(sentinel(root, 'aside'), 'still coai-panel-a', 'the original is where the swap left it');
    if (variant === 'a new directory') assert.equal(sentinel(root, 'coai-panel-a'), 'the swapped-in one');
    else assert.ok(fs.lstatSync(path.join(root, 'coai-panel-a')).isSymbolicLink(), 'the link stays');
  }
});

test('S7 a legacy run folder with a live or unknown claim is refused; one with a gone claim goes with its claim file', (t) => {
  const root = privateRoot(t);
  const runs = legacyRuns(root);
  const listing = list(root);

  const results = byName(remove(root, listing, Object.values(runs)));

  assert.deepEqual([results[runs.live].outcome, results[runs.live].check], ['refused', 'owner-live']);
  assert.deepEqual([results[runs.unknown].outcome, results[runs.unknown].check], ['refused', 'owner-unknown']);
  assert.equal(results[runs.gone].outcome, 'removed');
  assert.equal(results[runs.none].outcome, 'removed');
  assert.deepEqual(fs.readdirSync(root).sort(), [runs.live, `${runs.live}.owner`, runs.unknown, `${runs.unknown}.owner`].sort(),
    'the live and unknown ones keep folder and claim; the gone one and the unclaimed one are gone, claim included');
});

test('S8 when every drawn quarantine name is taken the pick is refused and nothing is renamed', { skip: !posix && 'the placeholder is a POSIX shape' }, (t) => {
  for (const occupant of ['an empty directory', 'a file']) {
    const root = privateRoot(t);
    make(root, 'coai-panel-a', 60);
    const listing = list(root);
    const taken = path.join(root, '.fixed-quarantine');
    if (occupant === 'a file') fs.writeFileSync(taken, 'not a directory');
    else fs.mkdirSync(taken);
    const before = fs.lstatSync(taken, { bigint: true });
    let draws = 0;

    const [result] = remove(root, listing, ['coai-panel-a'], { hooks: { drawName: () => { draws += 1; return taken; } } });

    assert.deepEqual([result.outcome, result.check], ['refused', 'quarantine-taken'], occupant);
    assert.equal(draws, MOVE_DRAWS, 'a taken draw draws again, up to the bound');
    const after = fs.lstatSync(taken, { bigint: true });
    assert.deepEqual([after.dev, after.ino], [before.dev, before.ino], 'the pre-created object is untouched');
    assert.equal(sentinel(root, 'coai-panel-a'), 'still coai-panel-a');
    assert.deepEqual(fs.readdirSync(root).sort(), ['.fixed-quarantine', 'coai-panel-a'], 'no placeholder left behind');
  }
});

test('S9 an unchanged pick is removed through the quarantine name by identity', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  make(root, 'coai-ctx-b', 60);
  const listing = list(root);
  const drawn = [];
  const before = fs.readdirSync(root).sort();
  const hooks = {
    drawName: (dir) => {
      const name = path.join(path.dirname(dir), `.${path.basename(dir)}.teststodelete-quarantine-${process.pid}-0a1b2c3d`);
      drawn.push(path.basename(name));
      return name;
    },
    beforeRename: (placeholder) => {
      if (posix) assert.deepEqual(fs.readdirSync(placeholder), [], 'the placeholder exists and is empty when the rename is about to happen');
    },
  };

  const [result] = remove(root, listing, ['coai-panel-a'], { hooks });

  assert.equal(result.outcome, 'removed');
  assert.match(drawn[0], /^\.coai-panel-a\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$/);
  assert.deepEqual(fs.readdirSync(root).sort(), before.filter((n) => n !== 'coai-panel-a'), 'only the picked folder is gone, and no quarantine is left');
  assert.equal(sentinel(root, 'coai-ctx-b'), 'still coai-ctx-b');
});

function runCli(args, options = {}) {
  return spawnSync(process.execPath, [CLI, ...args], { encoding: 'utf8', ...options });
}

test('S10 the CLI: list writes the listing, remove refuses a usage error with the legal forms, exit codes 0/1/2', (t) => {
  const root = s1Root(t);
  const file = path.join(privateRoot(t, 'old-coai-listing-'), 'listing.json');

  const listed = runCli(['list', '--tmp', root, '--out', file]);

  assert.equal(listed.status, 0, listed.stderr);
  const json = JSON.parse(fs.readFileSync(file, 'utf8'));
  assert.equal(json.tmp, root);
  assert.deepEqual(json.entries.map((e) => e.name).sort(), ['coai-ctx-b', 'coai-panel-a']);

  const removed = runCli(['remove', '--listing', file, 'coai-panel-a', 'coai-ctx-c']);

  assert.equal(removed.status, 1, 'one pick is not listed: exit 1');
  assert.match(removed.stdout, /^refused coai-ctx-c: not-listed$/m);
  assert.match(removed.stdout, /^refused coai-panel-a: not-attempted/m, '[S2-A code3 3] and nothing else was removed');
  assert.ok(fs.existsSync(path.join(root, 'coai-panel-a')));
  const both = runCli(['remove', '--listing', file, 'coai-panel-a', 'coai-ctx-b']);
  assert.equal(both.status, 0, 'every pick removed is exit 0');
  assert.match(both.stdout, /^removed coai-panel-a$/m);
  assert.ok(!fs.existsSync(path.join(root, 'coai-panel-a')), 'removed from the listing\'s root, with no --tmp given');

  const usage = runCli(['remove', 'coai-panel-a']);
  assert.equal(usage.status, 2);
  assert.match(usage.stderr, /list \[--tmp <dir>\] \[--out <file>\]/);
  assert.match(usage.stderr, /remove --listing <file> \[--tmp <dir>\] <name>\.\.\./);
  assert.equal(runCli([]).status, 2);
  assert.equal(runCli(['remove', '--listing', file, '--force', 'x']).status, 2, 'there is no --force');
});

test('S11 a rename that fails removes its own empty placeholder and refuses the pick', { skip: !posix && 'the placeholder is a POSIX shape' }, (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  const io = { ...fs, renameSync: () => { throw Object.assign(new Error('EACCES: permission denied'), { code: 'EACCES' }); } };
  const drawn = [];

  const [result] = remove(root, listing, ['coai-panel-a'], { io, hooks: { drawName: (dir) => { const name = path.join(path.dirname(dir), '.drawn-once'); drawn.push(name); return name; } } });

  assert.deepEqual([result.outcome, result.check], ['refused', 'error-EACCES']);
  assert.equal(sentinel(root, 'coai-panel-a'), 'still coai-panel-a');
  assert.equal(drawn.length, 1, 'a failed rename is not a taken name: no redraw');
  assert.deepEqual(fs.readdirSync(root), ['coai-panel-a'], 'the placeholder this move reserved is gone');
});

test('S12 a taken draw draws a new name, and the move lands on the second', { skip: !posix && 'the placeholder is a POSIX shape' }, (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  const occupied = path.join(root, '.occupied');
  fs.mkdirSync(occupied);
  fs.writeFileSync(path.join(occupied, 'someone-elses'), 'bytes');
  const before = fs.lstatSync(occupied, { bigint: true });
  const names = [occupied, path.join(root, '.free')];
  let draws = 0;

  const [result] = remove(root, listing, ['coai-panel-a'], { hooks: { drawName: () => names[draws++] } });

  assert.equal(result.outcome, 'removed');
  assert.equal(draws, 2);
  const after = fs.lstatSync(occupied, { bigint: true });
  assert.deepEqual([after.dev, after.ino], [before.dev, before.ino]);
  assert.equal(fs.readFileSync(path.join(occupied, 'someone-elses'), 'utf8'), 'bytes', 'the occupied name was never touched');
  assert.deepEqual(fs.readdirSync(root), ['.occupied'], 'the picked folder is gone and the second name is not left behind');
});

test('S13 remove uses the listing\'s temp root, and refuses a --tmp that is another root', (t) => {
  const rootA = privateRoot(t);
  const rootB = privateRoot(t);
  const elsewhere = privateRoot(t, 'old-coai-cwd-');
  make(rootA, 'coai-panel-a', 60);
  make(rootB, 'coai-panel-a', 60, 'root B\'s own');
  const file = path.join(elsewhere, 'listing.json');
  assert.equal(runCli(['list', '--tmp', rootA, '--out', file], { cwd: elsewhere }).status, 0);

  const wrong = runCli(['remove', '--listing', file, '--tmp', rootB, 'coai-panel-a'], { cwd: elsewhere });

  assert.equal(wrong.status, 2);
  assert.ok(wrong.stderr.includes(rootA) && wrong.stderr.includes(rootB), `both roots named: ${wrong.stderr}`);
  assert.equal(sentinel(rootA, 'coai-panel-a'), 'still coai-panel-a');
  assert.equal(sentinel(rootB, 'coai-panel-a'), 'root B\'s own');

  const same = runCli(['remove', '--listing', file, '--tmp', `${rootA}${path.sep}`, 'coai-panel-a'], { cwd: elsewhere });

  assert.equal(same.status, 0, same.stdout + same.stderr);
  assert.match(same.stdout, /^removed coai-panel-a$/m);
  assert.ok(!fs.existsSync(path.join(rootA, 'coai-panel-a')));
  assert.equal(sentinel(rootB, 'coai-panel-a'), 'root B\'s own');

  const bare = JSON.parse(fs.readFileSync(file, 'utf8'));
  delete bare.tmp;
  fs.writeFileSync(file, JSON.stringify(bare));
  const noRoot = runCli(['remove', '--listing', file, 'coai-panel-a'], { cwd: elsewhere });
  assert.equal(noRoot.status, 2, 'a listing that names no root is a usage error');
  assert.match(noRoot.stderr, /remove --listing <file> \[--tmp <dir>\] <name>\.\.\./);
  assert.equal(sentinel(rootB, 'coai-panel-a'), 'root B\'s own');
});

/** A quarantine of this script, as a killed run leaves it. */
const quarantineOf = (name, suffix = '99999-0a1b2c3d') => `.${name}.teststodelete-quarantine-${suffix}`;

test('S14 the listing reports this script\'s own quarantines, and remove accepts them by name', (t) => {
  const root = privateRoot(t);
  const panel = quarantineOf('coai-panel-a');
  const run = quarantineOf('testsToDelete-coai-run-abc123');
  const chat = quarantineOf('coai-chat-x');
  make(root, panel, 60);
  make(root, run, 60);
  fs.writeFileSync(path.join(root, 'testsToDelete-coai-run-abc123.owner'), `${claimOf(99999, 1700000000)}\n`);
  make(root, chat, 60);
  fs.writeFileSync(path.join(root, quarantineOf('coai-panel-b')), 'a file');
  const listing = list(root);

  assert.deepEqual(listing.entries.map((e) => [e.name, e.kind, e.of]).sort(),
    [[panel, 'quarantine', 'coai-panel-a'], [run, 'quarantine', 'testsToDelete-coai-run-abc123']].sort(),
    'the protected original and the file are not interrupted clean-ups of this script');

  const edited = { ...listing, entries: [...listing.entries, { ...listing.entries[0], name: chat, of: 'coai-chat-x' }] };
  const protectedResult = remove(root, edited, [chat])[0];
  assert.deepEqual([protectedResult.outcome, protectedResult.check], ['refused', 'protected']);
  assert.equal(sentinel(root, chat), 'still ' + chat);

  age(path.join(root, panel), 5);
  const changed = remove(root, listing, [panel])[0];
  assert.deepEqual([changed.outcome, changed.check], ['refused', 'changed']);
  assert.equal(sentinel(root, panel), `still ${panel}`);

  const runResult = remove(root, listing, [run])[0];
  assert.equal(runResult.outcome, 'removed');
  assert.ok(!fs.existsSync(path.join(root, run)));
  assert.ok(!fs.existsSync(path.join(root, 'testsToDelete-coai-run-abc123.owner')), 'the original name\'s claim goes with it, last');
});

test('S15 kill after the rename, a second run finishes it', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  const listingFile = path.join(root, '..', `${path.basename(root)}-listing.json`);
  fs.writeFileSync(listingFile, JSON.stringify(listing));
  t.after(() => fs.rmSync(listingFile, { force: true }));
  const child = `
    import fs from 'node:fs';
    import { removePicked } from ${JSON.stringify(pathToFileURL(LIB).href)};
    const [listingFile, tmp, neverSwept] = process.argv.slice(1);
    removePicked({ tmp, listing: JSON.parse(fs.readFileSync(listingFile, 'utf8')), picks: ['coai-panel-a'], neverSwept: JSON.parse(neverSwept),
      hooks: { afterRename: () => process.kill(process.pid, 'SIGKILL') } });
    console.log('the kill never came');`;

  let signal;
  try {
    execFileSync(process.execPath, ['--input-type=module', '-e', child, listingFile, root, JSON.stringify(NEVER_SWEPT)], { encoding: 'utf8', stdio: 'pipe' });
  } catch (error) {
    signal = error.signal;
  }

  assert.equal(signal, 'SIGKILL', 'the first run died between its rename and its removal');
  const left = fs.readdirSync(root);
  assert.equal(left.length, 1);
  assert.match(left[0], /^\.coai-panel-a\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$/, 'the quarantine is all that is left: the name is free');

  const second = list(root);
  assert.deepEqual(second.entries.map((e) => [e.name, e.kind, e.of]), [[left[0], 'quarantine', 'coai-panel-a']]);
  const [result] = remove(root, second, [left[0]], { probe: processProbe }); // the real table: the killed child is gone
  assert.equal(result.outcome, 'removed');
  assert.deepEqual(fs.readdirSync(root), [], 'the second run finished the first one\'s work');
});

test('S16 the quarantined object is re-checked after the move by identity AND last-write time; either change keeps it', { skip: !posix && 'the swap needs POSIX rename semantics' }, (t) => {
  for (const variant of ['its last-write time moved', 'it was replaced by another directory with the listed last-write time']) {
    const root = privateRoot(t);
    make(root, 'coai-panel-a', 60);
    const listing = list(root);
    const listed = listing.entries[0];
    const afterRename = (_from, quarantined) => {
      if (variant === 'its last-write time moved') {
        age(quarantined, 5);
        return;
      }
      // [S2-A Opus 2] The replacement exists BEFORE the original goes, so its inode cannot be the original's (ext4 and tmpfs reuse a freed one);
      // its last-write time is put back to the listed one, so ONLY the device and inode comparison can tell it apart.
      const replacement = `${quarantined}.replacement`;
      fs.mkdirSync(replacement);
      fs.writeFileSync(path.join(replacement, 'sentinel.txt'), 'the replacement');
      fs.utimesSync(replacement, new Date(listed.lastWriteMs), new Date(listed.lastWriteMs));
      fs.rmSync(quarantined, { recursive: true });
      fs.renameSync(replacement, quarantined);
      const now = fs.lstatSync(quarantined, { bigint: true });
      assert.equal(String(now.mtimeNs), listed.mtimeNs, 'the replacement carries the listed last-write time');
      assert.notEqual(String(now.ino), listed.ino, 'and a different inode');
    };

    const [result] = remove(root, listing, ['coai-panel-a'], { hooks: { afterRename } });

    assert.deepEqual([result.outcome, result.check], ['refused', 'moved-but-changed'], variant);
    const left = fs.readdirSync(root);
    assert.equal(left.length, 1, 'nothing was removed: the quarantined object stays under its quarantine name');
    assert.ok(fs.existsSync(path.join(root, left[0], 'sentinel.txt')), variant);
    const next = list(root);
    assert.deepEqual(next.entries.map((e) => [e.kind, e.of]), [['quarantine', 'coai-panel-a']], 'and the next listing shows it as an interrupted clean-up');
  }
});

test('S17 [S2-A Opus 1] a name that is not ONE path segment is refused before any path is built: the victim outside the root survives', (t) => {
  const outer = privateRoot(t, 'old-coai-escape-');
  const root = path.join(outer, 'tmp');
  fs.mkdirSync(root);
  const victim = make(outer, 'victim', 60, 'the victim\'s bytes'); // a sibling of the temp root, found by `coai-x/../../victim` below it
  const stat = fs.lstatSync(victim, { bigint: true });
  const identity = { dev: String(stat.dev), ino: String(stat.ino), mtimeNs: String(stat.mtimeNs), lastWriteMs: now - 60 * MINUTE, ageMinutes: 60 };
  const names = [
    ['coai-x/../../victim', 'coai'],
    ['coai-x\\..\\..\\victim', 'coai'],
    ['.coai-x/../../victim.teststodelete-quarantine-99999-0a1b2c3d', 'quarantine'],
    ['..', 'coai'],
    ['.', 'coai'],
  ];
  const listing = { tmp: root, entries: names.map(([name, kind]) => ({ name, kind, ...identity })) };

  const results = remove(root, listing, names.map(([name]) => name));

  assert.deepEqual(results.map((r) => [r.outcome, r.check]), names.map(() => ['refused', 'bad-name']));
  assert.equal(sentinel(outer, 'victim'), 'the victim\'s bytes', 'the victim outside the temp root was neither moved nor removed');
  assert.deepEqual(fs.readdirSync(outer).sort(), ['tmp', 'victim'], 'and nothing was made beside it');
  assert.deepEqual(fs.readdirSync(root), [], 'nor inside the root');
});

test('S18 [S2-A Opus 3] a quarantine whose renamer is alive or cannot be asked is refused; one whose renamer is gone is removed', (t) => {
  const root = privateRoot(t);
  const live = quarantineOf('coai-panel-a', '88888-0a1b2c3d');
  const unknown = quarantineOf('coai-panel-b', '77777-0a1b2c3d');
  const gone = quarantineOf('coai-panel-c', '99999-0a1b2c3d');
  for (const name of [live, unknown, gone]) make(root, name, 60);
  const listing = list(root);

  const results = byName(remove(root, listing, [live, unknown, gone]));

  assert.deepEqual([results[live].outcome, results[live].check], ['refused', 'owner-live'], 'a sweep that is running now waits to restore it');
  assert.deepEqual([results[unknown].outcome, results[unknown].check], ['refused', 'owner-unknown']);
  assert.equal(results[gone].outcome, 'removed');
  assert.deepEqual(fs.readdirSync(root).sort(), [live, unknown].sort());
});

test('S19 [S2-A Opus 4] a pick the caller does not own (kernel owner, rule §3) is refused', { skip: typeof process.getuid !== 'function' && 'no uid on this platform' }, (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  const strangers = {
    ...fs,
    lstatSync: (target, options) => {
      const stat = fs.lstatSync(target, options);
      return { dev: stat.dev, ino: stat.ino, mtimeNs: stat.mtimeNs, uid: stat.uid + 1n, isDirectory: () => stat.isDirectory() };
    },
  };

  const [result] = remove(root, listing, ['coai-panel-a'], { io: strangers });

  assert.deepEqual([result.outcome, result.check], ['refused', 'foreign-uid']);
  assert.equal(sentinel(root, 'coai-panel-a'), 'still coai-panel-a');
});

test('S20 [S2-A Opus 5] a legacy run folder whose claim turned live after the move is kept, with its claim', (t) => {
  const root = privateRoot(t);
  const name = 'testsToDelete-coai-run-abc123';
  make(root, name, 120);
  fs.writeFileSync(path.join(root, `${name}.owner`), `${claimOf(99999, 1700000000)}\n`);
  const listing = list(root);
  const afterRename = () => fs.writeFileSync(path.join(root, `${name}.owner`), `${claimOf(88888)}\n`); // a run took the claim in between

  const [result] = remove(root, listing, [name], { hooks: { afterRename } });

  assert.deepEqual([result.outcome, result.check], ['refused', 'owner-live']);
  const left = fs.readdirSync(root).sort();
  assert.equal(left.length, 2, `the quarantined folder and the claim stay: ${left}`);
  assert.ok(left.includes(`${name}.owner`));
  assert.equal(fs.readFileSync(path.join(root, `${name}.owner`), 'utf8'), `${claimOf(88888)}\n`, 'the claim is untouched');
});

test('S21 [S2-A gate 4] a rename that answers ENOENT is the missing refusal, on Windows and elsewhere', (t) => {
  for (const platform of ['win32', process.platform]) {
    const root = privateRoot(t);
    make(root, 'coai-panel-a', 60);
    const listing = list(root);
    const io = { ...fs, renameSync: () => { throw Object.assign(new Error('ENOENT: no such file or directory, rename'), { code: 'ENOENT' }); } };

    const [result] = remove(root, listing, ['coai-panel-a'], { io, hooks: { platform } });

    assert.deepEqual([result.outcome, result.check], ['refused', 'missing'], platform);
    assert.deepEqual(fs.readdirSync(root), ['coai-panel-a'], `no placeholder is left (${platform})`);
  }
});

test('S22 [S2-A gate 7] the listing is indexed by name once, not searched for every pick', (t) => {
  const root = privateRoot(t);
  for (const name of ['coai-panel-a', 'coai-panel-b', 'coai-panel-c']) make(root, name, 60);
  const listing = list(root);
  listing.entries.find = () => { throw new Error('a per-pick search of the entries'); };

  const results = remove(root, listing, ['coai-panel-a', 'coai-panel-b']);

  assert.deepEqual(results.map((r) => r.outcome), ['removed', 'removed']);
});

test('S23 [S2-A gate 6/9] each pick\'s result is reported as soon as it is decided, before the next pick is touched', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  make(root, 'coai-panel-b', 60);
  const listing = list(root);
  const reported = [];
  const seenWhenBStarts = [];
  const hooks = { afterRename: (from) => { if (path.basename(from) === 'coai-panel-b') seenWhenBStarts.push(reported.map((r) => r.name)); } };

  const results = remove(root, listing, ['coai-panel-a', 'coai-panel-b'], { hooks, onResult: (result) => reported.push(result) });

  assert.deepEqual(seenWhenBStarts, [['coai-panel-a']], 'a was already reported when b was being moved');
  assert.deepEqual(reported, results, 'every result is reported once, in order, and still returned');
});

test('S24 [S2-A gate 3] list shows the listing first and writes --out after; a write failure exits 2 with the listing already shown', (t) => {
  const root = s1Root(t);
  const nowhere = path.join(privateRoot(t, 'old-coai-out-'), 'no', 'such', 'folder', 'listing.json');

  const result = runCli(['list', '--tmp', root, '--out', nowhere]);

  assert.equal(result.status, 2, result.stderr);
  assert.match(result.stdout, /^coai	coai-panel-a	/m, 'the listing was printed before the write was tried');
  assert.match(result.stderr, /listing\.json|ENOENT/, 'and the failure is named');
});

test('S25 [S2-A code2 0] a quarantine-shaped name whose original is not one path segment is not a quarantine: not listed, never judged', { skip: !posix && 'a backslash is a separator on Windows' }, (t) => {
  const root = privateRoot(t);
  // A backslash is an ordinary file-name character on POSIX, so this directory can exist; its "original" would be coai-a\b, two segments on Windows.
  const crafted = '.coai-a\\b.teststodelete-quarantine-99999-0a1b2c3d';
  make(root, crafted, 60);
  const listing = list(root);

  assert.deepEqual(listing.entries.map((e) => e.name), [], 'defence in depth: judged null before any claim is read or unlinked');
  assert.equal(sentinel(root, crafted), `still ${crafted}`);
  const edited = { ...listing, entries: [{ name: crafted, kind: 'quarantine', of: 'coai-a\\b', dev: '1', ino: '1', mtimeNs: '1', lastWriteMs: 0, ageMinutes: 0 }] };
  assert.deepEqual(remove(root, edited, [crafted]).map((r) => [r.outcome, r.check]), [['refused', 'bad-name']]);
  // The listing's own filter and the quarantine pattern both stop this name first; judge() is the layer that must hold on its own.
  assert.equal(lib.judge?.(crafted, NEVER_SWEPT), null, 'judge() returns null for a quarantine whose original is not one segment');
  assert.equal(lib.judge?.(crafted.replace('\\', '-'), NEVER_SWEPT)?.kind, 'quarantine', 'and still knows a real quarantine (the scan sees what it knows is there)');
});

test('S26 [S2-A code2 1] the uid is checked again after the move: a quarantined object that is no longer the caller\'s is kept', { skip: typeof process.getuid !== 'function' && 'no uid on this platform' }, (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  let handedOver = false;
  const io = {
    ...fs,
    lstatSync: (target, options) => {
      const stat = fs.lstatSync(target, options);
      return { dev: stat.dev, ino: stat.ino, mtimeNs: stat.mtimeNs, uid: handedOver ? stat.uid + 1n : stat.uid, isDirectory: () => stat.isDirectory() };
    },
  };

  const [result] = remove(root, listing, ['coai-panel-a'], { io, hooks: { afterRename: () => { handedOver = true; } } });

  assert.deepEqual([result.outcome, result.check], ['refused', 'foreign-uid']);
  const left = fs.readdirSync(root);
  assert.equal(left.length, 1, 'nothing was removed: the object stays under its quarantine name');
  assert.ok(fs.existsSync(path.join(root, left[0], 'sentinel.txt')));
});

test('S27 [S2-A code2 2] a claim that cannot be read is `unreadable`, refuses the pick, and never aborts the listing', (t) => {
  const root = privateRoot(t);
  const name = 'testsToDelete-coai-run-abc123';
  make(root, name, 120);
  fs.mkdirSync(path.join(root, `${name}.owner`)); // a directory in the claim's place
  make(root, 'testsToDelete-coai-run-def456', 120);
  fs.writeFileSync(path.join(root, 'testsToDelete-coai-run-def456.owner'), `${claimOf(99999, 1700000000)}\n`);

  const listing = list(root);
  assert.equal(byName(listing.entries)[name].claim, 'unreadable', 'a directory where the claim should be');
  const [result] = remove(root, listing, [name]);
  assert.deepEqual([result.outcome, result.check], ['refused', 'owner-unreadable']);
  assert.equal(sentinel(root, name), `still ${name}`);

  const throwing = list(root, { readClaim: () => { throw Object.assign(new Error('EACCES: permission denied'), { code: 'EACCES' }); } });
  assert.deepEqual(throwing.entries.map((e) => [e.name, e.claim]), [[name, 'unreadable'], ['testsToDelete-coai-run-def456', 'unreadable']], 'a read that throws is unreadable for that entry and the listing goes on');
});

test('S28 [S2-A code2 4/8] list says which temp root it is scanning before it scans', (t) => {
  const missing = path.join(privateRoot(t, 'old-coai-start-'), 'no-such-root');

  const result = runCli(['list', '--tmp', missing, '--out', path.join(os.tmpdir(), 'never-written-listing.json')]);

  assert.equal(result.status, 2, 'a root that cannot be read is exit 2');
  assert.match(result.stdout, new RegExp(`^scanning ${missing.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'm'), 'the start line named the root before the scan failed');
});

test('S29 [S2-A code2 5] --tmp and the listing\'s root are compared by realpath; when they differ both are shown as given', (t) => {
  const rootA = privateRoot(t);
  const rootB = privateRoot(t);
  const elsewhere = privateRoot(t, 'old-coai-cwd-');
  make(rootA, 'coai-panel-a', 60);
  const file = path.join(elsewhere, 'listing.json');
  assert.equal(runCli(['list', '--tmp', rootA, '--out', file]).status, 0);
  const alias = path.join(elsewhere, 'alias-of-a');
  try {
    fs.symlinkSync(rootA, alias, 'dir');
  } catch (error) {
    t.skip(`this OS refuses to make a symbolic link without privilege: ${error.code}`);
    return;
  }
  const given = `${rootB}${path.sep}..${path.sep}${path.basename(rootB)}`;

  const wrong = runCli(['remove', '--listing', file, '--tmp', given, 'coai-panel-a'], { cwd: elsewhere });

  assert.equal(wrong.status, 2);
  assert.ok(wrong.stderr.includes(given), `the --tmp is shown as given: ${wrong.stderr}`);
  assert.ok(wrong.stderr.includes(rootA), 'and so is the listing\'s root');
  assert.ok(wrong.stderr.includes('omit --tmp to use the listing\'s root') && wrong.stderr.includes('list --tmp <dir>'), `[S2-A code3 4] the way out is named: ${wrong.stderr}`);
  assert.equal(sentinel(rootA, 'coai-panel-a'), 'still coai-panel-a');

  const same = runCli(['remove', '--listing', file, '--tmp', alias, 'coai-panel-a'], { cwd: elsewhere });

  assert.equal(same.status, 0, same.stdout + same.stderr);
  assert.match(same.stdout, /^removed coai-panel-a$/m, 'a link to the same root is the same root');
});

test('S30 [S2-A code2 6] a folder whose name is not one segment on THIS system is reported as refused/bad-name: seen, never offered', { skip: !posix && 'a backslash is a separator on Windows' }, (t) => {
  const root = privateRoot(t);
  make(root, 'coai-a\\b', 60);
  make(root, 'coai-panel-a', 60);

  const listing = list(root);

  assert.deepEqual(listing.entries.map((e) => e.name), ['coai-panel-a'], 'it is not offered');
  assert.deepEqual(listing.refused, [{ name: 'coai-a\\b', check: 'bad-name' }], 'but it is reported');
  const file = path.join(privateRoot(t, 'old-coai-listing-'), 'listing.json');
  const shown = runCli(['list', '--tmp', root, '--out', file]);
  assert.match(shown.stdout, /^refused\tcoai-a\\b\tbad-name/m);
  assert.deepEqual(JSON.parse(fs.readFileSync(file, 'utf8')).entries.map((e) => e.name), ['coai-panel-a']);
});

test('S31 [S2-A code2 7] removePicked takes its root from the listing, and refuses a tmp argument that is another root', (t) => {
  const rootA = privateRoot(t);
  const rootB = privateRoot(t);
  make(rootA, 'coai-panel-a', 60);
  make(rootB, 'coai-panel-a', 60, 'root B\'s own');
  const listing = list(rootA);

  const [wrong] = remove(rootB, listing, ['coai-panel-a']);
  assert.deepEqual([wrong.outcome, wrong.check], ['refused', 'wrong-root']);
  assert.equal(sentinel(rootA, 'coai-panel-a'), 'still coai-panel-a');
  assert.equal(sentinel(rootB, 'coai-panel-a'), 'root B\'s own');

  const [relative] = remove(rootA, { ...listing, tmp: 'relative/dir' }, ['coai-panel-a']);
  assert.deepEqual([relative.outcome, relative.check], ['refused', 'bad-listing'], 'a root that is not absolute is no root');

  const [derived] = removePicked({ listing, picks: ['coai-panel-a'], neverSwept: NEVER_SWEPT, ...world });
  assert.equal(derived.outcome, 'removed', 'no tmp argument: the listing\'s root is the root');
  assert.ok(!fs.existsSync(path.join(rootA, 'coai-panel-a')));
  assert.equal(sentinel(rootB, 'coai-panel-a'), 'root B\'s own');
});

test('S32 [S2-A code3 0] when the removal fails AFTER the move, the result names the quarantine, and the next list offers it', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  const listing = list(root);
  const io = {
    ...fs,
    rmSync: (target, options) => {
      if (path.basename(target).startsWith('.coai-panel-a.teststodelete-quarantine-')) throw Object.assign(new Error('EBUSY: resource busy or locked'), { code: 'EBUSY' });
      return fs.rmSync(target, options);
    },
  };

  const [result] = remove(root, listing, ['coai-panel-a'], { io });

  assert.deepEqual([result.outcome, result.check], ['refused', 'error-EBUSY']);
  assert.match(result.quarantine, /^\.coai-panel-a\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$/, 'the result says where the folder is now');
  assert.deepEqual(fs.readdirSync(root), [result.quarantine]);
  assert.deepEqual(list(root).entries.map((e) => [e.name, e.kind, e.of]), [[result.quarantine, 'quarantine', 'coai-panel-a']], 'a later list offers it, and remove can finish it');
  assert.match(lib.describeResult?.(result) ?? '', new RegExp(`^refused coai-panel-a: error-EBUSY.*${result.quarantine.replace(/[.]/g, '\\.')}`), 'the line a person reads names the quarantine');
  assert.equal(lib.describeResult?.({ name: 'coai-x', outcome: 'removed' }), 'removed coai-x');
});

test('S33 [S2-A code3 3] every pick is checked against the listing BEFORE any removal: one unlisted pick removes nothing, and every unlisted name is reported', (t) => {
  const root = privateRoot(t);
  make(root, 'coai-panel-a', 60);
  make(root, 'coai-panel-b', 60);
  const listing = list(root);

  const results = remove(root, listing, ['coai-panel-a', 'coai-panel-typo', 'coai-panel-b', 'coai-panel-typo2']);

  assert.deepEqual(results.map((r) => [r.name, r.outcome, r.check]), [
    ['coai-panel-a', 'refused', 'not-attempted'],
    ['coai-panel-typo', 'refused', 'not-listed'],
    ['coai-panel-b', 'refused', 'not-attempted'],
    ['coai-panel-typo2', 'refused', 'not-listed'],
  ]);
  assert.deepEqual(fs.readdirSync(root).sort(), ['coai-panel-a', 'coai-panel-b'], 'nothing was removed');
});
