import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  DeletionWorld, RoleDeletions, STRANDED_AFTER_MS, Tombstone, TombstoneStore, reserved,
} from '../roleDeletion';

/**
 * Deleting a role, and what happens when the mirror does not carry it.
 *
 * <p>Every case here is RUN. `RoleDeletions` takes the store, the settings, the clock and the
 * reporter as parameters for exactly that reason: "the host died between step two and step three" is
 * then "call step two, throw the object away, call the sweep" rather than a process nobody kills.</p>
 */

/** A store in memory, which is the whole point of the store being a parameter. */
function kept(): TombstoneStore & { readonly held: Map<string, Tombstone> } {
  const held = new Map<string, Tombstone>();

  return {
    held,
    put: async (tombstone) => {
      held.set(tombstone.roleId, tombstone);
    },
    read: async (roleId) => held.get(roleId),
    all: async () => [...held.values()],
    drop: async (roleId) => {
      held.delete(roleId);
    },
  };
}

interface Seen {
  readonly pruned: string[];
  readonly forgotten: string[];
  readonly said: string[];
}

/** A world whose configuration is a set of live role ids, so `gone` is a real question. */
function world(live: Set<string>, at = new Date('2026-09-18T12:00:00Z')): {
  readonly world: DeletionWorld;
  readonly seen: Seen;
  readonly store: ReturnType<typeof kept>;
  clock: Date;
} {
  const store = kept();
  const seen: Seen = { pruned: [], forgotten: [], said: [] };
  const holder = { clock: at };

  return {
    store,
    seen,
    get clock() {
      return holder.clock;
    },
    set clock(value: Date) {
      holder.clock = value;
    },
    world: {
      store,
      prune: async (roleId) => {
        seen.pruned.push(roleId);
        live.delete(roleId);
      },
      gone: (roleId) => !live.has(roleId),
      forget: async (promptIds) => {
        seen.forgotten.push(...promptIds);
      },
      now: () => holder.clock,
      say: (tombstone, reason) => seen.said.push(`${tombstone.roleId}: ${reason}`),
    },
  };
}

const ROLE = { id: 'Role2', name: 'Role 2', promptIds: ['role2-general'] };

test('a mirror that does not carry it leaves the TEXT on disk, and says why', async () => {
  // The defect itself. Asserting only "it reports" passes a build that deletes the text and then
  // complains about it, which is the 2026-09-16 incident with a notification bolted on.
  const live = new Set(['Role2', 'Other']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  await deletions.settled(false, 'another window is writing the settings');

  assert.deepEqual(one.seen.forgotten, [], 'the prose was deleted before the server had lost the role');
  assert.equal(one.store.held.size, 1, 'the tombstone was cleared although nothing finished');
  assert.deepEqual(one.seen.said, ['Role2: another window is writing the settings']);
  assert.equal(one.store.held.get('Role2')?.reason, 'another window is writing the settings');
});

test('the same reason twice over is said once, because a condition is not an attempt', async () => {
  const one = world(new Set(['Role2']));
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  await deletions.settled(false, 'the settings could not be written');
  await deletions.settled(false, 'the settings could not be written');

  assert.equal(one.seen.said.length, 1, 'a mirror failing twice is one condition, not two');
});

test('a mirror that carries it prunes the row, forgets the text and clears the tombstone', async () => {
  const live = new Set(['Role2', 'Other']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);

  assert.deepEqual(one.seen.pruned, ['Role2'], 'the row and its four records go FIRST, with each other');
  assert.deepEqual(one.seen.forgotten, [], 'and the text does not go with them');

  await deletions.settled(true, '');

  assert.deepEqual(one.seen.forgotten, ['role2-general']);
  assert.equal(one.store.held.size, 0, 'the tombstone outlived the deletion it recorded');
});

test('the LISTENER owns the sync and the deletion is told busy: the cleanup still completes', async () => {
  // The round's sharpest finding, and the normal path rather than an edge case. Writing `coai.roles`
  // fires VS Code's configuration listener, which starts the mirror's own schedule — so a deletion
  // that called `sync()` itself would be answered `busy`, the listener's sync would then succeed, and
  // nothing would resume the cleanup. A build that calls sync itself passes every other test here.
  const live = new Set(['Role2']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  // The schedule's attempt, which this deletion did not start and does not own.
  await deletions.settled(false, 'another window is writing the settings');

  assert.deepEqual(one.seen.forgotten, []);

  // And now the listener's own sync lands.
  await deletions.settled(true, '');

  assert.deepEqual(one.seen.forgotten, ['role2-general'],
    'the deletion needed a reactivation or a force to finish something that had already succeeded');
  assert.equal(one.store.held.size, 0);
});

test('a sync landing BETWEEN two of step two writes does not finish it; the next one does', async () => {
  // Step 2 is several `config.update` calls and each fires the listener, so a sync that landed
  // half-way carried an incomplete removal. The condition is that the role is ABSENT from the
  // configuration as it reads now, not that a write landed.
  const live = new Set(['Role2']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await one.world.store.put({
    roleId: 'Role2', name: 'Role 2', promptIds: ['role2-general'],
    askedAt: '2026-09-18T12:00:00.000Z', nonce: 'n1', reason: '', failedAt: '',
  });
  // The row is still there: this is the sync that landed before step 2 finished.
  await deletions.settled(true, '');

  assert.deepEqual(one.seen.forgotten, [], 'a half-written removal was treated as carried');

  await deletions.sweep();
  await deletions.settled(true, '');

  assert.deepEqual(one.seen.forgotten, ['role2-general']);
});

test('interrupted at each REAL gap, the sweep and the next outcome finish it', async () => {
  // Three gaps, and they are three because that is how many there are: the first draft of this test
  // named four and set up the same state for the last three, which is a bigger number proving the
  // same thing once. What differs between these is the state the dead host left behind.
  const gaps = [
    { name: 'after the tombstone, before the row', pruned: false, forgotten: false },
    { name: 'after the row, before the mirror carried it', pruned: true, forgotten: false },
    { name: 'after the text, before the tombstone was cleared', pruned: true, forgotten: true },
  ];
  for (const gap of gaps) {
    const live = new Set(['Role2']);
    const one = world(live);

    await one.world.store.put({
      roleId: 'Role2', name: 'Role 2', promptIds: ['role2-general'],
      askedAt: '2026-09-18T12:00:00.000Z', nonce: 'n1', reason: '', failedAt: '',
    });
    if (gap.pruned) {
      await one.world.prune('Role2');
      one.seen.pruned.length = 0;
    }
    if (gap.forgotten) {
      await one.world.forget(['role2-general']);
      one.seen.forgotten.length = 0;
    }

    // The host dies. A NEW object, with nothing remembered.
    const next = new RoleDeletions(one.world, () => 'n2');

    await next.sweep();
    await next.settled(true, '');

    assert.deepEqual(one.seen.pruned, ['Role2'],
      `interrupted ${gap.name}, the row was not pruned again - and a host that died before writing it `
      + 'never wrote it');
    assert.deepEqual(one.seen.forgotten, ['role2-general'], `interrupted ${gap.name}, it did not resume`);
    assert.equal(one.store.held.size, 0, `interrupted ${gap.name}, the tombstone was left standing`);
  }
});

test('a worker whose tombstone was cleared and whose id came back deletes NOTHING', async () => {
  // Idempotence protects a repeated operation on unchanged state. It does not protect a NEW role
  // that reuses an identity: without the nonce, this deletes the new role's text. (codex, the plan
  // round.)
  const live = new Set(['Role2']);
  const one = world(live);
  const slow = new RoleDeletions(one.world, () => 'n1');

  await slow.begin(ROLE);

  // Another window finishes the same deletion, and the person creates a role that takes the id back.
  await one.world.store.drop('Role2');
  one.seen.forgotten.length = 0;
  live.add('Role2');
  await one.world.store.put({
    roleId: 'Role2', name: 'A new role', promptIds: ['role2-general'],
    askedAt: '2026-09-18T13:00:00.000Z', nonce: 'LATER', reason: '', failedAt: '',
  });

  // The slow worker wakes up holding the OLD tombstone.
  await slow.settled(true, '');

  assert.deepEqual(one.seen.forgotten, [],
    'a stale worker deleted the text of the role that took the freed id');
});

test('a tombstoned id is not handed to a new role, and is again once it clears', async () => {
  const held: Tombstone[] = [{
    roleId: 'Role2', name: 'Role 2', promptIds: [], askedAt: '', nonce: 'n1', reason: '', failedAt: '',
  }];

  assert.deepEqual([...reserved(held)], ['role2'], 'the id is free while a deletion is on its way out');
  assert.deepEqual([...reserved([])], [], 'and it is never released once the deletion finishes');
});

test('a tombstone is stranded only after it has FAILED, and not while the write is in flight', async () => {
  const one = world(new Set(['Role2']));
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  assert.deepEqual(await deletions.stranded(), [], 'a deletion that has not failed is not stranded');

  await deletions.settled(false, 'the settings could not be written');
  assert.deepEqual(await deletions.stranded(), [],
    'a write that failed one second ago is shown to somebody as though it were stuck');

  one.clock = new Date(one.clock.getTime() + STRANDED_AFTER_MS + 1);
  const stuck = await deletions.stranded();

  assert.deepEqual(stuck.map((s) => s.roleId), ['Role2']);
  assert.equal(stuck[0]?.reason, 'the settings could not be written', 'stranded without its reason is a dead end');
});

test('finishing it anyway releases the id, and takes the text with it', async () => {
  const one = world(new Set(['Role2']));
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  await deletions.settled(false, 'a newer build owns the settings file');
  await deletions.finishAnyway('Role2');

  assert.deepEqual(one.seen.forgotten, ['role2-general']);
  assert.equal(one.store.held.size, 0, 'the id is still held, so the role cannot be recreated');
});
