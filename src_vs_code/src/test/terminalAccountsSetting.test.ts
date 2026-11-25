import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { CONFIG_FORMAT, CONFIG_VERSION, NEVER_TRANSFERRED, declaredSettings, exportedSettings, importedConfig } from '../configTransfer';
import { ALLOW_NEW_HOSTS_KEY, Inspected, SAFETY_KEYS, USER_LAYER_KEYS, allowNewHostsFrom, ignoredLayerOf, userLayerReader } from '../modelKeys';
import { DEFAULTS, OVERLAID_SETTINGS } from '../settingsShape';

/**
 * The setting `coai.terminalAccountsAllowNewHosts` (E1.S1, A1-T11): only the user layer decides, and nothing carries it in. Pure: the
 * reader over fake layers, the manifest as a file, the transfer rules.
 */

const KEY = ALLOW_NEW_HOSTS_KEY;
const inspector = (layers: Inspected) => (key: string): Inspected | undefined => (key === KEY ? layers : undefined);
const readWith = (layers: Inspected, merged: unknown): boolean => allowNewHostsFrom(userLayerReader(() => merged, inspector(layers))(KEY));
const manifest = JSON.parse(readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8')) as { contributes: { configuration: unknown } };
const property = ((): Record<string, unknown> => {
  const sections = manifest.contributes.configuration;
  const all = (Array.isArray(sections) ? sections : [sections]) as { properties?: Record<string, Record<string, unknown>> }[];

  return all.map((one) => one.properties?.['coai.terminalAccountsAllowNewHosts']).find((one) => one !== undefined) ?? {};
})();

test('A1-T11: only the user layer decides, in every mix of the layers', () => {
  assert.equal(readWith({ defaultValue: true }, true), true, 'nothing set');
  assert.equal(readWith({ defaultValue: true, globalValue: false, workspaceValue: true }, true), false, 'a workspace switched the setting on over the user\'s off');
  assert.equal(readWith({ defaultValue: true, globalValue: true, workspaceValue: false }, false), true, 'a workspace switched the setting off');
  assert.equal(readWith({ defaultValue: true, workspaceValue: false }, false), true, 'a workspace value alone decided');
  assert.equal(readWith({ defaultValue: true, workspaceFolderValue: false }, false), true, 'a folder value alone decided');
  for (const wrong of ['true', 0, {}, 'false']) {
    assert.equal(allowNewHostsFrom(wrong), false, `a value that is not a boolean turned the setting on: ${JSON.stringify(wrong)}`);
  }
  assert.equal(allowNewHostsFrom(null), false);
  assert.equal(readWith({ defaultValue: true, globalValue: null }, undefined), true, 'a null in the user layer reads as nothing held');
  assert.equal(allowNewHostsFrom(undefined), true);
  assert.equal(ignoredLayerOf(inspector({ workspaceValue: false }), KEY), 'workspace');
  assert.equal(ignoredLayerOf(inspector({ workspaceFolderValue: false }), KEY), 'folder');
  assert.equal(ignoredLayerOf(inspector({ workspaceValue: false, workspaceFolderValue: true }), KEY), 'folder', 'the folder is the layer VS Code would have applied');
  assert.equal(ignoredLayerOf(inspector({ globalValue: false }), KEY), undefined);
});

test('A1-T11: the manifest, the safety list, and the transfer rules', () => {
  assert.equal(property['type'], 'boolean');
  assert.equal(property['default'], true, 'the default is not on');
  assert.equal(property['scope'], 'application', 'the scope is not application');
  const words = String(property['markdownDescription']);
  assert.ok(words.includes('default') && words.includes('asked first') && words.includes('user'), 'the description does not name the default, "asked first" and the user setting');
  assert.deepEqual([...SAFETY_KEYS], ['kimiAllowNetworkSignIn', KEY]);
  assert.ok(USER_LAYER_KEYS.includes(KEY));
  assert.ok((NEVER_TRANSFERRED[KEY] ?? '').length > 20);
  const declared = declaredSettings(manifest.contributes.configuration);
  const imported = importedConfig(JSON.stringify({ format: CONFIG_FORMAT, version: CONFIG_VERSION, prompts: {}, settings: { [KEY]: false } }), declared);
  assert.ok(imported.ok && imported.refused.some((one) => one.name.endsWith(KEY) && one.why.startsWith('never transferred:')), 'an imported file decided the setting');
  assert.equal(KEY in exportedSettings(declared, (key) => (key === KEY ? false : undefined)), false, 'an export carried the key');
  assert.ok(!OVERLAID_SETTINGS.includes(KEY), 'a side\'s own value could decide the setting');
  assert.ok(!(KEY in (DEFAULTS as unknown as Record<string, unknown>)));
});
