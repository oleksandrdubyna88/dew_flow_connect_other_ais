import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { owned } from '../../scripts/owned.mjs';
import { report } from '../../scripts/coldstart-report.mjs';
import { writeResult } from '../../scripts/coldstart-output.mjs';
import { summarize } from '../../scripts/coldstart-lib.mjs';

/**
 * `coldstart-report.mjs` (the verdict of samples a driver other than `run-host.mjs` wrote — the Remote-WSL script's) and `coldstart-output.mjs` (the JSON
 * file and the table row both launchers write): same rule, same row, and a clear exit code for each way to be wrong.
 */

const quiet = () => {};
const T0 = 7_000_000;
const sample = (n, remoteName = 'wsl', m2 = 900) => ({
  schema: 1, sample: n, t0: T0, startedActive: false,
  marks: [{ name: 'eval', at: T0 - 100 }, { name: 'activateStart', at: T0 - 100 }, { name: 'activateReturn', at: T0 - 90 }, { name: 'firstData', at: T0 + 500 }, { name: 'ready', at: T0 + m2 - 10 }, { name: 'menuReady', at: T0 + m2 }],
  host: { remoteName, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Code' },
});

function files(t, records) {
  const dir = owned.ownedDir(t, 'mr');

  return records.map((record, at) => {
    const file = path.join(dir, `sample-${at + 1}.json`);
    fs.writeFileSync(file, JSON.stringify(record));

    return file;
  });
}

test('R1 three good records pass: the exit code is 0, the JSON is written, and the row is appended with the place named', async (t) => {
  const inputs = files(t, [sample(1), sample(2), sample(3)]);
  const out = path.join(owned.ownedDir(t, 'mr-out'), 'result.json');
  const results = path.join(owned.ownedDir(t, 'mr-res'), 'RESULTS.md');
  fs.writeFileSync(results, '| h |\n');
  const lines = [];
  const code = await report(['--json', out, '--row', '--label', 'remote-wsl', '--commit', 'abc12345', ...inputs], { log: (line) => lines.push(line), error: quiet, output: { resultsFile: results, now: () => new Date('2026-11-22T00:00:00Z') } });

  assert.equal(code, 0);
  assert.equal(JSON.parse(fs.readFileSync(out, 'utf8')).location, 'wsl');
  assert.equal(fs.readFileSync(results, 'utf8'), '| h |\n| 2026-11-22 | abc12345 | remote-wsl | wsl | linux | 10 | 900 / 900 / 900 | 500 / 500 / 500 | pass |\n');
  assert.ok(lines.some((line) => line.includes('passed')));
});

test('R2 a slow sample, a missing sample, a bad flag and an unreadable file each fail with their own code', async (t) => {
  const errors = [];
  const slow = await report(files(t, [sample(1), sample(2, 'wsl', 2500), sample(3)]), { log: quiet, error: (line) => errors.push(line) });
  assert.equal(slow, 1);
  assert.match(errors.join('\n'), /M2 max 2500 ms is over the 2000 ms limit/);

  assert.equal(await report(files(t, [sample(1), sample(2)]), { log: quiet, error: (line) => errors.push(line) }), 1);
  assert.match(errors.join('\n'), /2 sample\(s\) came back, 3 were needed/);
  assert.equal(await report(['--nope'], { log: quiet, error: quiet }), 64);
  assert.equal(await report([path.join(owned.ownedDir(t, 'mr-none'), 'missing.json')], { log: quiet, error: quiet }), 66);
});

test('R3 writeResult writes nothing the options do not ask for', async (t) => {
  const dir = owned.ownedDir(t, 'mr-w');
  const results = path.join(dir, 'RESULTS.md');
  fs.writeFileSync(results, 'x\n');
  await writeResult(summarize([sample(1), sample(2), sample(3)]), { json: undefined, row: false }, { resultsFile: results });

  assert.equal(fs.readFileSync(results, 'utf8'), 'x\n');
  assert.deepEqual(fs.readdirSync(dir), ['RESULTS.md']);
});

test('R4 gitHead gives the short commit, and a git that hangs is stopped with its whole tree at the deadline', async (t) => {
  const { gitHead } = await import('../../scripts/coldstart-output.mjs');
  const here = path.dirname(new URL(import.meta.url).pathname);
  assert.match(await gitHead(here), /^[0-9a-f]{8}$|^unknown$/);

  if (process.platform === 'win32') {
    return; // The fake git below is a shell script; the Windows tree kill is `taskkill /T /F`, the same call every launcher in scripts/ makes.
  }
  const dir = owned.ownedDir(t, 'mr-git');
  const marker = path.join(dir, 'grandchild.pid');
  const fake = path.join(dir, 'git');
  // A git that never answers and has a child of its own: killing only the direct child would leave `sleep` running.
  fs.writeFileSync(fake, `#!/bin/sh\nsleep 60 &\necho $! > '${marker}'\nwait\n`, { mode: 0o755 });
  const started = Date.now();
  const head = await gitHead(dir, { executable: fake, timeoutMs: 400 });

  assert.equal(head, 'unknown');
  assert.ok(Date.now() - started < 5000, 'the call ended at its own deadline, not when the fake git did');
  const grandchild = Number(fs.readFileSync(marker, 'utf8').trim());
  await new Promise((resolve) => setTimeout(resolve, 200));
  let alive = true;
  try { process.kill(grandchild, 0); } catch { alive = false; }
  assert.equal(alive, false, 'the child of the hung git was killed with it');
});

test('R5 writeResult takes the commit from an asynchronous source', async (t) => {
  const results = path.join(owned.ownedDir(t, 'mr-async'), 'RESULTS.md');
  fs.writeFileSync(results, '| h |\n');
  await writeResult(summarize([sample(1), sample(2), sample(3)]), { json: undefined, row: true, label: 'x' }, { resultsFile: results, now: () => new Date('2026-11-22T00:00:00Z'), commit: () => Promise.resolve('feedbeef') });
  assert.match(fs.readFileSync(results, 'utf8'), /\| 2026-11-22 \| feedbeef \| x \|/);
});

test('R6 the Remote-WSL script never touches the person\'s extensions: no uninstall, an isolated remote folder, and the three guards are in it', () => {
  const script = fs.readFileSync(path.join(path.dirname(new URL(import.meta.url).pathname), '..', '..', 'scripts', 'measure-remote-wsl.ps1'), 'utf8');
  const code = script.split('\n').filter((line) => !/^\s*(#|\.|<#|#>)/.test(line)).join('\n');

  assert.ok(!/--uninstall-extension/.test(code), 'the script uninstalls an extension: the person\'s own copy would be the one removed');
  assert.ok(!/--uninstall-extension/.test(script.replace(/^[\s\S]*?#>/, '')), 'no uninstall anywhere below the help block');
  assert.match(code, /\$env:VSCODE_EXTENSIONS = \$remoteExtensions/);
  assert.match(code, /\$env:VSCODE_AGENT_FOLDER = \$remoteServerData/);
  assert.match(code, /'VSCODE_EXTENSIONS\/u'/);
  assert.match(code, /\$remoteExtensions = "\$distroRun\/extensions"/, 'the isolated folder is inside the run folder, which is removed at the end');
  assert.match(code, /pgrep -f|'pgrep'/, 'guard 1: a running server is detected');
  assert.match(code, /did not land in the isolated folder/, 'guard 2: the install is checked');
  assert.match(code, /\$ownerAfter -ne \$ownerBefore/, 'guard 3: the person\'s folder is compared');
  assert.match(script, /^\s*Guard 3 \(after\)|# Guard 3 \(after\)/m);
});
