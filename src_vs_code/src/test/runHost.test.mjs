import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * `run-host.mjs` (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.6 and § 16 D-c, H0-H2; must-not-copy #14): the editor-host runner owns ONE run folder (`what` `host`, TEMP, TMP,
 * TMPDIR and the marker moved there) and, INSIDE it, three owned folders of the wrap — the workspace, the data directory and the editor's user-data directory
 * (`host-ws`, `host-data`, `host-profile`), each with its claim. Both texts of the plan stand.
 *
 * The folders are removed BEFORE the process exits: the old `main().then(exit).finally(rm)` ended the process first and removed nothing (#14). H1 is
 * that trap's RED-GREEN-RED: a `process.exit` before the `finally` leaves the run folder behind.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(here, '..', '..', 'scripts', 'run-host.mjs');
const MARKER = 'COAI_TEST_RUN_FOLDER';
const quiet = () => {};

/** H0's source read: it comes BEFORE any import of the script, which would make two folders and run the editor at import today. */
function assertSafeToImport() {
  const text = fs.readFileSync(script, 'utf8');
  const code = text.split('\n').filter((line) => !/^\s*(\/\/|\*|\/\*)/.test(line));
  assert.ok(code.some((line) => line.includes('process.argv[1]')), 'an argv guard: the script runs only when started as a script');
  assert.equal(code.some((line) => /mkdtemp/.test(line)), false, 'no folder is made with mkdtemp (the wrap makes them)');
  assert.equal(code.some((line) => /^(await\s+)?main\(/.test(line) || /^main\(\)/.test(line)), false, 'no module-level call of main()');
}

test('H0 run-host.mjs does nothing when imported', async () => {
  assertSafeToImport();
  const imported = await import(pathToFileURL(script).href);
  assert.equal(typeof imported.main, 'function');
});

const importHost = async () => {
  assertSafeToImport();
  return import(pathToFileURL(script).href);
};

test('H1 a launch that resolves: both launches got the folders, and after main the run folder and its claim are gone', async (t) => {
  const { main } = await importHost();
  const root = owned.ownedDir(t, 'h1');
  const launches = [];
  const env = {};
  const launch = async (options) => {
    const [workspace] = options.launchArgs;
    const data = options.extensionTestsEnv.COAI_DATA_DIR;
    const profileArg = options.launchArgs.find((arg) => arg.startsWith('--user-data-dir='));
    const runFolder = env[MARKER];
    launches.push({ options, workspace, data, profile: profileArg?.slice('--user-data-dir='.length), runFolder, existed: [workspace, `${workspace}.owner`, runFolder, `${runFolder}.owner`].map((file) => fs.existsSync(file)) });
  };

  const code = await main({ launch, bundle: quiet, env, root, onExit: quiet, log: quiet, error: quiet });

  assert.equal(code, 0);
  assert.equal(launches.length, 2);
  const [first, second] = launches;
  assert.deepEqual(first.options.launchArgs, [first.workspace, '--disable-extensions', `--user-data-dir=${first.profile}`]);
  const run = first.runFolder;
  assert.equal(path.dirname(run), root);
  assert.ok(path.basename(run).startsWith(owned.ownedPrefix('host')), 'the run folder is `host`');
  for (const name of ['TEMP', 'TMP', 'TMPDIR']) assert.equal(env[name], run, `${name} names the run folder`);
  assert.equal(owned.parseOwner(env.TESTSTODELETE_RUN).pid, process.pid, 'the run exports its own record');
  for (const [folder, what] of [[first.workspace, 'host-ws'], [first.data, 'host-data'], [first.profile, 'host-profile']]) {
    assert.equal(path.dirname(folder), run, `${what} is INSIDE the run folder`);
    assert.ok(path.basename(folder).startsWith(owned.ownedPrefix(what)), `${what}: ${path.basename(folder)}`);
  }
  assert.ok(first.existed.every(Boolean), 'the workspace, the run folder and their claims existed during the launch');
  assert.equal(first.options.extensionTestsEnv.COAI_DATA_SIDE, '');
  assert.deepEqual(second.options.extensionTestsEnv, { COAI_DATA_DIR: path.join(first.data, 'named-but-missing'), COAI_DATA_SIDE: '', COAI_HOST_MISSING_DATA_ROOT: '1' });
  assert.equal(second.profile, first.profile, 'the second launch uses the same folders');
  assert.deepEqual(fs.readdirSync(root), [], 'the run folder and every claim are gone before main returns');
});

test('H2 a launch that rejects: main returns 1 and still leaves nothing', async (t) => {
  const { main } = await importHost();
  const root = owned.ownedDir(t, 'h2');
  const seen = [];
  const launch = async (options) => {
    seen.push(options);
    throw new Error('the editor did not start');
  };
  const errors = [];

  const code = await main({ launch, bundle: quiet, env: {}, root, onExit: quiet, error: (line) => errors.push(line) });

  assert.equal(code, 1);
  assert.equal(seen.length, 2, 'both launches ran: the first failure does not hide the second');
  assert.ok(errors.join('\n').includes('the editor did not start'));
  assert.deepEqual(fs.readdirSync(root), []);
});
