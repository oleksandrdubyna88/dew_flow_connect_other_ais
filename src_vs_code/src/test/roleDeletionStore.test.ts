import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Tombstone } from '../roleDeletion';
import { deletionFile, deletionsDir, tombstonesIn } from '../roleDeletionStore';

/**
 * The tombstone on disk: it is a real directory of real files, so these tests use one.
 *
 * <p>What is NOT tested here is the coordinator — that takes its store by parameter and runs against
 * a map, which is the whole point. This file is about the two things only a filesystem can answer:
 * an id that may not become a path, and a file that does not parse.</p>
 */

function inATempDir(): { readonly dir: string; readonly dispose: () => void } {
  const dir = mkdtempSync(join(tmpdir(), 'coai-deletions-'));

  return { dir, dispose: () => rmSync(dir, { recursive: true, force: true }) };
}

const ONE: Tombstone = {
  roleId: 'Role2',
  name: 'Role 2',
  promptIds: ['role2-general'],
  askedAt: '2026-09-18T12:00:00.000Z',
  nonce: 'n1',
  reason: '',
  failedAt: '',
};

test('a tombstone written comes back, and clearing it is the end of it', async () => {
  const held = inATempDir();
  try {
    const store = tombstonesIn(() => held.dir);

    await store.put(ONE);

    assert.deepEqual(await store.read('Role2'), ONE);
    assert.deepEqual(await store.all(), [ONE]);

    await store.drop('Role2');

    assert.equal(await store.read('Role2'), undefined);
    assert.deepEqual(await store.all(), []);
    // And dropping what is already gone is not an error — step 5 is idempotent or the sweep is not.
    await store.drop('Role2');
  } finally {
    held.dispose();
  }
});

test('an id that may not become a path is REFUSED rather than sanitised', () => {
  // Nothing rather than a cleaned-up path, the answer `promptFile` gives and for the same reason: an
  // id this refuses came from somewhere it should not have, and quietly writing `....escaped.json`
  // would hide that. Rewriting would be worse still — the tombstone would then be about a different
  // role than the one being deleted. (antigravity, the plan round.)
  for (const bad of ['../escape', 'a/b', 'a\\b', '.', '', '2Role', 'con', 'NUL', 'role-2', 'a:b']) {
    assert.equal(deletionFile('/data', bad), undefined, `a path was built for ${JSON.stringify(bad)}`);
  }
  // And the companion every prohibition in this repository needs: the scan still matches what is
  // sanctioned, or it has stopped checking anything at all.
  assert.equal(deletionFile('/data', 'Role2'), '/data/deletions/Role2.json');
  assert.equal(deletionFile('/data', 'A_role_9'), '/data/deletions/A_role_9.json');
});

test('a file that does not parse strands ONE deletion, never all of them', async () => {
  const held = inATempDir();
  try {
    const store = tombstonesIn(() => held.dir);

    await store.put(ONE);
    mkdirSync(deletionsDir(held.dir), { recursive: true });
    writeFileSync(join(deletionsDir(held.dir), 'Broken.json'), '{ not json', 'utf8');
    writeFileSync(join(deletionsDir(held.dir), 'NoNonce.json'), '{"roleId":"NoNonce"}', 'utf8');
    writeFileSync(join(deletionsDir(held.dir), 'notes.txt'), 'nothing to do with this', 'utf8');

    assert.deepEqual((await store.all()).map((one) => one.roleId), ['Role2'],
      'one unreadable entry took the readable ones with it, so four deletions wait on a fifth');
  } finally {
    held.dispose();
  }
});

test('a claim is won by exactly one, and refused for a nonce that has moved on', async () => {
  // The rename is the only thing a filesystem does atomically, and it is what stands between two
  // windows and a deleted role's text. A read followed by a delete is two operations with an await
  // between them; both windows pass the read. (codex, the code round, Blocking.)
  const held = inATempDir();
  try {
    const store = tombstonesIn(() => held.dir);

    await store.put(ONE);

    assert.equal(await store.claim('Role2', 'n1'), true, 'the first claimant did not get it');
    assert.equal(await store.claim('Role2', 'n1'), false, 'a second window claimed it as well');
    // And the claimed deletion is still outstanding, because a window that dies holding a claim
    // leaves work that has to finish.
    assert.deepEqual((await store.all()).map((one) => one.roleId), ['Role2']);
    assert.deepEqual([...await store.reservedIds()], ['role2']);

    await store.drop('Role2');
    assert.deepEqual(await store.all(), [], 'the claim outlived the deletion it belonged to');
  } finally {
    held.dispose();
  }
});

test('a claim is refused when the id has come back under a NEW deletion', async () => {
  const held = inATempDir();
  try {
    const store = tombstonesIn(() => held.dir);

    await store.put({ ...ONE, nonce: 'LATER' });

    assert.equal(await store.claim('Role2', 'n1'), false,
      'a stale worker took a claim on a deletion that is not the one it read');
  } finally {
    held.dispose();
  }
});

test('an unreadable tombstone still RESERVES its id', async () => {
  // Releasing it would hand the id to a new role that then inherits the old one's prompt files —
  // and the next successful read would have the startup sweep prune the replacement. A name is
  // readable when a body is not. (codex, the code round.)
  const held = inATempDir();
  try {
    const store = tombstonesIn(() => held.dir);

    await store.put(ONE);
    fs.writeFileSync(join(deletionsDir(held.dir), 'Broken.json'), '{ not json', 'utf8');

    assert.deepEqual([...await store.reservedIds()].sort(), ['broken', 'role2'],
      'an id whose deletion cannot be read was handed back to the next role of that name');
    assert.deepEqual((await store.all()).map((one) => one.roleId), ['Role2'],
      'and the unreadable one was offered as a tombstone anybody could act on');
  } finally {
    held.dispose();
  }
});

test('a directory that is not there yet reads as no deletions, not as a failure', async () => {
  const held = inATempDir();
  try {
    // The first run of every installation. A throw here would make activation fail over a folder
    // that is empty by definition.
    assert.deepEqual(await tombstonesIn(() => join(held.dir, 'nothing')).all(), []);
  } finally {
    held.dispose();
  }
});

test('the data directory is read per call, because it can MOVE while the window is open', async () => {
  const first = inATempDir();
  const second = inATempDir();
  try {
    let where = first.dir;
    const store = tombstonesIn(() => where);

    await store.put(ONE);
    where = second.dir;

    assert.deepEqual(await store.all(), [],
      'the store kept the old directory, so a deletion would be written where nobody reads');

    await store.put(ONE);
    where = first.dir;
    assert.deepEqual((await store.all()).map((one) => one.roleId), ['Role2']);
  } finally {
    first.dispose();
    second.dispose();
  }
});
