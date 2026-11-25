import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { autoCounts, consentOf } from '../remoteHosts';
import { hostKeeper } from '../remoteKeeper';
import type { KeeperSeams } from '../remoteKeeper';
import { EMPTY_FILE, refusalsStore } from '../remoteRefusals';
import type { RefusalsRead } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, instant, row, view } from './hostsFakes';

/**
 * The cut of the accounts plan's stop, on the side of the doors: an automatic "yes" counts as NO answer at every door, whatever the setting
 * says, so the person is asked as before the accounts feature. A person's "yes" and every "no" stay as they are. The keeper here is built the
 * way the window builds it: with no `automatic` seam, so the build's constant decides; `remoteHosts.test.ts` builds its keepers with it on.
 */

const noFile: RefusalsRead = { kind: 'file', file: EMPTY_FILE, waiting: [] };

function keeperOf(backing: Backing, disk: FakeDisk, settingOn: boolean) {
  const seams: KeeperSeams = {
    state: view(backing, 'one-key'),
    key: KEY,
    refusals: refusalsStore(disk, () => disk.now, instant),
    now: () => new Date(disk.now).toISOString(),
    allowNew: () => settingOn,
    changed: () => undefined,
    unkept: () => undefined,
  };

  return hostKeeper(seams);
}

test('CUT-H1: an automatic "yes" is no answer at a door, the setting on or off; a person\'s "yes" and every "no" stay', () => {
  const stored = { auto: [row('h', 'yes', '2026-11-01T00:00:00.000Z', 'auto')], yes: [row('h', 'yes')], no: [row('h', 'no')] };

  for (const allowNew of [true, false]) {
    assert.equal(consentOf(noFile, stored.auto, 'h', allowNew), 'ask', `an automatic "yes" was read as an answer, setting ${allowNew}`);
    assert.equal(consentOf(noFile, stored.yes, 'h', allowNew), 'yes', `a person's "yes" was lost, setting ${allowNew}`);
    assert.equal(consentOf(noFile, stored.no, 'h', allowNew), 'no', `a "no" was lost, setting ${allowNew}`);
  }
});

test('CUT-H2: the session door asks (no run) on an automatic row with the setting on, and the accounts door writes no automatic row', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const keeper = keeperOf(backing, disk, true);
  await keeper.commit({ kind: 'auto', host: 'h' });

  assert.equal(await keeper.consent('h'), 'ask', 'the session door would read a host nobody said yes to');
  assert.equal((await keeper.admit('h')).kind, 'not-asked', 'the accounts door admitted a host on an automatic row');
  assert.equal((await keeper.admit('fresh')).kind, 'not-asked', 'the accounts door allowed a new host by itself');
  assert.equal((backing.values.get(KEY) as { host: string }[]).some((one) => one.host === 'fresh'), false, 'the accounts door wrote an automatic row');

  await keeper.commit({ kind: 'person', host: 'p', answer: 'yes' });
  assert.equal(await keeper.consent('p'), 'yes', 'a person\'s "yes" was lost');
  await keeper.commit({ kind: 'person', host: 'n', answer: 'no' });
  assert.equal(await keeper.consent('n'), 'no', 'a "no" was lost');
});

test('CUT-H3: the setting has no effect in this build: the effective value is off whatever it says', () => {
  assert.equal(autoCounts(true), false, 'the setting switched the automatic allow on');
  assert.equal(autoCounts(false), false);
  assert.equal(autoCounts(true, 'on'), true, 'the switch a later build turns is gone');
});
