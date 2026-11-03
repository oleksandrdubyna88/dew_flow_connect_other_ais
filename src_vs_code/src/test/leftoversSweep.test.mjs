import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import * as mount from '../../../.agents/conventions/tools/lib/owned.mjs';
import * as ownedModule from '../../scripts/owned.mjs';
import { readSweepSwitch } from '../../scripts/startOfRun.mjs';
import * as runner from '../../scripts/run-tests.mjs';

const { startOfRunSweep, loadOwned } = runner;

/**
 * The extension runner's start-of-run sweep (E5 S2-B, `todo/PLAN_test_leftovers_E5_S2_spec.md` B.4 N-S1..N-S9, L5): the MOUNTED reference with this
 * repository's token bound LAST, behind the ONE switch `shared/leftovers-sweep.json`, over the REAL temp root, before the runner makes anything.
 *
 * The private roots here are made with `mkdtemp` under `os.tmpdir()` (this file is outside `src_mcp/tests/Leftovers`; S6's baseline carries it).
 * A dead owner is a pid the injected probe calls `dead`: never the real probe on an exited child's pid (Windows hands exited pids to the next process),
 * except N-S9, which exists to run the real wrapper and the real reference on a real Windows runner.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(here, '..', '..', '..');
const minute = 60_000;
const DEAD_PID = 4_000_000;
const owner = mount.thisOwner();
const ownerToken = mount.thisOwnerToken();
const otherOwner = ownerToken === 'u1001' ? 'u1002' : 'u1001';
const probe = (pid) => (pid === DEAD_PID ? { state: 'dead' } : mount.processProbe(pid));

function privateRoot(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'leftovers-sweep-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}

/** A folder with a sentinel, its claim beside it, both 11 minutes old. `claim` undefined: no claim file. */
function plant(root, name, claim, { old = true } = {}) {
  const dir = path.join(root, name);
  fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'sentinel.txt'), `still ${name}`);
  if (claim !== undefined) fs.writeFileSync(`${dir}.owner`, claim);
  if (old) {
    const then = new Date(Date.now() - 11 * minute);
    fs.utimesSync(dir, then, then);
    if (claim !== undefined) fs.utimesSync(`${dir}.owner`, then, then);
  }
  return dir;
}

const claimOf = (pid) => `${mount.formatOwner({ ...owner, pid })}\n`;

/** What N-S2 plants: a dead owner's folder, a live one, another owner's, a legacy one, a chat that is nobody's business. */
function plantTheFive(root) {
  return {
    dead: plant(root, `testsToDelete-${ownerToken}-coai-run-abc123`, claimOf(DEAD_PID)),
    live: plant(root, `testsToDelete-${ownerToken}-coai-run-live123`, claimOf(process.pid)),
    foreign: plant(root, `testsToDelete-${otherOwner}-coai-x-abc123`, undefined),
    legacy: plant(root, 'testsToDelete-coai-run-1a2b3c', `devbox@linux:${DEAD_PID}:17`),
    chat: plant(root, 'coai-chat-live', undefined),
  };
}

const quarantinesIn = (root) => fs.readdirSync(root).filter((name) => /quarantine/i.test(name));

test('N-S1 the runner sweeps the REAL temp root before it makes anything, with the coai token bound last', async () => {
  const calls = [];
  const recording = (options) => { calls.push(options); return { removed: [], preserved: [], listed: [], errors: [] }; };
  const loadSweep = () => ownedModule.loadStartOfRunSweep({ importer: async () => ({ sweepLeftovers: recording }) });
  const sweepOptions = { token: 'mcp', now: 7 };

  await startOfRunSweep({ owned: ownedModule, log: () => {}, root: '/r', repo: '/repo', mode: 'collect', loadSweep, sweepOptions });

  assert.equal(calls.length, 1, 'the sweep is called once');
  assert.deepEqual(calls[0], { now: 7, repo: '/repo', tmp: '/r', token: 'coai' }, 'the token is the repository\'s though the caller said mcp; the caller\'s other options pass through');
});

test('N-S2 the reference collects a gone owner\'s coai folder, keeps a live one, lists a foreign one and a legacy one, and never looks at coai-*', async (t) => {
  const root = privateRoot(t);
  const planted = plantTheFive(root);
  const lines = [];

  const result = await startOfRunSweep({
    owned: ownedModule, log: (line) => lines.push(line), root, repo: repoRoot, mode: 'collect', sweepOptions: { probe, mine: () => true, now: Date.now() },
  });

  assert.ok(fs.existsSync(planted.live), 'a live owner keeps its folder');
  assert.ok(fs.existsSync(path.join(planted.live, 'sentinel.txt')));
  assert.ok(fs.existsSync(planted.foreign), 'another owner\'s folder is listed and kept');
  assert.ok(fs.existsSync(planted.legacy), 'a legacy name is listed and never collected');
  assert.equal(fs.readFileSync(path.join(planted.chat, 'sentinel.txt'), 'utf8'), 'still coai-chat-live', 'coai-chat-live keeps its bytes');
  const everything = [...result.removed, ...result.preserved.map((p) => p.path), ...result.listed, ...result.errors];
  assert.ok(!everything.some((line) => line.includes('coai-chat-live')), 'coai-chat-live is in no list');
  assert.ok(result.listed.some((line) => line.startsWith(`foreign: folder ${path.basename(planted.foreign)} — owner ${otherOwner}`)), `foreign line in ${JSON.stringify(result.listed)}`);
  assert.ok(result.listed.some((line) => line.startsWith('legacy: folder testsToDelete-coai-run-1a2b3c — remove with: ')), `legacy line in ${JSON.stringify(result.listed)}`);
  assert.ok(lines.some((line) => line.startsWith('run-tests: legacy: folder testsToDelete-coai-run-1a2b3c')), 'and the runner says it');
  if (process.platform === 'win32') {
    assert.deepEqual(result.removed, [], 'on Windows the reference lists and changes nothing (F16)');
    assert.ok(result.listed.some((line) => line.startsWith('unverified: ')), 'it lists the dead one instead');
    assert.ok(fs.existsSync(planted.dead));
  } else {
    assert.deepEqual(result.removed, [planted.dead], 'the gone owner\'s folder is the one removal');
    assert.ok(!fs.existsSync(planted.dead) && !fs.existsSync(`${planted.dead}.owner`), 'folder and claim are gone');
    assert.ok(result.preserved.some((p) => p.path === planted.live && p.reason === 'live'), `live in ${JSON.stringify(result.preserved)}`);
    assert.deepEqual(quarantinesIn(root), [], 'no quarantine entry is left behind');
  }
});

test('N-S3 the report is silent when there is nothing, one summary and one line per entry otherwise, and a thrown or unloadable sweep is one line that never stops the run', async () => {
  const run = async (sweep, loadSweep = async () => sweep) => {
    const lines = [];
    await startOfRunSweep({ owned: ownedModule, log: (line) => lines.push(line), root: '/r', repo: '/repo', mode: 'collect', loadSweep });
    return lines;
  };
  const empty = { removed: [], preserved: [], listed: [], errors: [] };
  const full = {
    removed: ['/r/gone'], preserved: [{ path: '/r/a', reason: 'live' }, { path: '/r/b', reason: 'unknown' }], listed: ['foreign: folder x — owner u1001'], errors: ['folder /r/y: boom'],
  };

  assert.deepEqual(await run(() => empty), [], 'nothing found, nothing said');
  assert.deepEqual(await run(() => full), [
    'run-tests: leftovers in /r — removed 1, preserved 2, listed 1',
    'run-tests: foreign: folder x — owner u1001',
    'run-tests: preserved: /r/a — live',
    'run-tests: preserved: /r/b — unknown',
    'run-tests: error: folder /r/y: boom',
  ]);
  const many = { ...empty, preserved: Array.from({ length: 25 }, (_, i) => ({ path: `/r/p${i}`, reason: 'live' })) };
  const capped = await run(() => many);
  assert.equal(capped.filter((line) => line.startsWith('run-tests: preserved: ')).length, 20, 'at most twenty preserved entries are named');
  assert.match(capped.at(-1), /^run-tests: … 5 more preserved entries are not shown/);
  assert.deepEqual(await run(() => { throw new Error('boom'); }), ['run-tests: leftovers sweep not run (boom); the tests still will']);
  assert.deepEqual(await run(undefined, async () => { throw new Error('boom'); }), ['run-tests: leftovers sweep not run (boom); the tests still will']);
});

test('N-S4 the mount missing is one line and exit 2 before anything is made', async () => {
  const errors = [];
  const exits = [];

  await loadOwned({
    importOwned: async () => { throw new Error('Cannot find module \'/x/.agents/conventions/tools/lib/owned.mjs\'\n    at somewhere'); },
    error: (line) => errors.push(line),
    exit: (code) => exits.push(code),
  });

  assert.deepEqual(exits, [2]);
  assert.equal(errors.length, 1, 'ONE line');
  assert.match(errors[0], /^run-tests: the shared conventions are not mounted \(Cannot find module .*\) — run: git submodule update --init \.agents\/conventions$/);
  assert.ok(!errors[0].includes('\n'), 'only the first line of the error');
  const text = fs.readFileSync(path.join(here, '..', '..', 'scripts', 'run-tests.mjs'), 'utf8');
  assert.match(text.slice(text.indexOf('async function main() {')), /^async function main\(\) \{\s*const owned = await loadOwned\(\);/, 'loadOwned is the first thing main does: nothing is spawned or made before it');
});

test('N-S5 the switch: the repository says collect; list-only calls no sweep and lists the owner-form and legacy names only', async () => {
  assert.equal(readSweepSwitch(repoRoot), 'collect', 'main carries collect: a typo is red here, never silent');
  const document = JSON.parse(fs.readFileSync(path.join(repoRoot, 'shared', 'leftovers-sweep.json'), 'utf8'));
  assert.deepEqual(Object.keys(document).sort(), ['startOfRunSweep', 'why']);

  const names = [`testsToDelete-${ownerToken}-coai-run-abc123`, `testsToDelete-${otherOwner}-coai-x-abc123`, 'testsToDelete-coai-run-1a2b3c', `.testsToDelete-${ownerToken}-coai-run-abc123.teststodelete-quarantine-4242-0a1b2c3d`,
    `testsToDelete-${ownerToken}-mcp-x-abc123`, 'coai-x'];
  const calls = [];
  const lines = [];
  const io = { readdirSync: (dir) => { calls.push(`readdirSync ${dir}`); return names; }, rmSync: () => calls.push('rm'), renameSync: () => calls.push('rename'), statSync: () => calls.push('stat') };

  const result = await startOfRunSweep({
    owned: ownedModule, log: (line) => lines.push(line), root: '/r', repo: '/repo', mode: 'list-only', io, loadSweep: () => { throw new Error('the sweep must not be loaded'); },
  });

  const switched = lines.filter((line) => line.startsWith('run-tests: switched-off: '));
  assert.equal(switched.length, 4, `owner-form, foreign owner, legacy, quarantine: ${JSON.stringify(lines)}`);
  assert.ok(switched.some((line) => line.includes('quarantine ')), 'the quarantine of an own name is listed');
  assert.ok(!lines.some((line) => line.includes('coai-x') || line.includes('-mcp-')), 'coai-x and another token are in no line');
  assert.ok(switched.every((line) => line.includes('shared/leftovers-sweep.json') && line.endsWith('nothing is removed')));
  assert.deepEqual(calls, ['readdirSync /r'], 'names were read; nothing was stat-ed, renamed or removed');
  assert.deepEqual(result.removed, []);

  const table = [
    ['{"startOfRunSweep":"collect"}', 'collect', undefined],
    ['{"startOfRunSweep":"list-only"}', 'list-only', undefined],
    ['{"startOfRunSweep":"COLLECT"}', 'list-only', 'COLLECT'],
    ['{"startOfRunSweep":"yes"}', 'list-only', 'yes'],
    ['{}', 'list-only', 'missing'],
    ['{', 'list-only', 'unreadable'],
    [undefined, 'list-only', 'unreadable'],
  ];
  for (const [text, mode, warns] of table) {
    const said = [];
    const read = readSweepSwitch('/repo', {
      io: { readFileSync: () => { if (text === undefined) throw Object.assign(new Error('ENOENT: no such file'), { code: 'ENOENT' }); return text; } },
      warn: (line) => said.push(line),
    });
    assert.equal(read, mode, `switch text ${text}`);
    if (warns === undefined) assert.deepEqual(said, [], `${text} is a plain value: no line`);
    else {
      assert.equal(said.length, 1, `${text}: one line`);
      assert.match(said[0], /^run-tests: the start-of-run sweep is list-only because shared\/leftovers-sweep\.json startOfRunSweep is /);
      assert.ok(said[0].includes(warns), `${said[0]} names ${warns}`);
    }
  }
});

test('N-S6 main loads the mount, sweeps, counts, then makes its folder — in that order', () => {
  const text = fs.readFileSync(path.join(here, '..', '..', 'scripts', 'run-tests.mjs'), 'utf8');
  const main = text.slice(text.indexOf('async function main() {'));

  assert.match(main, /^async function main\(\) \{\s*const owned = await loadOwned\(\);\s*await startOfRunSweep\(\{ owned \}\);\s*reportLeftoversFirst\(\);\s*const folder = beginRun\(\);/,
    'a count or a sweep after the run starts would read the run\'s own folder');
});

test('N-S7 on win32 the reference lists and changes nothing (F16 interim, Q6) — through the platform seam', async (t) => {
  const root = privateRoot(t);
  const { dead } = plantTheFive(root);

  const result = await startOfRunSweep({
    owned: ownedModule, log: () => {}, root, repo: repoRoot, mode: 'collect', sweepOptions: { platform: 'win32', probe, mine: () => true },
  });

  assert.deepEqual(result.removed, [], 'nothing is removed');
  assert.ok(result.listed.some((line) => line.startsWith('unverified: ') && line.includes(path.basename(dead))), `an unverified line names the folder: ${JSON.stringify(result.listed)}`);
  assert.ok(fs.existsSync(path.join(dead, 'sentinel.txt')) && fs.existsSync(`${dead}.owner`), 'the folder and its claim are intact');
  assert.deepEqual(quarantinesIn(root), []);
});

test('N-S8 the same verdicts under the root as given and as resolved', async (t) => {
  const root = privateRoot(t);
  const aliasHome = privateRoot(t);
  const alias = path.join(aliasHome, 'alias');
  try {
    fs.symlinkSync(root, alias, 'junction');
  } catch (error) {
    t.skip(`cannot make a directory link here (${error.code})`);
    return;
  }
  const dead = plant(root, `testsToDelete-${ownerToken}-coai-run-dead123`, claimOf(DEAD_PID));
  const live = plant(root, `testsToDelete-${ownerToken}-coai-run-live123`, claimOf(process.pid));
  const options = { probe, mine: process.platform === 'win32' ? () => true : mount.isMine };
  const sweep = (tmp) => startOfRunSweep({ owned: ownedModule, log: () => {}, root: tmp, repo: repoRoot, mode: 'collect', sweepOptions: options });

  const viaAlias = await sweep(alias);
  if (process.platform !== 'win32') plant(root, `testsToDelete-${ownerToken}-coai-run-dead123`, claimOf(DEAD_PID));
  const viaReal = await sweep(root);

  for (const [label, result, base] of [['alias', viaAlias, alias], ['real', viaReal, root]]) {
    assert.deepEqual(result.errors, [], label);
    assert.ok(result.preserved.some((p) => p.path === path.join(base, path.basename(live)) && p.reason === 'live'), `${label}: the live one is kept live`);
    if (process.platform !== 'win32') assert.deepEqual(result.removed, [path.join(base, path.basename(dead))], `${label}: the dead one is removed, by the root it was given`);
  }

  if (process.platform === 'darwin') {
    const resolved = fs.realpathSync.native(root);
    assert.notEqual(resolved, root, 'the premise: /var/… and /private/var/… are two spellings');
    assert.equal(mount.isMine(root), true);
    assert.equal(mount.isMine(resolved), true);
    for (const base of [root, resolved]) {
      const result = await sweep(base);
      assert.ok(result.preserved.some((p) => p.path === path.join(base, path.basename(live)) && p.reason === 'live'), `${base}: the sweep lists the live plant live`);
    }
  }
});

test('N-S9 on windows the REAL wrapper and the PINNED reference keep an otherwise collectible folder, listing it unverified (F16, Q6)', async (t) => {
  if (process.platform !== 'win32') {
    t.skip('Windows only: the `windows` CI job runs this against the real wrapper and the pinned reference');
    return;
  }
  // On win32 a failed premise FAILS the test: the leg exists to run it.
  const root = privateRoot(t);
  assert.equal(mount.isMine(root), true, 'premise: the private root is inside the profile');
  const child = spawnSync(process.execPath, ['-e', '0']);
  assert.equal(child.status, 0, 'premise: the child ran and exited');
  const pid = child.pid;
  assert.equal(mount.processProbe(pid).state, 'dead', 'premise: the exited child is dead');
  const claim = `${mount.formatOwner({ ...mount.thisOwner(), pid })}\n`;
  assert.notEqual(mount.parseOwner(claim).unreadable, true, 'premise: the claim parses');
  assert.equal(mount.ownerState(mount.parseOwner(claim), mount.processProbe, { nameOwner: ownerToken, callerOwner: ownerToken, kernelMine: true }), 'gone', 'premise: every key but the Windows gate says remove');
  const dir = plant(root, `testsToDelete-${ownerToken}-coai-win-abc123`, claim);
  const before = fs.readFileSync(`${dir}.owner`, 'utf8');
  const lines = [];

  const result = await startOfRunSweep({ owned: ownedModule, log: (line) => lines.push(line), root, repo: repoRoot, mode: 'collect' });

  assert.deepEqual(result.removed, [], 'nothing is removed on Windows');
  assert.equal(result.listed.filter((line) => line.startsWith('unverified: ')).length, 1, JSON.stringify(result.listed));
  assert.ok(result.listed[0].includes(path.basename(dir)) && result.listed[0].includes('the owner SID is not read on Windows (F16)'));
  assert.equal(fs.readFileSync(path.join(dir, 'sentinel.txt'), 'utf8'), `still ${path.basename(dir)}`);
  assert.equal(fs.readFileSync(`${dir}.owner`, 'utf8'), before, 'the claim is intact byte for byte');
  assert.deepEqual(quarantinesIn(root), []);
  assert.ok(lines.some((line) => line.includes('unverified: ')), 'the log carries the same');
});

/** The static `import` specifiers of a module's text (side-effect imports and `from` imports; a dynamic `import(` is not read). */
function importsOf(text) {
  return [...text.matchAll(/^\s*(?:import|export)\s[^'";]*?\bfrom\s*['"]([^'"]+)['"]|^\s*import\s*['"]([^'"]+)['"]/gm)].map((m) => m[1] ?? m[2]);
}

test('L5 the Windows sweep step needs no npm package: the sweep test\'s import graph is node: modules and repository files only', () => {
  const entry = [
    path.join(here, 'leftoversSweep.test.mjs'),
    path.join(here, '..', '..', 'scripts', 'run-tests.mjs'),
    path.join(here, '..', '..', 'scripts', 'owned.mjs'),
    path.join(here, '..', '..', 'scripts', 'startOfRun.mjs'),
    ...['leftovers-sweep.mjs', 'owned.mjs', 'owned-token.mjs', 'git.mjs'].map((name) => path.join(repoRoot, '.agents', 'conventions', 'tools', 'lib', name)),
  ];
  const seen = new Set();
  const queue = [...entry];
  while (queue.length > 0) {
    const file = queue.pop();
    if (seen.has(file)) continue;
    seen.add(file);
    for (const specifier of importsOf(fs.readFileSync(file, 'utf8'))) {
      if (specifier.startsWith('node:')) continue;
      assert.ok(specifier.startsWith('.'), `${path.relative(repoRoot, file)} imports ${specifier}: a bare package name would need \`npm ci\`, which the windows job does not run`);
      queue.push(path.resolve(path.dirname(file), specifier));
    }
  }
  assert.ok(seen.size >= entry.length, 'the walk read the files it was given');
});
