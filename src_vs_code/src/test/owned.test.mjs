import '../../scripts/inRunFolder.cjs';
import nodeFs, * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { syncBuiltinESMExports } from 'node:module';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  REPO_ROW, REPO_TOKEN, RunFolderRefused, SELF_TEST_VARIABLE, TEMP_VARIABLES, beginOwnedRun, owned, ownedScratch,
} from '../../scripts/owned.mjs';
import { startOwnedRun } from '../../scripts/ownedRun.mjs';

/**
 * The wrap (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.2, W1-W12): the ONE Node module that binds this repository's token and that makes
 * the owned RUN FOLDER of a runner or an entry script. The reference's own behaviour is the reference's tests (decision 11); these cases pin what the wrap adds:
 * the claim first and exclusive, naming THIS process, the withheld inherited record, the variables and the marker, the removal at exit.
 *
 * Every folder here is made inside a private root of the wrap itself (`owned.ownedDir(t, …)`), never with `mkdtemp`.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const MARKER = 'COAI_TEST_RUN_FOLDER';

const privateRoot = (t, what = 'w') => owned.ownedDir(t, what);
const quiet = () => {};

/** `fn` with the CommonJS `fs` methods replaced (and the ES module view synced), always restored. */
function withFs(replacements, fn) {
  const originals = {};
  for (const [name, make] of Object.entries(replacements)) {
    originals[name] = nodeFs[name];
    nodeFs[name] = make(originals[name]);
  }
  syncBuiltinESMExports();
  try {
    return fn();
  } finally {
    for (const [name, original] of Object.entries(originals)) nodeFs[name] = original;
    syncBuiltinESMExports();
  }
}

test('W1 the token and the row are the coai row of the Tokens table', () => {
  assert.equal(REPO_TOKEN, 'coai');
  const detail = fs.readFileSync(path.join(repoRoot, '.agents', 'conventions', 'common', 'test-leftovers-detail.md'), 'utf8');
  const row = detail.split('\n').find((line) => /^\|\s*`coai`\s*\|/.test(line));
  assert.ok(row, 'the coai row is in the Tokens table (found by content, not by line number)');
  assert.equal(REPO_ROW, row.split('|')[3].trim());
});

function sourcesUnder(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['node_modules', 'out', '.agents', 'dist', '.git'].includes(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) sourcesUnder(full, out);
    else if (/\.(mjs|cjs|js|ts)$/.test(entry.name)) out.push(full);
  }
  return out;
}

test('W2 no Node file but owned.mjs calls ownedFor', () => {
  const callers = [path.join(pkg, 'src'), path.join(pkg, 'scripts'), path.join(repoRoot, 'scripts'), path.join(repoRoot, 'http')]
    .flatMap((dir) => sourcesUnder(dir))
    .filter((file) => /\bownedFor\s*\(/.test(fs.readFileSync(file, 'utf8')))
    .map((file) => path.relative(repoRoot, file).split(path.sep).join('/'));
  // This file names the call in its own title and in this scan's pattern; the wrap is the only caller.
  assert.deepEqual(callers.filter((file) => !file.endsWith('src/test/owned.test.mjs')), ['src_vs_code/scripts/owned.mjs']);
});

test('W3 beginOwnedRun claims first, exclusively, then makes the folder', (t) => {
  const root = privateRoot(t, 'w3');
  const calls = [];
  const record = (kind) => (original) => function recorded(file, ...rest) {
    if (typeof file === 'string' && path.dirname(file) === root) calls.push({ kind, file, options: rest[1] });
    return original.call(this, file, ...rest);
  };
  const env = {};
  const run = withFs({ writeFileSync: record('claim'), mkdirSync: record('mkdir') }, () => beginOwnedRun({ what: 'w3', root, env, onExit: quiet, exit: quiet, selfTest: '' }));
  t.after(() => run.end());

  assert.deepEqual(calls.map((call) => call.kind), ['claim', 'mkdir']);
  assert.equal(calls[0].file, `${calls[1].file}.owner`, 'the claim is the sibling of the folder made next');
  assert.equal(calls[0].options?.flag, 'wx', 'exclusive');
  assert.equal(calls[1].file, run.folder);
});

test('W4 the claim names THIS process; an inherited TESTSTODELETE_RUN is withheld around it and put back', (t) => {
  const root = privateRoot(t, 'w4');
  const parent = owned.processProbe(process.ppid);
  const other = owned.formatOwner({ ...owned.thisOwner(), pid: process.ppid, ...(parent.start === undefined ? {} : { start: parent.start }) });
  const before = process.env.TESTSTODELETE_RUN;
  const claimPid = (run) => owned.parseOwner(fs.readFileSync(`${run.folder}.owner`, 'utf8').trim()).pid;
  try {
    process.env.TESTSTODELETE_RUN = other;
    const env = {};
    const run = beginOwnedRun({ what: 'w4', root, env, onExit: quiet, exit: quiet, selfTest: '' });
    t.after(() => run.end());
    assert.equal(claimPid(run), process.pid, 'the claim is this process\'s, not the inherited record\'s');
    assert.equal(process.env.TESTSTODELETE_RUN, other, 'the inherited value is back exactly');
    assert.equal(env.TESTSTODELETE_RUN, owned.formatOwner(owned.thisOwner()), 'the runner exports its OWN record to its children (the detail, §3)');

    delete process.env.TESTSTODELETE_RUN;
    const second = beginOwnedRun({ what: 'w4b', root, env: {}, onExit: quiet, exit: quiet, selfTest: '' });
    t.after(() => second.end());
    assert.equal(claimPid(second), process.pid);
    assert.equal(Object.hasOwn(process.env, 'TESTSTODELETE_RUN'), false, 'absent stays absent');
  } finally {
    if (before === undefined) delete process.env.TESTSTODELETE_RUN;
    else process.env.TESTSTODELETE_RUN = before;
  }
});

test('W5 a claim that cannot be written is a refusal, and nothing is left', (t) => {
  const root = privateRoot(t, 'w5');
  assert.equal(SELF_TEST_VARIABLE, 'COAI_RUNTEMP_SELFTEST');
  assert.throws(
    () => beginOwnedRun({ what: 'w5', root, env: {}, onExit: quiet, exit: quiet, selfTest: 'fail-claim' }),
    (error) => error instanceof RunFolderRefused && error.message.includes(root) && error.message.includes('EACCES'),
  );
  assert.deepEqual(fs.readdirSync(root), [], 'no claim and no folder');
  const probe = path.join(root, 'probe.owner');
  fs.writeFileSync(probe, 'x');
  fs.rmSync(probe);
});

test('W6 a missing root is a refusal naming it', (t) => {
  const root = path.join(privateRoot(t, 'w6'), 'missing');
  assert.throws(
    () => beginOwnedRun({ what: 'w6', root, env: {}, onExit: quiet, exit: quiet, selfTest: '' }),
    (error) => error instanceof RunFolderRefused && error.message.includes(root),
  );
  assert.equal(fs.existsSync(root), false, 'nothing was created');
});

test('W7 the variables and the marker name the folder; each mismatch self-test moves that ONE variable to the root', (t) => {
  const root = privateRoot(t, 'w7');
  const names = ['TEMP', 'TMP', 'TMPDIR'];
  const begin = (selfTest) => {
    const env = {};
    const run = beginOwnedRun({ what: 'w7', root, env, onExit: quiet, exit: quiet, selfTest });
    t.after(() => run.end());
    return { env, run };
  };

  const plain = begin('');
  for (const name of [...names, MARKER]) assert.equal(plain.env[name], plain.run.folder, name);
  assert.equal(path.dirname(plain.run.folder), root);

  for (const [value, moved] of [['mismatch-temp', 'TEMP'], ['mismatch-tmp', 'TMP'], ['mismatch-tmpdir', 'TMPDIR']]) {
    const { env, run } = begin(value);
    for (const name of names) assert.equal(env[name], name === moved ? root : run.folder, `${value}: ${name}`);
    assert.equal(env[MARKER], run.folder, `${value}: the marker still names the folder`);
  }
  const unknown = begin('no-such-value');
  for (const name of names) assert.equal(unknown.env[name], unknown.run.folder, 'an unknown value is ignored');
});

test('W8 end removes the folder, then its claim; twice is a no-op', (t) => {
  const root = privateRoot(t, 'w8');
  const run = beginOwnedRun({ what: 'w8', root, env: {}, onExit: quiet, exit: quiet, selfTest: '' });
  fs.writeFileSync(path.join(run.folder, 'inside.txt'), 'x');
  assert.ok(fs.existsSync(`${run.folder}.owner`));

  run.end();

  assert.equal(fs.existsSync(run.folder), false);
  assert.equal(fs.existsSync(`${run.folder}.owner`), false);
  assert.doesNotThrow(() => run.end());
});

test('W8 the end is registered with the exit hook the caller gives', (t) => {
  const root = privateRoot(t, 'w8x');
  const hooks = [];
  const run = beginOwnedRun({ what: 'w8x', root, env: {}, onExit: (fn) => hooks.push(fn), exit: quiet, selfTest: '' });
  assert.equal(hooks.length, 1);
  hooks[0]();
  assert.equal(fs.existsSync(run.folder), false, 'the exit hook alone removes the folder');
});

test('W9 exit-after-begin exits 3 after the folder exists, and the exit removes it', (t) => {
  const root = privateRoot(t, 'w9');
  const script = `import { startOwnedRun } from ${JSON.stringify(pathToFileURL(path.join(pkg, 'scripts', 'ownedRun.mjs')).href)};\n`
    + 'const run = await startOwnedRun({ what: \'w9\' }); console.log(\'after\', run.folder);\n';
  const env = { ...process.env };
  delete env.NODE_TEST_CONTEXT;
  delete env[MARKER];
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], {
    encoding: 'utf8', timeout: 60_000, env: { ...env, TEMP: root, TMP: root, TMPDIR: root, [SELF_TEST_VARIABLE]: 'exit-after-begin' },
  });

  assert.equal(result.status, 3, `${result.stdout}${result.stderr}`);
  assert.doesNotMatch(result.stdout, /after/, 'nothing ran after the run began');
  assert.deepEqual(fs.readdirSync(root), [], 'the exit removed the folder and its claim');
});

test('W10 the run folder is mine under the root as given and as resolved', (t) => {
  const base = privateRoot(t, 'w10');
  const real = path.join(base, 'real');
  fs.mkdirSync(real);
  const alias = path.join(base, 'alias');
  try {
    fs.symlinkSync(real, alias, 'dir');
  } catch (error) {
    t.skip(`a link cannot be made here (${error.code})`);
    return;
  }
  const roots = [alias];
  if (process.platform === 'darwin') {
    assert.notEqual(owned.tempRoot(), fs.realpathSync.native(owned.tempRoot()), 'the premise: the temp root is itself a link (/var is /private/var)');
    roots.push(owned.tempRoot());
  }
  for (const root of roots) {
    const run = beginOwnedRun({ what: 'w10', root, env: {}, onExit: quiet, exit: quiet, selfTest: '' });
    t.after(() => run.end());
    assert.equal(path.dirname(run.folder), root, 'the folder is under the root AS GIVEN, not as resolved');
    const resolved = fs.realpathSync.native(run.folder);
    assert.ok(owned.isMine(run.folder));
    assert.ok(owned.isMine(resolved));
    assert.deepEqual(
      owned.parseOwner(fs.readFileSync(`${run.folder}.owner`, 'utf8').trim()),
      owned.parseOwner(fs.readFileSync(`${resolved}.owner`, 'utf8').trim()),
      'the claim reads the same both ways',
    );
  }
});

test('W11 ownedScratch claims for this process, leaves TEMP alone, and is removed at exit', (t) => {
  const root = privateRoot(t, 'w11');
  const temp = TEMP_VARIABLES.map((name) => process.env[name]);
  const hooks = [];
  const scratch = ownedScratch('w11', { root, onExit: (fn) => hooks.push(fn), selfTest: '' });
  t.after(() => scratch.end());

  assert.equal(path.dirname(scratch.path), root);
  assert.equal(owned.classifyName(path.basename(scratch.path)).form, 'owner');
  assert.equal(owned.parseOwner(fs.readFileSync(`${scratch.path}.owner`, 'utf8').trim()).pid, process.pid);
  assert.deepEqual(TEMP_VARIABLES.map((name) => process.env[name]), temp, 'TEMP, TMP and TMPDIR are not touched: a probe that measures where a vendor CLI writes must not have it moved');
  assert.equal(hooks.length, 1);

  hooks[0]();
  assert.equal(fs.existsSync(scratch.path), false);
  assert.equal(fs.existsSync(`${scratch.path}.owner`), false);
  assert.doesNotThrow(() => scratch.end());
});

test('W12 startOwnedRun with no mount is one line and exit 2, and nothing is made', async (t) => {
  const root = privateRoot(t, 'w12');
  const errors = [];
  const exits = [];
  await startOwnedRun({
    what: 'w12', importOwned: async () => { throw new Error('Cannot find module owned.mjs'); }, error: (line) => errors.push(line), exit: (code) => { exits.push(code); }, root,
  });

  assert.deepEqual(exits, [2]);
  assert.equal(errors.length, 1);
  assert.match(errors[0], /git submodule update --init \.agents\/conventions/);
  assert.deepEqual(fs.readdirSync(root), []);
});

const childEnv = (root, extra = {}) => {
  const env = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN', SELF_TEST_VARIABLE]) delete env[name];
  return { ...env, TEMP: root, TMP: root, TMPDIR: root, ...extra };
};
const ownedRunUrl = pathToFileURL(path.join(pkg, 'scripts', 'ownedRun.mjs')).href;

test('W13 the run folder and the scratch are owner-only (0700 on POSIX): mkdtemp gave that, the reference\'s mkdir gives 0755 under umask 022', (t) => {
  const root = privateRoot(t, 'w13');
  const run = beginOwnedRun({ what: 'w13', root, env: {}, onExit: quiet, exit: quiet, selfTest: '' });
  t.after(() => run.end());
  const scratch = ownedScratch('w13s', { root, onExit: quiet, selfTest: '' });
  t.after(() => scratch.end());
  if (process.platform === 'win32') {
    t.skip('Windows has no mode bits: the profile\'s ACL is what it relies on');
    return;
  }
  assert.equal(fs.statSync(run.folder).mode & 0o777, 0o700, 'the run folder');
  assert.equal(fs.statSync(scratch.path).mode & 0o777, 0o700, 'the scratch');
});

test('W14 the default scratch of a probe says ONE line at exit: removed at exit, and how to keep it (two probes have no --reparse)', (t) => {
  const root = privateRoot(t, 'w14');
  const script = `import { startDefaultScratch } from ${JSON.stringify(ownedRunUrl)};\nconst s = await startDefaultScratch({ what: 'w14', prefix: 'probe-x' }); console.log(s.path);\n`;
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], { encoding: 'utf8', timeout: 60_000, env: childEnv(root) });

  assert.equal(result.status, 0, `${result.stdout}${result.stderr}`);
  const lines = result.stderr.trimEnd().split('\n');
  assert.equal(lines.length, 1, result.stderr);
  assert.equal(lines[0], `probe-x: scratch was ${result.stdout.trim()}, removed at exit — pass --scratch <dir> to keep it`);
  assert.deepEqual(fs.readdirSync(root), []);
});

test('W15 a temp root that is not there is ONE line and exit 78 for a default scratch, not a stack', (t) => {
  const root = path.join(privateRoot(t, 'w15'), 'missing');
  const script = `import { startDefaultScratch } from ${JSON.stringify(ownedRunUrl)};\nawait startDefaultScratch({ what: 'w15', prefix: 'probe-x' });\nconsole.log('went on');\n`;
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], { encoding: 'utf8', timeout: 60_000, env: childEnv(root) });

  assert.equal(result.status, 78, `${result.stdout}${result.stderr}`);
  assert.equal(result.stderr.trimEnd().split('\n').length, 1, result.stderr);
  assert.match(result.stderr, /^probe-x: nothing ran — .*missing/u);
  assert.doesNotMatch(result.stdout, /went on/u);
});
