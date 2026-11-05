import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * The entry scripts (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.4, § 6.4, E1-E3): every runner, live-run and measure script owns ONE run folder, made as the
 * FIRST statement after its imports, with TEMP, TMP and TMPDIR moved there (K8). The libraries they import (the twelve `seam-*.mjs` legs, `browserLayout.mjs`) get
 * none: their entry's folder contains them. `run-tests.mjs` and `run-host.mjs` have their own test files.
 *
 * E3 spawns an entry ONLY after E2 proved that its run begins before anything else: the live scripts call paid vendors, and a script that did not start its run
 * first must never be started here.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const SELF_TEST = 'COAI_RUNTEMP_SELFTEST';
const MARKER = 'COAI_TEST_RUN_FOLDER';

const inPkg = (file, what) => ({ file: `src_vs_code/scripts/${file}`, what });
const ENTRIES = Object.freeze([
  inPkg('run-seam.mjs', 'seam'), inPkg('run-contract.mjs', 'contract'), inPkg('run-parity.mjs', 'parity'),
  inPkg('live-agent.mjs', 'live-agent'), inPkg('live-api-settings-compat.mjs', 'compat-api'), inPkg('live-chat.mjs', 'live-chat'),
  inPkg('live-claude-models.mjs', 'live-models'), inPkg('live-close-consult-compat.mjs', 'compat-close'),
  inPkg('live-command-models-compat.mjs', 'compat-cmd-models'), inPkg('live-feature-schema-compat.mjs', 'compat-schema'),
  inPkg('live-feature-vendor-compat.mjs', 'compat-vendor'), inPkg('live-fresh.mjs', 'live-fresh'), inPkg('live-gauge-compat.mjs', 'compat-gauge'),
  inPkg('live-paths-per-side-compat.mjs', 'compat-paths'), inPkg('live-security-settings-compat.mjs', 'compat-security'),
  inPkg('live-vibe-runtime-compat.mjs', 'compat-vibe'), inPkg('measure-stream.mjs', 'measure-stream'), inPkg('measure-image.mjs', 'measure-image'),
  inPkg('measure-append.mjs', 'measure-append'), inPkg('measure-consultant-skew.mjs', 'measure-skew'), inPkg('measure-help-clicks.mjs', 'measure-clicks'),
  inPkg('measure-failure-layout.mjs', 'measure-failure'), inPkg('measure-answer-code-layout.mjs', 'measure-code'), inPkg('render-page.mjs', 'render-page'),
  { file: 'http/run-contracts.mjs', what: 'http-contracts' },
  { file: 'scripts/live-consult-all.mjs', what: 'live-consult' },
  { file: 'scripts/measure-idle-server.mjs', what: 'measure-idle' },
]);
const OWN_TESTS = new Set(['src_vs_code/scripts/run-tests.mjs', 'src_vs_code/scripts/run-host.mjs']);

test('E1 every runner, live and measure script is in the list', () => {
  const named = fs.readdirSync(path.join(pkg, 'scripts'))
    .filter((name) => /^(run-|live-|measure-).*\.mjs$|^render-page\.mjs$/.test(name))
    .map((name) => `src_vs_code/scripts/${name}`)
    .filter((file) => !OWN_TESTS.has(file));
  const expected = [...named, 'http/run-contracts.mjs', 'scripts/live-consult-all.mjs', 'scripts/measure-idle-server.mjs'].sort();
  assert.deepEqual(ENTRIES.map((entry) => entry.file).sort(), expected);
  for (const { file, what } of ENTRIES) {
    assert.ok(fs.existsSync(path.join(repoRoot, file)), file);
    assert.ok(what.length <= 17, `${file}: \`what\` ${what} is within the reference's budget`);
  }
});

/** The text after the LAST static import (a side-effect import included), past blank lines and comments. */
function firstStatementAfterImports(text) {
  const imports = [...text.matchAll(/^import\s[^;]*;[^\n]*$/gm)];
  const end = imports.length === 0 ? 0 : imports.at(-1).index + imports.at(-1)[0].length;
  return text.slice(end).replace(/^(\s+|\/\/[^\n]*|\/\*[\s\S]*?\*\/)+/, '');
}

function passesE2(entry) {
  const statement = firstStatementAfterImports(fs.readFileSync(path.join(repoRoot, entry.file), 'utf8'));
  return statement.startsWith(`const run = await startOwnedRun({ what: '${entry.what}'`);
}

for (const entry of ENTRIES) {
  test(`E2 ${entry.file} starts its owned run before anything else`, () => {
    const statement = firstStatementAfterImports(fs.readFileSync(path.join(repoRoot, entry.file), 'utf8'));
    assert.ok(statement.startsWith(`const run = await startOwnedRun({ what: '${entry.what}'`), `the first statement after the imports is: ${statement.slice(0, 100)}`);
  });

  test(`E3 ${entry.file}, stopped right after its run began, leaves nothing`, (t) => {
    if (!passesE2(entry)) {
      assert.fail('E2 failed for this entry: it is never spawned here (the live scripts call paid vendors)');
    }
    const root = owned.ownedDir(t, 'e3');
    const env = { ...process.env };
    for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN']) delete env[name];

    const result = spawnSync(process.execPath, [path.join(repoRoot, entry.file)], {
      cwd: path.dirname(path.join(repoRoot, entry.file)), encoding: 'utf8', timeout: 60_000, env: { ...env, TEMP: root, TMP: root, TMPDIR: root, [SELF_TEST]: 'exit-after-begin' },
    });

    assert.equal(result.status, 3, `${result.stdout}${result.stderr}`.slice(0, 1000));
    assert.deepEqual(fs.readdirSync(root), [], 'the run folder and its claim are gone');
    assert.equal(result.stderr, '');
  });
}

test('E4 the folders that hold DATA inside a run folder are made owner-only (0700), as mkdtemp made them', () => {
  const sites = [
    ['scripts/measure-idle-server.mjs', /idle-arm'\)[\s\S]{0,80}?mkdirSync\(data, \{ mode: 0o700 \}\)/u],
    ['scripts/live-consult-all.mjs', /mkdirSync\(data, \{ mode: 0o700 \}\)/u],
    ['scripts/probe-native-linux-host.mjs', /mode: 0o700/u],
    ['http/run-contracts.mjs', /mkdirSync\(dataDir, \{ mode: 0o700 \}\)/u],
    ['scripts/dotenv-name-vectors.mjs', /mkdirSync\(scratch, \{ recursive: true, mode: 0o700 \}\)/u],
  ];
  for (const [file, pattern] of sites) assert.match(fs.readFileSync(path.join(repoRoot, file), 'utf8'), pattern, file);
});

test('D1 a runner started through a symbolic link still runs (a green tick over nothing otherwise)', (t) => {
  const root = owned.ownedDir(t, 'd1');
  try {
    for (const [file, name] of [['scripts/run-lib-tests.mjs', 'lib.mjs'], ['src_vs_code/scripts/run-tests.mjs', 'rt.mjs'], ['src_vs_code/scripts/run-host.mjs', 'host.mjs']]) {
      fs.symlinkSync(path.join(repoRoot, file), path.join(root, name));
    }
  } catch (error) {
    t.skip(`a link cannot be made here (${error.code})`);
    return;
  }
  const env = { ...process.env };
  for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN']) delete env[name];
  const run = (link, args, extra = {}) => spawnSync(process.execPath, [path.join(root, link), ...args], { cwd: pkg, encoding: 'utf8', timeout: 60_000, env: { ...env, ...extra } });

  const lib = run('lib.mjs', ['scripts/lib/no-such.test.mjs']);
  assert.equal(lib.status, 2, `run-lib-tests ran nothing: ${lib.stdout}${lib.stderr}`);
  assert.match(lib.stderr, /is not a test file/u);
  const tests = run('rt.mjs', ['src/test/no-such.test.mjs']);
  assert.equal(tests.status, 2, `run-tests ran nothing: ${tests.stdout}${tests.stderr}`);
  const host = run('host.mjs', [], { [SELF_TEST]: 'exit-after-begin', TEMP: root, TMP: root, TMPDIR: root });
  assert.equal(host.status, 3, `run-host ran nothing: ${host.stdout}${host.stderr}`);
});

test('E5 a server\'s stop is awaitable: the promise settles when the process has EXITED, so its data folder is removed after it, not under it', async () => {
  const { stopServer } = await import('../../scripts/team-server-child.mjs');
  const child = spawn(process.execPath, ['-e', "process.on('SIGTERM', () => setTimeout(() => process.exit(0), 300)); setInterval(() => {}, 1000); console.log('up')"],
    { stdio: ['ignore', 'pipe', 'inherit'], detached: process.platform !== 'win32' });
  await new Promise((up) => child.stdout.once('data', up));
  const started = Date.now();

  await stopServer(child);

  assert.notEqual(child.exitCode ?? child.signalCode, null, 'the child had exited when stop settled');
  assert.ok(Date.now() - started >= 250, 'and stop waited for its graceful shutdown');
});

test('E6 the contracts runner stops its server BEFORE the wrap removes the run folder it lives in (the server\'s exit listener is prepended)', () => {
  assert.match(fs.readFileSync(path.join(repoRoot, 'http', 'run-contracts.mjs'), 'utf8'), /process\.prependListener\('exit', stop\)/u);
  const contract = fs.readFileSync(path.join(pkg, 'scripts', 'run-contract.mjs'), 'utf8');
  assert.match(contract, /await server\.stop\(\);\s*removeData\(data\)/u, 'run-contract waits for the server to exit before it removes the data');
  assert.match(contract, /finally \{\s*await runCleanUp\(\);/u);
});

test('D2 isMain only says "not main" on a REAL mismatch: when realpath fails it falls back to realpath, then to a resolved-path comparison', async () => {
  const { isMain } = await import('../../scripts/run-tests.mjs');
  const here2 = fileURLToPath(import.meta.url);
  const url = import.meta.url;
  assert.equal(isMain(url, here2), true);
  assert.equal(isMain(url, path.join(path.dirname(here2), '.', path.basename(here2))), true);
  assert.equal(isMain(url, ''), false, 'an empty argv[1]');
  assert.equal(isMain(url, path.join(path.dirname(here2), 'some-other-file.mjs')), false, 'a real mismatch');
  // realpathSync.native cannot resolve a name that is not there; the comparison of the resolved spellings still says "main" for the module\'s own, not-yet-existing path.
  const missing = path.join(path.dirname(here2), 'not-there-yet.mjs');
  assert.equal(isMain(pathToFileURL(missing).href, missing), true, 'fail open to "main", never to a silent no-op');
  assert.equal(isMain(pathToFileURL(missing).href, `${missing}.other`), false);
});

test('H3b the Windows budget of the vibe scratch is 98 (the file limit 259, less the separator, the deepest child 156 and `\\h12`)', () => {
  const source = fs.readFileSync(path.join(repoRoot, 'scripts', 'probe-vibe-confinement.mjs'), 'utf8');
  assert.match(source, /SCRATCH_ROOM = 259 - 1 - 156 - 4;/u);
  assert.match(source, /past 98 characters/u, 'the header says 98');
  assert.equal(/\b99\b/u.test(source.split('\n').filter((line) => line.startsWith('//')).join('\n')), false, 'no 99 left in the comments');
});

test('E7 the contracts runner does not remove its data in stop(): a throw there would skip the wrap\'s exit listener; the wrap removes the folder. Its header lists exit 2 and 78', () => {
  const text = fs.readFileSync(path.join(repoRoot, 'http', 'run-contracts.mjs'), 'utf8');
  const stop = text.slice(text.indexOf('function stop()'), text.indexOf("process.prependListener('exit', stop)"));
  assert.ok(stop.length > 50);
  assert.equal(/rmSync\(/u.test(stop), false, 'stop() removes nothing');
  assert.match(text.split('\n')[2], /\b2\b.*\b78\b/u, 'line 3: the exit codes include 2 and 78');
});

test('E8 every caller of a server\'s stop() awaits it before it removes the data (the seam legs, live-gauge-compat, run-contract)', () => {
  for (const file of ['seam-gauge.mjs', 'seam-usage-counts.mjs', 'live-gauge-compat.mjs', 'run-contract.mjs']) {
    const text = fs.readFileSync(path.join(pkg, 'scripts', file), 'utf8');
    const calls = [...text.matchAll(/^(.*)\bserver\??\.stop\(\)/gmu)].map((m) => m[1]);
    assert.ok(calls.length > 0, `${file} stops a server`);
    for (const before of calls) assert.match(before, /await\s*$/u, `${file}: a stop() that is not awaited`);
  }
});

// ---- E9 and E10 (E5 S6, `todo/PLAN_test_leftovers_E5_S6_brief.md` § 7.5): the two standing tests behind the inventory's kinds 2 and 4 ----------------------------------------
// Both read `shared/leftovers-exemptions.json`. Neither holds a file name of a row as a literal, so this file names no fourth-kind file.

const inventory = () => JSON.parse(fs.readFileSync(path.join(repoRoot, 'shared', 'leftovers-exemptions.json'), 'utf8'));
const tracked = () => spawnSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { cwd: repoRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).stdout.split('\0').filter(Boolean);
/** The first import statement of a file: the wrap must run before any other import's top level does. */
const firstImport = (text) => /^import\s[^;]*;/mu.exec(text)?.[0] ?? '';
const escapeRegExp = (text) => text.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
const readIfThere = (file) => {
  try {
    return fs.readFileSync(path.join(repoRoot, file), 'utf8');
  } catch {
    return undefined; // a tracked file deleted from the tree
  }
};

test('E9 an importer of an exempted library is an entry with the wrap or a test under the runner', () => {
  const INVENTORY = inventory();
  const TRACKED = tracked();
  const libraries = [...new Set(INVENTORY.rows.filter((row) => row.proof === 'library').map((row) => row.file))];
  assert.ok(libraries.length > 0, 'the inventory has library rows: the scan below is not over nothing');
  const entries = new Set(ENTRIES.map((entry) => entry.file));
  const sources = TRACKED.filter((file) => /\.(?:mjs|cjs|js|ts|mts|cts)$/u.test(file) && !file.startsWith('.agents/'));
  const problems = [];
  for (const row of new Set(INVENTORY.rows.filter((entry) => entry.proof === 'entry' && entry.file.endsWith('.mjs')).map((entry) => entry.file))) {
    if (!entries.has(row)) problems.push(`${row}: its row says it is an entry with the wrap, and it is not in ENTRIES, so nothing checks the wrap`);
  }
  for (const library of libraries) {
    const base = path.posix.basename(library);
    const importing = new RegExp(`(?:\\bfrom\\s+|\\bimport\\s*\\(\\s*|\\bimport\\s+|\\brequire\\s*\\(\\s*)['"\`][^'"\`]*/${escapeRegExp(base)}['"\`]`, 'u');
    const importers = sources.filter((file) => file !== library && importing.test(readIfThere(file) ?? ''));
    if (importers.length === 0) problems.push(`${library}: no file imports it, so its row has nothing left to prove`);
    for (const importer of importers) {
      const underRunner = /\.test\.[a-z]+$/u.test(importer) && /inRunFolder\.cjs/u.test(firstImport(readIfThere(importer) ?? ''));
      if (!entries.has(importer) && !underRunner) problems.push(`${importer} imports ${library}: one caller outside a run folder makes the line a finding, not an exemption`);
    }
  }
  assert.deepEqual(problems, []);
});

test('E10 no test file names a fourth-kind file, but the pinned pairs', () => {
  const INVENTORY = inventory();
  const TRACKED = tracked();
  const unreached = [...new Set(INVENTORY.rows.filter((row) => row.proof === 'unreached').map((row) => row.file))];
  assert.ok(unreached.length > 0, 'the inventory has fourth-kind rows: the scan below is not over nothing');
  const testRoots = ['src_mcp/tests/', 'src_mcp/tests_leftovers/', 'src_mcp/tests_fakecli/', 'src_server/tests/', 'src_bugs/tests/', 'src_bench/CoaiBench.Tests/', 'src_vs_code/src/test/'];
  const testFiles = TRACKED.filter((file) => /\.(?:test|spec)\.[a-z]+$/u.test(file) || testRoots.some((root) => file.startsWith(root)));
  assert.ok(testFiles.length > 500, `the test files are found (${testFiles.length})`);
  const found = new Set();
  for (const testFile of testFiles) {
    const text = readIfThere(testFile);
    if (text === undefined) continue;
    for (const file of unreached) {
      const base = path.posix.basename(file);
      if (text.includes(base) && new RegExp(`(?<![\\w.-])${escapeRegExp(base)}`, 'u').test(text)) found.add(`${file}\t${testFile}`);
    }
  }
  const named = INVENTORY.namedBy.map((pair) => `${pair.file}\t${pair.testFile}`);
  const readable = (pair) => pair.replaceAll('\t', ' <- ');
  assert.deepEqual([...found].filter((pair) => !named.includes(pair)).map(readable), [], 'a test file names a fourth-kind file and is not on the list: a person must read that test, and add the pair only if the line is not reached');
  assert.deepEqual(named.filter((pair) => !found.has(pair)).map(readable), [], 'a pinned pair no longer exists: remove it from namedBy');
  for (const pair of INVENTORY.namedBy) assert.ok(pair.why.trim().length > 0, `${pair.file} <- ${pair.testFile}: a pair carries its why`);
  for (const file of new Set(INVENTORY.namedBy.map((pair) => pair.file))) assert.ok(unreached.includes(file), `${file} is named by a pair and has no unreached row`);
});
