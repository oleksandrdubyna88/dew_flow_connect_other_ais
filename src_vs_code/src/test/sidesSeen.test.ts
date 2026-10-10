import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  KNOWN_FOR_MS, SIDE_SEEN_PREFIX, STAMP_EVERY_MS, knownFamilies, pathFieldsModeOf, prunedKeys, seenSides, sideSeenKey, stampSide, twoPathFields,
  type SideRecord,
} from '../sidesSeen';

/**
 * The sides this profile has been used on (todo/PLAN_paths_per_side.md E2.1): one `globalState` record per side, stamped at
 * activation at most once a day, a side known while it was seen within 90 days and pruned after.
 *
 * <p>Every time here is derived from one fixed `NOW` — the module is handed its clock, so nothing below expires with the
 * calendar (`testing.md`, *A test that reads the clock expires*).</p>
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 10, 12, 0, 0);

function record(family: SideRecord['family'], label: string, daysAgo: number): SideRecord {
  return { family, label, firstSeen: NOW - 200 * DAY, lastSeen: NOW - daysAgo * DAY };
}

test('a Windows and a WSL record seen within 90 days make both families known', () => {
  const known = knownFamilies([record('windows', 'Windows', 3), record('posix', 'WSL: Ubuntu', 89)], NOW);

  assert.deepEqual([...known].sort(), ['posix', 'windows']);
});

test('a side seen 91 days ago is pruned and no longer known — one seen exactly 90 days ago still is', () => {
  const entries: (readonly [string, unknown])[] = [
    [sideSeenKey('local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/x'), record('windows', 'Windows', 1)],
    [sideSeenKey('wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/x'), record('posix', 'WSL: Ubuntu', 91)],
    [sideSeenKey('wsl|Debian|/home/me/.vscode-server/data/User/globalStorage/x'), record('posix', 'WSL: Debian', 90)],
  ];

  assert.deepEqual(prunedKeys(entries, NOW), [entries[1]![0]], 'only the 91-day-old record is pruned');
  assert.deepEqual(seenSides(entries, NOW).map((one) => one.label), ['Windows', 'WSL: Debian']);
  assert.deepEqual([...knownFamilies([record('windows', 'Windows', 1), record('posix', 'WSL: Ubuntu', 91)], NOW)], ['windows'],
    'a WSL side gone for 91 days still makes two fields');
  assert.equal(KNOWN_FOR_MS, 90 * DAY);
});

test('a side never seen is stamped now, under its own key', () => {
  const stamp = stampSide(undefined, 'wsl|Ubuntu|/home/me/s', 'posix', 'WSL: Ubuntu', NOW);

  assert.deepEqual(stamp, {
    key: `${SIDE_SEEN_PREFIX}wsl|Ubuntu|/home/me/s`,
    record: { family: 'posix', label: 'WSL: Ubuntu', firstSeen: NOW, lastSeen: NOW },
  });
});

test('a side is stamped at most once a day, and a later stamp keeps when it was first seen', () => {
  const stored = { family: 'windows', label: 'Windows', firstSeen: NOW - 30 * DAY, lastSeen: NOW - STAMP_EVERY_MS + 1 };

  assert.equal(stampSide(stored, 'local|s', 'windows', 'Windows', NOW), undefined, 'stamped twice within one day');
  assert.deepEqual(stampSide({ ...stored, lastSeen: NOW - STAMP_EVERY_MS }, 'local|s', 'windows', 'Windows', NOW)?.record,
    { family: 'windows', label: 'Windows', firstSeen: NOW - 30 * DAY, lastSeen: NOW });
  assert.equal(STAMP_EVERY_MS, DAY);
});

test('whatever else is stored under a side key reads as no record: stamped afresh, pruned, never listed', () => {
  const broken: readonly unknown[] = [null, 'Windows', 7, { family: 'dos', label: 'x', firstSeen: 1, lastSeen: 1 },
    { family: 'posix', label: 'x', firstSeen: 1, lastSeen: 'yesterday' }, { family: 'posix', firstSeen: 1, lastSeen: NOW }];

  for (const stored of broken) {
    assert.deepEqual(stampSide(stored, 'local|s', 'windows', 'Windows', NOW)?.record,
      { family: 'windows', label: 'Windows', firstSeen: NOW, lastSeen: NOW }, `${JSON.stringify(stored)} was believed`);
  }
  const entries = broken.map((stored, at): readonly [string, unknown] => [sideSeenKey(`side-${at}`), stored]);
  assert.deepEqual(prunedKeys(entries, NOW), entries.map(([key]) => key));
  assert.deepEqual(seenSides(entries, NOW), []);
});

test('only keys of the side registry are read or pruned — every other key of the store is left alone', () => {
  const entries: (readonly [string, unknown])[] = [
    ['coai.firstSeenControls', { 'model.fast': NOW }],
    ['coai.settingsOverlay@local|s', { vendors: [] }],
    [sideSeenKey('local|s'), record('windows', 'Windows', 2)],
  ];

  assert.deepEqual(prunedKeys(entries, NOW), []);
  assert.deepEqual(seenSides(entries, NOW).map((one) => one.side), ['local|s']);
});

test('the sides seen are listed newest first, each with the side it belongs to', () => {
  const entries: (readonly [string, unknown])[] = [
    [sideSeenKey('a'), record('windows', 'Windows', 10)],
    [sideSeenKey('b'), record('posix', 'WSL: Ubuntu', 1)],
    [sideSeenKey('c'), record('posix', 'SSH', 40)],
  ];

  assert.deepEqual(seenSides(entries, NOW).map((one) => [one.side, one.label, one.family, one.lastSeen]), [
    ['b', 'WSL: Ubuntu', 'posix', NOW - DAY],
    ['a', 'Windows', 'windows', NOW - 10 * DAY],
    ['c', 'SSH', 'posix', NOW - 40 * DAY],
  ]);
});

test('the switch: Automatic gives two fields once both families were seen within 90 days, counting this window itself', () => {
  const here = { key: 'local|s', family: 'windows', label: 'Windows' } as const;
  const wsl = { side: 'wsl|s', ...record('posix', 'WSL: Ubuntu', 5) };
  const old = { side: 'wsl|o', ...record('posix', 'WSL: Old', 91) };

  assert.equal(twoPathFields({ here, seen: [wsl], mode: 'auto', now: NOW }), true, 'this window is Windows and a WSL side was seen');
  assert.equal(twoPathFields({ here, seen: [], mode: 'auto', now: NOW }), false);
  assert.equal(twoPathFields({ here, seen: [old], mode: 'auto', now: NOW }), false, 'a WSL side gone for 91 days still makes two fields');
  assert.equal(twoPathFields({ here, seen: [], mode: 'always', now: NOW }), true);
  assert.equal(twoPathFields({ here, seen: [wsl], mode: 'never', now: NOW }), false);
});

test('the switch reads auto from anything but always or never', () => {
  assert.deepEqual(['always', 'never', 'auto', undefined, 'sometimes', 3, null].map(pathFieldsModeOf),
    ['always', 'never', 'auto', 'auto', 'auto', 'auto', 'auto']);
});
