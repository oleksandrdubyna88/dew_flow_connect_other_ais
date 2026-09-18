import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  DeletionWorld, RoleDeletions, STRANDED_AFTER_MS, Tombstone, TombstoneStore, reserved,
} from '../roleDeletion';
import { payloadMentionsRole, serverSettingsJson } from '../serverSettingsFile';
import { DEFAULTS } from '../settingsShape';

/**
 * Deleting a role, and what happens when the mirror does not carry it.
 *
 * <p>Every case here is RUN. `RoleDeletions` takes the store, the settings, the clock and the
 * reporter as parameters for exactly that reason: "the host died between step two and step three" is
 * then "call step two, throw the object away, call the sweep" rather than a process nobody kills.</p>
 */

/** A store in memory, which is the whole point of the store being a parameter. */
function kept(): TombstoneStore & {
  readonly held: Map<string, Tombstone>;
  /** Run just before a claim, so a test can be the OTHER window finishing in that instant. */
  beforeClaim?: () => void;
} {
  const held = new Map<string, Tombstone>();
  const claimed = new Set<string>();

  return {
    held,
    put: async (tombstone) => {
      held.set(tombstone.roleId, tombstone);
    },
    read: async (roleId) => held.get(roleId),
    all: async () => [...held.values()],
    drop: async (roleId) => {
      held.delete(roleId);
      claimed.delete(roleId);
    },
    reservedIds: async () => new Set([...held.keys()].map((one) => one.toLowerCase())),
    // The rename, modelled: the nonce is checked INSIDE it, and a second claimant finds it taken.
    claim: async function claim(this: { beforeClaim?: () => void }, roleId, nonce) {
      this.beforeClaim?.();
      if (held.get(roleId)?.nonce !== nonce || claimed.has(roleId)) {
        return false;
      }
      claimed.add(roleId);

      return true;
    },
  };
}

/**
 * The payload a mirror would have carried for these roles, built by the REAL builder.
 *
 * <p>Through `serverSettingsJson` rather than a hand-written string, so the question the coordinator
 * asks of a payload is asked of a payload shaped the way the mirror actually writes one. A stub here
 * would let the two drift, and the drift would be a deletion that finishes against a write that
 * still carried the role.</p>
 */
function payloadFor(ids: readonly string[]): string {
  const rows = ids.map((id) => ({
    id,
    name: id,
    stage: 'code',
    programmingTask: true,
    active: true,
    prompts: [],
  }));

  return serverSettingsJson({ ...DEFAULTS, roles: rows }, []);
}

interface Seen {
  readonly pruned: string[];
  readonly forgotten: string[];
  readonly said: string[];
  /** When the page was told to come back, in ms. `0` is "now". */
  readonly redrawIn: number[];
}

/** A world whose configuration is a set of live role ids, so `gone` is a real question. */
function world(live: Set<string>, at = new Date('2026-09-18T12:00:00Z')): {
  readonly world: DeletionWorld;
  readonly seen: Seen;
  readonly store: ReturnType<typeof kept>;
  clock: Date;
} {
  const store = kept();
  const seen: Seen = { pruned: [], forgotten: [], said: [], redrawIn: [] };
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
      mentions: payloadMentionsRole,
      forget: async (promptIds) => {
        seen.forgotten.push(...promptIds);
      },
      now: () => holder.clock,
      say: (tombstone, reason) => seen.said.push(`${tombstone.roleId}: ${reason}`),
      changed: (inMs) => seen.redrawIn.push(inMs),
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
  await deletions.settled(false, '', 'another window is writing the settings');

  assert.deepEqual(one.seen.forgotten, [], 'the prose was deleted before the server had lost the role');
  assert.equal(one.store.held.size, 1, 'the tombstone was cleared although nothing finished');
  assert.deepEqual(one.seen.said, ['Role2: another window is writing the settings']);
  assert.equal(one.store.held.get('Role2')?.reason, 'another window is writing the settings');
});

test('the same reason twice over is said once, because a condition is not an attempt', async () => {
  const one = world(new Set(['Role2']));
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  await deletions.settled(false, '', 'the settings could not be written');
  await deletions.settled(false, '', 'the settings could not be written');

  assert.equal(one.seen.said.length, 1, 'a mirror failing twice is one condition, not two');
});

test('a mirror that carries it prunes the row, forgets the text and clears the tombstone', async () => {
  const live = new Set(['Role2', 'Other']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);

  assert.deepEqual(one.seen.pruned, ['Role2'], 'the row and its four records go FIRST, with each other');
  assert.deepEqual(one.seen.forgotten, [], 'and the text does not go with them');

  await deletions.settled(true, payloadFor([...live]), '');

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
  await deletions.settled(false, '', 'another window is writing the settings');

  assert.deepEqual(one.seen.forgotten, []);

  // And now the listener's own sync lands.
  await deletions.settled(true, payloadFor([...live]), '');

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
  await deletions.settled(true, payloadFor([...live]), '');

  assert.deepEqual(one.seen.forgotten, [], 'a half-written removal was treated as carried');

  await deletions.sweep();
  await deletions.settled(true, payloadFor([...live]), '');

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
    await next.settled(true, payloadFor([...live]), '');

    assert.deepEqual(one.seen.pruned, ['Role2'],
      `interrupted ${gap.name}, the row was not pruned again - and a host that died before writing it `
      + 'never wrote it');
    assert.deepEqual(one.seen.forgotten, ['role2-general'], `interrupted ${gap.name}, it did not resume`);
    assert.equal(one.store.held.size, 0, `interrupted ${gap.name}, the tombstone was left standing`);
  }
});

test('a worker whose tombstone was cleared and whose id came back deletes NOTHING', async () => {
  // Idempotence protects a repeated operation on unchanged state. It does not protect a NEW role
  // that reuses an identity.
  //
  // The FIRST version of this test passed with the nonce guard removed, and the round said so: it
  // put the new role back into the live set, so the payload still mentioned it and the coordinator
  // stopped one step earlier, at a check that has nothing to do with nonces. The role is absent
  // from the payload here on purpose, so the only thing that can refuse the deletion is the claim.
  // (codex, the code round, Blocking.)
  const live = new Set(['Role2']);
  const one = world(live);
  const slow = new RoleDeletions(one.world, () => 'n1');

  await slow.begin(ROLE);
  one.seen.forgotten.length = 0;

  // The other window finishes IN THE INSTANT between this worker reading the tombstone and acting
  // on it, and the person creates a role that takes the released id. That instant is the whole
  // defect: a check and a delete are two operations with an await between them.
  one.store.beforeClaim = () => {
    one.store.held.set('Role2', {
      roleId: 'Role2', name: 'A new role', promptIds: ['role2-general'],
      askedAt: '2026-09-18T13:00:00.000Z', nonce: 'LATER', reason: '', failedAt: '',
    });
  };
  await slow.settled(true, payloadFor([]), '');

  assert.deepEqual(one.seen.forgotten, [],
    'a stale worker deleted the text of the role that took the freed id');
  assert.equal(one.store.held.get('Role2')?.nonce, 'LATER',
    'and it dropped the NEW deletion on its way out');
});

test('a write that landed carrying the role does NOT finish the deletion', async () => {
  // The other half of the same lesson, and the one the round called Blocking twice. A mirror can
  // finish writing a payload that still contains the role and only then call back; asking "is the
  // role absent from the settings NOW" answers yes, because `begin` has pruned them in the
  // meantime. Current configuration is not evidence of what was acknowledged.
  const live = new Set(['Role2']);
  const one = world(live);
  const deletions = new RoleDeletions(one.world, () => 'n1');
  const stale = payloadFor(['Role2']);

  await deletions.begin(ROLE);
  await deletions.settled(true, stale, '');

  assert.deepEqual(one.seen.forgotten, [],
    'the text went on the strength of a write that still carried the role');
  assert.equal(one.store.held.size, 1);

  await deletions.settled(true, payloadFor([]), '');

  assert.deepEqual(one.seen.forgotten, ['role2-general'], 'and the write that DID carry it finished nothing');
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

  await deletions.settled(false, '', 'the settings could not be written');
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
  await deletions.settled(false, '', 'a newer build owns the settings file');
  await deletions.finishAnyway('Role2');

  assert.deepEqual(one.seen.forgotten, ['role2-general']);
  assert.equal(one.store.held.size, 0, 'the id is still held, so the role cannot be recreated');
});

test('the page is told when to come back, and told again when there is nothing to show', async () => {
  // The controls appeared only by accident before this. `remove` redraws the page before anything
  // can be stranded — that needs a terminal failure and then ten more seconds — and recording the
  // failure only writes a file. A person who left the Roles tab open was told to go there and found
  // nothing. (codex, the code round.)
  const one = world(new Set(['Role2']));
  const deletions = new RoleDeletions(one.world, () => 'n1');

  await deletions.begin(ROLE);
  assert.deepEqual(one.seen.redrawIn, [], 'a deletion that has not failed has nothing to show yet');

  await deletions.settled(false, '', 'the settings could not be written');

  assert.deepEqual(one.seen.redrawIn, [STRANDED_AFTER_MS],
    'the page was never told when the deletion would become stranded');

  await deletions.settled(true, payloadFor([]), '');

  assert.deepEqual(one.seen.redrawIn, [STRANDED_AFTER_MS, 0],
    'a resolved deletion sat on the page until something unrelated redrew it');
});
