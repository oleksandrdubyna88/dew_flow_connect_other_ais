import assert from 'node:assert/strict';
import { test } from 'node:test';

import { TeamTabHost } from '../teamTabHost';
import { TeamUsageCache } from '../teamUsageCache';
import { Ticker } from '../teamUsagePoll';

/**
 * Which Team-server figures are asked for, as the Review rounds page opens, shows its Team server tab, goes behind
 * another editor tab and closes — the real host model (`teamTabHost.ts`) driving the real cache, over a request the
 * test answers and a clock it turns by hand. No sidebar anywhere: what asks is the page alone, or nothing.
 */

const SERVER = { id: 'acme', url: 'https://acme.example.com' };

function handTurned(): { ticker: Ticker; turn: () => void; running: () => number } {
  const live = new Map<number, () => void>();
  let next = 0;

  return {
    ticker: { every: (fn) => { next += 1; live.set(next, fn); return next; }, stop: (handle) => { live.delete(handle as number); } },
    turn: () => { for (const fn of [...live.values()]) { fn(); } },
    running: () => live.size,
  };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 20; i++) {
    await Promise.resolve();
  }
}

/** The page, the host model, the cache and the clock, wired the way the panel wires them. */
function rig(): { host: TeamTabHost; asked: string[]; turn: () => void; running: () => number; later: () => void } {
  const clock = { now: 1_000_000 };
  const asked: string[] = [];
  const cache = new TeamUsageCache({
    now: () => clock.now,
    fetchUsage: async (_url, _token, window, scope) => {
      asked.push(`${scope}/${window}`);

      return { ok: true, status: 200, contract: 2, value: { window, vendors: [] } };
    },
    changed: () => undefined,
  });
  const { ticker, turn, running } = handTurned();
  const host: TeamTabHost = new TeamTabHost({
    refresh: () => cache.refresh([{
      server: SERVER,
      wants: host.wants(SERVER.id, 'today', [SERVER.id]),
      prepare: async () => ({ token: 't', admin: true, account: 'admin@example.com' }),
    }]),
    ticker,
    everyMs: 60_000,
  });

  return { host, asked, turn, running, later: () => { clock.now += 61_000; } };
}

test('ThePageRefreshesTeamUsage_WithTheSidebarClosed: an opened page asks at once, and every minute after', async () => {
  const { host, asked, turn, later } = rig();

  const close = host.opened();
  await settle();
  assert.deepEqual(asked, ['me/today'], 'the page opened with the sidebar closed and asked nothing');
  later();
  turn();
  await settle();
  close();

  assert.deepEqual(asked, ['me/today', 'me/today']);
});

test('company figures are asked only while the Team server tab is showing on a page in front', async () => {
  const { host, asked, turn, later } = rig();
  const close = host.opened();
  await settle();

  host.tabShown(true);
  later();
  turn();
  await settle();
  assert.ok(asked.includes('company/week'), `the tab is showing and nobody asked for the company: ${asked.join(', ')}`);

  host.visible(false);
  later();
  turn();
  await settle();
  assert.ok(asked.slice(-1).every((one) => one.startsWith('me/')),
    'the page went behind another tab and the server was still asked to parse its whole ledger for it');

  // Back in front, the page is still showing the Team server tab — nothing on it changed, so nothing was posted.
  host.visible(true);
  later();
  turn();
  await settle();
  close();

  assert.ok(asked.slice(-2).includes('company/week'), `the page came back to the front and its tab was forgotten: ${asked.join(', ')}`);
});

test('a closed page asks for nothing — not on its clock, and not through anything else that refreshes', async () => {
  const { host, running } = rig();
  const close = host.opened();
  await settle();
  host.tabShown(true);

  close();

  assert.equal(running(), 0, 'the page closed and its clock kept ticking');
  assert.deepEqual(host.wants(SERVER.id, 'today', [SERVER.id]).map((one) => one.scope), ['me'],
    'the page is closed, yet every sidebar refresh would go on asking for every colleague\'s spending');
});

test('a reopened page starts on Rounds: no company until its Team server tab is shown again', async () => {
  const { host } = rig();
  host.opened();
  host.tabShown(true);
  host.opened()();

  host.opened();

  assert.deepEqual(host.wants(SERVER.id, 'today', [SERVER.id]).map((one) => one.scope), ['me'],
    'a fresh page shows Rounds, and the host still believed the Team server tab was in front');
});
