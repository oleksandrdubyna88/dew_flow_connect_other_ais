import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { copyWouldReplace, ignoredWorkspaceValues, Inspected, MODEL_KEYS, SAFETY_KEYS, userLayerReader } from '../modelKeys';
import { settingsFrom } from '../settingsShape';
import { KeyValueStore, sideConfigReader } from '../sideSettings';
import { overlayKey, Side } from '../coaiInstall';

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
    'bugzModel', 'chatModel', 'chatModelName', 'chatModelPresets', 'chatPresetsMoved', 'consultants', 'qconsultRows', 'securityLane', 'vendors',
  ]);
});

test('the safety keys are the overrides that switch a server refusal off — here only kimiAllowNetworkSignIn — and they raise no model notice', () => {
  assert.deepEqual([...SAFETY_KEYS], ['kimiAllowNetworkSignIn']);
  const layers = { kimiAllowNetworkSignIn: { defaultValue: false, workspaceValue: true } };

  assert.equal(userLayerReader(() => true, inspector(layers))('kimiAllowNetworkSignIn'), false);
  assert.deepEqual(ignoredWorkspaceValues(inspector(layers)), [], 'no "a repository cannot choose a program" notice for it: nothing is run');
});

test('a repository cannot redirect the chat through the move\'s record or the chat\'s model name', () => {
  // The own review of epic 4's code round: the chat read both through the merged layers, so a cloned repository could
  // say which catalog row a moved preset — and a resumed conversation — lands on, and which model the chat row runs.
  const planted = [{ presetId: 'a', rowId: 'codex', runtime: 'codex', model: 'x', name: 'x' }];
  const read = userLayerReader(
    (key) => (key === 'chatPresetsMoved' ? planted : 'planted-model'),
    inspector({
      chatPresetsMoved: { defaultValue: [], workspaceValue: planted },
      chatModelName: { defaultValue: '', workspaceFolderValue: 'planted-model' },
    }),
  );

  assert.deepEqual(read('chatPresetsMoved'), []);
  assert.equal(read('chatModelName'), '');
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

test('Copy is offered only where it would replace nothing — this side\'s own settings when it keeps them', () => {
  const ignored = { key: 'vendors', layer: 'workspace' as const, value: [{ id: 'x' }], userHasOne: false };

  assert.equal(copyWouldReplace(ignored, undefined), false, 'no overlay: the copy lands in the user layer, which is empty');
  assert.equal(copyWouldReplace({ ...ignored, userHasOne: true }, undefined), true);
  assert.equal(copyWouldReplace(ignored, { vendors: [{ id: 'mine' }] }), true,
    'this side keeps its own settings and already holds vendors: a copy there would replace them (PR #681 review)');
  assert.equal(copyWouldReplace({ ...ignored, userHasOne: true }, {}), false,
    'the copy lands in the overlay, which holds nothing; the user layer is not where it goes');
});

/**
 * `onExhausted` is read from the layers that HOLD a value (parent § 4.6, U3): a window VS Code updated in place keeps the OLD
 * settings registry, whose default for it is `human`, and that must not be read as something the person chose.
 */
const STALE_REGISTRY = (): unknown => 'human';

test('AWindowWhoseRegistryStillSaysHumanReadsAuto', () => {
  const read = userLayerReader(STALE_REGISTRY, inspector({ onExhausted: { defaultValue: 'human' } }));

  assert.equal(read('onExhausted'), undefined, 'a registry default is not a held value');
  assert.equal(settingsFrom(read).onExhausted, 'auto');
});

test('TheLayersThatHoldAValueKeepTheirOrder', () => {
  const held = { defaultValue: 'human', globalValue: 'good_enough', workspaceValue: 'escalate', workspaceFolderValue: 'continue' };
  const read = (layers: Inspected): unknown => userLayerReader(STALE_REGISTRY, inspector({ onExhausted: layers }))('onExhausted');

  assert.equal(read(held), 'continue');
  assert.equal(read({ ...held, workspaceFolderValue: undefined }), 'escalate');
  assert.equal(read({ ...held, workspaceFolderValue: undefined, workspaceValue: undefined }), 'good_enough');
});

test('ThisSidesOwnValueStillWins', () => {
  // A guard: green before the change, because the overlay wraps the door whatever the door answers.
  const side: Side = { remoteName: undefined, hostname: 'DESKTOP', storagePath: 'C:/coai' };
  const seen: Record<string, unknown> = { [overlayKey(side)]: { onExhausted: 'good_enough' } };
  const store: KeyValueStore = { get: <T,>(key: string) => seen[key] as T | undefined, update: () => Promise.resolve() };
  const door = userLayerReader(STALE_REGISTRY, inspector({ onExhausted: { defaultValue: 'human', workspaceValue: 'escalate' } }));

  assert.equal(sideConfigReader(door, true, store, side)('onExhausted'), 'good_enough');
});

test('TheOtherKeysAreReadAsBefore', () => {
  // A guard: green before the change, next to "every other setting is read as VS Code merges it".
  assert.equal(userLayerReader(() => 7, inspector({ maxConcurrency: { defaultValue: 3 } }))('maxConcurrency'), 7);
  assert.equal(userLayerReader(() => 'x', inspector({ chatModel: { defaultValue: 'preset' } }))('chatModel'), 'preset');
  assert.equal(userLayerReader(() => true, inspector({ kimiAllowNetworkSignIn: { defaultValue: false } }))('kimiAllowNetworkSignIn'), false);
});
