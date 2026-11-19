import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { USER_WITNESS, USER_WITNESS_WORD } from '../onExhaustedMigration';
import { labelOf } from '../onExhaustedPanel';
import { staysInForce, WHERE } from '../onExhaustedTold';
import {
  ClientState,
  FakeUserSettings,
  KEY_X,
  KEY_Y,
  Rig,
  usualSettings,
  until,
} from './onExhaustedMigrationFakes';

/**
 * Two machines (research/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.8.4): each its own `ClientState` — its own overlays,
 * record and windows — sharing ONE `FakeUserSettings`, the synced user layer after Settings Sync has delivered. What a
 * real Sync does between two deliveries is not modelled (R1, R10): the only observation there can be is the owner's.
 */

/** Machine A keeps side X's overlay, machine B side Y's — both hold `human`. */
function machines(): { readonly a: ClientState; readonly b: ClientState; readonly settings: FakeUserSettings } {
  return {
    a: new ClientState({ [KEY_X]: { onExhausted: 'human' } }),
    b: new ClientState({ [KEY_Y]: { onExhausted: 'human' } }),
    settings: usualSettings(false),
  };
}

function on(client: ClientState, settings: FakeUserSettings, name: string): Rig {
  return new Rig({ client, settings, name, auto: true });
}

test('T32 ASecondMachineKeepsAChoiceMadeOnTheFirst', async () => {
  const { a, b, settings } = machines();
  assert.equal(await on(a, settings, 'A').run(), 'migrated');
  assert.equal(settings.layer('onExhausted', 'global'), undefined, 'the first machine cleared the user layer');

  settings.set('onExhausted', 'global', 'human'); // the person chooses it, on the first machine
  const second = on(b, settings, 'B');

  assert.equal(await second.run(), 'migrated');
  assert.equal(b.value()[KEY_Y] !== undefined && (b.value()[KEY_Y] as Record<string, unknown>)['onExhausted'], undefined, 'XB is cleared');
  assert.equal(settings.layer('onExhausted', 'global'), 'human', 'the user layer is the person\'s choice');
  assert.deepEqual(second.userEvents(), [], 'B made no user-layer update');
  assert.deepEqual(second.tells(), [staysInForce('Ask a human', WHERE.user)]);
});

test('T33 AFirstMachineWithNothingToRemoveStillWritesTheWitness', async () => {
  const { a, b, settings } = machines();
  settings.set('onExhausted', 'global', undefined);
  const first = on(a, settings, 'A');

  assert.equal(await first.run(), 'migrated');
  assert.deepEqual(first.userEvents().map((one) => one.kind), ['witness'], 'one user-layer update, the witness');
  assert.equal(settings.layer(USER_WITNESS, 'global'), USER_WITNESS_WORD);

  settings.set('onExhausted', 'global', 'human'); // the person chooses it on the first machine
  const second = on(b, settings, 'B');
  assert.equal(await second.run(), 'migrated');
  assert.equal(settings.layer('onExhausted', 'global'), 'human', 'the choice is kept');
  assert.deepEqual(second.userEvents(), [], 'B made no user-layer update');
});

test('T34 AChoiceDeliveredDuringTheAwaitedWriteStopsTheRemoval', async () => {
  const { b, settings } = machines();
  settings.hold(USER_WITNESS, 'unlanded');
  const second = new Rig({ client: b, settings, name: 'B', auto: true });
  const run = second.run();
  await until(() => settings.updates.some((one) => one.key === USER_WITNESS), 'B asked for its witness write');

  // Before it lands, machine A's file arrives: the witness, and `continue` — a choice made on A after A's run.
  settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  settings.set('onExhausted', 'global', 'continue');
  settings.release(USER_WITNESS);

  assert.equal(await run, 'migrated');
  assert.equal(settings.updates.some((one) => one.key === 'onExhausted'), false, 'B made no removal');
  assert.equal(settings.layer('onExhausted', 'global'), 'continue');
  assert.deepEqual(second.tells(), [staysInForce(labelOf('continue'), WHERE.user)]);

  // Second half, asserted as the residual it is (R1 (c)): the delivered choice is the recorded word itself, so B removes
  // it — no test of the value can tell the two apart.
  const same = machines();
  same.settings.hold(USER_WITNESS, 'unlanded');
  const other = new Rig({ client: same.b, settings: same.settings, name: 'B', auto: true });
  const again = other.run();
  await until(() => same.settings.updates.some((one) => one.key === USER_WITNESS), 'B asked for its witness write');
  same.settings.set(USER_WITNESS, 'global', USER_WITNESS_WORD);
  same.settings.set('onExhausted', 'global', 'human');
  same.settings.release(USER_WITNESS);
  assert.equal(await again, 'migrated');
  assert.equal(same.settings.layer('onExhausted', 'global'), undefined, 'a delivered human equal to the recorded one is removed');
});
