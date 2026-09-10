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

test('a cached catalog is ignored when the server it was fetched from is not the one configured now', () => {
  // The code round (codex, Major): a catalog is keyed by server ID alone, so a server whose URL was
  // corrected — or an ID reused in another workspace — hands the command an allowlist fetched from a
  // different endpoint, and a model withdrawn there reads as offered here.
  const vendors = [vendor({
    id: 'srv1-codex', runtime: 'remote', model: 'gpt-5.2', teamServerId: 'srv1', remoteVendor: 'codex',
  })];
  const discovery = discoveryFrom({ ...DISCOVERED, catalogs: { srv1: { ...DISCOVERED.catalogs.srv1, url: 'https://old.example.com' } } });
  const list = chatProvidersFrom(vendors, catalogUsing(discovery, [{ id: 'srv1', name: 'Company', url: 'https://coai.example.com' }]));
  const saved = legacyPick(list, vendors, 'srv1-codex');

  assert.strictEqual(openingModel(list, saved, 'o5-mini'), 'gpt-5.2', 'a catalog from another address was trusted');
});

test('a stored key that would reach Object.prototype is not one', () => {
  // `globalState` is JSON a previous build wrote, and `catalogs[id] = …` on a plain object is how a
  // `__proto__` key stops being data. (gemini, the code round.)
  const discovery = discoveryFrom({ codex: [], agy: [], catalogs: { __proto__: { vendors: [] } } });

  assert.deepStrictEqual(Object.keys(discovery.catalogs), [], 'a prototype key was taken as a server id');
  assert.strictEqual(({} as Record<string, unknown>)['vendors'], undefined, 'Object.prototype was polluted');
});

test('a server whose id is a prototype key is answered, not thrown at', () => {
  // CodeRabbit on PR #196, Major, and it reproduces: `Object.fromEntries` builds a normal-prototype
  // object, so `catalogs['__proto__']` returns Object.prototype itself — an INHERITED value, not a
  // missing one — and reading `.url.length` off it throws a TypeError that takes down the panel
  // render and the chat command for anybody with a server configured under that id.
  const discovery = discoveryFrom({ codex: [], agy: [], catalogs: {} });

  const built = catalogUsing(discovery, [{ id: '__proto__', name: 'Odd', url: 'https://coai.example.com' }]);

  assert.strictEqual(built.teamServers[0]!.catalog, undefined, 'an inherited value was read as a catalog');
});

test('a catalog kept from a FAILED refresh is not republished as current', () => {
  // CodeRabbit on PR #196, Major: the panel keeps the previous catalog when a refresh fails and marks
  // it `stale`. Storing it without that flag would launder it — `catalogUsing` rebuilds a state with
  // `stale: false`, and a model the server has since withdrawn reads as offered in the command.
  const vendors = [vendor({
    id: 'srv1-codex', runtime: 'remote', model: 'gpt-5.2', teamServerId: 'srv1', remoteVendor: 'codex',
  })];
  const servers = [{ id: 'srv1', name: 'Company', url: 'https://coai.example.com' }];
  const discovery = discoveryFrom({
    ...DISCOVERED,
    catalogs: { srv1: { ...DISCOVERED.catalogs.srv1, url: 'https://coai.example.com', stale: true } },
  });

  assert.strictEqual(catalogUsing(discovery, servers).teamServers[0]!.stale, true, 'the stale mark was laundered away');

  // The mark TRAVELS; the allowlist is still honoured, and that half of the review is declined on
  // purpose. The person picked their model FROM this list, in a panel that was showing it marked as
  // stale — refusing to honour that pick means silently opening on the row's own model instead,
  // which is the exact substitution this whole change exists to remove. Dropping stale catalogs
  // would also change `allowedModelsFor` for the reviewer cards, which read the same three states.
  const list = chatProvidersFrom(vendors, catalogUsing(discovery, servers));
  const saved = legacyPick(list, vendors, 'srv1-codex');

  assert.strictEqual(openingModel(list, saved, 'o5-mini'), 'o5-mini', 'a pick made from the shown list was not honoured');
});
