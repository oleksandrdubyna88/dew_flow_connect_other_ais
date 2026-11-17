import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ClientState, FakeUserSettings, GLOBAL, unregisteredWords } from './onExhaustedMigrationFakes';

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
