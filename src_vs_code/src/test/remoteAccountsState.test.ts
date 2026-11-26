import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import {
  STATE_KEY, accountsStateOf, armFor, isStopped, permissionRowOf, runRound, startTheRound, stateMerge, stopTheRound, stoppedOf,
} from '../remoteAccountsRound';
import type { RoundSeams } from '../remoteAccountsRound';
import { Backing, FakeDisk, KEY, admit, windowOver } from './hostsFakes';
import type { Window } from './hostsFakes';
import { OFF, accounts, claudeFound, world } from './roundFakes';
import { STAMP_MAX } from '../remotePermission';

/**
 * The Stop and the state row (E1.S3, A3-T23) and what a round leaves in the store (A3-T24): two or more windows of one profile over ONE
 * backing `globalState` in `'whole-state'` mode and ONE fake disk — S1's own fakes. The Stop is the refusals file's; the state row holds a copy.
 */

const T = '2026-11-24T10:00:00.000Z';
const T2 = '2026-11-24T11:00:00.000Z';

const stateOf = (w: Window): unknown => w.view.get(STATE_KEY);
const stoppedIn = (w: Window): Promise<boolean> => isStopped(w.keeper, () => stateOf(w));
/** What the timer decides for a window with an eligible host and nothing armed, given whether the round is stopped. */
const armWhen = (stopped: boolean): string => armFor({ eligible: true, permitted: true, armed: false, stopped, running: false });
const whole = (backing: Backing, disk: FakeDisk): Window => windowOver(backing, disk, { mode: 'whole-state' });

/** A round in window `w`: its own `stopped` seam, the keeper's own door counted; a fake call that answers one account. */
function roundIn(w: Window): { readonly seams: RoundSeams; readonly admitted: string[]; readonly called: string[] } {
  const base = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'x@example.test' }) });
  const admitted: string[] = [];
  const called: string[] = [];
  const seams: RoundSeams = {
    ...base.seams,
    stopped: () => stoppedIn(w),
    may: (host) => { admitted.push(host); return w.keeper.may('round', host); },
    call: (host, mode) => { called.push(`${host} ${mode}`); return base.seams.call(host, mode); },
  };

  return { seams, admitted, called };
}

test('A3-T23: the Stop holds against a stale window\'s view, and survives a reload', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = whole(backing, disk);
  const b = whole(backing, disk);

  await stopTheRound(a.keeper, T);
  assert.equal(await stoppedIn(a), true);
  // B was not synced: its own stale copy is what `stateMerge` is handed, and its write puts that whole copy back.
  await b.keeper.commitKey(STATE_KEY, { canBeShown: T2 }, stateMerge);
  assert.equal((backing.values.get(STATE_KEY) as { stoppedUtc?: string }).stoppedUtc, undefined, 'the fixture did not lose the copy, so this test proves nothing');

  assert.equal(await stoppedIn(b), true, 'a stale window\'s notice write started a stopped round');
  const round = roundIn(b);
  await runRound(round.seams, new Map(), 0, 'timer', OFF);
  assert.deepEqual([round.called, round.admitted], [[], []], 'a stale window\'s notice write started a stopped round');
  assert.equal(armWhen(await stoppedIn(b)), 'clear');

  const reloaded = [whole(backing, disk), whole(backing, disk)];
  for (const w of reloaded) {
    assert.equal(await stoppedIn(w), true, 'a reload forgot the Stop');
    assert.equal(armWhen(await stoppedIn(w)), 'clear', 'a timer was armed after a reload');
  }

  // The copy alone stops too: the file says running, the row has a `stoppedUtc` (another side's Stop).
  const copyBacking = new Backing();
  copyBacking.values.set(STATE_KEY, { stoppedUtc: T });
  const copyWindow = whole(copyBacking, new FakeDisk());
  assert.equal(await copyWindow.keeper.roundStop(), 'running');
  assert.equal(await stoppedIn(copyWindow), true, 'the copy of the Stop in the row did not stop the round');
});

test('A3-T23: a start clears the copy only after the file took it', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = whole(backing, disk);
  const b = whole(backing, disk);
  await stopTheRound(a.keeper, T);
  b.view.sync();

  assert.equal(await startTheRound(b.keeper), true);
  a.view.sync();
  b.view.sync();
  for (const w of [a, b]) {
    assert.equal(await stoppedIn(w), false, 'a start did not reach the other window');
  }
  assert.deepEqual(disk.file()?.stop, undefined);

  // A start the file did not take: `unwritten`.
  const c = whole(backing, disk);
  await stopTheRound(c.keeper, T);
  disk.failReplace = true;
  assert.equal(await startTheRound(c.keeper), false, 'a start that was not saved was shown as started');
  assert.equal(await stoppedIn(c), true, 'a start that was not saved was shown as started');
  assert.equal((stateOf(c) as { stoppedUtc?: string }).stoppedUtc, T, 'a start that was not saved was shown as started: the copy was cleared before the file took it');
  disk.failReplace = false;

  // A start over a damaged file: `damaged`.
  disk.files.set('refused.json', '{');
  const d = whole(backing, disk);
  assert.equal(await startTheRound(d.keeper), false);
  assert.equal(await stoppedIn(d), true, 'a start over a damaged file cleared the copy');
  assert.equal((stateOf(d) as { stoppedUtc?: string }).stoppedUtc, T, 'a start over a damaged file cleared the copy');
});

test('A3-T23: a Stop the file did not take is written as a copy all the same, and every admission of that window is unwritten', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.failReplace = true;
  const w = whole(backing, disk);

  await stopTheRound(w.keeper, T);

  assert.equal((stateOf(w) as { stoppedUtc?: string }).stoppedUtc, T, 'the copy of an unwritten Stop was not written');
  assert.equal((await admit(w, 'h')).kind, 'unwritten');
  assert.equal(await stoppedIn(w), true);
});

test('A3-T23: stopped() is asked before EVERY call: a Stop written to the file between two calls ends the round before the second', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const mine = whole(backing, disk);
  const other = whole(backing, disk);
  await mine.keeper.allow();
  const base = world({
    hosts: ['a', 'b'],
    hold: async (host) => { if (host === 'a') { await other.keeper.stopRound(); } },
  });
  const seams: RoundSeams = { ...base.seams, stopped: () => stoppedIn(mine), may: (host) => mine.keeper.may('round', host) };

  const result = await runRound(seams, new Map(), 0, 'timer', OFF);

  assert.deepEqual(base.calls.map((call) => call.host), ['a'], 'a stopped round made one more call');
  assert.equal(result.abandoned, true);
});

test('A3-T23: a state row that cannot be read is a stop, and only a person\'s press writes over it', async () => {
  for (const damaged of ['junk', null, 7, [], { stoppedUtc: 5 }] as const) {
    const name = JSON.stringify(damaged);
    const backing = new Backing();
    const disk = new FakeDisk();
    backing.values.set(STATE_KEY, damaged);
    const w = whole(backing, disk);

    assert.equal(stoppedOf(damaged), true, `${name}: a state row that could not be read let the round run`);
    assert.equal(await stoppedIn(w), true, `${name}: a state row that could not be read let the round run`);
    const round = roundIn(w);
    await runRound(round.seams, new Map(), 0, 'timer', OFF);
    assert.deepEqual([round.called, round.admitted], [[], []], `${name}: a state row that could not be read let the round run`);
    assert.equal(armWhen(await stoppedIn(w)), 'clear');
    // `canBeShown` writes nothing.
    await w.keeper.commitKey(STATE_KEY, { canBeShown: T }, stateMerge);
    assert.deepEqual(backing.values.get(STATE_KEY), damaged, `${name}: a notice time was written over a row that could not be read`);
    assert.deepEqual(w.view.updates.filter((one) => one.key === STATE_KEY), [], `${name}: an update was made`);

    // A person's `start` writes an empty row, and the next round runs; a `stop` writes a row with the time.
    const started = whole(backing, new FakeDisk());
    assert.equal(await startTheRound(started.keeper), true);
    assert.deepEqual(backing.values.get(STATE_KEY), {}, `${name}: a start did not write an empty row`);
    assert.equal(await stoppedIn(started), false);
    await started.keeper.allow();
    const next = roundIn(started);
    await runRound(next.seams, new Map(), 0, 'timer', OFF);
    assert.equal(next.called.length, 2, `${name}: after a start the round did not run`);

    const damagedAgain = new Backing();
    damagedAgain.values.set(STATE_KEY, damaged);
    const stopper = whole(damagedAgain, new FakeDisk());
    await stopTheRound(stopper.keeper, T);
    assert.deepEqual(damagedAgain.values.get(STATE_KEY), { stoppedUtc: T }, `${name}: a stop did not write a row with its time`);
  }
  for (const fine of [undefined, {}] as const) {
    const backing = new Backing();
    if (fine !== undefined) {
      backing.values.set(STATE_KEY, fine);
    }
    const w = whole(backing, new FakeDisk());
    assert.equal(stoppedOf(fine), false, 'the companion: an absent or empty row is not a stop');
    assert.equal(await stoppedIn(w), false);
  }
});

test('A3-T23: the state row — what it reads, and each change', () => {
  assert.deepEqual(accountsStateOf(undefined), { kind: 'state', state: {} });
  assert.deepEqual(accountsStateOf({ toldUtc: T, other: 1 }), { kind: 'state', state: { toldUtc: T, other: 1 } }, 'another key was not kept');
  assert.deepEqual(accountsStateOf({ stoppedUtc: 5 }), { kind: 'damaged' });
  assert.deepEqual(accountsStateOf('junk'), { kind: 'damaged' });

  const merged = (stored: unknown, change: Parameters<typeof stateMerge>[1]): { next: unknown; result: unknown } => {
    const { next, result } = stateMerge(stored, change);

    return { next: next?.value, result };
  };
  assert.deepEqual(merged({ canBeShownUtc: T }, { canBeShown: T2 }).next, undefined, 'the notice time was overwritten: the first stays, it is once for each installation');
  assert.deepEqual(merged(undefined, { canBeShown: T }).next, { canBeShownUtc: T });
  assert.deepEqual(merged({ toldUtc: T, extra: 1 }, { stop: T2 }).next, { toldUtc: T, extra: 1, stoppedUtc: T2 });
  assert.deepEqual(merged({ stoppedUtc: T, toldUtc: T }, { start: true }).next, { toldUtc: T });
  assert.equal(merged({ toldUtc: T }, { start: true }).next, undefined, 'a start with nothing to clear wrote');
});

test('A3-T24: nothing of a reading is stored', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const w = whole(backing, disk);
  await w.keeper.allow();
  const base = world({ hosts: ['a', 'b'], reply: (_host, mode) => claudeFound('secret-mark@example.test', { org: 'secret-org-mark', usage: mode === 'claude-usage' ? { state: 'read', windows: [], unmatched: 0 } : undefined }) });
  const seams: RoundSeams = { ...base.seams, may: (host) => w.keeper.may('round', host), stopped: () => stoppedIn(w) };

  let memory = new Map();
  for (let round = 0; round < 3; round += 1) {
    memory = new Map((await runRound(seams, memory, round, 'timer', { kind: 'every-round' })).memory);
  }
  assert.ok(memory.size === 2, 'the rounds did not run, so the store assertions below see nothing');
  // The window DOES hold the marks (in memory only): a reading that carried none would make the store checks below pass over nothing.
  const retained = JSON.stringify([...memory.values()]);
  assert.ok(retained.includes('secret-mark@example.test') && retained.includes('secret-org-mark'), 'the retained readings hold no secret mark, so "nothing was stored" proves nothing');

  const keys = [...backing.values.keys()].sort();
  assert.ok(keys.every((key) => key === KEY || key === STATE_KEY), `an unexpected key in globalState: ${keys.join(', ')}`);
  const state = backing.values.get(STATE_KEY);
  assert.ok(state === undefined || (Object.keys(state as object).length <= 3 && JSON.stringify(state).length < 150), 'the state row holds more than its three fields');
  const everything = JSON.stringify([...backing.values]) + JSON.stringify([...disk.files]);
  assert.ok(!everything.includes('secret-mark') && !everything.includes('secret-org-mark'), 'an email reached globalState');
  assert.deepEqual([...disk.files.keys()].filter((name) => name !== 'refused.json'), [], 'a file other than refused.json was written');
  assert.equal(backing.values.get(KEY), undefined, 'a round wrote a row of the list: since E2.S1 nothing automatic writes one');
});

type RowOf = (stored: unknown, change: Parameters<typeof stateMerge>[1]) => unknown;
const rowAfter: RowOf = (stored, change) => stateMerge(stored, change).next?.value;

test('E2S1-T8 (the merge half): the copy of a withdrawal only grows, and only an Allow replaces a value that is not a stamp or removes it', () => {
  // a copy: absent or lower is raised; the same stamp or higher writes nothing
  assert.deepEqual(rowAfter(undefined, { withdrawn: 4, utc: T, by: 'press' }), { withdrawnStamp: 4 });
  assert.deepEqual(rowAfter({ withdrawnStamp: 2, askedUtc: T }, { withdrawn: 4, utc: T, by: 'read' }), { withdrawnStamp: 4, askedUtc: T });
  assert.equal(rowAfter({ withdrawnStamp: 4 }, { withdrawn: 4, utc: T, by: 'press' }), undefined);
  assert.equal(rowAfter({ withdrawnStamp: 6 }, { withdrawn: 4, utc: T, by: 'press' }), undefined);
  // generated sequences of { withdrawn } changes: the stored stamp never falls
  let seed = 5;
  const next = (n: number): number => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
  for (let run = 0; run < 100; run += 1) {
    let stored: unknown;
    let top = 0;
    for (let step = 0; step < 12; step += 1) {
      const by = next(2) === 0 ? 'press' : 'read';
      const after = rowAfter(stored, { withdrawn: 1 + next(9), utc: T, by });
      stored = after ?? stored;
      const copy = (stored as { withdrawnStamp?: number } | undefined)?.withdrawnStamp ?? 0;
      assert.ok(copy >= top, 'the copy fell');
      top = copy;
    }
  }
  // a floor raises, never lowers; the restart removes
  assert.deepEqual(rowAfter({ withdrawnStamp: 2 }, { floor: 5, utc: T }), { withdrawnStamp: 5 });
  assert.equal(rowAfter({ withdrawnStamp: 5 }, { floor: 3, utc: T }), undefined);
  assert.equal(rowAfter(undefined, { floor: 0, utc: T }), undefined, 'a first Allow wrote a copy that is not a stamp');
  assert.deepEqual(rowAfter({ withdrawnStamp: 7, askedUtc: T }, { floor: 0, utc: T, restart: true }), { askedUtc: T });
  // the question's first asking is written once
  assert.deepEqual(rowAfter({}, { asked: T }), { askedUtc: T });
  assert.equal(rowAfter({ askedUtc: T }, { asked: T2 }), undefined);
});

test('E2S1-T21 (the row half): a copy that is not a stamp is only replaced by an Allow; a row that cannot be read stays stopped', () => {
  for (const bad of ['x', 1.5, 0, -2, {}, STAMP_MAX + 1, null]) {
    assert.equal(permissionRowOf({ withdrawnStamp: bad }).withdrawn, 'unreadable', `${JSON.stringify(bad)} was read as a stamp`);
    assert.equal(rowAfter({ withdrawnStamp: bad }, { withdrawn: 9, utc: T, by: 'read' }), undefined, `a read replaced ${JSON.stringify(bad)}`);
    assert.equal(rowAfter({ withdrawnStamp: bad }, { withdrawn: 9, utc: T, by: 'press' }), undefined, 'a press of Withdraw replaced a value that already withdraws');
    assert.deepEqual(rowAfter({ withdrawnStamp: bad }, { floor: 6, utc: T }), { withdrawnStamp: 6 }, 'an Allow did not replace it');
    assert.deepEqual(rowAfter({ withdrawnStamp: bad }, { floor: 0, utc: T }), {}, 'an Allow with no floor did not remove it');
  }
  // the row is not damaged by a copy of the wrong shape
  assert.deepEqual(accountsStateOf({ withdrawnStamp: 'x', stoppedUtc: T }), { kind: 'state', state: { withdrawnStamp: 'x', stoppedUtc: T } });
  assert.deepEqual(accountsStateOf({ askedUtc: 5 }), { kind: 'damaged' });
  // a row that cannot be read: stopped, no permission; a press writes the copy with the stop's time, a read writes nothing; an Allow leaves it stopped
  assert.deepEqual(permissionRowOf('junk'), { withdrawn: 'unreadable', stopped: true, askedUtc: undefined });
  assert.deepEqual(rowAfter('junk', { withdrawn: 3, utc: T, by: 'press' }), { withdrawnStamp: 3, stoppedUtc: T });
  assert.equal(rowAfter('junk', { withdrawn: 3, utc: T, by: 'read' }), undefined);
  assert.deepEqual(rowAfter('junk', { floor: 4, utc: T }), { withdrawnStamp: 4, stoppedUtc: T });
  assert.equal(stoppedOf(rowAfter('junk', { floor: 4, utc: T })), true, 'an Allow started a round that was stopped');
  assert.equal(rowAfter('junk', { asked: T }), undefined);
});
