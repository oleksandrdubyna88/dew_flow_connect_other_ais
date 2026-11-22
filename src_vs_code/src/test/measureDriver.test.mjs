import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import { createRequire } from 'node:module';
import * as path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { owned } from '../../scripts/owned.mjs';
import { deriveSample, summarize } from '../../scripts/coldstart-lib.mjs';

/**
 * The Remote-WSL driver extension (`scripts/measure-driver/extension.js`, installed by `scripts/measure-remote-wsl.ps1`).
 *
 * <p>It cannot run in a WSL window here, so its extension code is run against a fake `vscode` and what it WRITES is asserted: a record that
 * `coldstart-lib.mjs` accepts as a cold, acknowledged sample with its place named, the sample number taken from the folder's name, the window closed
 * after; and, when something is wrong, a record whose `error` says why — never a missing file, because the script would then wait out its timeout.</p>
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const driverFile = path.join(here, '..', '..', 'scripts', 'measure-driver', 'extension.js');
const require = createRequire(import.meta.url);
// The driver's settle time and its wait for an ack, shortened: the test would otherwise sit through the 30 s a real window waits.
process.env.COAI_MEASURE_DRIVER_SETTLE_MS = '20';
process.env.COAI_MEASURE_DRIVER_WAIT_MS = '300';
const clock = () => performance.timeOrigin + performance.now();

/** Loads the driver against a fake `vscode`; `plan` shapes what the editor does. Answers what the driver did. */
function load(folder, { active = false, installed = true, measureAnswer } = {}) {
  const ran = [];
  const vscode = {
    env: { remoteName: 'wsl', uiKind: 1 },
    workspace: { workspaceFolders: folder === undefined ? undefined : [{ uri: { fsPath: folder } }] },
    extensions: { getExtension: (id) => (installed && id === 'remsoftdev.connect-other-ais' ? { isActive: active, extensionPath: '/home/u/.vscode-server/extensions/coai' } : undefined) },
    commands: {
      executeCommand: async (command) => {
        ran.push(command);
        if (command === 'coai.__measure') {
          return measureAnswer?.();
        }
        return undefined;
      },
    },
  };
  const module = { exports: {} };
  const customRequire = (name) => (name === 'vscode' ? vscode : require(name));
  new Function('require', 'module', 'exports', fs.readFileSync(driverFile, 'utf8'))(customRequire, module, module.exports);

  return { activate: module.exports.activate, ran };
}

/** What the extension would answer: the entry's marks, and a ready ack shortly after whatever moment it is asked. */
const answer = () => {
  const now = clock();

  return {
    schema: 1,
    marks: [{ name: 'eval', at: now - 3000 }, { name: 'activateReturn', at: now - 2990 }, { name: 'viewResolve', at: now - 1 }, { name: 'firstData', at: now }, { name: 'ready', at: now }],
    host: { remoteName: 'wsl', uiKind: 1, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Code' },
  };
};

async function recordOf(t, options) {
  const folder = path.join(owned.ownedDir(t, 'drv'), 'sample-2');
  fs.mkdirSync(folder);
  const driver = load(options.noFolder ? undefined : folder, options);
  driver.activate();
  const file = path.join(folder, 'coai-measure-result.json');
  for (let waited = 0; !fs.existsSync(file) && waited < 6000; waited += 50) {
    await new Promise((wake) => setTimeout(wake, 50));
  }

  return { driver, file, record: fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : undefined, folder };
}

test('D1 the driver writes a record the rule reads as a cold, acknowledged sample in WSL, numbered from its folder, and closes the window', async (t) => {
  const { driver, record, folder } = await recordOf(t, { measureAnswer: answer });

  assert.ok(record !== undefined, 'a record was written');
  assert.equal(record.sample, 2);
  assert.equal(record.startedActive, false);
  assert.deepEqual(record.driver, { remoteName: 'wsl', platform: process.platform, hostname: record.driver.hostname, extensionPath: '/home/u/.vscode-server/extensions/coai', uiKind: 1 });
  assert.ok(driver.ran.indexOf('workbench.view.extension.coai') < driver.ran.indexOf('coai.__measure'), 'the sidebar is opened before the marks are read');
  assert.equal(driver.ran.at(-1), 'workbench.action.closeWindow', 'the window is closed after the record is written');
  assert.deepEqual(fs.readdirSync(folder).filter((name) => name.endsWith('.part')), [], 'no half-written file is left');
  const derived = deriveSample(record);
  assert.deepEqual(derived.problems, []);
  assert.equal(derived.location, 'wsl');
  assert.ok(derived.m2 !== null && derived.m2 >= 0 && derived.m2 < 5000);
  assert.equal(summarize([record, record, record]).location, 'wsl');
});

test('D2 an extension that is already active, and one that is not installed, each leave a record saying why', async (t) => {
  const active = await recordOf(t, { active: true, measureAnswer: answer });
  assert.match(active.record.error, /already active/);
  assert.match(deriveSample(active.record).problems[0], /the driver failed: the extension was already active/);

  const missing = await recordOf(t, { installed: false, measureAnswer: answer });
  assert.match(missing.record.error, /is not installed in this extension host \(remoteName wsl\)/);
});

test('D3 with no marks command the record names COAI_MEASURE, and a window with no folder does nothing', async (t) => {
  const mute = await recordOf(t, { measureAnswer: () => { throw new Error('command not found'); } });
  assert.match(mute.record.error, /COAI_MEASURE=1 did not reach this extension host/);

  const none = load(undefined);
  none.activate();
  await new Promise((wake) => setTimeout(wake, 200));
  assert.deepEqual(none.ran, [], 'without a workspace folder the driver does nothing');
});
