import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { owned } from '../../scripts/owned.mjs';
import { overlay } from '../../scripts/coldstart-overlay.mjs';

/**
 * `coldstart-overlay.mjs` lays the measurement over an older checkout (the v1 baseline, CI job `measure-ref`). The patch itself fits only c24e5141, which a
 * shallow CI clone does not have, so CI's own `measure-ref` run is what proves it applies; here: the patch is checked before it is applied, the harness files
 * all exist and land where the old tree compiles them, and a folder that is not a checkout of this repository is refused.
 */

test('O1 the patch is checked, then applied, then every harness file lands in the old tree', (t) => {
  const repo = owned.ownedDir(t, 'ov');
  fs.mkdirSync(path.join(repo, 'src_vs_code'));
  fs.writeFileSync(path.join(repo, 'src_vs_code', 'package.json'), '{}');
  const calls = [];
  overlay(repo, { run: (args, cwd) => calls.push({ args: args.slice(0, 2), cwd }), log: () => {} });

  assert.deepEqual(calls.map((one) => one.args[1] === undefined ? one.args[0] : `${one.args[0]} ${one.args[1]}`.replace(/ .*measure-v1\.patch$/u, ' <patch>')), ['apply --check', 'apply <patch>']);
  assert.ok(calls.every((one) => one.cwd === path.resolve(repo)));
  for (const file of ['scripts/run-host.mjs', 'scripts/coldstart-lib.mjs', 'scripts/coldstart-output.mjs', 'src/test/host/measureScenario.ts', 'src/measure.ts', 'src/measureMarks.ts']) {
    assert.ok(fs.existsSync(path.join(repo, 'src_vs_code', file)), `${file} was not laid over the old tree`);
  }
  assert.equal(fs.readFileSync(path.join(repo, 'src_vs_code', 'src', 'measure.ts'), 'utf8').includes("import type * as vscode from 'vscode'"), true);
});

test('O2 a folder without the extension package is refused before anything is changed', (t) => {
  const repo = owned.ownedDir(t, 'ov2');
  const calls = [];

  assert.throws(() => overlay(repo, { run: (args) => calls.push(args), log: () => {} }), /is not the extension's package/);
  assert.deepEqual(calls, []);
});
