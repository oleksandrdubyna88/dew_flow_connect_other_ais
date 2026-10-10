import assert from 'node:assert/strict';
import { test } from 'node:test';

import { PersonListing, ServerResult, Usage } from '../teamServerApi';
import { Prepared, TeamUsageCache, UsageTarget, UsageWant } from '../teamUsageCache';

/**
 * The people listing (`GET /api/people`) through the one-owner cache (todo/PLAN_team_usage_by_person.md, story 3.1):
 * asked only of an admin, kept apart from the spending, and forgotten with the company figures on any refusal.
 */

const SERVER = { id: 'acme', url: 'https://acme.example.com' };
const ALICE: PersonListing = { email: 'alice@example.com', displayName: 'Alice Smith', lastUsedUtc: '2026-10-10T09:00:00Z' };

type Answer<T> = (result: ServerResult<T>) => void;

function rig(): {
  cache: TeamUsageCache;
  listings: Answer<readonly PersonListing[]>[];
  usages: { scope: string; window: string; answer: Answer<Usage> }[];
} {
  const listings: Answer<readonly PersonListing[]>[] = [];
  const usages: { scope: string; window: string; answer: Answer<Usage> }[] = [];
  const cache = new TeamUsageCache({
    now: () => 1_000_000,
    fetchUsage: (_url, _token, window, scope) => new Promise((resolve) => { usages.push({ scope, window, answer: resolve }); }),
    fetchPeople: () => new Promise((resolve) => { listings.push(resolve); }),
    changed: () => undefined,
  });

  return { cache, listings, usages };
}

function target(wants: readonly UsageWant[], prepared: Partial<Prepared> = {}): UsageTarget {
  return { server: SERVER, wants, prepare: async () => ({ token: 't', admin: true, account: 'admin@example.com', ...prepared }) };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

const PEOPLE: UsageWant = { scope: 'people', window: '' };
const COMPANY: UsageWant = { scope: 'company', window: 'week' };

test('an admin is asked for who is signed in, and the answer is kept apart from the spending', async () => {
  const { cache, listings } = rig();

  const pending = cache.refresh([target([PEOPLE])]);
  await settle();
  assert.equal(listings.length, 1, 'the tab wants the people listing and nobody asked the server for it');
  listings[0]!({ ok: true, status: 200, contract: 2, value: [ALICE] });
  await pending;

  assert.deepEqual(cache.cell(SERVER.id, 'people', '')?.people, [ALICE]);
  assert.equal(cache.cell(SERVER.id, 'people', '')?.usage, undefined);
});

test('a non-admin is never asked for the people listing', async () => {
  const { cache, listings } = rig();

  const pending = cache.refresh([target([PEOPLE], { admin: false })]);
  await settle();

  assert.equal(listings.length, 0, 'a caller the catalog says is no admin was asked who is signed in');
  for (const answer of listings) { answer({ ok: true, status: 200, contract: 2, value: [] }); }
  await pending;
});

test('a 403 on the listing forgets the company figures too, and the other way round', async () => {
  const one = rig();
  const both = one.cache.refresh([target([PEOPLE, COMPANY])]);
  await settle();
  one.usages[0]!.answer({ ok: true, status: 200, contract: 2, value: { window: 'week', vendors: [] } });
  one.listings[0]!({ ok: true, status: 200, contract: 2, value: [ALICE] });
  await both;
  const again = one.cache.refresh([target([PEOPLE])], true);
  await settle();
  one.listings[1]!({ ok: false, status: 403, contract: 2, message: '/api/people is for admins.' });
  await again;

  assert.equal(one.cache.cell(SERVER.id, 'company', 'week')?.usage, undefined,
    'the listing was refused and the company figures stayed');
  assert.equal(one.cache.cell(SERVER.id, 'people', '')?.people, undefined);
  assert.equal(one.cache.cell(SERVER.id, 'people', '')?.refused, true);

  const two = rig();
  const first = two.cache.refresh([target([PEOPLE, COMPANY])]);
  await settle();
  two.usages[0]!.answer({ ok: true, status: 200, contract: 2, value: { window: 'week', vendors: [] } });
  two.listings[0]!({ ok: true, status: 200, contract: 2, value: [ALICE] });
  await first;
  const refused = two.cache.refresh([target([COMPANY])], true);
  await settle();
  two.usages[1]!.answer({ ok: false, status: 403, contract: 2, message: 'scope=company is for admins.' });
  await refused;

  assert.equal(two.cache.cell(SERVER.id, 'people', '')?.people, undefined,
    'the company view was refused and the list of who is signed in stayed on screen');
});

test('an older server that has no listing says why, and keeps nothing', async () => {
  const { cache, listings } = rig();

  const pending = cache.refresh([target([PEOPLE])]);
  await settle();
  listings[0]!({ ok: false, status: 404, contract: 2, message: 'the server answered 404' });
  await pending;

  const cell = cache.cell(SERVER.id, 'people', '');
  assert.equal(cell?.people, undefined);
  assert.equal(cell?.refused, false, 'a server too old for the listing is not a refusal of the caller');
  assert.equal(cell?.problem, 'the server answered 404');
});
