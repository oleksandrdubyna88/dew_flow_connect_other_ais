import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { NEVER_TRANSFERRED } from '../configTransfer';
import { type PanelState } from '../panelView';
import type { ProviderHealth, ProvidersAnswer } from '../providers';
import { serverSettingsJson } from '../serverSettingsFile';
import { type Side } from '../coaiInstall';
import { type Inspected, userLayerReader } from '../modelKeys';
import { DEFAULTS, OVERLAID_SETTINGS, envBlock, settingMessageFrom, settingWrite, settingsFrom } from '../settingsShape';
import { sideConfigReader } from '../sideSettings';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { pageTree } from './pageTree';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * `coai.kimiAllowNetworkSignIn` — E6 of todo/PLAN_kimi_and_vibe_on_linux.md, DL6 and Q7. The server half (coai-mcp) refuses a Kimi sign-in
 * folder on network storage (nfs, cifs, sshfs …) by default, because a Kimi on ANOTHER machine that shares the folder can refresh the
 * same rotating token and sign this one out. This is the person's way to say "I never run Kimi elsewhere": a setting, written to the
 * data directory's `settings.json` as `COAI_KIMI_ALLOW_NETWORK_SIGNIN` — which `PanelSettings.KimiAllowNetworkSignIn` reads — and a
 * tick-box on the Kimi card, because every action is a panel control and never only a settings-editor entry.
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const KEY = 'COAI_KIMI_ALLOW_NETWORK_SIGNIN';

const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
  contributes: { configuration: { properties: Record<string, { type?: string; default?: unknown; scope?: string; markdownDescription?: string; description?: string }> } };
};

function stateOn(hostKind: PanelState['hostKind'], allow = false, reported: Record<string, ProviderHealth> = {}): PanelState {
  const answer: ProvidersAnswer = {
    reported, asked: true, answered: true, notes: { unrecognised: [], vaultNote: '', vaultKeys: [], vault: 'not-reported', settingProblems: [] },
  };

  return {
    ...panelState('reviewers'), vendors: [DEFAULT_VENDORS[0]!, KIMI], hostKind, providers: answer, settings: { ...DEFAULTS, kimiAllowNetworkSignIn: allow },
  };
}

const page = (state: PanelState) => runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });
const boxesIn = (state: PanelState) => page(state).controls.filter((one) => one.dataset['setting'] === 'kimiAllowNetworkSignIn');

// ---------------------------------------------------------------- the setting and its wire key

test('it is off by default and writes nothing: a pristine file stays empty', () => {
  assert.equal(DEFAULTS.kimiAllowNetworkSignIn, false);
  assert.equal(envBlock(DEFAULTS)[KEY], undefined);
});

test('switched on, it crosses as COAI_KIMI_ALLOW_NETWORK_SIGNIN=true, in the file the server parses', () => {
  assert.equal(envBlock({ ...DEFAULTS, kimiAllowNetworkSignIn: true })[KEY], 'true');
  const written = JSON.parse(serverSettingsJson({ ...DEFAULTS, kimiAllowNetworkSignIn: true }, DEFAULT_VENDORS)) as Record<string, string>;

  assert.equal(written[KEY], 'true');
});

test('only a stored boolean true turns it on — a string, a number or null stays on the safe side of a rotating sign-in', () => {
  assert.equal(settingsFrom((key) => (key === 'kimiAllowNetworkSignIn' ? true : undefined)).kimiAllowNetworkSignIn, true);
  for (const stored of [undefined, 'true', 1, null, 'yes']) {
    assert.equal(settingsFrom((key) => (key === 'kimiAllowNetworkSignIn' ? stored : undefined)).kimiAllowNetworkSignIn, false, String(stored));
  }
});

test('it is a property of THIS side’s home, so a side can hold its own; and a config export never carries it to another machine', () => {
  assert.ok(OVERLAID_SETTINGS.includes('kimiAllowNetworkSignIn'), 'it would stay shared between a Windows window and a WSL one');
  assert.ok(NEVER_TRANSFERRED['kimiAllowNetworkSignIn'] !== undefined && NEVER_TRANSFERRED['kimiAllowNetworkSignIn'].length > 20,
    'an import must not switch the refusal off on a machine whose home is somebody else’s share');
});

test('the manifest declares it: a boolean, off, and the description names the risk of being signed out', () => {
  const declared = manifest.contributes.configuration.properties['coai.kimiAllowNetworkSignIn'];

  assert.ok(declared !== undefined, 'coai.kimiAllowNetworkSignIn is not in package.json');
  assert.equal(declared.type, 'boolean');
  assert.equal(declared.default, false);
  const said = declared.markdownDescription ?? declared.description ?? '';
  assert.match(said, /network storage/iu);
  assert.match(said, /signed out/iu);
  assert.match(said, /another machine/iu);
});

// ---------------------------------------------------------------- the panel control

test('the Kimi card carries ONE tick-box for it on WSL2 and on Linux, and ticking it writes the setting', () => {
  for (const host of ['wsl2', 'linux'] as const) {
    const state = stateOn(host);
    const boxes = boxesIn(state);
    assert.equal(boxes.length, 1, `${host}: the box is drawn once`);
    assert.equal(boxes[0]!.checked, false, `${host}: off by default`);
    assert.equal(boxes[0]!.dataset['vendor'], undefined, `${host}: a plain setting, never a row's field`);

    const shown = page(state);
    const box = shown.controls.find((one) => one.dataset['setting'] === 'kimiAllowNetworkSignIn')!;
    box.checked = true;
    box.fire('change');

    assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(shown))), { kind: 'plain', key: 'kimiAllowNetworkSignIn', value: true }, host);
  }
});

test('the box draws what is stored, and says what ticking it risks', () => {
  const state = stateOn('wsl2', true);

  assert.equal(boxesIn(state)[0]!.checked, true);
  const card = pageTree(page(state).html).one((node) => node.dataset['modelCard'] === 'kimi', 'the Kimi card');
  assert.match(card.text(), /signed out/u, 'the card names the risk beside the box');
  assert.match(card.text(), /network storage/u);
});

test('a Windows host, a WSL1 host and another row draw no box: there is no mount table to judge, or nothing to admit', () => {
  for (const host of ['windows', 'wsl1', 'darwin'] as const) {
    assert.equal(boxesIn(stateOn(host)).length, 0, host);
  }
  const codexOnly: PanelState = { ...stateOn('wsl2'), vendors: [DEFAULT_VENDORS[0]!] };
  assert.equal(boxesIn(codexOnly).length, 0, 'no Kimi row, no box');
});

// ---------------------------------------------------------------- the row's own warning

test('with the override on, the server’s one-line warning about the network sign-in folder is said on the Kimi card', () => {
  const warning = 'your Kimi sign-in folder is on network storage (nfs4) and coai.kimiAllowNetworkSignIn admits it: a Kimi on another machine that shares this home can refresh the same sign-in and sign you out, and coai cannot see it';
  const state = stateOn('wsl2', true, { kimi: { provider: 'kimi', auth: 'cli', note: '', warning } });
  const card = pageTree(page(state).html).one((node) => node.dataset['modelCard'] === 'kimi', 'the Kimi card');

  assert.match(card.text(), /Runs with a warning:/u);
  assert.ok(card.text().includes('network storage (nfs4)'), 'the server’s own sentence, not a paraphrase');
});

// ---------------------------------------------------------------- who may switch it on (the review of 2026-10-18, A)

/**
 * This override admits a sign-in folder that another machine may refresh. A cloned repository's `.vscode/settings.json` — or a
 * Settings Sync copy from another machine — must never switch the refusal off for the person. Two layers hold that line, and
 * each has its own test: the manifest `scope` (VS Code does not apply a machine-scoped value from a workspace, and Settings Sync
 * does not carry it; a WSL remote window reads its own machine's value), and the reader (the user layer only — the same door the
 * model keys go through, so a value a workspace did write is not read).
 */
const WSL: Side = { remoteName: 'wsl', distro: 'Ubuntu', storagePath: '/home/x/.vscode-server/data/User/globalStorage/coai' };

/** A window's configuration as VS Code answers it: `get` merges a workspace value over the user one, `inspect` names each layer. */
function readerOver(layers: Inspected, perSide: boolean, overlay: Record<string, unknown> = {}) {
  const merged = layers.workspaceFolderValue ?? layers.workspaceValue ?? layers.globalValue ?? layers.defaultValue;
  const inspect = (key: string) => (key === 'kimiAllowNetworkSignIn' ? layers : undefined);
  const store = { get: <T,>() => overlay as T, update: () => Promise.resolve() };

  return sideConfigReader(userLayerReader((key) => (key === 'kimiAllowNetworkSignIn' ? merged : undefined), inspect), perSide, store, WSL);
}

const allowedBy = (reader: (key: string) => unknown) => settingsFrom(reader).kimiAllowNetworkSignIn;

test('the manifest declares it machine-scoped: a workspace cannot set it and Settings Sync does not copy it', () => {
  assert.equal(manifest.contributes.configuration.properties['coai.kimiAllowNetworkSignIn']?.scope, 'machine');
});

test('a workspace or folder value does not switch it on, whether this side keeps its own settings or not', () => {
  for (const layer of ['workspaceValue', 'workspaceFolderValue'] as const) {
    for (const globalValue of [undefined, false]) {
      const layers: Inspected = { defaultValue: false, globalValue, [layer]: true };

      assert.equal(allowedBy(readerOver(layers, false)), false, `${layer}, user ${String(globalValue)}, one shared layer`);
      assert.equal(allowedBy(readerOver(layers, true)), false, `${layer}, user ${String(globalValue)}, per side with an EMPTY overlay`);
      assert.equal(envBlock(settingsFrom(readerOver(layers, true)))[KEY], undefined, `${layer}: nothing crosses to the server`);
    }
  }
});

test('the person’s own tick still switches it on, shared or per side — and a workspace “false” cannot switch it back off', () => {
  assert.equal(allowedBy(readerOver({ defaultValue: false, globalValue: true }, false)), true);
  assert.equal(allowedBy(readerOver({ defaultValue: false, globalValue: true, workspaceValue: false }, false)), true);
  assert.equal(allowedBy(readerOver({ defaultValue: false }, true, { kimiAllowNetworkSignIn: true })), true, 'this side’s own overlay');
  assert.equal(envBlock(settingsFrom(readerOver({ defaultValue: false, globalValue: true }, false)))[KEY], 'true');
});
