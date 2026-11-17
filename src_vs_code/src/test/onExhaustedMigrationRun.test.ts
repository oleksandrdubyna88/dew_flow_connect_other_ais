import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ON_EXHAUSTED_MIGRATION_KEY as KEY,
  OVERLAY_WITNESS,
  SETTLE_MS,
  USER_WITNESS,
  USER_WITNESS_WORD,
} from '../onExhaustedMigration';
import { labelOf } from '../onExhaustedPanel';
import { NOW_AUTO, staysInForce, WHERE } from '../onExhaustedTold';
import {
  activation,
  afterADeath,
  chooseInOverlay,
  flush,
  GLOBAL,
  KEY_STRING,
  KEY_X,
  KEY_Y,
  KEY_Z,
  overlayOf,
  Pauses,
  Rig,
  SIDE_X,
  SIDE_Y,
  unregisteredWords,
  until,
  World,
  world,
  X_BESIDE,
} from './onExhaustedMigrationFakes';

/**
 * One window runs the migration (todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.8.2). The usual fixture, **U**: the
 * user layer `human` with no witness; overlay X (`human`, `vendors` and `rounds` beside it); overlay Y (`continue`), whose
 * side has no registry record; overlay Z (`auto`); a slot that holds a string; `workspaceValue: 'human'` and
 * `workspaceFolderValue: 'continue'`; both keys declared; the per-side switch off. **U0** is U without the two workspace
 * values. No test builds a panel, and no test sleeps: every pause is the `pause` port.
 */

test('T7 EveryLocationIsClearedOnce_AndThePersonIsToldOnce', async () => {
  const of = world();
  const rig = activation(of, 'A');

  assert.equal(await rig.run(), 'migrated');

  const version = rig.ports.version;
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the user value is gone');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'and the witness is the one fixed word');
  assert.deepEqual(overlayOf(of.client, KEY_X), { ...X_BESIDE, [OVERLAY_WITNESS]: version }, 'X is cleared, witnessed, and keeps what is beside it');
  assert.deepEqual(overlayOf(of.client, KEY_Y), { [OVERLAY_WITNESS]: version });
  assert.deepEqual(overlayOf(of.client, KEY_Z), { onExhausted: 'auto' }, 'a location that holds auto is not written');
  assert.equal(of.client.value()[KEY_STRING], 'not an overlay', 'a slot that is no record is not written');
  assert.deepEqual(of.client.value()[KEY], { completed: version, by: 'A', told: true });
  assert.equal(rig.tells().length, 1);
  assert.equal(rig.afters(), 1);
  assert.deepEqual(of.settings.updates.map((one) => one.key), [USER_WITNESS, 'onExhausted'], 'two user-layer updates: the witness, then the removal');
});

test('T8 ASecondActivationDoesNothing', async () => {
  const of = world();
  assert.equal(await activation(of, 'A').run(), 'migrated', 'the positive companion: the same fixture, something happens');
  const updates = of.settings.updates.length;

  const second = activation(of, 'B');

  assert.equal(await second.run(), 'already');
  assert.deepEqual(second.events, [], 'no write to the store, no pause, no after, no tell, no user-layer update');
  assert.equal(of.settings.updates.length, updates);
});

test('T9 AChoiceMadeAfterwardsIsKept_AlsoWithTheMarkerLost', async () => {
  const of = world(false);
  assert.equal(await activation(of, 'A').run(), 'migrated');
  of.settings.set('onExhausted', 'global', 'human');
  const chooser = chooseInOverlay(of.client, SIDE_X, 'human');
  assert.equal(chooser.get<Record<string, unknown>>(KEY_X)?.['onExhausted'], 'human', 'the choice is stored through the real writer');
  const updates = of.settings.updates.length;

  assert.equal(await activation(of, 'B').run(), 'already', '(a) a new activation does nothing');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human');

  await of.client.window().update(KEY, undefined);
  assert.equal(of.client.value()[KEY], undefined, '(b) the record is lost, as a stale write leaves it');
  assert.equal(await activation(of, 'C').run(), 'migrated', 'a new activation runs in full');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'the user layer is kept by its witness alone');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human', 'and X by its own');
  assert.equal(of.settings.updates.length, updates, 'no removal, and no second witness write');
});

test('T10 TheMirrorComesAfterEveryClearAndBeforeTheMarker_AndOnEveryWayOut', async () => {
  // First half: the mirror follows the removal and the settle pause, and the record has no marker yet.
  const of = world();
  const first = activation(of, 'A');
  assert.equal(await first.run(), 'migrated');
  const at = (kind: string): number => first.events.findIndex((one) => one.kind === kind);
  const mirror = first.events.findIndex((one) => one.kind === 'after');
  const marker = first.events.findIndex((one) => one.kind === 'write' && one.key === KEY && (one.value as { completed?: string }).completed !== undefined);
  assert.ok(at('clear user') < mirror, 'the mirror follows the removal');
  assert.deepEqual(first.events[mirror - 1], { kind: 'pause', ms: SETTLE_MS }, 'and the settle pause');
  const mirrored = first.events[mirror];
  assert.ok(mirrored?.kind === 'after' && mirrored.record.completed === undefined, 'at that moment the record has no completed');
  assert.ok(mirror < marker, 'the marker is written after it');

  // Second half: a run that loses its claim before Y's turn stops busy and still calls `after` once.
  const lost = world();
  const loser = activation(lost, 'A', {
    beforeTurn: (n) => {
      if (n === 2) {
        const other = lost.client.window();
        void other.update(KEY, { ...(other.get<Record<string, unknown>>(KEY) ?? {}), claim: { by: 'other', at: lost.clock.now() } });
        lost.client.deliver(loser.window);
      }
    },
  });
  assert.equal(await loser.run(), 'busy');
  assert.equal(loser.afters(), 1, 'after was called once on the way out');

  // Third half: the first run dies in the settle pause; the resumed run clears nothing and calls `after` once.
  const died = world();
  const dying = activation(died, 'A', { pauses: new Pauses(died.clock) });
  void dying.run();
  await until(() => dying.pauses.count() === 1, 'the claim pause');
  dying.pauses.next();
  await until(() => dying.pauses.count() === 1, 'the settle pause of the mirror');
  assert.equal(dying.afters(), 0, 'the first run died before it called after');
  afterADeath(died);
  died.client.deliver();
  const resumed = activation(died, 'B');
  assert.equal(await resumed.run(), 'migrated');
  assert.equal(resumed.afters(), 1);
  assert.deepEqual(resumed.userEvents(), [], 'the resumed run touched the user layer not at all');
  assert.deepEqual(resumed.writesTo(KEY_X), [], 'nor X');
});

test('T11 WhatEachLocationHeldIsWrittenBeforeTheFirstClear_AndNeverAgain', async () => {
  const of = world();
  const first = activation(of, 'A');
  assert.equal(await first.run(), 'migrated');
  const held = { user: 'human', [KEY_X]: 'human', [KEY_Y]: 'continue' };
  const carries = first.events.findIndex((one) => one.kind === 'write' && one.key === KEY && (one.value as { held?: unknown }).held !== undefined);
  const firstClear = first.events.findIndex((one) => one.kind === 'witness' || one.kind === 'clear user' || (one.kind === 'write' && (one.key === KEY_X || one.key === KEY_Y)));
  assert.ok(carries >= 0 && carries < firstClear, 'held is written before every overlay write and every user-layer update');
  const written = first.events[carries];
  assert.deepEqual(written?.kind === 'write' ? (written.value as { held: unknown }).held : undefined, held);

  // Interrupted after X's clear, the values change, the resumed run keeps the same record.
  const again = world();
  again.client.dieAfter(3);
  const dead = activation(again, 'A', { pauses: new Pauses(again.clock) });
  void dead.run();
  await until(() => dead.pauses.count() === 1, 'the claim pause');
  dead.pauses.next();
  await flush();
  again.settings.set('onExhausted', 'global', 'good_enough');
  chooseInOverlay(again.client, SIDE_Y, 'good_enough');
  afterADeath(again);
  const resumed = activation(again, 'B');
  assert.equal(await resumed.run(), 'migrated');
  const mirrored = resumed.events.find((one) => one.kind === 'after');
  assert.deepEqual(mirrored?.kind === 'after' ? mirrored.record.held : undefined, held, 'the resumed run\'s record has the same held');
});

test('T12 InterruptedAfterTheFirstClear_AChoiceMadeMeanwhileIsKept', async () => {
  const of = world();
  of.client.dieAfter(3); // the claim, held, then X's clear: it lands, and the host dies
  const dead = activation(of, 'A', { pauses: new Pauses(of.clock) });
  void dead.run();
  await until(() => dead.pauses.count() === 1, 'the claim pause');
  dead.pauses.next();
  await flush();
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'X was cleared before the host died');

  chooseInOverlay(of.client, SIDE_X, 'human');
  chooseInOverlay(of.client, SIDE_Y, 'good_enough');
  afterADeath(of);
  const resumed = activation(of, 'B');

  assert.equal(await resumed.run(), 'migrated');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human', 'X is kept: its witness names it, though its value is the recorded one again');
  assert.equal(overlayOf(of.client, KEY_Y)['onExhausted'], 'good_enough', 'Y is kept: it is no longer the recorded value');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the user layer is cleared');
  assert.equal(resumed.tells().length, 1, 'one tell in all');
});

test('T13 AnOverlayIsClearedAndWitnessedInOneWrite', async () => {
  const of = world();
  const rig = activation(of, 'A');
  assert.equal(await rig.run(), 'migrated');

  const writes = rig.writesTo(KEY_X) as Record<string, unknown>[];
  assert.equal(writes.length, 1, 'X\'s key is written exactly once');
  for (const value of writes) {
    assert.equal(value['onExhausted'], undefined, 'no value under X\'s key still holds the choice');
    assert.equal(value[OVERLAY_WITNESS], rig.ports.version, 'and every one carries the witness: there is no instant between');
  }
});

test('T14 TheHostDiesRightAfterTheUserClear_AHumanChosenThenIsKept', async () => {
  const setup = async (): Promise<World> => {
    const of = world(false);
    of.settings.dieAfter('onExhausted');
    const dead = activation(of, 'A', { pauses: new Pauses(of.clock) });
    void dead.run();
    await until(() => dead.pauses.count() === 1, 'the claim pause');
    dead.pauses.next();
    await flush();
    assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the removal landed and nothing after it ran');
    afterADeath(of);

    return of;
  };

  const nothingChosen = await setup();
  assert.equal(await activation(nothingChosen, 'B').run(), 'migrated', '(a) resumed with nothing chosen');
  assert.equal(nothingChosen.settings.layer('onExhausted', 'global'), undefined, 'the user layer is still empty');

  const chosen = await setup();
  chosen.settings.set('onExhausted', 'global', 'human');
  const resumed = activation(chosen, 'B');
  assert.equal(await resumed.run(), 'migrated', '(b) the person chooses human first');
  assert.equal(chosen.settings.layer('onExhausted', 'global'), 'human');
  assert.deepEqual(resumed.userEvents(), [], 'the resumed run made no user-layer update');
});

test('T15 RetryingAnAttemptThatMayHaveLandedWouldResetALaterChoice_SoItIsNeverRetried', async () => {
  // R2, chosen: a host that dies after the witness landed and before the removal was asked for leaves the old value in
  // place for good. The other order would reset a LATER `human` — the finding this design answers.
  const of = world(false);
  of.settings.dieAfter(USER_WITNESS);
  const dead = activation(of, 'A', { pauses: new Pauses(of.clock) });
  void dead.run();
  await until(() => dead.pauses.count() === 1, 'the claim pause');
  dead.pauses.next();
  await flush();
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD, 'the witness landed');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'and the removal was never asked for');
  afterADeath(of);
  const resumed = activation(of, 'B');

  assert.equal(await resumed.run(), 'migrated');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'the value stays');
  assert.deepEqual(resumed.userEvents(), [], 'the resumed run made no user-layer update');
  assert.deepEqual(resumed.tells(), [staysInForce('Ask a human', WHERE.user)], 'the notification names the kept value');
});

test('T16 ARemovalTheSettingsFileRefuses_WithdrawsTheWitness_WritesNoMarker_IsSaidOnce_AndTheNextStartFinishes', async () => {
  const of = world();
  const refusal = new Error('EACCES: settings.json is read-only');
  of.settings.refuse('onExhausted', refusal);
  const first = activation(of, 'A');

  assert.equal(await first.run(), 'refused');
  assert.deepEqual(first.userEvents().map((one) => one.kind), ['witness', 'clear user', 'withdraw'], 'written, then withdrawn');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), undefined, 'no witness is left');
  const record = of.client.record();
  assert.equal(record.completed, undefined, 'no marker');
  assert.equal(record.claim, undefined, 'no claim');
  assert.equal(record.refusedTold, true);
  assert.deepEqual(first.refusals(), [refusal.message], 'the person is told once, with the error\'s own text');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'X is cleared');
  assert.equal(overlayOf(of.client, KEY_Y)['onExhausted'], undefined, 'and Y');
  assert.equal(first.afters(), 1);

  const second = activation(of, 'B');
  assert.equal(await second.run(), 'refused', 'a second refusing activation');
  assert.deepEqual(second.refusals(), [], 'and the person is not told again');

  of.settings.cure('onExhausted');
  const third = activation(of, 'C');
  assert.equal(await third.run(), 'migrated', 'the file is cured');
  assert.equal(of.settings.layer('onExhausted', 'global'), undefined, 'the user layer is cleared');
  assert.equal(third.tells().length, 1);
});

test('T17 AWriteThatDidNotLastIsSeenBeforeTheMarker', async () => {
  const of = world();
  const stale = of.client.window();
  const rig = activation(of, 'A', { pauses: new Pauses(of.clock) });
  const run = rig.run();
  await until(() => rig.pauses.count() === 1, 'the claim pause');
  rig.pauses.next();
  await until(() => rig.pauses.count() === 1, 'the settle pause of the mirror');

  // A stale write (V3): X's old record comes back, with the rest of the record as it is now.
  const late = of.client.window();
  void late.update(KEY_X, stale.get(KEY_X));
  of.client.deliver(rig.window);
  rig.pauses.next();

  const outcome = await Promise.race([run, flush().then(() => 'still waiting for a pause')]);
  assert.equal(outcome, 'incomplete', 'the marker is not written while X holds its old value');
  assert.equal(of.client.record().completed, undefined, 'no marker');
  assert.equal(of.client.record().claim, undefined, 'the claim is released');
  assert.deepEqual(rig.tells(), []);
  assert.equal(rig.afters(), 1);

  const next = activation(of, 'B');
  assert.equal(await next.run(), 'migrated', 'the next activation completes');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'and clears X');
});

test('T18 AWindowThatDoesNotKnowTheNewManifestChangesNothing', async () => {
  const of = world();
  of.settings.undeclare(USER_WITNESS);
  const before = of.client.value();
  const updates = of.settings.updates.length;
  const idle = activation(of, 'A');

  assert.equal(await idle.run(), 'not-ready');
  assert.deepEqual(of.client.value(), before, 'nothing is written to the store, not even a claim');
  assert.equal(of.settings.updates.length, updates, 'nor to the settings');
  assert.deepEqual(idle.pauses.asked, [], 'and no pause is asked');

  of.settings.declare(USER_WITNESS);
  assert.equal(await activation(of, 'B').run(), 'migrated', 'declared, the same fixture migrates');
});

test('T19 AFreshInstallIsToldOnce_AndWritesTheWitness', async () => {
  const fresh = world(false, {});
  fresh.settings.set('onExhausted', 'global', undefined);
  const rig = activation(fresh, 'A');

  assert.equal(await rig.run(), 'migrated');
  assert.ok(rig.events.filter((one) => one.kind === 'write').every((one) => one.kind === 'write' && one.key === KEY), 'every store write is under the record\'s key');
  assert.deepEqual(fresh.settings.updates, [{ key: USER_WITNESS, value: USER_WITNESS_WORD, target: GLOBAL }], 'one user-layer update: the witness');
  const carries = rig.events.find((one) => one.kind === 'write' && (one.value as { held?: unknown }).held !== undefined);
  assert.deepEqual(carries?.kind === 'write' ? (carries.value as { held: unknown }).held : undefined, {}, 'the first write that carries held has held: {}');
  assert.equal(rig.afters(), 1);
  assert.deepEqual(rig.tells(), [NOW_AUTO]);

  const unnamed = world(false, {});
  assert.equal(await activation(unnamed, 'A', { version: '' }).run(), 'migrated');
  assert.equal(unnamed.client.record().completed, 'unknown', 'an empty version is stored as unknown');
});

test('T20 AWorkspaceValueIsNeverEdited', async () => {
  const of = world();
  of.settings.set(USER_WITNESS, 'workspace', 'stray');
  const rig = activation(of, 'A');

  assert.equal(await rig.run(), 'migrated');
  assert.equal(of.settings.layer('onExhausted', 'workspace'), 'human', 'the workspace value is unchanged');
  assert.equal(of.settings.layer('onExhausted', 'folder'), 'continue', 'the folder value too');
  assert.equal(of.settings.layer(USER_WITNESS, 'workspace'), 'stray', 'and the workspace\'s witness setting');
  assert.ok(of.settings.updates.length > 0 && of.settings.updates.every((one) => one.target === GLOBAL), 'every user-layer update has the Global target');
});

test('T21 TheCatalogMigrationAndThisRunTakeTurnsOnOneOverlay', async () => {
  const of = world();
  const rig = activation(of, 'A');
  let open: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => {
    open = resolve;
  });
  const NEW_VENDORS = [{ id: 'newer' }];
  const catalog = rig.turns.migrate(async () => {
    const copy = rig.window.get<Record<string, unknown>>(KEY_X);
    await gate;
    await rig.window.update(KEY_X, { ...copy, vendors: NEW_VENDORS });
  });

  const run = rig.run();
  await until(() => rig.events.some((one) => one.kind === 'write' && one.key === KEY && (one.value as { held?: unknown }).held !== undefined), 'the run reached the overlays');
  await flush();
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], 'human', 'the run waits for the catalog\'s turn');
  open();
  await catalog;

  assert.equal(await run, 'migrated');
  const x = overlayOf(of.client, KEY_X);
  assert.deepEqual(x['vendors'], NEW_VENDORS, 'the catalog\'s write is kept');
  assert.equal(x['onExhausted'], undefined, 'and the choice is gone');
  assert.equal(x[OVERLAY_WITNESS], rig.ports.version, 'with its witness');
});

test('T22 ACompletedRunThatNeverToldIsToldByTheNextActivation', async () => {
  const of = world(false, { [KEY]: { completed: '0.88.0', by: 'gone' } });
  const before = of.client.value();
  const rig = activation(of, 'A');

  assert.equal(await rig.run(), 'migrated');
  assert.equal(rig.tells().length, 1, 'one tell');
  assert.equal(of.client.record().told, true);
  assert.equal(of.client.record().claim, undefined, 'no claim was written');
  assert.equal(rig.afters(), 0, 'no after');
  assert.deepEqual({ ...of.client.value(), [KEY]: undefined }, { ...before, [KEY]: undefined }, 'nothing was cleared');
  assert.deepEqual(of.settings.updates, []);

  const further = activation(of, 'B');
  assert.equal(await further.run(), 'already');
  assert.deepEqual(further.tells(), [], 'a further activation tells nothing');
});

test('T37 TheNotificationNamesAValueThatStaysInForce', async () => {
  // (1) U, the per-side switch on, this side's overlay is X; before X's turn the person sets X to good_enough.
  const one = world();
  const sided: Rig = activation(one, 'A', {
    perSide: true,
    side: SIDE_X,
    beforeTurn: (n) => {
      if (n === 1) {
        chooseInOverlay(one.client, SIDE_X, 'good_enough');
        one.client.deliver(sided.window);
      }
    },
  });
  assert.equal(await sided.run(), 'migrated');
  assert.equal(overlayOf(one.client, KEY_X)['onExhausted'], 'good_enough', 'X is kept');
  assert.deepEqual(sided.tells(), [staysInForce(labelOf('good_enough'), WHERE.side)]);

  // (2) U as it is: the folder's `continue` runs.
  const two = world();
  const workspace = activation(two, 'A');
  assert.equal(await workspace.run(), 'migrated');
  assert.deepEqual(workspace.tells(), [staysInForce('Continue, and say so', WHERE.workspace)]);

  // (3) U0 with the user layer `continue` and another machine's witness already there.
  const three = world(false);
  three.settings.set('onExhausted', 'global', 'continue');
  three.settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  const synced = activation(three, 'A');
  assert.equal(await synced.run(), 'migrated');
  assert.equal(three.settings.layer('onExhausted', 'global'), 'continue', 'the value is kept');
  assert.deepEqual(synced.tells(), [staysInForce('Continue, and say so', WHERE.user)]);

  // (4) U0: everything is cleared.
  const four = world(false);
  const cleared = activation(four, 'A');
  assert.equal(await cleared.run(), 'migrated');
  assert.deepEqual(cleared.tells(), [NOW_AUTO]);
});

test('T38 ASettingsFileThatRefusesTheWitness_RemovesNothing', async () => {
  const refusal = new Error('EROFS: read-only settings');
  const of = world();
  of.settings.refuse(USER_WITNESS, refusal);
  const first = activation(of, 'A');

  assert.equal(await first.run(), 'refused');
  assert.equal(of.settings.updates.some((one) => one.key === 'onExhausted'), false, 'no removal was asked for');
  assert.equal(of.settings.layer('onExhausted', 'global'), 'human', 'the user layer still holds its value');
  assert.equal(of.settings.layer(USER_WITNESS, 'global'), undefined, 'no witness');
  assert.equal(of.client.record().completed, undefined, 'no marker');
  assert.equal(overlayOf(of.client, KEY_X)['onExhausted'], undefined, 'X is cleared');
  assert.equal(overlayOf(of.client, KEY_Y)['onExhausted'], undefined, 'and Y');
  assert.equal(first.afters(), 1);
  assert.deepEqual(first.refusals(), [refusal.message], 'the person is told once, with the error\'s text');
  const second = activation(of, 'B');
  assert.equal(await second.run(), 'refused');
  assert.deepEqual(second.refusals(), [], 'and not again at a second refusing activation');

  // Second half: nothing in the user layer — "Your earlier choice there is still in force" would be false.
  const empty = world(false);
  empty.settings.set('onExhausted', 'global', undefined);
  empty.settings.refuse(USER_WITNESS, refusal);
  const quiet = activation(empty, 'A');
  assert.equal(await quiet.run(), 'refused');
  assert.equal(empty.client.record().completed, undefined, 'no marker');
  assert.deepEqual(quiet.refusals(), [], 'the person is NOT told');
  assert.ok(quiet.logs.some((line) => line.includes(refusal.message)), 'the refusal is in the log');

  empty.settings.cure(USER_WITNESS);
  assert.equal(await activation(empty, 'B').run(), 'migrated', 'cured, the next activation completes');
  assert.equal(empty.settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD);
});

test('T40 ARefusalThatNamesAnUnknownKeyIsAWait_NotAWarning', async () => {
  // First half: VS Code's own words — the registry loses the key after step 0 has passed.
  const one = world();
  const first = activation(one, 'A', {
    beforeTurn: (n) => {
      if (n === 1) {
        one.settings.undeclare(USER_WITNESS);
      }
    },
  });
  assert.equal(await first.run(), 'not-ready');
  assert.deepEqual(first.refusals(), [], 'the warning is not shown');
  assert.equal(one.settings.updates.some((call) => call.key === 'onExhausted'), false, 'no removal');
  assert.equal(one.settings.updates.some((call) => call.key === USER_WITNESS && call.value === USER_WITNESS_WORD), true, 'the witness write was asked for, and refused in VS Code\'s words');
  assert.equal(one.client.record().completed, undefined, 'no marker');
  assert.equal(one.client.record().claim, undefined, 'the claim is released');
  assert.equal(first.afters(), 1);
  assert.equal(unregisteredWords(USER_WITNESS).includes(USER_WITNESS), true, 'the words name the key');

  // Second half: the refusal is in another language, and the key is undeclared.
  const two = world();
  const german = new Error('Schreiben in die Benutzereinstellungen nicht möglich: die Einstellung ist nicht registriert.');
  const second = activation(two, 'A', {
    beforeTurn: (n) => {
      if (n === 1) {
        two.settings.undeclare(USER_WITNESS);
        two.settings.refuse(USER_WITNESS, german);
      }
    },
  });
  assert.equal(await second.run(), 'not-ready', 'asking the registry again is the second question');
  assert.deepEqual(second.refusals(), []);

  // Third half: the same words with the key declared are a refusal of the file.
  const three = world();
  three.settings.refuse(USER_WITNESS, german);
  const third = activation(three, 'A');
  assert.equal(await third.run(), 'refused');
  assert.deepEqual(third.refusals(), [german.message]);
});

