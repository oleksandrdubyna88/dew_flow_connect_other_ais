import assert from 'node:assert/strict';
import { test } from 'node:test';
import { EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from '../chatDiscovery';
import { chatProvidersFrom, legacyPick, openingModel } from '../chatModels';
import { Vendor } from '../vendors';

/**
 * What the PANEL discovered, handed to the surface that cannot discover it.
 *
 * <p>Three of the four model sources are FETCHED rather than read — the `codex` and `agy` CLIs' own
 * lists and a Team server's allowlist — and all three fetches live in the panel. `chatCatalogFrom`
 * in `chatCommand.ts` passed them EMPTY, so the command's idea of what a row can be pointed at was
 * "the model it is configured to" and nothing else. That was harmless while the panel offered the
 * same flat list of rows. It stopped being harmless the moment the panel offered a two-step picker
 * over its own discovered lists: the person picks `gemini-3.8-flash-low` in the sidebar, the command
 * builds a list that has never heard of it, and the conversation quietly opens on the row's own
 * model instead — a different model, billed, in a voice nobody chose.</p>
 *
 * <p><b>Found by this product's own plan gate</b> (codex, Blocking): "the panel discovers and selects
 * a Team model … `chatCatalogFrom` passes empty discovered lists; `resolveChatPick` can therefore
 * reject the selected name or use the configured row model when the user starts chat." It is the
 * open tail the provider plan recorded, and the tail became a defect when the picker shipped.</p>
 *
 * <p>Pure over a plain store, so every rule below is a unit test rather than a claim.</p>
 */

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'agy',
    runtime: 'antigravity',
    model: 'gemini-3.7-flash-high',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

const DISCOVERED = {
  codex: [{ id: 'gpt-5.2', label: 'gpt-5.2' }, { id: 'gpt-5.2-mini', label: 'gpt-5.2-mini' }],
  agy: [{ id: 'gemini-3.8-flash-low', label: 'Gemini 3.8 Flash (Low)' }],
  catalogs: {
    srv1: {
      serverVersion: '0.5.6',
      isAdmin: false,
      error: '',
      vendors: [{ id: 'codex', runtime: 'codex', models: ['gpt-5.2', 'o5-mini'], slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0 } }],
    },
  },
};

test('a discovered model the panel offered is one the command can still honour', () => {
  // The whole point: this exact value is what the operator's own row is set to, and it exists in no
  // curated list — only in what `agy models` answered, in the panel.
  const vendors = [vendor()];
  const discovery = discoveryFrom(DISCOVERED);
  const list = chatProvidersFrom(vendors, catalogUsing(discovery, []));
  const saved = legacyPick(list, vendors, 'agy');

  assert.strictEqual(openingModel(list, saved, 'gemini-3.8-flash-low'), 'gemini-3.8-flash-low');
});

test('with nothing discovered the command falls back, which is the defect this closes', () => {
  const vendors = [vendor()];
  const list = chatProvidersFrom(vendors, catalogUsing(EMPTY_DISCOVERY, []));
  const saved = legacyPick(list, vendors, 'agy');

  assert.strictEqual(openingModel(list, saved, 'gemini-3.8-flash-low'), 'gemini-3.7-flash-high');
});

test('a Team server’s allowlist crosses too, attached to the server it belongs to', () => {
  // `teamServerId` and `remoteVendor` are how a remote row finds its allowlist — the second is the
  // field this family lost once for three releases, so a fixture that omitted it would be testing
  // the fallback and calling it the feature.
  const vendors = [vendor({
    id: 'srv1-codex', runtime: 'remote', model: 'gpt-5.2', teamServerId: 'srv1', remoteVendor: 'codex',
  })];
  const servers = [{ id: 'srv1', name: 'Company', url: 'https://coai.example.com' }];
  const discovery = discoveryFrom(DISCOVERED);
  const list = chatProvidersFrom(vendors, catalogUsing(discovery, servers));
  const saved = legacyPick(list, vendors, 'srv1-codex');

  assert.strictEqual(openingModel(list, saved, 'o5-mini'), 'o5-mini', 'the server’s allowlist did not cross');
});

test('a store holding nothing, or junk, is an empty discovery rather than a crash', () => {
  // `globalState` is written by this extension, but it OUTLIVES it: a value written by an older
  // version is the ordinary case on every update, and it must not take the chat down with it.
  assert.deepStrictEqual(discoveryFrom(undefined), EMPTY_DISCOVERY);
  assert.deepStrictEqual(discoveryFrom('nonsense'), EMPTY_DISCOVERY);
  assert.deepStrictEqual(discoveryFrom({ codex: 'not a list', agy: 7, catalogs: null }), EMPTY_DISCOVERY);
});

test('a half-valid entry is dropped, and the rest of the list survives it', () => {
  const discovery = discoveryFrom({
    codex: [{ id: 'gpt-5.2', label: 'gpt-5.2' }, { id: '', label: 'nameless' }, 'a string'],
    agy: [],
    catalogs: {},
  });

  assert.deepStrictEqual(discovery.codex, [{ id: 'gpt-5.2', label: 'gpt-5.2' }]);
});
