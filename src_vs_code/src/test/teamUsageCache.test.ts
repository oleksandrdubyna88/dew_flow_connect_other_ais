import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ServerResult, Usage } from '../teamServerApi';
import { Prepared, TeamUsageCache, UsageScope, UsageTarget, UsageWant } from '../teamUsageCache';

/**
 * The Team-server usage cache, run (todo/PLAN_team_usage_by_person.md, story 1.1).
 *
 * <p>Every test here drives the cache the way the panel does — `refresh` with the servers and what the surfaces want of
 * them — over a stubbed request whose answers land when the TEST says so. That is the only way to put two answers out
 * of order, which is what most of these defects are.</p>
 */

/** One request the cache made, and the means to answer it. */
interface Call {
  readonly window: string;
  readonly scope: UsageScope;
  readonly answer: (result: ServerResult<Usage>) => void;
}

const SERVER = { id: 'acme', url: 'https://coai.example.com' };

/** A body as the server answers it, told apart by its one vendor's run count. */
function body(window: string, runs: number): Usage {
  return { window, vendors: [{ vendor: 'codex', runs, failed: 0, tokensIn: runs * 10, tokensOut: runs, seconds: 1 }] };
}

function ok(value: Usage): ServerResult<Usage> {
  return { ok: true, status: 200, contract: 2, value };
}

function refused(status: number): ServerResult<Usage> {
  return { ok: false, status, contract: 2, message: `the server answered ${status}` };
}

/** A cache over a clock and a request the test drives by hand. */
function rig(_wasAdmin = false): {
  cache: TeamUsageCache; calls: Call[]; changed: () => number; clock: { now: number };
} {
  const calls: Call[] = [];
  const clock = { now: 1_000_000 };
  let changes = 0;
  const cache = new TeamUsageCache({
    now: () => clock.now,
    fetchUsage: (_url, _token, window, scope) => new Promise((resolve) => {
      calls.push({ window, scope, answer: resolve });
    }),
    changed: () => { changes += 1; },
  });

  return { cache, calls, changed: () => changes, clock };
}

function target(wants: readonly UsageWant[], prepared: Partial<Prepared> = {}): UsageTarget {
  return {
    server: SERVER,
    wants,
    prepare: async () => ({ token: 't0ken', admin: false, account: 'me@example.com', ...prepared }),
  };
}

const me = (window: string): UsageWant => ({ scope: 'me', window });
const company = (window: string): UsageWant => ({ scope: 'company', window });

/** Let every settled promise run its continuations — the requests are stubbed, so nothing real is awaited. */
async function settle(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

function runsShown(cache: TeamUsageCache, scope: UsageScope, window: string): number | undefined {
  return cache.cell(SERVER.id, scope, window)?.usage?.vendors[0]?.runs;
}

test('AWindowPress_ReasksTheServer: a different window is asked at once, however fresh the last answer is', async () => {
  const { cache, calls } = rig();

  const first = cache.refresh([target([me('today')])]);
  await settle();
  calls[0]!.answer(ok(body('today', 1)));
  await first;

  const pressed = cache.refresh([target([me('year')])]);
  await settle();

  assert.deepEqual(calls.map((one) => one.window), ['today', 'year'],
    'the person chose Year and the server was never asked for it — the page went on showing Today\'s answer');
  calls[1]?.answer(ok(body('year', 2)));
  await pressed;
});

test('TodayThenYear_DuringARefresh: a selection changed while a refresh is running schedules its own request', async () => {
  const { cache, calls } = rig();

  const slow = cache.refresh([target([me('today')])]);
  await settle();
  const pressed = cache.refresh([target([me('year')])]);
  await settle();

  assert.deepEqual(calls.map((one) => one.window), ['today', 'year'],
    'Year was pressed while Today was still being asked, and it was dropped on the floor');
  calls[0]!.answer(ok(body('today', 1)));
  calls[1]?.answer(ok(body('year', 2)));
  await Promise.all([slow, pressed]);
});

test('AnAnswerForAnOldWindow_IsDropped: Today landing after Year never shows under Year', async () => {
  const { cache, calls } = rig();

  const slow = cache.refresh([target([me('today')])]);
  await settle();
  const fast = cache.refresh([target([me('year')])]);
  await settle();
  calls[1]?.answer(ok(body('year', 365)));
  await settle();
  calls[0]!.answer(ok(body('today', 1)));
  await Promise.all([slow, fast]);

  assert.equal(runsShown(cache, 'me', 'year'), 365, 'Year shows the answer to an older question about Today');
  assert.equal(runsShown(cache, 'me', 'today'), 1, 'and Today keeps its own');
});

test('AnAnswerForAnOldWindow_IsDropped: of two requests for ONE window, the older answer landing last is discarded', async () => {
  const { cache, calls } = rig();

  const older = cache.refresh([target([me('today')])], true);
  await settle();
  const newer = cache.refresh([target([me('today')])], true);
  await settle();
  calls[1]?.answer(ok(body('today', 2)));
  await settle();
  calls[0]!.answer(ok(body('today', 1)));
  await Promise.all([older, newer]);

  assert.equal(runsShown(cache, 'me', 'today'), 2, 'the answer to the OLDER request overwrote the newer one');
});

test('MeAndCompany_KeepTheirOwnTotals: a delayed personal answer never lands on the company figures', async () => {
  const { cache, calls } = rig(true);

  const both = cache.refresh([target([me('week'), company('week')], { admin: true })]);
  await settle();
  const companyCall = calls.find((one) => one.scope === 'company');
  const meCall = calls.find((one) => one.scope === 'me');
  assert.ok(companyCall !== undefined && meCall !== undefined,
    `an admin wanting both was asked for ${calls.map((one) => one.scope).join(', ')} only`);
  companyCall.answer(ok(body('week', 900)));
  await settle();
  meCall.answer(ok(body('week', 3)));
  await both;

  assert.equal(runsShown(cache, 'company', 'week'), 900, 'the company tab shows one person\'s totals');
  assert.equal(runsShown(cache, 'me', 'week'), 3);
});

test('company is decided by THIS refresh\'s catalog, not the one held before it', async () => {
  // The first refresh after signing in has no catalog yet: the held one says nothing, this refresh's says admin.
  const { cache, calls } = rig(false);

  const first = cache.refresh([target([company('week')], { admin: true })]);
  await settle();

  assert.deepEqual(calls.map((one) => one.scope), ['company'],
    'an admin\'s first refresh asked for their own figures because the catalog it read was the one from before');
  calls[0]?.answer(ok(body('week', 900)));
  await first;
});

test('a non-admin is never asked for company, whatever a surface wants', async () => {
  const { cache, calls } = rig(false);

  const one = cache.refresh([target([company('week')], { admin: false })]);
  await settle();

  assert.ok(calls.every((call) => call.scope !== 'company'), 'company was asked of a server that says this is no admin');
  for (const call of calls) { call.answer(ok(body('week', 1))); }
  await one;
});

test('a 403 on company EVICTS the company figures — a demoted admin keeps no colleague\'s numbers', async () => {
  const { cache, calls } = rig(true);

  const first = cache.refresh([target([company('week')], { admin: true })]);
  await settle();
  calls[0]!.answer(ok(body('week', 900)));
  await first;

  const again = cache.refresh([target([company('week')], { admin: true })], true);
  await settle();
  calls[1]?.answer(refused(403));
  await again;

  assert.equal(cache.cell(SERVER.id, 'company', 'week')?.usage, undefined,
    'the server said this account is no longer an admin, and the page kept showing everybody\'s spending');
});

test('a 401 on company evicts too', async () => {
  const { cache, calls } = rig(true);

  const first = cache.refresh([target([company('week')], { admin: true })]);
  await settle();
  calls[0]!.answer(ok(body('week', 900)));
  await first;
  const again = cache.refresh([target([company('week')], { admin: true })], true);
  await settle();
  calls[1]?.answer(refused(401));
  await again;

  assert.equal(cache.cell(SERVER.id, 'company', 'week')?.usage, undefined);
});

test('a failed PERSONAL request keeps the last good answer and says what went wrong', async () => {
  // Unchanged from before the extraction: an outage is not an eviction, and a person's own figures are theirs.
  const { cache, calls, clock } = rig();

  const first = cache.refresh([target([me('today')])]);
  await settle();
  calls[0]!.answer(ok(body('today', 4)));
  await first;
  clock.now += 61_000;
  const again = cache.refresh([target([me('today')])]);
  await settle();
  calls[1]?.answer({ ok: false, status: 0, contract: undefined, message: 'it did not answer within 10s' });
  await again;

  assert.equal(runsShown(cache, 'me', 'today'), 4);
  assert.equal(cache.cell(SERVER.id, 'me', 'today')?.problem, 'it did not answer within 10s');
});

test('an account change evicts what the last account was shown', async () => {
  const { cache, calls, clock } = rig();

  const first = cache.refresh([target([me('today')], { account: 'alice@example.com' })]);
  await settle();
  calls[0]!.answer(ok(body('today', 4)));
  await first;
  clock.now += 61_000;
  const again = cache.refresh([target([me('today')], { account: 'bob@example.com' })]);
  await settle();
  calls[1]?.answer({ ok: false, status: 0, contract: undefined, message: 'it did not answer within 10s' });
  await again;

  assert.equal(cache.cell(SERVER.id, 'me', 'today')?.usage, undefined,
    'Bob is signed in now, and the page shows Alice\'s figures under his name');
});

test('signing out or removing a server evicts every key it had', async () => {
  const { cache, calls } = rig(true);

  const first = cache.refresh([target([me('today'), company('week')], { admin: true })]);
  await settle();
  for (const call of calls) { call.answer(ok(body(call.window, 5))); }
  await first;
  cache.evictServer(SERVER.id);

  assert.equal(cache.cell(SERVER.id, 'me', 'today'), undefined);
  assert.equal(cache.cell(SERVER.id, 'company', 'week'), undefined);
});

test('an answer that lands after its server was evicted is not written back', async () => {
  const { cache, calls } = rig();

  const pending = cache.refresh([target([me('today')])]);
  await settle();
  cache.evictServer(SERVER.id);
  calls[0]!.answer(ok(body('today', 4)));
  await pending;

  assert.equal(cache.cell(SERVER.id, 'me', 'today'), undefined, 'a signed-out server came back from a request in flight');
});

test('a refresh with nothing due asks nothing and says nothing — no repaint loop', async () => {
  const { cache, calls, changed } = rig();

  const first = cache.refresh([target([me('today')])]);
  await settle();
  calls[0]!.answer(ok(body('today', 1)));
  await first;
  const told = changed();
  await cache.refresh([target([me('today')])]);

  assert.equal(calls.length, 1, 'a fresh answer was asked again');
  assert.equal(changed(), told, 'a refresh that asked nothing still told the page something changed');
});

test('each answer that lands tells the page, so it repaints without the sidebar', async () => {
  const { cache, calls, changed } = rig();

  const pending = cache.refresh([target([me('today')])]);
  await settle();
  calls[0]!.answer(ok(body('today', 1)));
  await pending;

  assert.ok(changed() > 0, 'an answer landed and nobody was told');
});

test('a key in flight says so, and stops saying so when it lands', async () => {
  const { cache, calls } = rig();

  const pending = cache.refresh([target([me('today')])]);
  await settle();
  assert.equal(cache.isAsking(SERVER.id, 'me', 'today'), true);
  calls[0]!.answer(ok(body('today', 1)));
  await pending;

  assert.equal(cache.isAsking(SERVER.id, 'me', 'today'), false);
});
