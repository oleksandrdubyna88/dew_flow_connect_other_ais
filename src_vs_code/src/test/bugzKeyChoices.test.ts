import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ADOPT_PREFIX, DISCARD, NO_SERVER, revokeRefusal, settleChoice, settledSentence } from '../bugzKeyChoices';

/**
 * What the Bugz key flows offer and say, as values (research/PLAN_bugz_keys_per_server.md, the code round of coai
 * session d5cdb1b2): nothing destructive a click away from a toast, adoption only under a server the person can read,
 * every outcome said, and no revoke against a server that did not issue the key.
 */

test('with a server the choice offers to adopt them for it, spelt as they will be filed, and to discard them', () => {
  const choice = settleChoice('HTTPS://Bugs.Example.com/', true);

  assert.deepEqual(choice.actions, [`${ADOPT_PREFIX}https://bugs.example.com`, DISCARD]);
  assert.match(choice.detail, /only if https:\/\/bugs\.example\.com issued them/u);
});

test('without a server nothing can be adopted, and the choice says how to make it possible', () => {
  const choice = settleChoice('', false);

  assert.deepEqual(choice.actions, [DISCARD]);
  assert.match(choice.detail, /set the server first/u);
});

test('the choice says discarding does not revoke, and when to discard instead of adopting', () => {
  const { detail } = settleChoice('https://bugs.example.com', true);

  assert.match(detail, /does NOT revoke them/u);
  assert.match(detail, /ever named different Bugz servers, discard them/u);
});

test('every outcome is said, and the ones that leave the keys where they were are warnings', () => {
  assert.equal(settledSentence('adopted', 'https://bugs.example.com').as, 'information');
  assert.match(settledSentence('adopted', 'https://bugs.example.com').title, /filed under https:\/\/bugs\.example\.com/u);
  assert.match(settledSentence('discarded', '').title, /Revoke them on their server/u);
  assert.match(settledSentence('none', '').title, /another window/u);
  assert.equal(settledSentence('not-kept', 'https://bugs.example.com').as, 'warning');
  assert.equal(settledSentence('no-server', '').title, NO_SERVER);
});

test('a held key is revoked only on the server that issued it; a record naming none is refused', () => {
  assert.equal(revokeRefusal('https://bugs.example.com'), '');
  assert.match(revokeRefusal(''), /does not say which server issued it/u);
  assert.match(revokeRefusal('   '), /is kept/u);
});
