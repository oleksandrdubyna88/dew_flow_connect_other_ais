import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { test } from 'node:test';
import { build } from 'esbuild';
import { optionsFor, readEntries, ROOT } from '../../scripts/bundle-lib.mjs';
import { summarize } from '../../scripts/coldstart-lib.mjs';

/**
 * The marks of the cold-start measurement and the command that reads them (`shell/measure.ts`, wired in `shell/entry.ts`).
 *
 * <p>The entry bundle is built in memory exactly as `npm run bundle` builds it and `activate` is run against a fake `vscode` that keeps the
 * command handlers it is given. What is asserted is what a driver in a real host would see: `coai.__measure` exists only with COAI_MEASURE=1;
 * it answers marks in the order the extension makes them and the host's place; a mark made by another bundle's copy of the file is in the same
 * list; and a 3 s sleep planted into `activate` fails the verdict — the definition of done of the measure launch, run here on the real bundle
 * and the real rule because a host launch cannot run on every machine.</p>
 */

const KEY = Symbol.for('coai.measure.marks');
const config = readEntries();
const bundle = config.bundles.find((one) => one.id === 'extension');

/** The entry bundle's code; `planted` replaces the text of shell/entry.ts inside the build. */
async function entryCode(planted) {
  const options = optionsFor(bundle, { write: false }, config);
  if (planted !== undefined) {
    const entry = join(ROOT, bundle.entry);
    options.plugins = [{
      name: 'planted-entry',
      setup(b) {
        b.onLoad({ filter: /\.ts$/ }, (args) => (args.path === entry ? { contents: planted, loader: 'ts', resolveDir: join(ROOT, 'shell') } : undefined));
      },
    }];
  }

  return (await build(options)).outputFiles[0].text;
}

/** A `vscode` that records the commands it is asked to register, with a place for the host to be. */
function fakeVscode(remoteName) {
  const handlers = new Map();
  const dispose = { dispose: () => undefined };
  const api = {
    commands: { registerCommand: (id, run) => { handlers.set(id, run); return dispose; } },
    window: { registerWebviewViewProvider: () => dispose, registerWebviewPanelSerializer: () => dispose, registerUriHandler: () => dispose },
    env: { remoteName, uiKind: 1, appName: 'Visual Studio Code' },
    version: '1.99.0',
    ExtensionMode: { Production: 1, Development: 2, Test: 3 },
  };

  return { api, handlers };
}

/** Evaluates the bundle with a FRESH mark list and runs `activate`; answers the registered handlers. */
function activated(code, { env = {}, remoteName } = {}) {
  delete globalThis[KEY];
  const { api, handlers } = fakeVscode(remoteName);
  const module = { exports: {} };
  const fakeRequire = (name) => {
    if (name === 'vscode') return api;
    if (name === 'node:path' || name === 'path') return require('node:path');
    throw new Error(`the entry may not load ${name}`);
  };
  new Function('module', 'exports', 'require', '__dirname', 'process', code)(module, module.exports, fakeRequire, join(ROOT, 'dist'), { env, platform: 'linux' });
  module.exports.activate({ subscriptions: [] });

  return handlers;
}

const { createRequire } = await import('node:module');
const require = createRequire(import.meta.url);

test('without COAI_MEASURE=1 the command is not registered, and with it it is: in every extension mode, since activate reads no mode', async () => {
  const code = await entryCode();

  assert.equal(activated(code).has('coai.__measure'), false);
  assert.equal(activated(code, { env: { COAI_MEASURE: '0' } }).has('coai.__measure'), false);
  assert.equal(activated(code, { env: { COAI_MEASURE: '1' } }).has('coai.__measure'), true);
});

test('the command answers eval, activateStart and activateReturn in that order, the place of the host, and nothing else about the editor', async () => {
  const code = await entryCode();
  const report = activated(code, { env: { COAI_MEASURE: '1' }, remoteName: 'wsl' }).get('coai.__measure')();

  assert.equal(report.schema, 1);
  assert.deepEqual(report.marks.map((one) => one.name), ['eval', 'activateStart', 'activateReturn']);
  assert.ok(report.marks[0].at <= report.marks[1].at && report.marks[1].at <= report.marks[2].at);
  assert.ok(Math.abs(report.marks[0].at - Date.now()) < 5_000, 'a mark is a wall-clock moment in ms since 1970');
  assert.deepEqual(report.host, { remoteName: 'wsl', uiKind: 1, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Visual Studio Code' });
  assert.equal(activated(code, { env: { COAI_MEASURE: '1' } }).get('coai.__measure')().host.remoteName, null, 'a local host says null, not a name');
});

test('the view provider marks viewResolve at once, before it loads anything', async () => {
  const code = await entryCode();
  delete globalThis[KEY];
  const { api, handlers } = fakeVscode(undefined);
  let provider;
  api.window.registerWebviewViewProvider = (_id, given) => { provider = given; return { dispose: () => undefined }; };
  const module = { exports: {} };
  const fakeRequire = (name) => {
    if (name === 'vscode') return api;
    if (name === 'node:path' || name === 'path') return require('node:path');
    throw new Error(`the entry may not load ${name}`);
  };
  new Function('module', 'exports', 'require', '__dirname', 'process', code)(module, module.exports, fakeRequire, join(ROOT, 'no-such-dist'), { env: { COAI_MEASURE: '1' }, platform: 'linux' });
  module.exports.activate({ subscriptions: [] });
  await assert.rejects(async () => provider.resolveWebviewView({}, {}, {}), /the entry may not load/, 'the module cannot be loaded in this fake, which is how the mark is shown to come first');
  const names = handlers.get('coai.__measure')().marks.map((one) => one.name);

  assert.deepEqual(names, ['eval', 'activateStart', 'activateReturn', 'viewResolve', 'moduleRequire:sidebar', 'moduleRequire'], 'viewResolve is first, then the loader marks the start of the sidebar module\'s require and, that failing in this fake, the legacy module\'s');
});

test('another bundle\'s copy of shell/measure.ts writes into the same list, and its module-evaluation mark is moduleEval, not a second eval', async () => {
  const code = await entryCode();
  const handlers = activated(code, { env: { COAI_MEASURE: '1' } });
  // The legacy module bundle carries its own copy of the file: build one alone and run it as that bundle would.
  const copy = (await build({ ...optionsFor({ entry: 'shell/measure.ts', out: 'unused.js' }, { write: false }, config), minify: false })).outputFiles[0].text;
  const exported = { exports: {} };
  new Function('module', 'exports', 'require', 'process', copy)(exported, exported.exports, () => { throw new Error('no imports'); }, { env: {} });
  exported.exports.mark('ready');
  exported.exports.mark('firstData');
  const names = handlers.get('coai.__measure')().marks.map((one) => one.name);

  assert.deepEqual(names, ['eval', 'activateStart', 'activateReturn', 'moduleEval', 'ready', 'firstData']);
});

test('a module the loader is requiring names its own evaluation mark: moduleEval:<id>; the legacy module keeps moduleEval', async () => {
  const code = await entryCode();
  const handlers = activated(code, { env: { COAI_MEASURE: '1' } });
  const copy = (await build({ ...optionsFor({ entry: 'shell/measure.ts', out: 'unused.js' }, { write: false }, config), minify: false })).outputFiles[0].text;
  const evaluate = (loading) => {
    const exported = { exports: {} };
    globalThis[Symbol.for('coai.measure.loading')] = loading;
    try {
      new Function('module', 'exports', 'require', 'process', copy)(exported, exported.exports, () => undefined, { env: {} });
    } finally {
      globalThis[Symbol.for('coai.measure.loading')] = undefined;
    }
  };
  evaluate('sidebar');
  evaluate('legacy');
  const names = handlers.get('coai.__measure')().marks.map((one) => one.name);

  assert.deepEqual(names, ['eval', 'activateStart', 'activateReturn', 'moduleEval:sidebar', 'moduleEval']);
});

test('only a few marks of one name are kept: a panel that repaints all day cannot grow the list', async () => {
  const code = await entryCode();
  const handlers = activated(code, { env: { COAI_MEASURE: '1' } });
  const copy = (await build({ ...optionsFor({ entry: 'shell/measure.ts', out: 'unused.js' }, { write: false }, config), minify: false })).outputFiles[0].text;
  const exported = { exports: {} };
  new Function('module', 'exports', 'require', 'process', copy)(exported, exported.exports, () => undefined, { env: {} });
  for (let at = 0; at < 100; at += 1) exported.exports.mark('firstData');

  assert.equal(handlers.get('coai.__measure')().marks.filter((one) => one.name === 'firstData').length, 8);
});

/** The raw record `measureScenario.ts` would write for a host that answered `report`, with the ack `after` ms after the clock. */
function recordOf(report, after) {
  const t0 = report.marks.at(-1).at + 1;

  return { schema: 1, sample: 1, t0, startedActive: false, marks: [...report.marks, { name: 'ready', at: t0 + after - 1 }, { name: 'menuReady', at: t0 + after }], host: report.host };
}

test('a 3 s sleep planted in activate fails the verdict on M1; the same bundle without it passes', async () => {
  const source = (await import('node:fs')).readFileSync(join(ROOT, 'shell', 'entry.ts'), 'utf8');
  const anchor = '  const legacy = () => lazy(MODULE_LEGACY);';
  const planted = source.replace(anchor, `${anchor}\n  for (const until = Date.now() + 3000; Date.now() < until;) { /* a planted sleep */ }`);
  assert.notEqual(planted, source, 'the plant found no place in shell/entry.ts: update this test with the file');

  const slow = recordOf(activated(await entryCode(planted), { env: { COAI_MEASURE: '1' } }).get('coai.__measure')(), 100);
  const fast = recordOf(activated(await entryCode(), { env: { COAI_MEASURE: '1' } }).get('coai.__measure')(), 100);
  const verdict = summarize([{ ...slow, sample: 1 }, { ...fast, sample: 2 }, { ...fast, sample: 3 }]);

  assert.equal(verdict.pass, false);
  assert.ok(verdict.m1.max >= 2900, `M1 max ${verdict.m1.max}`);
  assert.match(verdict.problems.join('\n'), /M1 max \d+(\.\d+)? ms is over the 100 ms limit/);
  assert.equal(summarize([{ ...fast, sample: 1 }, { ...fast, sample: 2 }, { ...fast, sample: 3 }]).pass, true, 'the bundle as it ships is inside the budget');
});
