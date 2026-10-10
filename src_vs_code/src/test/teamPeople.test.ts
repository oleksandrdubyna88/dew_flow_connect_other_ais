import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ModelPrice } from '../modelPrices';
import { idleList, joinPeople, lastSeen, personCard, PeopleView } from '../teamPeople';
import { PersonListing, PersonUsage, peopleFrom } from '../teamServerApi';
import { pageTree } from './pageTree';

/**
 * The people of the Team server tab (story 3.1): spending joined with who is signed in — case-insensitively, every
 * spender kept, the signed-in who spent nothing listed apart — and "last seen" at the server's one-hour resolution.
 */

const NOW = Date.UTC(2026, 9, 10, 12, 0);
const HOUR = 3_600_000;
const SOL: ModelPrice = { inPerMillion: 2, outPerMillion: 10, source: 'openrouter' };
const view: PeopleView = { serverId: 'acme', price: (model) => (model === 'gpt-5.6-sol' ? SOL : undefined), palette: () => 'var(--c)', now: NOW };

function spender(email: string, runs = 1): PersonUsage {
  return {
    email,
    vendors: [{
      vendor: 'codex', runs, failed: 0, tokensIn: 1_000_000, tokensOut: 100_000, seconds: 1,
      models: [{ model: 'gpt-5.6-sol', runs, failed: 0, tokensIn: 1_000_000, tokensOut: 100_000 }],
    }],
  };
}

function listing(email: string, displayName: string, hoursAgo: number): PersonListing {
  return { email, displayName, lastUsedUtc: new Date(NOW - hoursAgo * HOUR).toISOString() };
}

test('people are joined by email whatever its casing, and every spender is kept — named or not', () => {
  const joined = joinPeople(
    [spender('Alice@Example.com'), spender('ghost@example.com')],
    [listing('alice@example.com', 'Alice Smith', 0.5), listing('carol@example.com', 'Carol', 30)],
  );

  assert.deepEqual(joined.cards.map((one) => [one.usage.email, one.name]),
    [['Alice@Example.com', 'Alice Smith'], ['ghost@example.com', '']], 'a spender without a session was dropped, or a casing missed its name');
  assert.deepEqual(joined.idle.map((one) => one.email), ['carol@example.com'], 'the signed-in who spent nothing are the idle ones');
});

test('last seen is said at the server\'s resolution: within the hour, hours, days — and a dash for no stamp', () => {
  assert.equal(lastSeen(new Date(NOW - 0.5 * HOUR).toISOString(), NOW), 'last seen within the hour');
  assert.equal(lastSeen(new Date(NOW - 5.2 * HOUR).toISOString(), NOW), 'last seen 5 h ago');
  assert.equal(lastSeen(new Date(NOW - 75 * HOUR).toISOString(), NOW), 'last seen 3 days ago');
  assert.equal(lastSeen('', NOW), 'last seen —');
  assert.equal(lastSeen('not a time', NOW), 'last seen —');
});

test('a card carries the name, the email, the ~$ from its models, and last seen — and searches by name too', () => {
  const [card] = joinPeople([spender('alice@example.com')], [listing('Alice@example.com', 'Alice Smith', 2)]).cards;
  const tree = pageTree(personCard(card!, view));
  const details = tree.one((node) => node.tagName === 'DETAILS', 'card');

  assert.match(details.text(), /Alice Smith/);
  assert.match(details.text(), /alice@example\.com/);
  assert.match(details.text(), /~\$3\.00/, 'one million in at 2 and a hundred thousand out at 10 is ~$3.00');
  assert.match(details.text(), /last seen 2 h ago/);
  assert.equal(details.attrs['data-search'], 'alice@example.com alice smith');
  assert.equal(details.attrs['data-cost'], '3');
  assert.equal(details.attrs['data-fold'], 'acme|alice@example.com');
});

test('a hostile display name is text, on the card and in the idle list', () => {
  const evil = '<img src="x" onerror="alert(1)">';
  const [card] = joinPeople([spender('alice@example.com')], [listing('alice@example.com', evil, 1)]).cards;
  const html = personCard(card!, view) + idleList('acme', [listing('bob@example.com', evil, 1)], NOW);
  const tree = pageTree(html);

  assert.equal(tree.find((node) => node.tagName === 'IMG').length, 0, 'a display name from the identity provider became an element');
  assert.ok(tree.text().includes(evil));
});

test('the idle list counts and names the signed-in who spent nothing, and says who is not in it', () => {
  const tree = pageTree(idleList('acme', [listing('bob@example.com', 'Bob', 1), listing('carol@example.com', '', 50)], NOW));

  assert.match(tree.text(), /Signed in, no recorded runs in this window: 2/);
  assert.match(tree.text(), /Bob/);
  assert.match(tree.text(), /carol@example\.com · last seen 2 days ago/);
  assert.match(tree.text(), /raw identity-provider token/);
});

test('a people listing is believed only as far as its shape', () => {
  const rows = peopleFrom([
    { email: 'a@example.com', displayName: 'A', lastUsedUtc: '2026-10-10T10:00:00Z', token: 'never-kept' },
    { displayName: 'no email' },
    'a string',
    null,
    { email: 'b@example.com' },
  ]);

  assert.deepEqual(rows, [
    { email: 'a@example.com', displayName: 'A', lastUsedUtc: '2026-10-10T10:00:00Z' },
    { email: 'b@example.com', displayName: '', lastUsedUtc: '' },
  ]);
  assert.deepEqual(peopleFrom({ not: 'a list' }), []);
});
