import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { CONFIG_FORMAT, declaredSettings, exportedSettings, importedConfig } from '../configTransfer';
import { familyOf } from '../pathFamily';
import { sideKey } from '../coaiInstall';
import { settingsFrom, ConfigReader } from '../settingsShape';
import { KeyValueStore, sideConfigReader } from '../sideSettings';
import { userLayerReader } from '../modelKeys';
import { labelOf } from '../onExhaustedPanel';
import { stampSide } from '../sidesSeen';
import {
  heldText,
  isDue,
  OVERLAY_WITNESS,
  planOf,
  recordOf,
  USER_WITNESS,
  USER_WITNESS_WORD,
  userLayerOf,
} from '../onExhaustedMigration';
import { HeldLayers, inForceOf, NOT_RESET, NOW_AUTO, sentenceFor, SetWhere, staysInForce, WHERE } from '../onExhaustedTold';
import {
  ClientState,
  FakeUserSettings,
  GLOBAL,
  KEY_STRING,
  KEY_X,
  KEY_Y,
  SIDE_X,
} from './onExhaustedMigrationFakes';

/**
 * The decisions of the one-time migration that reset every saved *When the rounds run out* to Auto: the record, which
 * location is due, the user layer's port, and what a person is told (todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md
 * § S2.8.1). None of it needs the panel, a window or an editor.
 */

test('T1 TheMarkerIsReadDefensively', () => {
  assert.equal(recordOf('0.88.0').completed, '0.88.0', 'a bare string in the slot reads as completed');
  assert.equal(recordOf({ completed: '0.87.0' }).completed, '0.87.0', 'an older version means done too');
  assert.equal(recordOf({ completed: '9.9.9' }).completed, '9.9.9', 'and a newer one: the migration is one-time, never per version');
  for (const unreadable of [42, null, [], {}, { completed: 5 }, undefined, true]) {
    assert.equal(recordOf(unreadable).completed, undefined, `${JSON.stringify(unreadable)} is not a marker`);
  }
  assert.equal(recordOf({ claim: { by: 'A', at: 'soon' } }).claim, undefined, 'a claim with a non-number `at` is dropped');
  assert.deepEqual(recordOf({ claim: { by: 'A', at: 5 }, held: { user: 'human', x: 7 }, told: true, refusedTold: 1 }), {
    claim: { by: 'A', at: 5 },
    held: { user: 'human' },
    told: true,
  });
});

test('T2 OverlaysAreFoundByTheirOwnKeys_AForgottenSideToo', async () => {
  const client = new ClientState();
  const store = client.window();
  const registered = stampSide(undefined, sideKey(SIDE_X), familyOf('linux'), 'Linux', 1_700_000_000_000);
  assert.ok(registered, 'the registry record for side X is a real one');
  await store.update(registered.key, registered.record);
  await store.update(KEY_X, { onExhausted: 'human' });
  await store.update(KEY_Y, { onExhausted: 'continue' });
  await store.update(KEY_STRING, 'not an overlay');
  await store.update('coai.settingsOverlayX', { onExhausted: 'human' });

  assert.deepEqual(planOf(store, undefined), { [KEY_X]: 'human', [KEY_Y]: 'continue' },
    'side Y has no registry record and its overlay is found all the same; the string slot and the look-alike key are not');
});

test('T3 OnlyAValueThatKeepsAPersonOffAutoIsHeld', async () => {
  for (const kept of ['human', 'continue', 'humna', null, 5]) {
    assert.equal(heldText(kept), typeof kept === 'string' ? kept : JSON.stringify(kept), `${JSON.stringify(kept)} is held`);
  }
  assert.equal(heldText(undefined), undefined);
  assert.equal(heldText('auto'), undefined, 'a location that holds auto is already what is wanted');

  const store = new ClientState({ [KEY_X]: { onExhausted: 'human', [OVERLAY_WITNESS]: '0.88.0' }, [KEY_Y]: { onExhausted: 'auto' } }).window();
  assert.deepEqual(planOf(store, 'human'), { user: 'human', [KEY_X]: 'human' },
    'an overlay that carries the witness is planned like any other (the witness is asked in isDue); an auto one is not planned');
});

test('T4 ALocationIsDueOnlyWhileUnwitnessedAndUnchanged', () => {
  assert.equal(isDue(false, 'human', 'human'), true, 'all three conditions hold');
  assert.equal(isDue(true, 'human', 'human'), false, 'a witness names it');
  assert.equal(isDue(false, undefined, 'human'), false, 'it holds nothing to migrate');
  assert.equal(isDue(false, 'auto', 'human'), false, 'it holds auto');
  assert.equal(isDue(false, 'continue', 'human'), false, 'the person chose again after the record was made');
  assert.equal(isDue(false, 'human', undefined), false, 'it was not in the record');
  assert.equal(isDue(false, undefined, undefined), false, 'nothing held and nothing recorded is no location to clear');
});

test('T5 TheSentencesAreTheParentsAndNothingWider', () => {
  // Typed out here on purpose: these are the literals that must be word for word the parent's (§ 4.6).
  assert.equal(NOW_AUTO, 'When the rounds run out is now Auto — the gate raises no new hold and never waits for you; it logs and moves on. '
    + 'Your AI may still ask you a question its consultants could not settle, or when no consultant can be had. Change it in Reviews › The gate.');
  assert.equal(NOT_RESET, 'When the rounds run out could not be reset to Auto in your settings: the settings file refused the change. '
    + 'Your earlier choice there is still in force. ConnectOtherAIs tries again at the next start.');
  assert.equal(staysInForce('Ask a human', WHERE.user), 'When the rounds run out now defaults to Auto, but Ask a human stays in force in this window: '
    + 'your user settings sets it. Change it in Reviews › The gate.');
  assert.deepEqual({ ...WHERE }, { side: 'this side\'s own settings', workspace: 'this workspace\'s .vscode/settings.json', user: 'your user settings' });
  for (const sentence of [NOW_AUTO, NOT_RESET, staysInForce('Ask a human', WHERE.user)]) {
    assert.doesNotMatch(sentence, /never asks you anything/u);
  }
});

test('T6 TheUserLayerReadsAndWritesThePersonsOwnLayerOnly', async () => {
  const settings = new FakeUserSettings();
  settings.declare('onExhausted', 'auto');
  settings.declare(USER_WITNESS);
  for (const key of ['onExhausted', USER_WITNESS]) {
    settings.set(key, 'global', 'human');
    settings.set(key, 'workspace', 'continue');
    settings.set(key, 'folder', 'good_enough');
  }
  const layer = userLayerOf(() => settings.config(), GLOBAL);

  assert.equal(layer.value(), 'human', 'the value is the person\'s own layer, not the workspace\'s or the folder\'s');
  assert.equal(layer.witness(), 'human');
  settings.set(USER_WITNESS, 'global', undefined);
  assert.equal(layer.witness(), undefined, 'a witness set only in the workspace and folder layers reads as absent');

  await layer.writeWitness();
  await layer.withdrawWitness();
  await layer.clear();
  assert.deepEqual(settings.updates, [
    { key: USER_WITNESS, value: USER_WITNESS_WORD, target: GLOBAL },
    { key: USER_WITNESS, value: undefined, target: GLOBAL },
    { key: 'onExhausted', value: undefined, target: GLOBAL },
  ]);
  for (const key of ['onExhausted', USER_WITNESS]) {
    assert.equal(settings.layer(key, 'workspace'), 'continue', 'a workspace value is never written');
    assert.equal(settings.layer(key, 'folder'), 'good_enough', 'a folder value is never written');
  }

  assert.equal(layer.known(), true, 'a declared witness key is known');
  settings.set('onExhausted', 'global', 'continue');
  assert.equal(layer.value(), 'continue', 'a value set between two reads is seen by the second');
  settings.undeclare(USER_WITNESS);
  assert.equal(layer.known(), false, 'an undeclared one is not');
});

/** One row of T39: what the window's three reads hold. */
interface InForceRow {
  readonly name: string;
  readonly layers: HeldLayers;
  readonly perSide: boolean;
  readonly overlay: Record<string, unknown>;
  readonly where: SetWhere | undefined;
}

const ROWS: readonly InForceRow[] = [
  { name: 'this side\'s overlay holds continue, the switch on', layers: { globalValue: 'human' }, perSide: true, overlay: { onExhausted: 'continue' }, where: 'side' },
  { name: 'the same with the switch off', layers: { globalValue: 'human' }, perSide: false, overlay: { onExhausted: 'continue' }, where: 'user' },
  { name: 'an overlay that holds auto over a workspace human', layers: { workspaceValue: 'human' }, perSide: true, overlay: { onExhausted: 'auto' }, where: 'side' },
  { name: 'a folder over a workspace over the user layer', layers: { workspaceFolderValue: 'continue', workspaceValue: 'human', globalValue: 'good_enough' }, perSide: false, overlay: {}, where: 'workspace' },
  { name: 'a workspace over the user layer', layers: { workspaceValue: 'escalate', globalValue: 'human' }, perSide: false, overlay: {}, where: 'workspace' },
  { name: 'the user layer alone', layers: { globalValue: 'human' }, perSide: false, overlay: {}, where: 'user' },
  { name: 'nothing held', layers: {}, perSide: false, overlay: {}, where: undefined },
  { name: 'a junk word', layers: { globalValue: 'humna' }, perSide: false, overlay: {}, where: 'user' },
  { name: 'a null in the folder falls to the workspace', layers: { workspaceFolderValue: null, workspaceValue: 'human' }, perSide: false, overlay: {}, where: 'workspace' },
];

/** What the real reader answers for those reads — the old registry's default `human` answered by `merged`. */
function readerAnswer(row: InForceRow): string {
  const store: KeyValueStore = {
    get: <T,>() => row.overlay as T,
    update: () => Promise.resolve(),
  };
  const merged: ConfigReader = (section) => (section === 'onExhausted' ? 'human' : undefined);
  const reader = sideConfigReader(userLayerReader(merged, () => row.layers), row.perSide, store, SIDE_X);

  return settingsFrom(reader).onExhausted;
}

test('T39 TheValueInForceIsTheOneTheReaderAnswers_AndItsPlace', () => {
  for (const row of ROWS) {
    const inForce = inForceOf(row.layers, row.perSide, row.overlay);
    const answered = readerAnswer(row);
    const expected = answered === 'auto' ? NOW_AUTO : staysInForce(labelOf(answered as Parameters<typeof labelOf>[0]), WHERE[row.where ?? 'user']);

    assert.equal(inForce.where, row.where, `${row.name}: where`);
    assert.equal(sentenceFor(inForce), expected, `${row.name}: the sentence rests on what the reader answers (${answered})`);
  }
  assert.equal(readerAnswer(ROWS.find((row) => row.name === 'nothing held') as InForceRow), 'auto',
    'with nothing held the reader answers auto, never the registry\'s old default — S1\'s fix forward');
});

/** The repository's `src_vs_code`. The suite runs from its root, which is what `npm test` does. */
const ROOT = process.cwd();

const sourceOf = (name: string): string => readFileSync(join(ROOT, 'src', name), 'utf8');

/** The module specifiers a source file imports. */
const importsOf = (source: string): readonly string[] => [...source.matchAll(/\bfrom '([^']+)'/gu)].map((match) => match[1] ?? '');

const NEW_MODULES = ['onExhaustedMigration.ts', 'onExhaustedTold.ts', 'onExhaustedMigrationHost.ts'];

test('T30 TheRunStartsFromActivation_NeverFromThePanel', () => {
  // A source scan is right here: there is no program a unit test can run to see where a call is made from (.agents/PROJECT.md).
  assert.equal(sourceOf('extension.ts').match(/startOnExhaustedMigration\(/gu)?.length, 1, 'activation calls it exactly once');
  for (const panel of ['panelProvider.ts', 'panelView.ts']) {
    assert.doesNotMatch(sourceOf(panel), /startOnExhaustedMigration|onExhaustedMigration/u, `${panel} must not start the run`);
  }
  for (const name of NEW_MODULES) {
    const imports = importsOf(sourceOf(name));
    assert.equal(imports.some((one) => one === './panelProvider' || one === './panelView'), false, `${name} imports the panel`);
  }
});

test('T30 the scan still finds the imports it looks at — a pattern that matches nothing passes forever', () => {
  assert.ok(importsOf(sourceOf('onExhaustedMigration.ts')).includes('./coaiInstall'), 'the run module imports ./coaiInstall');
  assert.ok(importsOf(sourceOf('onExhaustedMigrationHost.ts')).includes('./onExhaustedMigration'), 'the host imports the run');
  assert.equal(sourceOf('extension.ts').includes("from './onExhaustedMigrationHost'"), true, 'activation imports the host module');
});

test('the record door is built only behind the test-mode check, and is optional in what activation returns', () => {
  const text = sourceOf('extension.ts');
  const built = [...text.matchAll(/onExhaustedMigration\s*:\s*\(\)\s*=>/gu)];

  assert.equal(built.length, 1, `the scan expected ONE place that hands out the record, found ${built.length}`);
  const before = text.slice(Math.max(0, (built[0]?.index ?? 0) - 160), built[0]?.index ?? 0);
  assert.match(before, /extensionMode\s*===\s*vscode\.ExtensionMode\.Test\s*\?\s*\{\s*$/u, 'the record is reachable outside test mode');
  assert.match(text, /readonly onExhaustedMigration\?:\s*\(\)\s*=>\s*unknown/u, 'the field of CoaiExports is not optional, so production would have to supply it');
});

test('T42 TheWitnessNeverTravelsInAConfigFile', () => {
  const manifest = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8')) as {
    readonly contributes: { readonly configuration: { readonly properties: Record<string, Record<string, unknown>> } };
  };
  const property = manifest.contributes.configuration.properties['coai.onExhaustedResetToAuto'];
  assert.ok(property !== undefined, 'the manifest declares the witness');
  assert.equal(property['type'], 'string');
  assert.equal(Object.hasOwn(property, 'default'), false, 'no default: the registry gives a string key \'\'');
  assert.equal(Object.hasOwn(property, 'scope'), false, 'no scope: a workspace could set it, and Settings Sync must carry it');

  const declared = declaredSettings(manifest);
  assert.ok(Object.hasOwn(declared, USER_WITNESS), 'the witness is a declared setting, so only NEVER_TRANSFERRED keeps it out of a file');
  assert.deepEqual(exportedSettings(declared, (key) => (key === USER_WITNESS ? USER_WITNESS_WORD : undefined)), {}, 'an export carries no witness');
  const imported = importedConfig(JSON.stringify({ format: CONFIG_FORMAT, version: 2, settings: { [USER_WITNESS]: USER_WITNESS_WORD }, prompts: {} }), declared);
  assert.ok(imported.ok, 'the file is readable');
  assert.deepEqual(imported.ok ? imported.settings : { unread: true }, {}, 'an import writes no witness');
  assert.deepEqual(imported.ok ? imported.refused.map((one) => one.name) : [], [USER_WITNESS], 'and refuses it by name');
});
