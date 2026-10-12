import assert from 'node:assert/strict';
import { test } from 'node:test';

import { overlayKey, type Side } from '../coaiInstall';
import { otherSideDataDirectories } from '../otherSideValues';
import { sideHereOf, sideSeenKey, stampSide, type SideReader } from '../sidesSeen';

/**
 * The other side's data directory, read from its overlay through the side registry (research/PLAN_paths_per_side.md E3.4):
 * a WSL window wrote `/mnt/z/coai` into ITS overlay, and a Windows window — whose own choice is `Z:\coai` — can show it,
 * read-only. The keys are computed by the real `overlayKey` / `sideSeenKey` / `sideHereOf`, never typed by hand.
 */

const NOW = Date.UTC(2026, 9, 10);
const DAY = 24 * 60 * 60 * 1000;

const WINDOWS: Side = { storagePath: 'C:\\Users\\jinx\\AppData\\Roaming\\Code\\User\\globalStorage\\coai' };
const UBUNTU: Side = { remoteName: 'wsl', distro: 'Ubuntu', storagePath: '/home/jinx/.vscode-server/data/User/globalStorage/coai' };
const DEBIAN: Side = { remoteName: 'wsl', distro: 'Debian', storagePath: '/home/jinx/.vscode-server/data/User/globalStorage/coai' };
const ALPINE: Side = { remoteName: 'wsl', distro: 'Alpine', storagePath: '/home/jinx/.vscode-server/data/User/globalStorage/coai' };

/** A store holding a registry record and an overlay per side (any stored value, as `globalState` may hold), that refuses any write. */
function storeOf(entries: readonly (readonly [Side, string, unknown, number])[]): SideReader & { update(): never } {
  const values = new Map<string, unknown>();
  for (const [side, platform, overlay, seen] of entries) {
    const here = sideHereOf(side, platform);
    values.set(sideSeenKey(here.key), stampSide(undefined, here.key, here.family, here.label, seen)!.record);
    values.set(overlayKey(side), overlay);
  }

  return {
    keys: () => [...values.keys()],
    get: (key) => values.get(key),
    update: () => {
      throw new Error('the other side\'s overlay was written from here');
    },
  };
}

test('a Windows window reads the WSL side\'s chosen data directory from that side\'s overlay', () => {
  const store = storeOf([
    [WINDOWS, 'win32', { dataDirectory: 'Z:\\coai' }, NOW],
    [UBUNTU, 'linux', { dataDirectory: '/mnt/z/coai', dataSide: 'wsl' }, NOW - DAY],
  ]);
  const found = otherSideDataDirectories(store, sideHereOf(WINDOWS, 'win32'), NOW);

  assert.deepEqual(found.map(({ label, family, directory, dataSide }) => ({ label, family, directory, dataSide })), [
    { label: 'WSL: Ubuntu', family: 'posix', directory: '/mnt/z/coai', dataSide: 'wsl' },
  ]);
});

test('every side of the other family is listed, newest first; this family\'s sides are not the other side', () => {
  const store = storeOf([
    [WINDOWS, 'win32', { dataDirectory: 'Z:\\coai' }, NOW],
    [UBUNTU, 'linux', { dataDirectory: '/mnt/z/coai' }, NOW - 3 * DAY],
    [DEBIAN, 'linux', {}, NOW - DAY],
  ]);

  assert.deepEqual(otherSideDataDirectories(store, sideHereOf(WINDOWS, 'win32'), NOW).map((one) => [one.label, one.directory]), [
    ['WSL: Debian', ''],
    ['WSL: Ubuntu', '/mnt/z/coai'],
  ], 'a side that chose nothing is listed with an empty directory, never dropped');
  assert.deepEqual(otherSideDataDirectories(store, sideHereOf(UBUNTU, 'linux'), NOW).map((one) => one.directory), ['Z:\\coai'], 'Debian is the same family as Ubuntu');
});

test('a side past the 90 days, or with an overlay that is not a record, reads as no side or no choice — never an error', () => {
  const store = storeOf([
    [WINDOWS, 'win32', {}, NOW],
    [UBUNTU, 'linux', { dataDirectory: 7 }, NOW - DAY],
    [DEBIAN, 'linux', { dataDirectory: '/old' }, NOW - 91 * DAY],
    [ALPINE, 'linux', null, NOW - 2 * DAY],
  ]);

  assert.deepEqual(otherSideDataDirectories(store, sideHereOf(WINDOWS, 'win32'), NOW).map((one) => [one.label, one.directory]), [
    ['WSL: Ubuntu', ''],
    ['WSL: Alpine', ''],
  ]);
});
