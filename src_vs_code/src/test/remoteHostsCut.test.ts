import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { autoCounts, consentOf, refusedBy } from '../remoteHosts';
import type { HostKeeper } from '../remoteKeeper';
import { hostKeeper } from '../remoteKeeper';
import { readsKey, remoteRoad } from '../remoteRead';
import type { Purpose, RoadSeams } from '../remoteRead';
import { runRound } from '../remoteAccountsRound';
import type { RemoteRun } from '../remoteSsh';
import type { KeeperSeams } from '../remoteKeeper';
import { EMPTY_FILE, refusalsStore } from '../remoteRefusals';
import type { RefusalsRead } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, instant, row, view } from './hostsFakes';
import { callsOf, world } from './roundFakes';

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

// ───────────────── one test for each door that asks the consent ─────────────────
// The doors: the session road's two presses (take the question, add the question: `remoteRoad` with each Purpose), the round's door
// (`keeper.admit`, which is `admitHost`, and the round over it), the terminal items' key (`readsKey`) and `refusedBy`. Each is built over a keeper
// with NO `automatic` seam and the setting ON, as the window builds it, and over a persisted automatic row.

const NAME = 'Creds: h / s1';
type Answer = 'auto' | 'yes' | 'no';

/** A keeper whose list holds one row for 'h': an automatic "yes", a person's "yes" or a person's "no". The setting is on. */
async function withRow(answer: Answer): Promise<HostKeeper> {
  const keeper = keeperOf(new Backing(), new FakeDisk(), true);
  await keeper.commit(answer === 'auto' ? { kind: 'auto', host: 'h' } : { kind: 'person', host: 'h', answer });

  return keeper;
}

/** The session road over a keeper: what it asked the person and how many runs it started. */
async function press(keeper: HostKeeper, purpose: Purpose): Promise<{ readonly refusal: string | undefined; readonly asked: number; readonly runs: number }> {
  const counted = { asked: 0, runs: 0 };
  const refused: RemoteRun = { kind: 'refused', refusal: 'the fake run answers nothing' };
  const seams: RoadSeams = {
    store: { consent: (host) => keeper.consent(host), answer: (host, answer) => keeper.commit({ kind: 'person', host, answer }) },
    ask: () => { counted.asked += 1; return Promise.resolve(undefined); },
    liveName: () => NAME,
    run: () => { counted.runs += 1; return Promise.resolve(refused); },
    pick: () => Promise.resolve(undefined),
    progress: (_host, work) => work(),
    keep: () => undefined,
  };
  const answer = await remoteRoad(seams, NAME, purpose, undefined);

  return { refusal: answer.kind === 'refused' ? answer.refusal : undefined, ...counted };
}

for (const purpose of ['question', 'prompts'] as const) {
  test(`CUT-D1 (${purpose}): the session road on an automatic row, the setting on, asks and starts no run; a person's "yes" reads, a "no" refuses`, async () => {
    const auto = await press(await withRow('auto'), purpose);
    assert.equal(auto.runs, 0, 'the road read a host nobody said yes to');
    assert.equal(auto.asked, 1, 'the road did not ask, as for a host with no answer yet');

    const yes = await press(await withRow('yes'), purpose);
    assert.equal(yes.asked, 0, 'a person\'s "yes" was asked again');
    assert.ok(yes.runs > 0, 'a person\'s "yes" did not read');

    const no = await press(await withRow('no'), purpose);
    assert.deepEqual([no.runs, no.asked], [0, 0], 'a "no" read or asked');
    assert.notEqual(no.refusal, undefined);
  });
}

test('CUT-D2: the round\'s door (admitHost) on an automatic row, the setting on, reads nothing; a person\'s "yes" reads, a "no" refuses', async () => {
  assert.equal((await (await withRow('auto')).admit('h')).kind, 'not-asked', 'the door admitted an automatic row');
  assert.equal((await (await withRow('yes')).admit('h')).kind, 'read');
  assert.equal((await (await withRow('no')).admit('h')).kind, 'refused');
});

test('CUT-D3: a round over the door, with the reading handed on, calls no runner on an automatic row; a person\'s "yes" is called, a "no" is not', async () => {
  const calls = async (answer: Answer): Promise<readonly string[]> => {
    const keeper = await withRow(answer);
    const w = world({ hosts: ['h'] });
    await runRound({ ...w.seams, admit: (host) => keeper.admit(host) }, new Map(), 0, 'timer', undefined, 'on');

    return callsOf(w);
  };

  assert.deepEqual(await calls('auto'), [], 'a runner was called for a host nobody said yes to');
  assert.deepEqual(await calls('yes'), ['h N']);
  assert.deepEqual(await calls('no'), []);
});

test('CUT-D4: the terminal items\' key follows "answered no" only: an automatic row shows them, as no answer does; a "no" hides them', async () => {
  const shown = async (answer: Answer): Promise<boolean> => readsKey(NAME, await (await withRow(answer)).standings());

  assert.equal(await shown('auto'), true, 'an automatic row hid the items');
  assert.equal(await shown('yes'), true);
  assert.equal(await shown('no'), false, 'a "no" left the items shown');
});

test('CUT-D5: refusedBy says "refused" only for a "no", whatever the row beside it', () => {
  const auto = [row('h', 'yes', '2026-11-01T00:00:00.000Z', 'auto')];

  assert.equal(refusedBy(noFile, auto, 'h'), false);
  assert.equal(refusedBy(noFile, [row('h', 'yes')], 'h'), false);
  assert.equal(refusedBy(noFile, [row('h', 'no')], 'h'), true);
});
