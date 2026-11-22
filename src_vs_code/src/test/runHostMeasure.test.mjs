import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * `run-host.mjs measure` (v2 extension.md § 2, epic E2 story S2 task t1): one editor launch per sample, each with a user-data directory, an extensions
 * directory, a workspace and a data directory of its own; COAI_MEASURE=1 and the record file in the launch's environment; a failing sample, a missing
 * ack, a slow activation or an extension that was already active fails the run; the folders are gone before `measure` returns.
 *
 * The editor is a fake `launch` that writes the record `measureScenario.ts` would write. Whether the real scenario drives a real editor right is what
 * CI's measure jobs prove; what is asserted here is everything the launcher itself decides.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(here, '..', '..', 'scripts', 'run-host.mjs');
const quiet = () => {};
const T0 = 5_000_000;

/** What the scenario writes for one sample; `m2: null` is a launch whose ack never came. */
function record(sample, { m1 = 15, m2 = 900, startedActive = false } = {}) {
  const marks = [{ name: 'eval', at: T0 - 3000 }, { name: 'activateStart', at: T0 - 3000 }, { name: 'activateReturn', at: T0 - 3000 + m1 }, ...(m2 === null ? [] : [{ name: 'firstData', at: T0 + 700 }, { name: 'ready', at: T0 + m2 - 10 }, { name: 'menuReady', at: T0 + m2 }])];

  return { schema: 1, sample, t0: T0, startedActive, marks, host: { remoteName: null, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Code' } };
}

/** A launch that behaves as `plan(sample)` says: a record to write, an error to throw, or both. */
function fakeLaunch(plan, seen = []) {
  return async (options) => {
    const env = options.extensionTestsEnv;
    const sample = Number(env.COAI_MEASURE_SAMPLE);
    const arg = (name) => options.launchArgs.find((one) => one.startsWith(`${name}=`))?.slice(name.length + 1);
    seen.push({ options, sample, profile: arg('--user-data-dir'), extensions: arg('--extensions-dir'), workspace: options.launchArgs[0], data: env.COAI_DATA_DIR, record: env.COAI_MEASURE_OUT, existed: [arg('--user-data-dir'), arg('--extensions-dir'), options.launchArgs[0], env.COAI_DATA_DIR].map((one) => fs.existsSync(one)) });
    const step = plan(sample);
    if (step.write !== undefined) {
      fs.mkdirSync(path.dirname(env.COAI_MEASURE_OUT), { recursive: true });
      fs.writeFileSync(env.COAI_MEASURE_OUT, JSON.stringify(step.write));
    }
    if (step.fail !== undefined) {
      throw new Error(step.fail);
    }
  };
}

async function run(t, argv, plan, extra = {}) {
  const { measure } = await import(pathToFileURL(script).href);
  const root = owned.ownedDir(t, 'hm');
  const seen = [];
  const errors = [];
  const logs = [];
  const exits = [];
  const realExit = process.exit;
  process.exit = (code) => { exits.push(code); };
  try {
    const code = await measure({ argv, launch: fakeLaunch(plan, seen), bundle: quiet, env: {}, root, onExit: quiet, log: (line) => logs.push(line), error: (line) => errors.push(line), socketLimit: 10_000, ...extra });
    return { code, seen, errors, logs, exits, root };
  } finally {
    process.exit = realExit;
  }
}

test('M1 three samples, three launches: each with its own profile, extensions, workspace and data folder, COAI_MEASURE=1 and its own record; all gone after', async (t) => {
  const out = path.join(owned.ownedDir(t, 'hm-out'), 'result.json');
  const results = path.join(owned.ownedDir(t, 'hm-res'), 'RESULTS.md');
  fs.writeFileSync(results, '| header |\n');
  const { code, seen, errors, exits, root } = await run(t, ['--json', out, '--row', '--label', 'unit', '--commit', 'abc12345'], (sample) => ({ write: record(sample) }),
    { output: { resultsFile: results, now: () => new Date('2026-11-22T10:00:00Z') } });

  assert.equal(code, 0, errors.join('\n'));
  assert.deepEqual(exits, [], 'measure returns the code; only the command line exits');
  assert.equal(seen.length, 3);
  assert.deepEqual(seen.map((one) => one.sample), [1, 2, 3]);
  for (const key of ['profile', 'extensions', 'workspace', 'data', 'record']) {
    assert.equal(new Set(seen.map((one) => one[key])).size, 3, `${key} is new for every sample`);
  }
  for (const one of seen) {
    assert.ok(one.existed.every(Boolean), 'the four folders existed during the launch');
    assert.equal(one.options.extensionTestsEnv.COAI_MEASURE, '1');
    assert.equal(one.options.extensionTestsEnv.COAI_MEASURE_RECORD_ONLY, '0');
    assert.ok(one.options.extensionTestsPath.endsWith(path.join('out', 'test', 'host', 'measureScenario.js')));
    assert.ok(one.options.launchArgs.includes('--disable-extensions'));
    for (const folder of [one.profile, one.extensions, one.workspace, one.data]) assert.equal(path.dirname(folder), path.dirname(one.profile), 'every folder is inside the one run folder');
  }
  const result = JSON.parse(fs.readFileSync(out, 'utf8'));
  assert.equal(result.pass, true);
  assert.equal(result.samples.length, 3);
  assert.deepEqual(result.m2, { min: 900, median: 900, max: 900 });
  assert.equal(fs.readFileSync(results, 'utf8'), '| header |\n| 2026-11-22 | abc12345 | unit | local | linux | 15 | 900 / 900 / 900 | 700 / 700 / 700 | pass |\n');
  assert.deepEqual(fs.readdirSync(root), [], 'the run folder and every claim are gone before measure returns');
});

test('M2 a planted 3 s sleep in activation fails the launch, naming M1', async (t) => {
  const { code, errors } = await run(t, [], (sample) => ({ write: record(sample, { m1: sample === 2 ? 3000 : 15 }) }));

  assert.equal(code, 1);
  assert.match(errors.join('\n'), /M1 max 3000 ms is over the 100 ms limit/);
});

test('M3 an M2 over 2000 ms in any one sample fails, a missing ack fails, and the others still ran', async (t) => {
  const slow = await run(t, [], (sample) => ({ write: record(sample, { m2: sample === 3 ? 2400 : 500 }) }));
  assert.equal(slow.code, 1);
  assert.match(slow.errors.join('\n'), /M2 max 2400 ms is over the 2000 ms limit/);

  const missing = await run(t, [], (sample) => ({ write: record(sample, { m2: sample === 1 ? null : 500 }) }));
  assert.equal(missing.code, 1);
  assert.equal(missing.seen.length, 3, 'a sample without an ack does not stop the rest');
  assert.match(missing.errors.join('\n'), /sample 1: no menuReady/);
});

test('M4 an extension that was already active is not a cold start', async (t) => {
  const { code, errors } = await run(t, [], (sample) => ({ write: record(sample, { startedActive: sample === 1 }) }));

  assert.equal(code, 1);
  assert.match(errors.join('\n'), /sample 1: the extension was already active/);
});

test('M5 a launch that fails and leaves no record is a failed sample; one that fails after writing its record is read, and still fails', async (t) => {
  const none = await run(t, [], (sample) => (sample === 2 ? { fail: 'the editor did not start' } : { write: record(sample) }));
  assert.equal(none.code, 1);
  assert.match(none.errors.join('\n'), /sample 2: the editor did not start/);
  assert.match(none.errors.join('\n'), /sample 2: the launch left no record/);
  assert.equal(none.seen.length, 3);

  const after = await run(t, [], (sample) => ({ write: record(sample, { m2: null }), fail: 'no fresh menuReady' }));
  assert.equal(after.code, 1);
  assert.match(after.errors.join('\n'), /no menuReady from the sidebar/);
});

test('M6 --record-only is the v1 baseline: no limit, the extension may have been active, and the scenario is told', async (t) => {
  const { code, seen } = await run(t, ['--record-only'], (sample) => ({ write: { ...record(sample, { m2: 7000, startedActive: true }), marks: [{ name: 'ready', at: T0 + 7000 }] } }));

  assert.equal(code, 0);
  assert.equal(seen[0].options.extensionTestsEnv.COAI_MEASURE_RECORD_ONLY, '1');
});

test('M7 --samples chooses how many launches, and a bad flag is refused before any folder is made', async (t) => {
  const two = await run(t, ['--samples', '2'], (sample) => ({ write: record(sample) }));
  assert.equal(two.seen.length, 2);
  assert.equal(two.code, 0);

  const bad = await run(t, ['--sample', '2'], () => ({}));
  assert.equal(bad.code, 64);
  assert.equal(bad.seen.length, 0);
  assert.match(bad.errors.join('\n'), /unknown flag --sample/);
});
