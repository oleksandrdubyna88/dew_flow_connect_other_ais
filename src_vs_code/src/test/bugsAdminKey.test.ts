import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  ADMIN_KEY,
  ISSUANCE_ATTEMPT,
  PENDING_ISSUANCE,
  Secrets,
  adminKey,
  beginIssuance,
  endIssuance,
  holdIssuance,
  issuanceAttempt,
  pendingIssuance,
  releaseIssuance,
  setAdminKey,
} from '../bugsAdminKey';

/**
 * Where the admin key lives, and where a key nobody has copied waits.
 *
 * <p>The pending issuance is the plan round's blocking finding made into a mechanism: a webview
 * cannot veto its own disposal, so the guarantee cannot live in a modal. These tests are about the
 * property that replaces it — the key survives whatever happens to the window, and leaves only by an
 * explicit copy or an explicit discard.</p>
 */

/** The three members of `vscode.SecretStorage`, over a map. */
class Store implements Secrets {
  private readonly held = new Map<string, string>();

  /** Every write, in order — so a test can see WHEN something was stored, not only that it was. */
  readonly writes: string[] = [];

  get(key: string): Thenable<string | undefined> {
    return Promise.resolve(this.held.get(key));
  }

  store(key: string, value: string): Thenable<void> {
    this.held.set(key, value);
    this.writes.push(`store ${key}`);

    return Promise.resolve();
  }

  delete(key: string): Thenable<void> {
    this.held.delete(key);
    this.writes.push(`delete ${key}`);

    return Promise.resolve();
  }

  /** What is in the box, for an assertion about storage itself. */
  raw(key: string): string | undefined {
    return this.held.get(key);
  }
}

const issued = {
  id: 'aaaa1111',
  key: 'the-key-itself',
  note: 'the tuesday workshop',
  createdUtc: '2026-09-17T10:00:00.0000000Z',
  // The server that ISSUED it. Carried so a discard can never be sent to one that did not.
  server: 'https://bugs.example',
};

test('with nothing stored, there is no key and no pending issuance', async () => {
  const store = new Store();

  assert.equal(await adminKey(store), '', 'absent reads as empty, never as undefined');
  assert.equal(await pendingIssuance(store), undefined);
});

test('a key is stored trimmed, because it is pasted', async () => {
  const store = new Store();

  await setAdminKey(store, '  a-key-with-spaces-around-it \n');

  assert.equal(await adminKey(store), 'a-key-with-spaces-around-it');
});

/**
 * An empty value CLEARS the key rather than storing nothing under it.
 *
 * <p>Storing `''` would leave a secret that exists and is not a key: every request would go out with
 * `Bearer ` and come back 401, and the tab would show the rejected face — which says the server did
 * not accept the key, when in fact there is no key. The no-key face is the true one.</p>
 */
test('setting an empty key removes it instead of storing nothing', async () => {
  const store = new Store();
  await setAdminKey(store, 'a-real-key');

  await setAdminKey(store, '   ');

  assert.equal(store.raw(ADMIN_KEY), undefined, 'it must be gone, not present and empty');
  assert.equal(await adminKey(store), '');
});

/** The whole point: it is written, and it is still there afterwards. */
test('a pending issuance survives being stored and read back', async () => {
  const store = new Store();

  await holdIssuance(store, issued);

  assert.deepEqual(await pendingIssuance(store), issued);
  assert.ok(store.raw(PENDING_ISSUANCE)?.includes('the-key-itself'));
});

test('a pending issuance leaves only when it is released', async () => {
  const store = new Store();
  await holdIssuance(store, issued);

  await releaseIssuance(store);

  assert.equal(await pendingIssuance(store), undefined);
  assert.deepEqual(
    store.writes,
    // Holding an issuance also clears the ATTEMPT that led to it: the attempt exists to say a key
    // may be unaccounted for, and once the key itself is held it is accounted for.
    [`store ${PENDING_ISSUANCE}`, `delete ${ISSUANCE_ATTEMPT}`, `delete ${PENDING_ISSUANCE}`],
  );
});

/**
 * The attempt is written BEFORE the request and survives everything after it.
 *
 * <p>This is the narrowed window: the server commits the key before this process hears anything, so
 * a death in that instant leaves a live key with no local record. The attempt cannot name the key —
 * nothing here can — but it is the difference between the next open saying "a key may exist, it is
 * the newest row" and saying nothing at all.</p>
 */
test('an attempt is recorded before the request and survives having no answer', async () => {
  const store = new Store();

  await beginIssuance(store, { server: 'https://bugs.example', note: 'the tuesday workshop' });

  assert.deepEqual(
    await issuanceAttempt(store),
    { server: 'https://bugs.example', note: 'the tuesday workshop' },
  );
  assert.equal(await pendingIssuance(store), undefined, 'an attempt is not a key');
});

test('an attempt is cleared when the key it was for is held', async () => {
  const store = new Store();
  await beginIssuance(store, { server: 'https://bugs.example', note: 'n' });

  await holdIssuance(store, issued);

  assert.equal(await issuanceAttempt(store), undefined, 'it is accounted for now');
  assert.deepEqual(await pendingIssuance(store), issued);
});

test('an attempt is cleared when it is explicitly ended', async () => {
  const store = new Store();
  await beginIssuance(store, { server: 'https://bugs.example', note: 'n' });

  await endIssuance(store);

  assert.equal(await issuanceAttempt(store), undefined);
});

/** The issuing server travels with the key, so a discard cannot be sent to a different one. */
test('a pending issuance remembers which server issued it', async () => {
  const store = new Store();

  await holdIssuance(store, issued);

  assert.equal((await pendingIssuance(store))?.server, 'https://bugs.example');
});

/**
 * A stored value that cannot be parsed is treated as none rather than thrown.
 *
 * <p>This is read on every open of the tab. A panel that refuses to draw because of a malformed
 * secret is worse than one that has lost track of a key — the listing can still show it, newest
 * first, which is the recovery story 2 ordered the listing for.</p>
 */
test('a pending issuance that is not readable is none, not an exception', async () => {
  const store = new Store();
  await store.store(PENDING_ISSUANCE, 'not json at all {');

  assert.equal(await pendingIssuance(store), undefined);
});

test('a pending issuance missing its key is none — half a record is not a key', async () => {
  const store = new Store();
  await store.store(PENDING_ISSUANCE, JSON.stringify({ id: 'aaaa1111' }));

  assert.equal(await pendingIssuance(store), undefined);
});

test('a pending issuance with no note still reads, because a note is optional', async () => {
  const store = new Store();
  await store.store(PENDING_ISSUANCE, JSON.stringify({ id: 'aaaa1111', key: 'k' }));

  assert.deepEqual(
    await pendingIssuance(store),
    // A record written by an older build has no `server`. It still reads, and the panel falls back
    // to the current one — losing the key over a missing field would be the worse failure.
    { id: 'aaaa1111', key: 'k', note: '', createdUtc: '', server: '' },
  );
});

test('an attempt that is not readable is none rather than an exception', async () => {
  const store = new Store();
  await store.store(ISSUANCE_ATTEMPT, 'not json {');

  assert.equal(await issuanceAttempt(store), undefined);
});

/** The key and the pending issuance are separate: clearing one must not clear the other. */
test('clearing the admin key leaves a pending issuance alone', async () => {
  const store = new Store();
  await setAdminKey(store, 'a-real-key');
  await holdIssuance(store, issued);

  await setAdminKey(store, '');

  assert.equal(await adminKey(store), '');
  assert.deepEqual(
    await pendingIssuance(store),
    issued,
    'the key that nobody copied is the one thing that must not be lost by tidying up',
  );
});
