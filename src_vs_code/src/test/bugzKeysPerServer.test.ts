import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  adminKey,
  adoptLegacyKeys,
  contributorKey,
  credentialsFor,
  discardLegacyKeys,
  keyName,
  legacyKeysHeld,
  Secrets,
  setAdminKey,
  setContributorKey,
} from '../bugsAdminKey';

/**
 * A Bugz key is filed under the server that issued it (research/PLAN_bugz_keys_per_server.md).
 *
 * <p>`coai.bugzServer` is a per-side setting, and the keys were stored under two fixed names — so with two sides
 * naming two servers, the one admin key was sent to whichever server a side named, and the host that received a key
 * it never issued could replay it against the one that did. The old keys are never filed automatically: nothing proves
 * which server issued them, and filing one under the wrong server is the replay itself (plan round, session
 * d5cdb1b2). The person adopts them for a server, or discards them.</p>
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

test('the credentials for a request carry the key of the very server the request goes to', async () => {
  const store = new Store();
  await setAdminKey(store, PROD, 'prod-admin');

  assert.deepEqual(await credentialsFor(store, PROD), { server: PROD, key: 'prod-admin' });
  assert.deepEqual(await credentialsFor(store, STAGING), { server: STAGING, key: '' });
});

test('the names keys are filed under are pinned, so a change to the normaliser cannot orphan a stored key', () => {
  assert.equal(keyName('admin', PROD), 'coai.bugs.adminKey:https://bugs.example.com');
  assert.equal(keyName('contributor', 'HTTPS://Bugs.Example.com:443/'), 'coai.bugs.contributorKey:https://bugs.example.com');
  assert.equal(keyName('admin', 'https://bugs.example.com:8443/api/'), 'coai.bugs.adminKey:https://bugs.example.com:8443/api');
  assert.equal(keyName('admin', '   '), '');
  assert.doesNotThrow(() => keyName('admin', 'not a url at all'));
});

test('with no server nothing is stored, and the setter says so rather than pretending', async () => {
  const store = new Store();

  assert.equal(await setAdminKey(store, '  ', 'orphan'), 'no-server');
  assert.equal(await setContributorKey(store, '', 'orphan'), 'no-server');
  assert.equal(store.held.size, 0);
  assert.equal(await setAdminKey(store, PROD, 'k'), 'stored');
  assert.equal(await setAdminKey(store, PROD, '  '), 'cleared');
});

test('old fixed-name keys are held aside, read for no server at all', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');

  assert.equal(await legacyKeysHeld(store), true);
  assert.equal(await adminKey(store, PROD), '', 'never filed by guessing which server issued them');
});

test('adopting files the old keys under the server the person named, read back before the old names go', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');
  store.held.set('coai.bugs.contributorKey', 'old-contributor');

  assert.equal(await adoptLegacyKeys(store, PROD), 'adopted');
  assert.equal(await adminKey(store, PROD), 'old-admin');
  assert.equal(await contributorKey(store, PROD), 'old-contributor');
  assert.equal(await legacyKeysHeld(store), false);
  assert.equal(await adoptLegacyKeys(store, PROD), 'none', 'twice is once');
});

test('adopting never overwrites a key already filed for that server', async () => {
  const store = new Store();
  await setAdminKey(store, PROD, 'new-admin');
  store.held.set('coai.bugs.adminKey', 'old-admin');

  await adoptLegacyKeys(store, PROD);

  assert.equal(await adminKey(store, PROD), 'new-admin');
  assert.equal(await legacyKeysHeld(store), false);
});

test('adopting with no server keeps the old keys exactly where they are', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');

  assert.equal(await adoptLegacyKeys(store, ''), 'no-server');
  assert.equal(store.held.get('coai.bugs.adminKey'), 'old-admin');
});

test('a write that does not read back leaves the old key in place', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');
  const lossy: Secrets = { get: (k) => store.get(k), store: () => Promise.resolve(), delete: (k) => store.delete(k) };

  assert.equal(await adoptLegacyKeys(lossy, PROD), 'not-kept');
  assert.equal(store.held.get('coai.bugs.adminKey'), 'old-admin', 'deleted only once the new one is proven held');
});

test('discarding removes the old keys and files nothing', async () => {
  const store = new Store();
  store.held.set('coai.bugs.adminKey', 'old-admin');
  store.held.set('coai.bugs.contributorKey', 'old-contributor');

  await discardLegacyKeys(store);

  assert.equal(store.held.size, 0);
});

test('the old fixed names appear in the key module alone — no other file reads a key around the server', () => {
  const src = path.join(__dirname, '..', '..', 'src');
  const files = fs.readdirSync(src).filter((name) => name.endsWith('.ts') && name !== 'bugsAdminKey.ts');
  const OLD = /['"\x60]coai\.bugs\.(?:adminKey|contributorKey)['"\x60:]/u;

  assert.match("const k = 'coai.bugs.adminKey';", OLD, 'the scan would miss the plain spelling');
  assert.deepEqual(files.filter((name) => OLD.test(fs.readFileSync(path.join(src, name), 'utf8'))), []);
});
