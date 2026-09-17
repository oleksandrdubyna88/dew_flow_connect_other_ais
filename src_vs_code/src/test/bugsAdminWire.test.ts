import assert from 'node:assert/strict';
import { test } from 'node:test';

import { mayCarryAKey } from '../bugsAdminApi';
import { readIssued, readKeysPage, readRevocation } from '../bugsAdminWire';

/**
 * The server's answers, READ rather than cast — and the addresses a credential may cross.
 *
 * <p>Both were code-round findings and both are about the same thing: this extension holds a
 * credential and talks to a host named by a setting, so neither the answer nor the address can be
 * taken on trust.</p>
 */

test('a page of keys is read field by field', () => {
  const read = readKeysPage({
    items: [{ id: 'aaaa1111', note: 'alice', createdUtc: 'u', lastSeenMonth: '2026-09', sent: 3, waiting: 1 }],
    limit: 50,
    total: 1,
    nextBefore: 'cursor-one',
  });

  assert.equal(read.kind, 'read');
  assert.deepEqual(read.kind === 'read' ? read.value.items[0] : undefined, {
    id: 'aaaa1111', note: 'alice', createdUtc: 'u', lastSeenMonth: '2026-09', sent: 3, waiting: 1,
  });
  assert.equal(read.kind === 'read' ? read.value.nextBefore : '', 'cursor-one');
});

/**
 * `items: null` used to crash the redraw.
 *
 * <p>The cast said it was a `KeysPage`, the page then read `.length` of nothing, and the tab stayed
 * on whatever it had been showing — a failure with no message anywhere.</p>
 */
test('a page whose items are not a list is malformed, not a crash', () => {
  for (const body of [{ items: null }, { items: 'nope' }, {}, null, 'a string', 42]) {
    assert.equal(readKeysPage(body).kind, 'malformed', `${JSON.stringify(body)} is not a page`);
  }
});

test('a row without an id is malformed — a key that cannot be named cannot be revoked', () => {
  assert.equal(readKeysPage({ items: [{ note: 'alice' }] }).kind, 'malformed');
  assert.equal(readKeysPage({ items: [{ id: '' }] }).kind, 'malformed');
});

/** Absent stays ABSENT: the month means "never used" and the promise says that word. */
test('a row with no month and no revocation keeps both absent rather than empty', () => {
  const read = readKeysPage({ items: [{ id: 'a', note: '', createdUtc: '', sent: 0, waiting: 0 }] });
  const row = read.kind === 'read' ? read.value.items[0]! : undefined;

  assert.ok(row !== undefined);
  assert.equal('lastSeenMonth' in row, false, 'absent, not an empty string');
  assert.equal('revokedUtc' in row, false);
});

test('an empty month or revocation from the server is treated as absent', () => {
  const read = readKeysPage({
    items: [{ id: 'a', note: '', createdUtc: '', lastSeenMonth: '', revokedUtc: '', sent: 0, waiting: 0 }],
  });
  const row = read.kind === 'read' ? read.value.items[0]! : undefined;

  assert.equal(row !== undefined && 'lastSeenMonth' in row, false);
  assert.equal(row !== undefined && 'revokedUtc' in row, false);
});

test('a count that is not a number is zero rather than undefined arithmetic', () => {
  const read = readKeysPage({ items: [{ id: 'a', sent: 'three', waiting: null }], total: 'many' });

  assert.equal(read.kind === 'read' ? read.value.items[0]?.sent : -1, 0);
  assert.equal(read.kind === 'read' ? read.value.total : -1, 0);
});

/**
 * THE one that matters most: an issuance without a key.
 *
 * <p>The cast let it through, `holdIssuance` wrote it, and reading it back rejected it — so the
 * only copy of a committed credential was destroyed by the code meant to preserve it.</p>
 */
test('an issuance that carries no key is malformed rather than stored', () => {
  assert.equal(readIssued({ id: 'aaaa1111', note: 'n', createdUtc: 'u' }).kind, 'malformed');
  assert.equal(readIssued({ id: 'aaaa1111', key: '', note: 'n' }).kind, 'malformed');
  assert.equal(readIssued({ key: 'k' }).kind, 'malformed', 'nor one that cannot be named');
  assert.equal(readIssued({ id: 'aaaa1111', key: 'k' }).kind, 'read');
});

test('a malformed answer says the server may still have acted', () => {
  const read = readIssued({});

  assert.match(read.kind === 'malformed' ? read.why : '', /may still\s+have acted/u);
  assert.match(read.kind === 'malformed' ? read.why : '', /check the listing/u);
});

/** A missing `changed` reads as "it changed": the safe reading is that something happened. */
test('a revocation without a changed flag is read as having changed', () => {
  const read = readRevocation({ id: 'a', revokedUtc: 'u' });

  assert.equal(read.kind === 'read' ? read.value.changed : undefined, true);
});

test('a revocation that says changed false is read as false', () => {
  const read = readRevocation({ id: 'a', revokedUtc: 'u', changed: false });

  assert.equal(read.kind === 'read' ? read.value.changed : undefined, false);
});

/**
 * Where the admin key may go.
 *
 * <p>The address is a SETTING — edited by a button, stored in `settings.json`, synced between
 * machines — so the host this extension sends a credential to can change without anybody typing it
 * here. Over plain `http` to a real host that key crosses the network in clear text.</p>
 */
test('https is allowed', () => {
  assert.equal(mayCarryAKey('https://bugs.remsoft.dev'), '');
  assert.equal(mayCarryAKey('https://bugs.remsoft.dev/'), '', 'a trailing slash is not a difference');
});

test('plain http to a real host is refused, and the refusal says why', () => {
  const why = mayCarryAKey('http://bugs.remsoft.dev');

  assert.match(why, /will not be sent/u);
  assert.match(why, /clear text/u);
});

/** Loopback over http is how the server is run while it is being written, and nothing leaves. */
test('http to loopback is allowed', () => {
  for (const address of ['http://127.0.0.1:5080', 'http://localhost:5080', 'http://[::1]:5080']) {
    assert.equal(mayCarryAKey(address), '', `${address} never leaves the machine`);
  }
});

test('something that is not an address at all is refused before anything is sent', () => {
  assert.match(mayCarryAKey(''), /not an address/u);
  assert.match(mayCarryAKey('bugs.remsoft.dev'), /not an address/u, 'no scheme is not an address');
});

/** A scheme that is neither is refused rather than assumed to be safe. */
test('a stranger scheme is refused', () => {
  assert.match(mayCarryAKey('ftp://bugs.remsoft.dev'), /will not be sent/u);
  assert.match(mayCarryAKey('file:///etc/passwd'), /will not be sent/u);
});
