import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import guard from '../../scripts/runFolderGuard.cjs';
import { discover } from '../../scripts/run-tests.mjs';
import { owned } from '../../scripts/owned.mjs';

/**
 * A test file started DIRECTLY refuses (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.3, G1-G6; plan P1): its FIRST import checks the marker the runner sets
 * (`COAI_TEST_RUN_FOLDER`, K1) and the three temp variables against it (K6), and ends the process with ONE line and exit 78 before the file's other imports
 * and its tests run. `node --test x` reports that file as failed, so no direct start passes quietly.
 *
 * Every folder here is a private root of the wrap; the children run with TEMP, TMP and TMPDIR pointed at it, the marker removed, and must leave it empty.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const MARKER = 'COAI_TEST_RUN_FOLDER';
const PREFIX = 'coai tests: no test ran — ';
const GUARD = path.join(pkg, 'scripts', 'inRunFolder.cjs');

function childEnv(root) {
  const env = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN']) delete env[name];
  return { ...env, TEMP: root, TMP: root, TMPDIR: root };
}

test('G1 refusal(): the table', (t) => {
  assert.equal(guard.MARKER, MARKER);
  assert.equal(guard.REFUSED_EXIT_CODE, 78);
  assert.equal(guard.REFUSAL_PREFIX, PREFIX);
  const root = owned.ownedDir(t, 'g1');
  const folder = path.join(root, 'folder');
  fs.mkdirSync(folder);
  fs.writeFileSync(`${folder}.owner`, 'claim\n');
  const good = { [MARKER]: folder, TEMP: folder, TMP: folder, TMPDIR: folder };
  const other = path.join(root, 'other');
  const asFile = path.join(root, 'a-file');
  fs.writeFileSync(asFile, 'x');
  const noClaim = path.join(root, 'no-claim');
  fs.mkdirSync(noClaim);
  const claimIsDirectory = path.join(root, 'claim-dir');
  fs.mkdirSync(claimIsDirectory);
  fs.mkdirSync(`${claimIsDirectory}.owner`);
  const all = (marker) => ({ [MARKER]: marker, TEMP: marker, TMP: marker, TMPDIR: marker });

  assert.equal(guard.refusal({ env: good }), null, 'all good');
  const cases = [
    ['unset', {}, MARKER],
    ['empty', { ...good, [MARKER]: '' }, MARKER],
    ['relative', all('relative/folder'), 'relative/folder'],
    ['missing', all(path.join(root, 'missing')), path.join(root, 'missing')],
    ['a file at the path', all(asFile), asFile],
    ['no claim', all(noClaim), `${noClaim}.owner`],
    ['the claim a directory', all(claimIsDirectory), `${claimIsDirectory}.owner`],
    ['TEMP differs', { ...good, TEMP: other }, `TEMP='${other}'`],
    ['TMP differs', { ...good, TMP: other }, `TMP='${other}'`],
    ['TMPDIR differs', { ...good, TMPDIR: other }, `TMPDIR='${other}'`],
    ['TEMP unset', { ...good, TEMP: undefined }, 'TEMP'],
  ];
  for (const [name, env, mention] of cases) {
    const reason = guard.refusal({ env });
    assert.equal(typeof reason, 'string', `${name} is refused`);
    assert.ok(reason.includes(mention), `${name}: the reason names what failed (${mention}): ${reason}`);
    assert.equal(reason.includes('\n'), false, `${name}: one line`);
  }
  const line = guard.line('x');
  assert.ok(line.startsWith(PREFIX));
  assert.equal(line.includes('\n'), false);
  assert.match(line, /run-tests\.mjs/);
  assert.match(line, /run-lib-tests\.mjs/);
});

function lineCount(result) {
  return `${result.stdout}${result.stderr}`.split('\n').filter((line) => line.includes(PREFIX)).length;
}

test('G2 a compiled test started with node --test refuses before its tests', (t) => {
  const root = owned.ownedDir(t, 'g2');
  const file = path.join('out', 'test', 'runtimeSinceIsAhead.test.js');
  assert.ok(fs.existsSync(path.join(pkg, file)), `${file} is not compiled — run npm run compile first`);

  const result = spawnSync(process.execPath, ['--test', file], { cwd: pkg, encoding: 'utf8', env: childEnv(root), timeout: 60_000 });

  assert.equal(result.status, 1, `${result.stdout}${result.stderr}`.slice(0, 1500));
  assert.ok(lineCount(result) >= 1, 'the refusal line is in the output');
  const passed = /(?:ℹ|#) pass (\d+)/.exec(result.stdout);
  assert.ok(passed === null || Number(passed[1]) === 0, `no test passed: ${passed?.[0]}`);
  assert.deepEqual(fs.readdirSync(root), [], 'nothing was created');
});

test('G3 a compiled test started with plain node refuses with exit 78 and one line', (t) => {
  const root = owned.ownedDir(t, 'g3');
  const result = spawnSync(process.execPath, [path.join('out', 'test', 'runtimeSinceIsAhead.test.js')], { cwd: pkg, encoding: 'utf8', env: childEnv(root), timeout: 60_000 });

  assert.equal(result.status, 78, `${result.stdout}${result.stderr}`.slice(0, 1500));
  assert.equal(result.stderr.trimEnd().split('\n').length, 1, result.stderr);
  assert.ok(result.stderr.startsWith(PREFIX));
  assert.deepEqual(fs.readdirSync(root), []);
});

test('G4 a scripts/lib test started directly refuses', (t) => {
  const root = owned.ownedDir(t, 'g4');
  const result = spawnSync(process.execPath, ['--test', path.join('scripts', 'lib', 'kimi.test.mjs')], { cwd: repoRoot, encoding: 'utf8', env: childEnv(root), timeout: 60_000 });

  assert.equal(result.status, 1, `${result.stdout}${result.stderr}`.slice(0, 1500));
  assert.ok(lineCount(result) >= 1);
  assert.deepEqual(fs.readdirSync(root), []);
});

/** The first line of CODE of a file: past blank lines, a shebang, `//` and block comments and `'use strict'`. */
function firstCodeLine(text) {
  let inBlock = false;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (inBlock) {
      if (line.includes('*/')) inBlock = false;
      continue;
    }
    if (line === '' || line.startsWith('//') || line.startsWith('#!') || /^['"]use strict['"];?$/.test(line)) continue;
    if (line.startsWith('/*')) {
      inBlock = !line.includes('*/');
      continue;
    }
    return line;
  }
  return '';
}

const posix = (file) => file.split(path.sep).join('/');
const relativeTo = (file) => {
  const relative = posix(path.relative(path.dirname(file), GUARD));
  return relative.startsWith('.') ? relative : `./${relative}`;
};

function testSources() {
  const { compiled, sources } = discover(pkg);
  const ts = compiled.map((file) => path.join(pkg, 'src', 'test', `${path.basename(file, '.js')}.ts`));
  const contracts = fs.readdirSync(path.join(pkg, 'src', 'test')).filter((name) => name.endsWith('.contract.ts')).map((name) => path.join(pkg, 'src', 'test', name));
  const walk = (dir) => fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => (entry.isDirectory()
    ? walk(path.join(dir, entry.name))
    : entry.name.endsWith('.test.mjs') ? [path.join(dir, entry.name)] : []));
  return [...new Set([...ts, ...sources, ...contracts, ...walk(path.join(repoRoot, 'scripts'))])];
}

test('G5 every test file imports the guard first', () => {
  const files = testSources();
  assert.ok(files.length > 500, `the set is the whole suite (${files.length})`);
  const failures = [];
  for (const file of files) {
    const expected = `import '${relativeTo(file)}';`;
    const found = firstCodeLine(fs.readFileSync(file, 'utf8'));
    if (found !== expected) failures.push(`${path.relative(repoRoot, file)}: first code line is ${JSON.stringify(found.slice(0, 80))}`);
  }
  const out = path.join(pkg, 'out', 'test');
  for (const name of fs.readdirSync(out).filter((n) => n.endsWith('.test.js') || n.endsWith('.contract.js'))) {
    const first = fs.readFileSync(path.join(out, name), 'utf8').split('\n').find((line) => line.includes('require('));
    if (first?.trim() !== 'require("../../scripts/inRunFolder.cjs");') failures.push(`out/test/${name}: first require is ${JSON.stringify(first?.trim())}`);
  }
  assert.deepEqual(failures.slice(0, 15), [], `${failures.length} file(s) do not import the guard first`);
});

test('G6 nothing but a test file imports the guard', () => {
  const holders = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (['node_modules', 'out', 'dist', '.agents', '.git'].includes(entry.name)) continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (/\.(mjs|cjs|js|ts)$/.test(entry.name) && !/\.test\.|\.contract\.ts$/.test(entry.name)
        && !['inRunFolder.cjs', 'runFolderGuard.cjs'].includes(entry.name) && fs.readFileSync(full, 'utf8').includes('inRunFolder.cjs')) {
        holders.push(posix(path.relative(repoRoot, full)));
      }
    }
  };
  for (const dir of [path.join(pkg, 'src'), path.join(pkg, 'scripts'), path.join(repoRoot, 'scripts'), path.join(repoRoot, 'http')]) walk(dir);
  assert.deepEqual(holders, []);
});
