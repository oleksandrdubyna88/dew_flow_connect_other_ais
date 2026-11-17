import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CLAIM_STALE_MS,
  CLAIM_TRIES,
  migrateOnExhausted,
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

  // V6: an unregistered key stays in the user model, so its layers answer what the file holds; only its default is undefined.
  settings.set('x', 'global', 'kept');
  settings.set('x', 'workspace', 'near');
  assert.equal(settings.inspect('x').globalValue, 'kept');
  assert.equal(settings.inspect('x').workspaceValue, 'near');
  assert.equal(settings.inspect('x').defaultValue, undefined);
  settings.set('x', 'workspace', undefined);

  // V5: a VALUE written to an undeclared key is refused in VS Code's words; its removal is accepted.
  await assert.rejects(Promise.resolve(settings.update('x', 'v', GLOBAL)), new Error(unregisteredWords('x')));
  await settings.update('x', undefined, GLOBAL);
  assert.equal(settings.inspect('x').globalValue, undefined, 'the removal of an undeclared key is accepted and lands');

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

/** A window of the client taken while A waits in its claim pause: its copy holds A's claim, and no `held` yet. */
async function staleWindowAt(of: World, a: Rig): Promise<FakeWindow> {
  await until(() => a.pauses.count() === 1, 'A in its claim pause');

  return of.client.window();
}

/** The stale window writes any key — its whole copy replaces the stored value — and A's own copy is replaced by it. */
function staleWrite(of: World, stale: FakeWindow, a: Rig): void {
  void stale.update('coai.unrelated', 1);
  of.client.deliver(a.window);
}

const witnessAsked = (of: World): number => of.settings.updates.filter((one) => one.key === USER_WITNESS).length;

/** A run held at its witness write — the file has not answered — with a stale window taken in the claim pause. */
async function heldAtWitness(): Promise<{ of: World; a: Rig; run: Promise<unknown>; stale: FakeWindow }> {
  const of = world(false);
  of.settings.hold(USER_WITNESS, 'unlanded');
  const a = activation(of, 'A', { pauses: new Pauses(of.clock) });
  const run = a.run();
  const stale = await staleWindowAt(of, a);
  a.pauses.next();
  await until(() => witnessAsked(of) === 1, 'A asked for its witness write');

  return { of, a, run, stale };
}

test('T36b NoMarkerFromACopyThatLostItsRecord', async () => {
  // A stale window's write can carry A's claim with no `held` and the overlays as they were: the claim and `held` are two
  // writes. A copy like that must never read as "nothing is due".

  // (i) the copy arrives from A's `after`: no marker, no tell, the claim released; the next activation finishes.
  const one = world(false);
  const a = activation(one, 'A', { pauses: new Pauses(one.clock) });
  const runA = a.run();
  const staleA = await staleWindowAt(one, a);
  a.onAfter = () => {
    staleWrite(one, staleA, a);
  };
  await drive(a, runA);
  assert.equal(await runA, 'incomplete');
  assert.equal(one.client.record().completed, undefined, 'no completed in the stored value');
  assert.equal(recordOf(a.window.get(KEY)).completed, undefined, 'nor in A\'s copy');
  assert.deepEqual(a.tells(), [], 'no tell');
  assert.equal(one.client.record().claim, undefined, 'the claim is released');
  assert.ok(a.logs.some((line) => line.includes('(marker)')), 'and the log says why, at the marker');
  one.client.deliver();
  const next = activation(one, 'B');
  assert.equal(await next.run(), 'migrated', 'the next activation completes');
  assert.equal(overlayOf(one.client, KEY_X)['onExhausted'], undefined, 'X is cleared');
  assert.equal(overlayOf(one.client, KEY_X)[OVERLAY_WITNESS], next.ports.version, 'and witnessed');
  assert.equal(a.tells().length + next.tells().length, 1, 'exactly one tell');

  // (ii) the copy arrives before the first overlay turn: the user layer is not touched.
  const two = world(false);
  const staleB: { window?: FakeWindow } = {};
  const b: Rig = activation(two, 'A', {
    pauses: new Pauses(two.clock),
    beforeTurn: (n) => {
      if (n === 1 && staleB.window !== undefined) {
        staleWrite(two, staleB.window, b);
      }
    },
  });
  const runB = b.run();
  staleB.window = await staleWindowAt(two, b);
  await drive(b, runB);
  assert.equal(await runB, 'incomplete');
  assert.equal(witnessAsked(two), 0, 'no witness write in the settings log');
  assert.equal(two.settings.layer('onExhausted', 'global'), 'human', 'the user layer is still human');
  assert.ok(b.logs.some((line) => line.includes('(overlay turn)')), 'the first overlay turn says why');
  two.client.deliver();
  const again = activation(two, 'C');
  assert.equal(await again.run(), 'migrated');
  assert.equal(two.settings.layer('onExhausted', 'global'), undefined, 'the user value is removed');
  assert.equal(two.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'and witnessed');

  // (ii-b) the copy arrives after the last overlay turn, so only the user layer's own check can see it.
  const three = world(false);
  const staleC: { window?: FakeWindow } = {};
  const c: Rig = activation(three, 'A', { pauses: new Pauses(three.clock) });
  let turns = 0;
  const wrapped = {
    ...c.ports,
    turn: async <T>(work: () => Promise<T>): Promise<T> => {
      const result = await c.ports.turn(work);
      turns += 1;
      if (turns === 3 && staleC.window !== undefined) {
        staleWrite(three, staleC.window, c);
      }

      return result;
    },
  };
  const runC = migrateOnExhausted(wrapped);
  staleC.window = await staleWindowAt(three, c);
  await drive(c, runC);
  assert.equal(await runC, 'incomplete');
  assert.equal(witnessAsked(three), 0, 'no witness write');
  assert.equal(three.settings.layer('onExhausted', 'global'), 'human', 'the user layer is still human');

  // (iii) the record is read again after the witness write.
  // (iii-a) the record is lost during the await: the witness is withdrawn, the value kept, the next start clears it.
  const lost = await heldAtWitness();
  staleWrite(lost.of, lost.stale, lost.a);
  lost.of.settings.unhold(USER_WITNESS);
  lost.of.settings.release(USER_WITNESS);
  await drive(lost.a, lost.run);
  assert.equal(await lost.run, 'incomplete');
  assert.deepEqual(lost.a.userEvents().map((one) => one.kind), ['witness', 'withdraw'], 'the witness comes out again');
  assert.equal(lost.of.settings.layer(USER_WITNESS, 'global'), undefined, 'and is gone from the file');
  assert.equal(lost.of.settings.layer('onExhausted', 'global'), 'human', 'the value is kept');
  lost.of.client.deliver();
  const last = activation(lost.of, 'B');
  assert.equal(await last.run(), 'migrated');
  assert.equal(lost.of.settings.layer('onExhausted', 'global'), undefined, 'the next start clears it');
  assert.equal(lost.of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'and witnesses it');

  // (iii-b) the claim is taken over during the await and the value is untouched: the witness is A's, so A removes the value
  // itself and keeps the witness; it ends busy, and the window that took over finds the layer settled.
  const taken = await lostClaimAt(() => undefined);
  assert.equal(taken.outcome, 'busy');
  assert.deepEqual(taken.a.userEvents().map((one) => one.kind), ['witness', 'clear user'], 'A asks for its witness and the removal, and never withdraws');
  assert.equal(taken.of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'the witness stays in the file');
  assert.equal(taken.of.settings.layer('onExhausted', 'global'), undefined, 'the value is removed');
  taken.of.client.deliver();
  const took = activation(taken.of, 'other');
  assert.equal(await took.run(), 'migrated', 'the window that took over finishes');
  assert.deepEqual(took.userEvents(), [], 'and finds the user layer settled');
  assert.equal(taken.of.settings.layer('onExhausted', 'global'), undefined, 'the value stays reset');
  await assertChoiceSurvives(taken.of, taken.a, 'busy', ['witness', 'clear user'], 'iii-b (the claim was taken over)');
});

/**
 * (iii-c) A loses the claim during its witness await, and what the other run did meanwhile is `done`: the witness is ONE
 * fixed word with no owner, and one on disk certifies a removal its writer is about to make — so A never takes it out
 * after losing the claim. It removes the value when the value is still the recorded one, and leaves it alone otherwise.
 */
async function lostClaimAt(done: (of: World, took: FakeWindow) => void): Promise<{ of: World; a: Rig; outcome: unknown }> {
  const held = await heldAtWitness();
  const took = held.of.client.window();
  void took.update(KEY, { ...held.of.client.record(), claim: { by: 'other', at: held.of.clock.now() } });
  done(held.of, took);
  held.of.client.deliver(held.a.window);
  held.of.settings.unhold(USER_WITNESS);
  held.of.settings.release(USER_WITNESS);
  await drive(held.a, held.run);

  return { of: held.of, a: held.a, outcome: await held.run };
}

/** Another machine of the same person: its own client store, the same settings file (the witness syncs). */
function secondMachine(of: World): World {
  return { client: world(false).client, settings: of.settings, clock: of.clock };
}

/** What the person chose afterwards must outlive every run, on this machine and on a second one. */
async function assertChoiceSurvives(of: World, a: Rig, outcome: unknown, events: readonly string[], why: string): Promise<void> {
  assert.equal(outcome, 'busy', why);
  assert.deepEqual(a.userEvents().map((one) => one.kind), events, `${why}: A asked for its witness and never withdrew it`);
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, `${why}: the witness is still in the file`);
  of.settings.set('onExhausted', 'global', 'continue');
  const machine = activation(secondMachine(of), 'M2');
  await machine.run();
  assert.equal(of.settings.layer('onExhausted', 'global'), 'continue', `${why}: the second machine keeps the value chosen afterwards`);
}

test('T36b-c ALostClaim_LeavesTheWitnessToTheRunThatOwnsIt', async () => {
  // (iii-c1) the other run COMPLETED, and the value is untouched (another machine's witness was already in the file). Its
  // marker carries no `held`, as `mark` writes it: A removes the value by what it recorded, and the witness stays.
  const completed = await lostClaimAt((of, took) => {
    void took.update(KEY, { completed: '0.88.0', by: 'other' });
    of.settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  });
  assert.equal(completed.of.settings.layer('onExhausted', 'global'), undefined, 'c1: A removed the value it recorded');
  await assertChoiceSurvives(completed.of, completed.a, completed.outcome, ['witness', 'clear user'], 'c1 (another run completed)');

  // (iii-c2) the other run REMOVED the value and relies on the witness; nothing completed yet.
  const removed = await lostClaimAt((of) => {
    of.settings.set('onExhausted', 'global', undefined);
    of.settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  });
  assert.equal(recordOf(removed.of.client.record()).completed, undefined, 'c2 holds: nobody completed');
  await assertChoiceSurvives(removed.of, removed.a, removed.outcome, ['witness'], 'c2 (another run removed the value)');

  // (iii-c3) the person CHOSE another value meanwhile: it is not the recorded one, so nothing of A's may undo the witness.
  const chose = await lostClaimAt((of) => {
    of.settings.set('onExhausted', 'global', 'good_enough');
    of.settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  });
  assert.equal(chose.outcome, 'busy', 'c3');
  assert.deepEqual(chose.a.userEvents().map((one) => one.kind), ['witness'], 'c3: no withdrawal and no removal');
  assert.equal(chose.of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'c3: the witness stays');
  assert.equal(chose.of.settings.layer('onExhausted', 'global'), 'good_enough', 'c3: and so does the choice');
});

test('T36b-d AWitnessStillPending_IsNotACompletedReset_AFinishesItsOwnRemoval', async () => {
  // CodeRabbit's interleaving. A's witness LANDS in the file but its promise is held. B was taken from a stale copy before
  // A's claim reached it, so it never heard A: it reads the witness, treats the user layer as settled, skips it and writes
  // its marker. A's removal is what the witness certified; it must still make it.
  const of = world(false);
  of.settings.hold(USER_WITNESS, 'landed');
  const stale = of.client.window();
  const a = activation(of, 'A', { pauses: new Pauses(of.clock) });
  const runA = a.run();
  await until(() => a.pauses.count() === 1, 'A in its claim pause');
  a.pauses.next();
  await until(() => witnessAsked(of) === 1, 'A asked for its witness write');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'the witness is on disk');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'and the value is still there');

  const b = activation(of, 'B', { window: stale });
  assert.equal(await b.run(), 'migrated', 'B settles from its stale copy');
  assert.deepEqual(b.userEvents(), [], 'B saw the witness and skipped the user layer');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'so the value is still there');
  of.client.deliver(a.window);
  of.settings.release(USER_WITNESS);

  assert.equal(await runA, 'busy', 'A lost the claim');
  assert.deepEqual(a.userEvents().map((one) => one.kind), ['witness', 'clear user'], 'A removes the value and never withdraws');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the user layer ends without human');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'and the witness stays');
  of.client.deliver();
  assert.equal(await activation(of, 'C').run(), 'already', 'a later activation finds the reset complete');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'and the value stays reset');
  await assertChoiceSurvives(of, a, 'busy', ['witness', 'clear user'], 'd (a pending witness)');
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
