import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { PREFIX as LEGACY_WORD } from '../../../.agents/conventions/tools/lib/owned.mjs';
import { owned } from '../../scripts/owned.mjs';

/**
 * The Node wrapper of the repository-wide check (E5 S6, `todo/PLAN_test_leftovers_E5_S6_brief.md` § 7.4): the SHIPPED guard (`.agents/conventions/tools/leftovers-check.mjs`,
 * `--profile node`) over this tree, with the `raw-temp` baseline. The guard is the mount's, not a port: the C# half has its own guard (`LeftoversRepositoryGuardTests`).
 *
 * What the shipped Node guard finds in a `.mjs` is what N8 pins and nothing more: it has no reader that folds a word split over `+`, `.concat`, `.join('')`, a template hole, a
 * `\u` or `\x` escape, a one-character class or half of the word (F30 of the conventions' follow-ups plan owns that). So this repository's zero for `legacy-name` and
 * `owner-blind` is zero WHOLE literals, F30 open. No test here, and no sentence of the PR, may read as if the guard folds.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const git = (cwd, args) => spawnSync('git', args, { cwd, encoding: 'utf8' });
const repo = git(here, ['rev-parse', '--show-toplevel']).stdout.trim();
const guard = path.join(repo, '.agents', 'conventions', 'tools', 'leftovers-check.mjs');
const IDENTITY = 'the multi-model review extension and server';
const INVENTORY = path.join(repo, 'shared', 'leftovers-exemptions.json');
const GUARD_URL = pathToFileURL(guard).href;
const BASELINE_URL = pathToFileURL(path.join(repo, '.agents', 'conventions', 'tools', 'lib', 'raw-temp-baseline.mjs')).href;
const MARKER_WORD = `${LEGACY_WORD}exempt:`;

/** The two temp APIs of a planted line, in pieces: this file is read by the guard it tests, and a whole spelling would be a `raw-temp` site of its own. */
const MAKE_TEMP = ['mkd', 'temp', 'Sync'].join('');
const TEMP_ROOT = ['tmp', 'dir'].join('');
const plantedLine = (what) => `const d = ${MAKE_TEMP}(join(${TEMP_ROOT}(), '${what}-'));`;

const COMMON = ['--profile', 'node', '--token', 'coai', '--identity', IDENTITY];
const runGuard = (root, extra = []) => spawnSync(process.execPath, [guard, '--repo', root, ...extra], { encoding: 'utf8', timeout: 120_000 });
const said = (result) => `${result.stdout}${result.stderr}`;

/** A private git repository in the run folder. `--repo` is what GIT says the root is, never the folder as it was built: on the hosted Windows runner the temp path holds a short name (RUNNER~1). */
function privateRepo(t, what, files) {
  const dir = owned.ownedDir(t, what);
  assert.equal(git(dir, ['init', '-q']).status, 0, 'git init');
  const root = path.resolve(git(dir, ['rev-parse', '--show-toplevel']).stdout.trim());
  write(root, files);
  return root;
}

function write(root, files) {
  for (const [name, text] of Object.entries(files)) {
    const target = path.join(root, ...name.split('/'));
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
  }
}

const commit = (root) => {
  const run = (...args) => assert.equal(git(root, ['-c', 'user.name=t', '-c', 'user.email=t@example.com', ...args]).status, 0, args.join(' '));
  run('add', '-A');
  run('commit', '-q', '-m', 'base');
};

test('N1 the shipped guard is clean on this tree', () => {
  const result = runGuard(repo, COMMON);
  assert.equal(result.status, 0, `the guard answered ${result.status}:\n${said(result)}`);
});

test('N2 exit 1 and exit 2 are red with the tool\'s sentence', (t) => {
  const planted = privateRepo(t, 'guard-planted', { 'scripts/x.mjs': `${plantedLine('x')}\n` });
  const red = runGuard(planted, COMMON);
  assert.equal(red.status, 1, said(red));
  assert.match(said(red), /scripts\/x\.mjs:1 — raw-temp/u);

  const empty = privateRepo(t, 'guard-empty', { 'README.md': 'nothing a guard reads\n' });
  const nothing = runGuard(empty, COMMON);
  assert.equal(nothing.status, 2, said(nothing));
  assert.match(said(nothing), /scanned nothing/u);
});

/** The site of a marker: the line under it, or its own line when code stands before the comment. */
function siteOf(line, markerLine) {
  const at = line.indexOf(MARKER_WORD);
  const opener = /(?:\/\/|#|--|\/\*|\*|<!--)\s*$/u.exec(line.slice(0, at));
  return opener !== null && line.slice(0, opener.index).trim() === '' ? markerLine + 1 : markerLine;
}

/** Every valid marker the Node guard reads, as `file:site:class`, with the hash of the site's trimmed line. */
async function markersOnDisk() {
  const { discover, readExemptions } = await import(GUARD_URL);
  const { siteSha256 } = await import(BASELINE_URL);
  const found = [];
  for (const file of discover(repo, 'node')) {
    let bytes;
    try {
      bytes = fs.readFileSync(path.join(repo, file));
    } catch {
      continue; // a path that is gone is skipped, as the guard skips it
    }
    if (bytes.subarray(0, 8192).includes(0)) continue; // binary: not text to be read
    const lines = bytes.toString('utf8').split(/\r?\n/u);
    for (const [cls, set] of readExemptions(file, lines).valid) {
      for (const markerLine of set) {
        const site = siteOf(lines[markerLine - 1], markerLine);
        found.push({ file, line: site, class: cls, sha256: siteSha256(lines[site - 1] ?? '') });
      }
    }
  }
  return found;
}

test('N3 the Node half of the inventory: the markers on disk are the rows, and each row holds its site\'s hash', async () => {
  const inventory = JSON.parse(fs.readFileSync(INVENTORY, 'utf8'));
  const rows = inventory.rows.filter((row) => row.guards.includes('node'));
  const onDisk = await markersOnDisk();
  const key = (entry) => `${entry.file}:${entry.line}:${entry.class}`;
  const inTree = new Map(onDisk.map((entry) => [key(entry), entry]));
  const inRows = new Map(rows.map((entry) => [key(entry), entry]));
  const problems = [
    ...[...inTree.keys()].filter((name) => !inRows.has(name)).map((name) => `in the tree, not in the inventory: ${name}`),
    ...[...inRows.keys()].filter((name) => !inTree.has(name)).map((name) => `in the inventory, not in the tree: ${name}`),
    ...[...inRows].filter(([name, row]) => inTree.has(name) && inTree.get(name).sha256 !== row.sha256)
      .map(([name, row]) => `the site changed under its marker: ${name} — the inventory holds ${row.sha256}, the line hashes to ${inTree.get(name).sha256}`),
  ];
  assert.deepEqual(problems, [], 'update shared/leftovers-exemptions.json in the same change as the marker or the site');
});

test('N4 every root of the scan has at least one file', async () => {
  const { discover } = await import(GUARD_URL);
  const files = discover(repo, 'node');
  for (const root of ['src_vs_code/src/test/', 'scripts/', '.github/workflows/']) {
    assert.ok(files.some((file) => file.startsWith(root)), `scanned nothing under ${root} — discovery broke; the repository is not clean`);
  }
});

test('N5 the token is a registered row and written once', async (t) => {
  const { scanRepository } = await import(GUARD_URL);
  const tokenTexts = (root, token) => scanRepository({ repo: root, token, identity: IDENTITY, profile: 'node' }).findings.filter((entry) => entry.cls === 'token').map((entry) => entry.text);
  // The constants are spelled in pieces: the guard reads THIS file for a token constant too.
  const CS_NAME = ['Repo', 'Token'].join('');
  const JS_NAME = ['REPO', '_TOKEN'].join('');
  const csharp = (value) => ({ 'src/OwnedToken.cs': `public const string ${CS_NAME} = "${value}";\n` });

  const real = privateRepo(t, 'token-real', csharp('coai'));
  assert.deepEqual(tokenTexts(real, 'coai'), [], 'the real mount lists coai, and it is written once');

  const apart = privateRepo(t, 'token-apart', { ...csharp('coai'), 'scripts/owned-token.mjs': `export const ${JS_NAME} = 'other';\n` });
  const apartTexts = tokenTexts(apart, 'coai');
  assert.ok(apartTexts.some((text) => /token constants differ: .*`other` in scripts\/owned-token\.mjs:1.*`coai` in src\/OwnedToken\.cs:1/u.test(text)), apartTexts.join('\n'));

  const capital = tokenTexts(real, 'Coai');
  assert.equal(capital.length, 1, capital.join('\n'));
});

const PROOF = `${LEGACY_WORD}baseline-proof:`;
const baselineOf = (entries, scopes = [{ root: 'scripts/', proof: 'scripts/runFolder.test.mjs', guard: 'node' }]) =>
  `${JSON.stringify({ about: 'raw-temp sites inside the owned run folder; only shrinks', class: 'raw-temp', scopes, entries }, null, 2)}\n`;
const proofFile = `// ${PROOF} scripts/\n`;
const rawLine = plantedLine('x');

async function hashOf(text) {
  const { siteSha256 } = await import(BASELINE_URL);
  return siteSha256(text);
}

test('N6 the baseline only shrinks', async (t) => {
  const sha = await hashOf(rawLine);
  const shaB = await hashOf(plantedLine('y'));
  const base = { file: 'scripts/a.mjs', sha256: sha, line: 1 };
  const files = { 'scripts/runFolder.test.mjs': proofFile, 'scripts/a.mjs': `${rawLine}\n`, 'raw-temp-baseline.json': baselineOf([base]) };
  const shrinks = (root, extra = []) => runGuard(root, ['--baseline-shrinks', '--base', 'HEAD', ...extra]);
  const changed = (name, file, expect, sentence) => {
    const root = privateRepo(t, name, files);
    commit(root);
    write(root, { 'raw-temp-baseline.json': file });
    const result = shrinks(root);
    assert.equal(result.status, expect, `${name}: ${said(result)}`);
    if (sentence !== undefined) assert.match(said(result), sentence, name);
  };

  changed('shrink-same', baselineOf([base]), 0, /only shrinks against HEAD/u);
  changed('shrink-removed', baselineOf([]), 0, /only shrinks against HEAD/u);
  changed('shrink-new-key', baselineOf([base, { file: 'scripts/a.mjs', sha256: shaB, line: 2 }]), 1, /new key/u);
  changed('shrink-raised', baselineOf([base, { ...base, line: 2 }]), 1, /raised count/u);
  changed('shrink-new-scope', baselineOf([base], [{ root: 'scripts/', proof: 'scripts/runFolder.test.mjs', guard: 'node' }, { root: 'tools/', proof: 'tools/p.test.mjs', guard: 'node' }]), 1, /new scope/u);
  changed('shrink-proof', baselineOf([base], [{ root: 'scripts/', proof: 'scripts/other.test.mjs', guard: 'node' }]), 1, /changed proof/u);
  changed('shrink-guard', baselineOf([base], [{ root: 'scripts/', proof: 'scripts/runFolder.test.mjs', guard: 'csharp' }]), 1, /changed guard/u);

  const created = privateRepo(t, 'shrink-create', { 'README.md': 'x\n' });
  commit(created);
  write(created, { 'raw-temp-baseline.json': baselineOf([base]) });
  const creation = shrinks(created);
  assert.equal(creation.status, 1, said(creation));
  assert.match(said(creation), /a creation/u, 'without the creation flag a creation is refused');
});

/** Findings of the guard with the baseline on a private tree, as `file:line — class` lines. */
function findingsOf(root) {
  const result = runGuard(root, ['--baseline', ...COMMON]);
  const lines = said(result).split(/\r?\n/u).map((line) => line.trim()).filter((line) => /^\S+:\d+ — /u.test(line));
  return { status: result.status, lines, text: said(result) };
}

test('N7 the baseline\'s second pass', async (t) => {
  const sha = await hashOf(rawLine);
  const other = plantedLine('y');
  const shaOther = await hashOf(other);
  const proofAndRaw = (body) => ({ 'scripts/runFolder.test.mjs': proofFile, 'scripts/a.mjs': body });
  const entry = (hash, line = 1) => ({ file: 'scripts/a.mjs', sha256: hash, line });
  const tree = (name, body, entries, scopes) => {
    const root = privateRepo(t, name, { ...proofAndRaw(body), 'raw-temp-baseline.json': baselineOf(entries, scopes) });
    return findingsOf(root);
  };

  const clean = tree('second-clean', `${rawLine}\n`, [entry(sha)]);
  assert.equal(clean.status, 0, clean.text);

  const twice = tree('second-twice', `${rawLine}\n${rawLine}\n`, [entry(sha)]);
  assert.equal(twice.status, 1, twice.text);
  assert.deepEqual(twice.lines.map((line) => line.replace(/ — .*$/u, '')), ['scripts/a.mjs:2'], 'the second identical line is a finding, the first one passes');
  assert.match(twice.lines[0], / — raw-temp/u);

  const swapped = tree('second-swapped', `${rawLine}\n${other}\n`, [entry(sha), entry(await hashOf('const z = 1;'), 2)]);
  assert.equal(swapped.status, 1, swapped.text);
  assert.ok(swapped.lines.some((line) => / — raw-temp/u.test(line)), swapped.text);
  assert.ok(swapped.lines.some((line) => / — baseline/u.test(line)), swapped.text);

  const stale = tree('second-stale', `${rawLine}\n`, [entry(sha), entry(shaOther, 2)]);
  assert.equal(stale.status, 1, stale.text);
  assert.ok(stale.lines.some((line) => / — baseline/u.test(line) && /no line/u.test(line)), stale.text);

  const noMarker = privateRepo(t, 'second-noproof', { 'scripts/runFolder.test.mjs': '// nothing here\n', 'scripts/a.mjs': `${rawLine}\n`, 'raw-temp-baseline.json': baselineOf([entry(sha)]) });
  const noProof = findingsOf(noMarker);
  assert.equal(noProof.status, 1, noProof.text);
  assert.match(noProof.text, /proof missing/u);

  const moved = tree('second-moved', `\n\n${rawLine}\n`, [entry(sha, 1)]);
  assert.equal(moved.status, 0, `a moved line keeps its entry: ${moved.text}`);
});

// N8 -------------------------------------------------------------------------------------------------------------------------------------------------------

const noYaml = () => { throw new Error('no workflow is read here'); };

/** What the shipped guard answers for one line of a `.mjs`: the classes, sorted. The word comes from the mount, so this file holds no whole literal of it. */
async function classesOf(line) {
  const { scanText } = await import(GUARD_URL);
  return [...new Set(scanText('scripts/zz.mjs', line, [], noYaml).map((entry) => entry.cls))].sort();
}

test('N8 what the shipped guard detects in a .mjs: a whole legacy literal, and a bare-word glob over the word (F30 owns the rest)', async () => {
  assert.deepEqual(await classesOf(`const name = '${LEGACY_WORD}coai-x-abc123';`), ['legacy-name']);
  assert.deepEqual(await classesOf(`for (const f of fs.globSync('${LEGACY_WORD}*')) rmSync(f, { recursive: true });`), ['owner-blind']);
  assert.deepEqual(await classesOf(`const name = '${LEGACY_WORD}u1000-coai-x-abc123';`), []);
  // Pin what is detected, never what is not: when F30 ships, a spelling that answers [] today becomes a finding, and this test must stay green.
});
