import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ignoredWorkspaceValues, Inspected, MODEL_KEYS, userLayerReader } from '../modelKeys';

/**
 * The settings that name a model — and therefore a program to run — are read from the person's own
 * layer only (PLAN_one_model_catalog.md, E1.1).
 *
 * <p>Without this, a cloned repository's `.vscode/settings.json` could set `coai.vendors` with an
 * `executablePath` of its choosing, and the next review would run it. VS Code merges the workspace
 * value over the user one for every setting whose manifest declares no scope, which is all of ours.</p>
 */

function inspector(layers: Record<string, Inspected>): (key: string) => Inspected | undefined {
  return (key) => layers[key];
}

const PLANTED = [{ id: 'codex', runtime: 'codex', model: '', executablePath: './evil' }];
const OWN = [{ id: 'claude', runtime: 'claude', model: '' }];

test('a workspace value of a model-bearing setting changes nothing that is read', () => {
  const read = userLayerReader(
    () => PLANTED,
    inspector({ vendors: { defaultValue: undefined, globalValue: OWN, workspaceValue: PLANTED } }),
  );

  assert.deepEqual(read('vendors'), OWN);
});

test('a folder value is ignored the same way as a workspace one', () => {
  const read = userLayerReader(
    () => 'planted',
    inspector({ chatModel: { defaultValue: '', workspaceFolderValue: 'planted' } }),
  );

  assert.equal(read('chatModel'), '');
});

test('with nothing in the user layer the manifest default answers, never the workspace', () => {
  const read = userLayerReader(
    () => PLANTED,
    inspector({ vendors: { defaultValue: [], workspaceValue: PLANTED } }),
  );

  assert.deepEqual(read('vendors'), []);
});

test('every other setting is read as VS Code merges it, workspace included', () => {
  const read = userLayerReader(
    (key) => (key === 'maxConcurrency' ? 7 : undefined),
    inspector({ maxConcurrency: { defaultValue: 4, workspaceValue: 7 } }),
  );

  assert.equal(read('maxConcurrency'), 7);
});

test('the guarded settings are every key that names a model or a program to run', () => {
  assert.deepEqual([...MODEL_KEYS].sort(), [
    'bugzModel', 'chatModel', 'chatModelPresets', 'consultants', 'qconsultRows', 'securityLane', 'vendors',
  ]);
});

test('the notice names each ignored key with the layer that held it, and nothing else', () => {
  const found = ignoredWorkspaceValues(inspector({
    vendors: { globalValue: OWN, workspaceValue: PLANTED },
    bugzModel: { workspaceFolderValue: 'local/x' },
    maxConcurrency: { workspaceValue: 9 },
    chatModel: { globalValue: 'claude' },
  }));

  assert.deepEqual(found, [
    { key: 'vendors', layer: 'workspace', value: PLANTED, userHasOne: true },
    { key: 'bugzModel', layer: 'folder', value: 'local/x', userHasOne: false },
  ]);
});

test('a key with no workspace or folder value raises no notice', () => {
  assert.deepEqual(ignoredWorkspaceValues(inspector({ vendors: { globalValue: OWN } })), []);
});
