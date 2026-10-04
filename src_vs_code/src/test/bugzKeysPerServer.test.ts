import assert from 'node:assert/strict';
import { test } from 'node:test';
import { adminKey, contributorKey, keyName, migrateLegacyKeys, Secrets, setAdminKey, setContributorKey } from '../bugsAdminKey';

/**
 * A Bugz key is filed under the server that issued it (research/PLAN_bugz_keys_per_server.md).
 *
 * <p>`coai.bugzServer` is a per-side setting, and the keys were stored under two fixed names — so with two sides
 * naming two servers, the one admin key was sent to whichever server a side named, and the host that received a key
 * it never issued could replay it against the one that did.</p>
 */

class Store implements Secrets {
  readonly held = new Map<string, string>();

  get(key: string): Thenable<string | undefined> {
    return Promise.resolve(this.held.get(key));
  }

  store(key: string, value: string): Thenable<void> {
    this.held.set(key, value);

    return Promise.resolve();
  }

  delete(key: string): Thenable<void> {
    this.held.delete(key);

    return Promise.resolve();
  }
}

const PROD = 'https://bugs.example.com';
const STAGING = 'https://staging.example.com';

test('a key set for one server is never read for another', async () => {
  const store = new Store();
  await setAdminKey(store, PROD, 'prod-admin');
  await setContributorKey(store, PROD, 'prod-contributor');

  assert.equal(await adminKey(store, PROD), 'prod-admin');
  assert.equal(await adminKey(store, STAGING), '', 'staging must not be handed production\'s admin key');
  assert.equal(await contributorKey(store, STAGING), '');
});

test('one server spelt two ways is one server', async () => {
  const store = new Store();
  await setAdminKey(store, 'HTTPS://Bugs.Example.com:443/', 'k');

  assert.equal(await adminKey(store, PROD), 'k');
  assert.equal(keyName('admin', 'https://bugs.example.com/'), keyName('admin', PROD));
});

test('with no server there is no key, and nothing is stored', async () => {
  const store = new Store();
  await setAdminKey(store, '  ', 'orphan');

  assert.equal(store.held.size, 0);
  assert.equal(await adminKey(store, ''), '');
});

test('the old fixed-name keys move once to the server the shared setting names, and the old names go', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');
  store.held.set('coai.bugs.contributorKey', 'old-contributor');

  assert.equal(await migrateLegacyKeys(store, PROD), 'moved');
  assert.equal(await adminKey(store, PROD), 'old-admin');
  assert.equal(await contributorKey(store, PROD), 'old-contributor');
  assert.equal(store.held.has('coai.bugs.adminKey'), false);
  assert.equal(store.held.has('coai.bugs.contributorKey'), false);
  assert.equal(await migrateLegacyKeys(store, PROD), 'none', 'twice is once');
});

test('with no shared server the old keys stay where they are, sent nowhere', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');

  assert.equal(await migrateLegacyKeys(store, ''), 'no-server');
  assert.equal(store.held.get('coai.bugs.adminKey'), 'old-admin');
  assert.equal(await adminKey(store, PROD), '');
});

test('a key already filed for that server is never overwritten by an old one', async () => {
  const store = new Store();
  await setAdminKey(store, PROD, 'new-admin');
  store.held.set('coai.bugs.adminKey', 'old-admin');

  await migrateLegacyKeys(store, PROD);

  assert.equal(await adminKey(store, PROD), 'new-admin');
  assert.equal(store.held.has('coai.bugs.adminKey'), false, 'the old one is dropped once a newer one exists');
});
