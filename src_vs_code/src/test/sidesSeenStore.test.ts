import assert from 'node:assert/strict';
import { test } from 'node:test';

import { sideKey } from '../coaiInstall';
import { forgetSide, sideHereOf, sideSeenKey, sidesIn, stampThisSide, type SideHere, type SideStore } from '../sidesSeen';

/**
 * The side registry against a store (research/PLAN_paths_per_side.md E2.2): what activation writes into `globalState`, at
 * most once a day, what it prunes, and what **Forget** removes. The store is a map behind the three `Memento` members the
 * registry uses — the same members `context.globalState` answers in a real host, where `test:host` proves the stamp lands.
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 10, 12, 0, 0);

/** A `Memento` as far as the registry uses one, counting its writes; `update(key, undefined)` deletes, as VS Code's does. */
class MapStore implements SideStore {
  writes = 0;

  constructor(readonly values = new Map<string, unknown>()) {}

  keys(): readonly string[] {
    return [...this.values.keys()];
  }

  get(key: string): unknown {
    return this.values.get(key);
  }

  update(key: string, value: unknown): PromiseLike<void> {
    this.writes += 1;
    if (value === undefined) {
      this.values.delete(key);
    } else {
      this.values.set(key, value);
    }

    return Promise.resolve();
  }
}

const WINDOWS: SideHere = { key: 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais', family: 'windows', label: 'Windows' };
/** A reporter for a store that must not refuse: any call is the failure. */
const NOT_REFUSED = (error: unknown): void => { assert.fail(`a working store reported a failure: ${String(error)}`); };

const WSL = { family: 'posix', label: 'WSL: Ubuntu', firstSeen: NOW - 20 * DAY, lastSeen: NOW - 2 * DAY };

test('activation stamps this side, and the store then lists it beside the other side it already held', async () => {
  const store = new MapStore(new Map([[sideSeenKey('wsl|Ubuntu|/home/me/s'), WSL]]));

  await stampThisSide(store, WINDOWS, NOW, NOT_REFUSED);

  assert.deepEqual(store.get(sideSeenKey(WINDOWS.key)), { family: 'windows', label: 'Windows', firstSeen: NOW, lastSeen: NOW });
  assert.deepEqual(sidesIn(store, NOW).map((one) => [one.side, one.family]), [[WINDOWS.key, 'windows'], ['wsl|Ubuntu|/home/me/s', 'posix']]);
});

test('a second activation the same day writes nothing at all', async () => {
  const store = new MapStore();
  await stampThisSide(store, WINDOWS, NOW, NOT_REFUSED);
  const after = store.writes;

  await stampThisSide(store, WINDOWS, NOW + DAY - 1, NOT_REFUSED);

  assert.equal(store.writes, after, 'a window start within one day wrote to globalState again');
  assert.equal(after, 1, 'the first stamp was not exactly one write');
});

test('a stamp prunes the sides not seen for 90 days, and leaves every other key of the store alone', async () => {
  const gone = sideSeenKey('wsl|Old|/home/me/s');
  const store = new MapStore(new Map<string, unknown>([
    [gone, { ...WSL, lastSeen: NOW - 91 * DAY }],
    ['coai.firstSeenControls', { 'model.fast': NOW }],
    [sideSeenKey('wsl|Ubuntu|/home/me/s'), WSL],
  ]));

  await stampThisSide(store, WINDOWS, NOW, NOT_REFUSED);

  assert.equal(store.get(gone), undefined, 'a side gone for 91 days was kept');
  assert.deepEqual([...store.keys()].sort(), ['coai.firstSeenControls', sideSeenKey('wsl|Ubuntu|/home/me/s'), sideSeenKey(WINDOWS.key)].sort());
});

test('Forget removes another side\'s record — and never this window\'s own, which is in use now', async () => {
  const other = 'wsl|Ubuntu|/home/me/s';
  const store = new MapStore(new Map<string, unknown>([[sideSeenKey(other), WSL]]));
  await stampThisSide(store, WINDOWS, NOW, NOT_REFUSED);

  assert.equal(await forgetSide(store, WINDOWS.key, WINDOWS.key), false);
  assert.equal(await forgetSide(store, other, WINDOWS.key), true);

  assert.deepEqual(sidesIn(store, NOW).map((one) => one.side), [WINDOWS.key]);
});

test('a window names its side as the This side page does: a local Windows window is Windows, never "this machine"', () => {
  const local = { storagePath: 'C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais', hostname: 'box' };
  const wsl = { remoteName: 'wsl', distro: 'Ubuntu', hostname: 'box', storagePath: '/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais' };

  assert.deepEqual(sideHereOf(local, 'win32'), { key: sideKey(local), family: 'windows', label: 'Windows' });
  assert.deepEqual(sideHereOf(wsl, 'linux'), { key: sideKey(wsl), family: 'posix', label: 'WSL: Ubuntu' });
});

test('a store that refuses the stamp is reported, never thrown — and the next activation with a working store still records the side', async () => {
  // The coordinator's accepted finding on E2: the stamp is never awaited by activation, so a rejection nobody observes
  // would be an unhandled rejection with no trace. It is caught and handed to the reporter (the extension host's log).
  const values = new Map<string, unknown>();
  const refusing: SideStore = {
    keys: () => [...values.keys()],
    get: (key) => values.get(key),
    update: () => Promise.reject(new Error('the state database is locked')),
  };
  const reported: unknown[] = [];

  await assert.doesNotReject(stampThisSide(refusing, WINDOWS, NOW, (error) => { reported.push(error); }), 'a refused stamp rejected');
  assert.deepEqual(reported.map((error) => (error as Error).message), ['the state database is locked'], 'the refusal was swallowed, not reported');

  const working = new MapStore(values);
  await stampThisSide(working, WINDOWS, NOW + 60_000, () => { assert.fail('a working store reported a failure'); });
  assert.deepEqual(working.get(sideSeenKey(WINDOWS.key)), { family: 'windows', label: 'Windows', firstSeen: NOW + 60_000, lastSeen: NOW + 60_000 },
    'after a refused stamp, the next one did not record the side');
});
