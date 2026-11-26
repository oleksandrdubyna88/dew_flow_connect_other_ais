import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { STATE_KEY, runRound } from '../remoteAccountsRound';
import { readsKey } from '../remoteRead';
import type { DoorAnswer } from '../remotePermission';
import { STAMP_MAX, withdrawalName, withdrawalText } from '../remotePermission';
import { textOf } from '../remoteRefusals';
import type { RefusalsFile } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, Window, instant, row, windowOver } from './hostsFakes';
import { callsOf, world } from './roundFakes';

/**
 * The one door and the permission (E2.S1): the stamp rule through the door, the stale lock holder, the stale window, a withdrawal the disk did not take,
 * the copy, the door's table in its order, the upgrade, the sides, a withdrawal that wins over generated histories, the copy that is not a stamp and
 * *Save now*. Pure: a fake `globalState` that several windows share, and a fake folder for each side.
 */

const U0 = '2026-11-20T09:00:00.000Z';
const H = 'h';
const wname = (stamp: number): string => withdrawalName('withdrawn-', stamp);
const fileText = (stamp: number | undefined, extra: Partial<RefusalsFile> = {}): string =>
  textOf({ v: 1, gen: 4, by: 'seed', refused: [], ...(stamp === undefined ? {} : { grant: { utc: U0, stamp } }), ...extra } as RefusalsFile);
const doors = async (w: Window, host = H): Promise<readonly [DoorAnswer, DoorAnswer]> => [await w.keeper.may('round', host), await w.keeper.may('session', host)];

/** A side whose file and withdrawal files are as the table of § P5 says. */
function seeded(grantStamp: number | undefined, files: readonly number[] = []): FakeDisk {
  const disk = new FakeDisk();
  if (grantStamp !== undefined) {
    disk.files.set('refused.json', fileText(grantStamp));
  }
  files.forEach((stamp) => disk.files.set(wname(stamp), withdrawalText(stamp, U0)));

  return disk;
}

test('E2S1-T2 (the door half): the first six rows of the stamp rule, through both doors', async () => {
  const rows: readonly (readonly [string, number | undefined, readonly number[], number | undefined, DoorAnswer])[] = [
    ['no grant', undefined, [], undefined, 'no-permission'],
    ['grant 1, nothing', 1, [], undefined, 'yes'],
    ['grant 1, a file 1: equal is not above', 1, [1], undefined, 'no-permission'],
    ['grant 2, a file 1', 2, [1], undefined, 'yes'],
    ['grant 3, files 1 and 2, copy 2', 3, [1, 2], 2, 'yes'],
    ['grant 3, no file, copy 3: only in the copy', 3, [], 3, 'no-permission'],
  ];
  for (const [name, grantStamp, files, copy, want] of rows) {
    const backing = new Backing();
    if (copy !== undefined) {
      backing.values.set(STATE_KEY, { withdrawnStamp: copy });
    }
    const w = windowOver(backing, seeded(grantStamp, files));
    assert.deepEqual(await doors(w), [want === 'yes' ? 'yes' : want, want], name);
  }
});

test('E2S1-T5: the stale lock holder — a former holder renames its stale refused.json over the new one, and the withdrawal still stands', async () => {
  for (const points of [['allow', 'withdraw'], ['allow', 'withdraw', 'allow', 'withdraw'], ['allow', 'allow', 'withdraw']] as const) {
    const backing = new Backing();
    const disk = new FakeDisk();
    const a = windowOver(backing, disk);
    const snapshots: (string | undefined)[] = [disk.files.get('refused.json')];
    for (const press of points) {
      if (press === 'allow') {
        await a.keeper.allow();
      } else {
        await a.keeper.withdraw();
      }
      snapshots.push(disk.files.get('refused.json'));
    }
    for (const text of snapshots) {
      // the window that saw the withdrawal is closed; a former holder renames an earlier text over the file
      if (text === undefined) {
        disk.files.delete('refused.json');
      } else {
        disk.files.set('refused.json', text);
      }
      const reloaded = windowOver(backing, disk);
      assert.deepEqual(await doors(reloaded), ['no-permission', 'no-permission'], `a stale rename of ${String(text).slice(0, 60)} put a permission back after a withdrawal (${points.join(', ')})`);
    }
  }
  // the mirror: the holder's file has NO grant and the last word is an Allow: never `yes` by a regain, and the next Allow is given
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  await a.keeper.commit({ kind: 'person', host: 'g', answer: 'no' });
  const before = disk.files.get('refused.json') as string;
  await a.keeper.allow();
  disk.files.set('refused.json', before);
  // the window that remembers the newer file, with its grant, is written forward by a regain — which gives no grant
  assert.deepEqual(await doors(a, 'x'), ['no-permission', 'no-permission'], 'a regain wrote the remembered grant back');
  const reloaded = windowOver(backing, disk);
  assert.deepEqual(await doors(reloaded, 'x'), ['no-permission', 'no-permission'], 'a file that went back gave a permission no press gave');
  assert.equal((await reloaded.keeper.allow()).kind, 'given');
  assert.deepEqual(await doors(reloaded, 'x'), ['yes', 'yes']);
});

test('E2S1-T6: the stale window — a whole-state write of the old object does not take a withdrawal back, and does not take an Allow back', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  await a.keeper.allow();
  await a.keeper.withdraw();
  assert.equal((backing.values.get(STATE_KEY) as { withdrawnStamp?: number }).withdrawnStamp, 2, 'the fixture did not write the copy');
  // A is disposed. B is stale: it writes the WHOLE state (the copy gone)
  await b.keeper.commitKey(KEY, {}, () => ({ next: { value: [row('z', 'no')] }, result: 1 }));
  assert.equal(backing.values.get(STATE_KEY), undefined, 'the fixture did not lose the copy, so this test proves nothing');
  for (const reloaded of [windowOver(backing, disk, { mode: 'whole-state' }), b]) {
    assert.deepEqual(await doors(reloaded), ['no-permission', 'no-permission'], 'a stale window took a withdrawal back');
  }
  // an Allow given after the withdrawal: B's stale write does not take it back
  const c = windowOver(backing, disk, { mode: 'whole-state' });
  await c.keeper.allow();
  assert.deepEqual(await doors(c), ['yes', 'yes']);
  await b.keeper.commitKey(KEY, {}, () => ({ next: { value: [row('z', 'no')] }, result: 1 }));
  assert.deepEqual(await doors(windowOver(backing, disk, { mode: 'whole-state' })), ['yes', 'yes'], 'a stale write took an Allow back');
  // a stale write that brings back a "yes" row allows nothing without the permission
  const bare = new Backing();
  bare.values.set(KEY, [row(H, 'yes'), row('g', 'yes', U0, 'auto')]);
  const fresh = windowOver(bare, new FakeDisk());
  assert.deepEqual(await doors(fresh), ['no-permission', 'no-permission']);
  assert.deepEqual(await doors(fresh, 'g'), ['no-permission', 'no-permission']);
});

test('E2S1-T7: a withdrawal the disk did not take — the copy is written first, the door says waiting, Save now lands it; an Allow the file did not take changes nothing', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.allow();
  await a.keeper.commit({ kind: 'person', host: 'r', answer: 'no' });
  disk.failWithdrawn = true;
  const calls = disk.calls.length;
  const result = await a.keeper.withdraw();
  assert.deepEqual(result, { kind: 'unwritten', why: 'disk' });
  const rowAt = disk.calls.indexOf('row', calls);
  const fileAt = disk.calls.findIndex((one, n) => n >= calls && one.startsWith('withdrawn:'));
  assert.ok(rowAt >= 0 && fileAt >= 0 && rowAt < fileAt, 'the copy was written after the file was asked');
  assert.deepEqual(a.unkeptWithdrawal, ['disk'], 'the person was not told');
  assert.deepEqual([await a.keeper.may('session', H), await a.keeper.may('round', H)], ['waiting', 'waiting']);
  assert.deepEqual([await a.keeper.may('session', 'r'), await a.keeper.may('round', 'r')], ['no', 'no'], 'a refused host answers no before it answers waiting (X10)');
  assert.equal(await a.keeper.held(), 1, 'a withdrawal that waits is not held');
  // a second window of the side, after it has seen the copy: waiting on both doors
  b.view.sync();
  assert.deepEqual(await doors(b), ['waiting', 'waiting']);
  // a second window that has not seen the copy reads on (residual 15)
  const c = windowOver(new Backing(), disk);
  assert.deepEqual(await doors(c), ['yes', 'yes']);
  // Save now with the disk healed lands it: the file is there, both windows say no-permission
  disk.failWithdrawn = false;
  await a.keeper.saveWaiting();
  assert.equal(disk.withdrawnNames().length >= 1, true, 'Save now did not land the withdrawal');
  assert.deepEqual(await doors(a), ['no-permission', 'no-permission']);
  assert.deepEqual(await doors(b), ['no-permission', 'no-permission']);
  assert.equal(await a.keeper.held(), 0);

  // an Allow the file did not take: the bytes are the same, nothing is held, held() is unchanged
  for (const [why, set] of [['busy', (d: FakeDisk) => { d.lock = { token: 'hung' }; }], ['disk', (d: FakeDisk) => { d.failReplace = true; }]] as const) {
    const bare = new FakeDisk();
    const w = windowOver(new Backing(), bare);
    set(bare);
    const before = bare.files.get('refused.json');
    const held = await w.keeper.held();
    const answer = await w.keeper.allow();
    assert.deepEqual(answer, { kind: 'unwritten', why }, `the Allow was ${JSON.stringify(answer)}`);
    assert.equal(bare.files.get('refused.json'), before, 'an Allow the file did not take changed the file');
    assert.equal(await w.keeper.held(), held, 'an Allow the file did not take is held');
    assert.deepEqual(await doors(w), ['no-permission', 'no-permission']);
  }
  // an Allow over a held withdrawal (F4): the copy erased by a stale write, the store says kept, the held withdrawal is dropped and the door says yes
  const back = new Backing();
  const disk2 = new FakeDisk();
  const d = windowOver(back, disk2);
  await d.keeper.allow();
  disk2.failWithdrawn = true;
  await d.keeper.withdraw();
  back.values.delete(STATE_KEY);
  d.view.sync();
  assert.equal((await d.keeper.allow()).kind, 'kept');
  assert.deepEqual(await doors(d), ['yes', 'yes'], 'a held withdrawal stood after an Allow that was kept');
  assert.deepEqual(await doors(d), ['yes', 'yes'], 'the held withdrawal came back at the next read');
  // a withdrawal that is busy: the pressing window says waiting, a second window of the side says no-permission
  const back3 = new Backing();
  const disk3 = new FakeDisk();
  const e = windowOver(back3, disk3);
  const f = windowOver(back3, disk3);
  await e.keeper.allow();
  disk3.lockSays = 'held';
  assert.deepEqual(await e.keeper.withdraw(), { kind: 'unwritten', why: 'busy' });
  assert.equal(await e.keeper.may('session', H), 'waiting');
  f.view.sync();
  disk3.lockSays = undefined;
  assert.equal(await f.keeper.may('session', H), 'no-permission');
});

test('E2S1-T8: the copy — a copy above the folder is written into it at the first read, the folder above the copy raises the copy, the copy only grows', async () => {
  const backing = new Backing();
  backing.values.set(STATE_KEY, { withdrawnStamp: 5 });
  const disk = seeded(5, []);
  const a = windowOver(backing, disk);
  assert.equal(await a.keeper.may('session', H), 'no-permission', 'a copy above the folder did not withdraw at once');
  assert.deepEqual(disk.withdrawnNames(), [wname(5)], 'the copy was not written into the folder');
  // the folder's highest is 7 and the copy 5: the copy is 7 after one read
  const back2 = new Backing();
  back2.values.set(STATE_KEY, { withdrawnStamp: 5 });
  const disk2 = seeded(8, [7]);
  const b = windowOver(back2, disk2);
  assert.equal(await b.keeper.may('session', H), 'yes');
  assert.equal((back2.values.get(STATE_KEY) as { withdrawnStamp?: number }).withdrawnStamp, 7, 'the folder was not copied out');
  // a copy that is absent is raised to the folder's highest
  const back3 = new Backing();
  const c = windowOver(back3, seeded(9, [4]));
  await c.keeper.may('session', H);
  assert.equal((back3.values.get(STATE_KEY) as { withdrawnStamp?: number }).withdrawnStamp, 4);
});

test('E2S1-T9: the one door — every row of the table for both callers, each built so ONLY that row holds, then the order', async () => {
  const allowed = async (setup: (w: Window, disk: FakeDisk, backing: Backing) => Promise<void> | void): Promise<readonly [DoorAnswer, DoorAnswer]> => {
    const backing = new Backing();
    const disk = new FakeDisk();
    const w = windowOver(backing, disk);
    await w.keeper.allow();
    await setup(w, disk, backing);

    return doors(w);
  };
  assert.deepEqual(await allowed(() => undefined), ['yes', 'yes']);
  assert.deepEqual(await allowed(async (w) => { await w.keeper.commit({ kind: 'person', host: H, answer: 'no' }); }), ['no', 'no']);
  assert.deepEqual(await allowed((w, _d, backing) => { backing.values.set(KEY, 'junk'); w.view.sync(); }), ['damaged', 'damaged']);
  assert.deepEqual(await allowed(async (w, disk) => { disk.lock = { token: 'hung' }; await w.keeper.commit({ kind: 'person', host: 'g', answer: 'no' }); }), ['waiting', 'waiting']);
  assert.deepEqual(await allowed(async (w) => { await w.keeper.withdraw(); }), ['no-permission', 'no-permission']);
  assert.deepEqual(await allowed(async (w) => { await w.keeper.stopRound(); }), ['stopped', 'yes'], 'the Stop stops the round and not the session read');
  assert.deepEqual(await doors(windowOver(new Backing(), new FakeDisk())), ['no-permission', 'no-permission']);
  // the order: two rows hold, the earlier one is the answer
  assert.deepEqual(await allowed((w, _d, backing) => { backing.values.set(KEY, [row(H, 'no'), 'junk']); w.view.sync(); }), ['no', 'no'], 'no over damaged');
  assert.deepEqual(await allowed(async (w, disk) => { disk.files.set('refused.json', 'junk'); disk.failWithdrawn = true; await w.keeper.withdraw(); }), ['damaged', 'damaged'], 'damaged over waiting');
  const waitingOver = windowOver(new Backing(), new FakeDisk());
  waitingOver.keeper.forgetHeld();
  const failing = new FakeDisk();
  failing.failWithdrawn = true;
  const unsaved = windowOver(new Backing(), failing);
  await unsaved.keeper.withdraw();
  assert.deepEqual(await doors(unsaved), ['waiting', 'waiting'], 'waiting over no-permission');
  const stoppedNoPermission = windowOver(new Backing(), new FakeDisk());
  await stoppedNoPermission.keeper.stopRound();
  assert.deepEqual(await doors(stoppedNoPermission), ['no-permission', 'no-permission'], 'no-permission over stopped');
  // a "yes" row or an automatic row is never an answer
  const back = new Backing();
  back.values.set(KEY, [row(H, 'yes'), row('g', 'yes', U0, 'auto')]);
  const rows = windowOver(back, new FakeDisk());
  assert.deepEqual(await doors(rows), ['no-permission', 'no-permission']);
});

test('E2S1-T11: the upgrade — the Stop stays and an Allow does not start a stopped round; markers of the pre-release wait; the pre-release file has no permission', async () => {
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  await w.keeper.stopRound();
  assert.equal((await w.keeper.allow()).kind, 'given');
  assert.equal(await w.keeper.may('round', H), 'stopped');
  assert.equal(await w.keeper.may('session', H), 'yes');
  await w.keeper.startRound();
  assert.equal(await w.keeper.may('round', H), 'yes');
  // a pre-release file: v, gen, by, refused, stop and no grant
  const old = new FakeDisk();
  old.files.set('refused.json', JSON.stringify({ v: 1, gen: 3, by: 'x', refused: [{ host: 'n', utc: U0 }], stop: { utc: U0 } }));
  const pre = windowOver(new Backing(), old);
  assert.deepEqual(await doors(pre, 'z'), ['no-permission', 'no-permission']);
  assert.equal(await pre.keeper.may('session', 'n'), 'no');
  // markers of the pre-release wait
  const marked = new FakeDisk();
  marked.lock = { token: 'hung' };
  marked.files.set('unsure-a.json', JSON.stringify({ v: 1, kind: 'refuse', host: 'm', utc: U0 }));
  assert.equal(await windowOver(new Backing(), marked).keeper.may('session', H), 'waiting');
  // the state row of the pre-release is not damaged
  const back = new Backing();
  back.values.set(STATE_KEY, { toldUtc: U0, lastAutoNoticeUtc: U0 });
  assert.equal(await windowOver(back, new FakeDisk()).keeper.may('session', H), 'no-permission');
});

test('E2S1-T16: the sides — a grant is of one side and is never copied; a withdrawal reaches every side that sees its copy', async () => {
  const shared = new Backing();
  const sideA = new FakeDisk();
  const sideB = new FakeDisk();
  const a = windowOver(shared, sideA);
  const b = windowOver(shared, sideB);
  await a.keeper.allow();
  b.view.sync();
  assert.equal(await b.keeper.may('session', H), 'no-permission', 'an Allow on side A allowed side B');
  assert.equal(stampsOnDisk(sideB), undefined, 'a grant was copied into side B\'s folder');
  await b.keeper.allow();
  const replacesOnB = sideB.called('replace').length;
  await a.keeper.withdraw();
  b.view.sync();
  assert.equal(await b.keeper.may('session', H), 'no-permission', 'a withdrawal on A did not stop B at its next read');
  assert.ok(sideB.withdrawnNames().length >= 1, 'the withdrawal was not written into side B\'s own folder');
  await a.keeper.allow();
  b.view.sync();
  assert.equal(await a.keeper.may('session', H), 'yes');
  assert.equal(await b.keeper.may('session', H), 'no-permission', 'an Allow on A gave side B back');
  assert.equal(sideB.called('replace').length, replacesOnB, 'a grant was written into B\'s file by something that is not B\'s Allow');
  await b.keeper.allow();
  assert.equal(await b.keeper.may('session', H), 'yes');
});

const stampsOnDisk = (disk: FakeDisk): number | undefined => (JSON.parse(disk.files.get('refused.json') ?? '{}') as { grant?: { stamp?: number } }).grant?.stamp;

test('E2S1-T20: a withdrawal wins — generated histories: after a last Withdraw no door says yes, and whenever one says yes an Allow came after the last Withdraw', async () => {
  let seed = 20260126;
  const pick = (n: number): number => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
  type Op = 'allow' | 'withdraw' | 'stale-rename' | 'stale-write' | 'reload' | 'withdraw-while-unreadable' | 'allow-b' | 'withdraw-b' | 'interleave' | 'mend';
  const ops: readonly Op[] = ['allow', 'withdraw', 'stale-rename', 'stale-write', 'reload', 'withdraw-while-unreadable', 'allow-b', 'withdraw-b', 'interleave', 'mend'];
  /** I1: after every Allow that was given, the folder holds a readable withdrawal one below the grant, or the grant is 1. */
  let givens = 0;
  const holdsI1 = (disk: FakeDisk, answer: Awaited<ReturnType<Window['keeper']['allow']>>, where: string): void => {
    if (answer.kind === 'given') {
      givens += 1;
      assert.ok(answer.grant.stamp === 1 || disk.files.has(wname(answer.grant.stamp - 1)), `I1: no floor file below grant ${answer.grant.stamp} (${where})`);
    }
  };
  for (let history = 0; history < 120; history += 1) {
    const backing = new Backing();
    const disk = new FakeDisk();
    let a = windowOver(backing, disk, { mode: 'whole-state' });
    let b = windowOver(backing, disk, { mode: 'whole-state' });
    const snapshots: string[] = [];
    let lastAllow = -1;
    let lastWithdraw = -1;
    const log: Op[] = [];
    for (let step = 0; step < 4 + pick(5); step += 1) {
      const op = ops[pick(ops.length)] as Op;
      log.push(op);
      if (op === 'allow' || op === 'allow-b') {
        const mine = op === 'allow' ? a : b;
        mine.view.sync();
        const answer = await mine.keeper.allow();
        holdsI1(disk, answer, log.join(' '));
        if (answer.kind === 'given') {
          lastAllow = step;
        }
      } else if (op === 'withdraw' || op === 'withdraw-b') {
        const mine = op === 'withdraw' ? a : b;
        mine.view.sync();
        if ((await mine.keeper.withdraw()) === 'withdrawn') {
          lastWithdraw = step;
        }
      } else if (op === 'interleave') {
        // an Allow in window A and a Withdraw in window B at once: the Withdraw is the last word, whichever lands first
        a.view.sync();
        b.view.sync();
        const [answer] = await Promise.all([a.keeper.allow(), b.keeper.withdraw()]);
        holdsI1(disk, answer, log.join(' '));
        lastWithdraw = step;
      } else if (op === 'mend') {
        // the file is damaged and a person's answer writes it again: no grant, the round stopped
        disk.files.set('refused.json', 'junk');
        a.view.sync();
        await a.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
        lastWithdraw = step;
      } else if (op === 'stale-rename') {
        const old = snapshots[pick(Math.max(1, snapshots.length))];
        if (old !== undefined) {
          disk.files.set('refused.json', old);
        }
      } else if (op === 'stale-write') {
        await b.keeper.commitKey(KEY, {}, () => ({ next: { value: [] }, result: 1 }));
      } else if (op === 'reload') {
        a = windowOver(backing, disk, { mode: 'whole-state' });
        b = windowOver(backing, disk, { mode: 'whole-state' });
      } else {
        disk.files.set(wname(900 + step), '{');
        lastWithdraw = step;
      }
      const text = disk.files.get('refused.json');
      if (text !== undefined) {
        snapshots.push(text);
      }
    }
    const fresh = windowOver(backing, disk, { mode: 'whole-state' });
    for (const answer of await doors(fresh)) {
      if (answer === 'yes') {
        assert.ok(lastAllow > lastWithdraw, `a door said yes with no Allow after the last Withdraw: ${log.join(' ')} (history ${history})`);
      }
    }
  }
  assert.ok(givens > 50, `companion: the histories gave the permission only ${givens} times, so I1 was held over almost nothing`);
  // I1 after every given: the folder holds withdrawn-<g-1>, or g is 1
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  for (let n = 0; n < 6; n += 1) {
    await w.keeper.allow();
    const stamp = stampsOnDisk(disk) as number;
    assert.ok(stamp === 1 || disk.files.has(wname(stamp - 1)), `no floor file below grant ${stamp}`);
    await w.keeper.withdraw();
  }
});

test('E2S1-T20 (B1): an Allow that died after its write does not outlive a later withdrawal of a window that had not seen the copy', async () => {
  // V has not seen the copy: its stamp (4) is below the grant (6) that the dead Allow writes while V waits for the claim
  const disk = new FakeDisk();
  const v = windowOver(new Backing(), disk);
  disk.lockSays = 'held';
  disk.afterWithdrawn = () => undefined;
  disk.beforeLockNow = (n) => {
    if (n === 2) {
      // the Allow's window is gone: its floor file and its grant landed, and its claim's owner is shown gone
      disk.files.set(wname(5), withdrawalText(5, U0));
      disk.files.set('refused.json', fileText(6));
      disk.lockSays = 'gone';
    }
  };
  const done = await v.keeper.withdraw();
  assert.equal(done, 'withdrawn');
  const fresh = windowOver(new Backing(), disk);
  assert.deepEqual(await doors(fresh), ['no-permission', 'no-permission'], 'an Allow that died after its write outlived a later withdrawal');
  // the same with a step-10 listing of an Allow that fails: the Allow makes the withdrawal at its own stamp
  const failing = new FakeDisk();
  const w = windowOver(new Backing(), failing);
  failing.afterListing = (n) => { if (n === 2) { failing.failWithdrawals = true; } };
  const answer = await w.keeper.allow();
  assert.notEqual(answer.kind, 'given', 'an Allow whose read-back could not be made ended as given');
});

test('E2S1-T20 (R33): an Allow whose claim was taken by a person\'s press after its write starts again and does not end as kept over a later withdrawal', async () => {
  const disk = new FakeDisk();
  disk.files.set(wname(2), withdrawalText(2, U0));
  const w = windowOver(new Backing(), disk);
  let taken = false;
  const replacing = disk.replace;
  disk.replace = async (text) => {
    await replacing(text);
    if (!taken) {
      taken = true;
      // a person's press takes the claim after the write; a withdrawal of a window that had not seen the copy lands BELOW the grant (stamp 1 < 3) while the Allow starts again
      disk.lock = { token: 'a person took it' };
      disk.ownerAlive = false;
      disk.files.set(wname(1), withdrawalText(1, U0));
    }
  };
  const result = await w.keeper.allow();
  assert.notEqual(result.kind, 'kept', 'an Allow that had written answered kept when it started again');
  assert.deepEqual(await doors(windowOver(new Backing(), disk)), ['no-permission', 'no-permission'], 'a withdrawal that landed below the grant did not stand');
});

test('E2S1-T21: a copy that is not a stamp withdraws on every side, no read replaces it, an Allow does; the top starts again', async () => {
  for (const bad of ['x', 1.5, 0, -3, {}, STAMP_MAX + 1]) {
    const backing = new Backing();
    backing.values.set(STATE_KEY, { withdrawnStamp: bad });
    const disk = seeded(1, []);
    const w = windowOver(backing, disk);
    assert.deepEqual(await doors(w), ['no-permission', 'no-permission'], `a copy that is ${JSON.stringify(bad)} did not withdraw`);
    assert.deepEqual((backing.values.get(STATE_KEY) as { withdrawnStamp?: unknown }).withdrawnStamp, bad, 'a read replaced a copy that is not a stamp');
    const given = await w.keeper.allow();
    assert.equal(given.kind, 'given', `a copy that is ${JSON.stringify(bad)}: the Allow came to ${JSON.stringify(given)}`);
    assert.equal(await w.keeper.may('session', H), 'yes');
  }
  // a row that cannot be read at all: stopped and no permission; after an Allow the row is readable and STILL stopped
  const back = new Backing();
  back.values.set(STATE_KEY, 'junk');
  const w = windowOver(back, new FakeDisk());
  assert.deepEqual(await doors(w), ['no-permission', 'no-permission']);
  assert.equal((await w.keeper.allow()).kind, 'given');
  assert.equal(await w.keeper.may('session', H), 'yes');
  assert.equal(await w.keeper.may('round', H), 'stopped', 'an Allow started a round that was stopped');
  // the floor at STAMP_MAX: the Allow restarts at stamp 1 and is given; a withdrawal at the top takes STAMP_MAX and wins
  const top = new FakeDisk();
  top.files.set(wname(STAMP_MAX), withdrawalText(STAMP_MAX, U0));
  const t = windowOver(new Backing(), top);
  const given = await t.keeper.allow();
  assert.ok(given.kind === 'given' && given.restarted && given.grant.stamp === 1, JSON.stringify(given));
  assert.deepEqual(top.withdrawnNames(), [], 'the restart left a withdrawal file');
  assert.equal(await t.keeper.may('session', H), 'yes');
  const high = new FakeDisk();
  high.files.set('refused.json', fileText(STAMP_MAX - 1));
  high.files.set(wname(STAMP_MAX), withdrawalText(STAMP_MAX, U0));
  const h = windowOver(new Backing(), high);
  assert.equal(await h.keeper.withdraw(), 'withdrawn');
  assert.ok(high.files.has(wname(STAMP_MAX)));
  assert.equal(await h.keeper.may('session', H), 'no-permission');
});

test('E2S1-T22: Save now frees a lock that hangs when nothing waits, and lands a withdrawal that waits after the press took the lock', async () => {
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  disk.lock = { token: 'hung' };
  assert.deepEqual(await w.keeper.allow(), { kind: 'unwritten', why: 'busy' });
  await w.keeper.saveWaiting();
  assert.deepEqual(disk.claims.filter((one) => one === 'person'), ['person'], 'Save now with nothing waiting did not claim once with take: person');
  assert.equal(disk.called('replace').length, 0, 'Save now wrote something');
  assert.equal((await w.keeper.allow()).kind, 'given');
  // a withdrawal that is busy behind a lock whose note cannot be read: the claim is recorded BEFORE the withdrawal's lockNow
  const busy = new FakeDisk();
  const x = windowOver(new Backing(), busy);
  await x.keeper.allow();
  busy.lock = { token: 'hung' };
  busy.lockSays = undefined;
  assert.deepEqual(await x.keeper.withdraw(), { kind: 'unwritten', why: 'busy' });
  const mark = busy.calls.length;
  await x.keeper.saveWaiting();
  const claimAt = busy.calls.findIndex((one, n) => n >= mark && one === 'claim:person');
  const lookAt = busy.calls.findIndex((one, n) => n >= mark && one === 'lockNow');
  assert.ok(claimAt >= 0 && lookAt > claimAt, 'the press did not take the lock before the withdrawal looked at it');
  assert.deepEqual(await doors(x), ['no-permission', 'no-permission']);
});

test('E2S1-T9 (carried CUT-D2, CUT-D3, CUT-D4): the round over the door — an automatic or a person\'s "yes" row reads nothing without the permission and is no answer with it', async () => {
  const rows = { auto: [row(H, 'yes', U0, 'auto')], yes: [row(H, 'yes')], no: [row(H, 'no')] } as const;
  for (const kind of ['auto', 'yes', 'no'] as const) {
    const backing = new Backing();
    backing.values.set(KEY, rows[kind]);
    const w = windowOver(backing, new FakeDisk());
    assert.equal(await w.keeper.may('round', H), kind === 'no' ? 'no' : 'no-permission', `${kind}: the door admitted a row with no permission`);
    await w.keeper.allow();
    const round = world({ hosts: [H] });
    await runRound({ ...round.seams, may: (host) => w.keeper.may('round', host) }, new Map(), 0, 'timer', undefined);
    assert.deepEqual(callsOf(round), kind === 'no' ? [] : [`${H} N`], `${kind}: the round over the door called the wrong hosts`);
  }
  // CUT-D4: the terminal items' key follows "answered no" only
  for (const kind of ['auto', 'yes', 'no'] as const) {
    const backing = new Backing();
    backing.values.set(KEY, rows[kind]);
    assert.equal(readsKey(`Creds: ${H} / s`, await windowOver(backing, new FakeDisk()).keeper.standings()), kind !== 'no');
  }
  await instant();
});

test('E2S1-F3: an Allow that is kept or given does not say so while a later Withdraw of its window still waits', async () => {
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  await w.keeper.allow();
  // a kept Allow (the grant is in force), then a Withdraw pressed behind it in the same queue, which neither the profile (its copy) nor the disk takes yet
  w.view.update = async () => { throw new Error('the profile is full'); };
  disk.failWithdrawn = true;
  const allowing = w.keeper.allow();
  const withdrawing = w.keeper.withdraw();
  const answer = await allowing;
  assert.equal(answer.kind, 'withdrawn', `an Allow said ${answer.kind} while a later Withdraw waited`);
  assert.deepEqual(await withdrawing, { kind: 'unwritten', why: 'disk' });
  assert.deepEqual(await doors(w), ['waiting', 'waiting']);
  disk.failWithdrawn = false;
  await w.keeper.saveWaiting();
  assert.deepEqual(await doors(w), ['no-permission', 'no-permission']);
});
