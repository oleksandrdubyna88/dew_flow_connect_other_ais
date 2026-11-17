import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CLAIM_STALE_MS,
  CLAIM_TRIES,
  ON_EXHAUSTED_MIGRATION_KEY as KEY,
  OVERLAY_WITNESS,
  recordOf,
  SETTLE_MS,
  USER_WITNESS,
  USER_WITNESS_WORD,
} from '../onExhaustedMigration';
import { writeOverlay } from '../sideSettings';
import {
  activation,
  chooseInOverlay,
  ClientState,
  FakeClock,
  FakeUserSettings,
  FakeWindow,
  flush,
  GLOBAL,
  KEY_X,
  KEY_Y,
  overlayOf,
  Pauses,
  Rig,
  SIDE_X,
  unregisteredWords,
  until,
  usualSettings,
  usualState,
  World,
  world,
} from './onExhaustedMigrationFakes';

/**
 * Two windows of one profile over ONE client store and ONE settings file
 * (todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.8.3).
 */

test('T29 TheFakesBehaveAsTheEditorIsRead', async () => {
  // Every double here is a READING of VS Code's source: extHostMemento.ts (V1–V3) and configurationEditing.ts /
  // extHostConfiguration.ts (V5, V6). It is green from the start because it runs no production line: it pins the
  // doubles every other test in these four files rests on. What it does NOT prove (R10): the lag between two real
  // windows, two real machines, and what a real Settings Sync delivers when.
  const client = new ClientState();
  const one = client.window();
  const other = client.window();

  // V1: a window sees its own update at once; the other sees it only after `deliver()`.
  await one.update('a', { n: 1, gone: undefined });
  assert.deepEqual(one.get('a'), { n: 1 }, 'an object is stored as its JSON clone, an undefined member dropped');
  assert.equal(other.get('a'), undefined);
  client.deliver();
  assert.deepEqual(other.get('a'), { n: 1 });

  // V2/V3: a window's update replaces the client's WHOLE value, so a key the other wrote and did not deliver is gone.
  await other.update('b', 2);
  await one.update('c', 3);
  client.deliver();
  assert.equal(other.get('b'), undefined, 'the stale window\'s write was dropped, as V2 says');
  assert.equal(other.get('c'), 3);

  // `update(key, undefined)` deletes, and a delivery to one window leaves the others stale.
  await one.update('c', undefined);
  client.deliver(other);
  assert.equal(other.get('c'), undefined);
  assert.equal(client.window().get('c'), undefined, 'a window opened later takes the client\'s value');

  // V5/V6: an undeclared key has no default; a declared string key with none has ''.
  const settings = new FakeUserSettings();
  assert.equal(settings.inspect('x').defaultValue, undefined);
  settings.declare('x');
  assert.equal(settings.inspect('x').defaultValue, '');
  settings.undeclare('x');
  assert.equal(settings.inspect('x').defaultValue, undefined);

  // V5: a VALUE written to an undeclared key is refused in VS Code's words; its removal is accepted.
  await assert.rejects(Promise.resolve(settings.update('x', 'v', GLOBAL)), new Error(unregisteredWords('x')));
  await settings.update('x', undefined, GLOBAL);

  // A snapshot does not see a later write; the next snapshot does.
  settings.declare('y');
  const snapshot = settings.config();
  await settings.update('y', 'later', GLOBAL);
  assert.equal(snapshot.inspect('y')?.globalValue, undefined);
  assert.equal(settings.config().inspect('y')?.globalValue, 'later');
});

/** Resolves a window's waiting pauses one by one until its run has ended. */
async function drive(rig: Rig, run: Promise<unknown>): Promise<void> {
  let ended = false;
  void run.then(() => {
    ended = true;
  });
  for (let guard = 0; guard < 50 && !ended; guard += 1) {
    await flush();
    if (rig.pauses.count() > 0) {
      rig.pauses.next();
    }
  }
  await run;
}

test('T23 TwoWindowsStartingTogether_LeaveOneResultAndOneNotification', async () => {
  // This is the DELIVERED case: each window heard the other within the claim's pause. When they do not, T41 and the
  // parent's residual (iii) say what happens instead.
  const of = world();
  const a = activation(of, 'A', { pauses: new Pauses(of.clock) });
  const b = activation(of, 'B', { pauses: new Pauses(of.clock) });
  const runA = a.run();
  const runB = b.run();
  await until(() => a.pauses.count() === 1 && b.pauses.count() === 1, 'both windows in their claim pause');
  of.client.deliver();
  a.pauses.next();
  b.pauses.next();
  await until(() => a.pauses.count() === 1, 'A waiting out B\'s claim');
  await drive(b, runB);
  of.client.deliver();
  a.pauses.next();

  assert.equal(await runB, 'migrated');
  assert.equal(await runA, 'already');
  assert.equal(a.tells().length + b.tells().length, 1, 'one notification');
  assert.equal(of.settings.updates.filter((one) => one.key === 'onExhausted' && one.value === undefined).length, 1, 'one removal');
  assert.equal(of.client.record().by, 'B', 'by is B');
  assert.deepEqual(a.window.writes.map((one) => one.key), [KEY], 'A\'s write log holds its claim and nothing else');
  assert.equal((a.window.writes[0]?.value as { claim?: { by: string } }).claim?.by, 'A');
});

test('T24 WindowBIsHeldAfterItsRead_AFinishes_ThePersonChooses_BChangesNothing', async () => {
  const of = world(false);
  const b = activation(of, 'B', { pauses: new Pauses(of.clock) });
  const runB = b.run();
  await until(() => b.pauses.count() === 1, 'B held in its claim pause');

  const a = activation(of, 'A');
  assert.equal(await a.run(), 'migrated', 'A never saw B\'s claim and runs to the end');

  of.settings.set('onExhausted', 'global', 'human');
  chooseInOverlay(of.client, SIDE_X, 'human');
  of.client.deliver();
  b.pauses.next();

  assert.equal(await runB, 'already');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'the user layer is the person\'s choice');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human', 'and so is X');
  assert.equal(a.tells().length + b.tells().length, 1, 'one tell');
  assert.equal(of.client.record().by, 'A');
  assert.deepEqual(b.window.writes.map((one) => one.key), [KEY], 'B wrote its claim and nothing more');
});

test('T25 AMarkerAloneIsNoLock_AndThatIsThePoint', async () => {
  // T24's interleaving through a NAIVE run — read the marker, clear what it sees, write the marker, tell. It runs no
  // production line: it shows the fixture can see the defect the witnesses exist for. It is green from the start.
  const of = world(false);
  const told: string[] = [];
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  const naive = async (window: FakeWindow, hold?: Promise<void>): Promise<void> => {
    if (window.get(KEY) !== undefined) {
      return;
    }
    const sawX = window.get<Record<string, unknown>>(KEY_X);
    await hold;
    await window.update(KEY_X, { ...sawX, onExhausted: undefined });
    await of.settings.update('onExhausted', undefined, GLOBAL);
    await window.update(KEY, { completed: '0.88.0' });
    told.push('told');
  };

  const b = of.client.window();
  const waiting = naive(b, held);
  const a = of.client.window();
  await naive(a);
  of.settings.set('onExhausted', 'global', 'human');
  await writeOverlay(of.client.window(), SIDE_X, 'onExhausted', 'human');
  release();
  await waiting;

  assert.equal(told.length, 2, 'two notifications');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'and the person\'s human in the user layer is reset');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'and in X');
});

test('T26 AWindowThatLostItsClaimWritesNothingMore', async () => {
  const of = world();
  const a: Rig = activation(of, 'A', {
    beforeTurn: (n) => {
      if (n === 2) {
        const other = of.client.window();
        void other.update(KEY, { ...of.client.record(), claim: { by: 'other', at: of.clock.now() } });
        of.client.deliver(a.window);
      }
    },
  });

  assert.equal(await a.run(), 'busy');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'A cleared X before it lost the claim');
  assert.equal(overlayOf(of.client, KEY_Y)['onExhausted'], 'continue', 'Y is untouched by A');
  assert.equal(of.client.record().completed, undefined, 'A writes no marker');
  assert.deepEqual(a.tells(), [], 'and tells nothing');
});

test('T27 AFreshClaimIsWaitedOut_AStaleOneIsTaken_AClockAheadIsCapped', async () => {
  const claimed = (at: (now: number) => number): World => {
    const clock = new FakeClock();
    const state = { ...usualState(), [KEY]: { claim: { by: 'other', at: at(clock.now()) } } };

    return { client: new ClientState(state), settings: usualSettings(true), clock };
  };

  const young = claimed((now) => now - 10_000);
  const waited = activation(young, 'A');
  assert.equal(await waited.run(), 'migrated', 'a 10 s old claim is waited out, then the run completes');
  assert.equal(waited.pauses.asked[0], CLAIM_STALE_MS - 10_000);

  const stale = claimed((now) => now - CLAIM_STALE_MS);
  const taken = activation(stale, 'A');
  assert.equal(await taken.run(), 'migrated');
  assert.equal(taken.pauses.asked[0], SETTLE_MS, 'a claim CLAIM_STALE_MS old is taken at once');

  const ahead = claimed((now) => now + 300_000);
  const capped = activation(ahead, 'A');
  await capped.run();
  assert.equal(capped.pauses.asked[0], CLAIM_STALE_MS, 'a claim five minutes ahead is waited for no longer than CLAIM_STALE_MS');

  const busy = claimed((now) => now);
  const held = activation(busy, 'A', { pauses: new Pauses(busy.clock) });
  const run = held.run();
  for (let wait = 0; wait < CLAIM_TRIES; wait += 1) {
    await until(() => held.pauses.count() === 1, 'a wait for the fresh claim');
    // The other window rewrites its claim at the moment this wait ends.
    const other = busy.client.window();
    void other.update(KEY, { claim: { by: 'other', at: busy.clock.now() + (held.pauses.asked.at(-1) ?? 0) } });
    busy.client.deliver(held.window);
    held.pauses.next();
  }
  assert.equal(await run, 'busy', 'a fresh foreign claim rewritten during each wait ends busy after CLAIM_TRIES waits');
  assert.deepEqual(held.window.writes, [], 'and this window wrote nothing');
});

test('T28 OnlyTheWindowThatReadsItsOwnNameInTheMarkerTells', async () => {
  const of = world();
  const a = activation(of, 'A', { pauses: new Pauses(of.clock) });
  const run = a.run();
  for (let pause = 0; pause < 2; pause += 1) {
    await until(() => a.pauses.count() === 1, 'a pause of the run');
    a.pauses.next();
  }
  await until(() => a.pauses.count() === 1 && of.client.record().completed !== undefined, 'A in the pause after its marker');
  const other = of.client.window();
  void other.update(KEY, { ...of.client.record(), by: 'other' });
  of.client.deliver(a.window);
  a.pauses.next();

  assert.equal(await run, 'already');
  assert.deepEqual(a.tells(), [], 'no tell');
});

test('T35 AStaleWindowBringsAnOverlayBack_TheNextActivationClearsIt', async () => {
  const of = world(false);
  const stale = of.client.window();
  const a = activation(of, 'A');
  assert.equal(await a.run(), 'migrated');
  of.client.deliver(a.window);
  of.settings.set('onExhausted', 'global', 'human');
  await stale.update('coai.unrelated', 1);

  // The residual, asserted as it is (R3): a stale window that writes ANY key puts its whole copy back.
  assert.equal(of.client.record().completed, undefined, 'the stored value has no marker');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human', 'X holds its old value again');
  assert.equal(overlayOf(of.client, KEY_X)[OVERLAY_WITNESS], undefined, 'with no witness member');

  of.client.deliver();
  const c = activation(of, 'C');
  assert.equal(await c.run(), 'migrated');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'the next activation clears X');
  assert.equal(overlayOf(of.client, KEY_X)[OVERLAY_WITNESS], c.ports.version, 'and witnesses it');
  assert.equal(a.tells().length + c.tells().length, 2, 'the notification was shown a second time');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'the user layer\'s choice is kept');
  assert.deepEqual(c.userEvents(), [], 'C made no user-layer update: the witness in the settings file was out of the stale write\'s reach');
});

test('T36 NoMarkerFromACopyWithAnUnsettledLocation', async () => {
  const of = world(false);
  let stale: FakeWindow | undefined;
  const a: Rig = activation(of, 'A', {
    beforeTurn: (n) => {
      if (n === 1) {
        stale = of.client.window();
      }
    },
  });
  a.onAfter = () => {
    void stale?.update('coai.unrelated', 1);
    of.client.deliver(a.window);
  };

  assert.equal(await a.run(), 'incomplete');
  assert.equal(of.client.record().completed, undefined, 'no completed in the stored value');
  assert.equal(recordOf(a.window.get(KEY)).completed, undefined, 'nor in A\'s copy');
  assert.deepEqual(a.tells(), [], 'no tell');
  assert.equal(of.client.record().claim, undefined, 'the claim is released');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the user layer is cleared');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'and witnessed all the same');

  of.client.deliver();
  assert.equal(await activation(of, 'B').run(), 'migrated', 'the next activation completes');
});

test('T41 TwoWindowsThatNeverHearEachOther_RemoveTheUserValueOnce_AndMayTellTwice', async () => {
  const removals = (of: World): number => of.settings.updates.filter((one) => one.key === 'onExhausted' && one.value === undefined).length;
  const witnessAsks = (of: World): number => of.settings.updates.filter((one) => one.key === USER_WITNESS).length;

  const of = world(false);
  of.settings.hold(USER_WITNESS, 'unlanded');
  const a = activation(of, 'A');
  const b = activation(of, 'B');
  const runs = [a.run(), b.run()];
  await until(() => witnessAsks(of) === 2, 'both windows asked for their witness write');
  of.settings.release(USER_WITNESS);
  assert.deepEqual(await Promise.all(runs), ['migrated', 'migrated']);

  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'the file holds the word, written twice');
  assert.equal(removals(of), 1, 'exactly ONE removal: the second window read the value again and found nothing');
  assert.equal(a.tells().length + b.tells().length, 2, 'two notifications — the parent\'s residual (iii)');
  of.client.deliver();
  assert.ok(of.client.record().completed !== undefined, 'the stored value has the marker');
  for (const key of [KEY_X, KEY_Y]) {
    assert.equal(overlayOf(of.client, key)['onExhausted'], undefined);
    assert.ok(overlayOf(of.client, key)[OVERLAY_WITNESS] !== undefined);
  }
  assert.equal(await activation(of, 'C').run(), 'already');

  // Second half, asserted as the residual it is (R3): B reads before A's removal has landed, so the log holds TWO.
  const late = world(false);
  late.settings.hold(USER_WITNESS, 'unlanded');
  late.settings.hold('onExhausted', 'unlanded');
  const d = activation(late, 'D');
  const e = activation(late, 'E');
  const both = [d.run(), e.run()];
  await until(() => witnessAsks(late) === 2, 'both asked for their witness write');
  late.settings.release(USER_WITNESS);
  await until(() => removals(late) === 2, 'both asked for the removal');
  late.settings.release('onExhausted');
  await Promise.all(both);
  assert.equal(removals(late), 2, 'the removal was asked for twice: a choice made in between would be lost');
});
