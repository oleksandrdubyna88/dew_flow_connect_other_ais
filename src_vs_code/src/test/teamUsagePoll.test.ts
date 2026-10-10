import assert from 'node:assert/strict';
import { test } from 'node:test';

import { TeamUsageCache, USAGE_FRESH_MS } from '../teamUsageCache';
import { pollWhileOpen, Ticker } from '../teamUsagePoll';

/** A ticker the test turns by hand: `turn()` is one interval passing. */
function handTurned(): { ticker: Ticker; turn: () => void; running: () => number } {
  const live = new Map<number, () => void>();
  let next = 0;

  return {
    ticker: {
      every: (fn) => { next += 1; live.set(next, fn); return next; },
      stop: (handle) => { live.delete(handle as number); },
    },
    turn: () => { for (const fn of [...live.values()]) { fn(); } },
    running: () => live.size,
  };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

test('ThePageRefreshesTeamUsage_WithTheSidebarClosed: the open page asks the server on its own clock', async () => {
  // No sidebar anywhere in this test: the only thing that can ask is the page's own interval.
  const clock = { now: 1_000_000 };
  const asked: string[] = [];
  const cache = new TeamUsageCache({
    now: () => clock.now,
    fetchUsage: async (_url, _token, window) => {
      asked.push(window);

      return { ok: true, status: 200, contract: 2, value: { window, vendors: [] } };
    },
    changed: () => undefined,
  });
  const target = {
    server: { id: 'acme', url: 'https://coai.example.com' },
    wants: [{ scope: 'me' as const, window: 'today' }],
    prepare: async () => ({ token: 't', admin: false, account: 'a@example.com' }),
  };
  const { ticker, turn } = handTurned();

  const stop = pollWhileOpen(() => cache.refresh([target]), USAGE_FRESH_MS, ticker);
  turn();
  await settle();
  clock.now += USAGE_FRESH_MS + 1;
  turn();
  await settle();
  stop();

  assert.deepEqual(asked, ['today', 'today'],
    'the page was open for two minutes with the sidebar closed, and the Team server was never asked');
});

test('closing the page stops its clock', () => {
  const { ticker, running } = handTurned();

  const stop = pollWhileOpen(async () => undefined, 60_000, ticker);
  assert.equal(running(), 1, 'the page started no clock');
  stop();

  assert.equal(running(), 0, 'the page closed and its clock kept ticking');
});

test('a tick still running when the next comes is not doubled, and a failed tick is said, not thrown', async () => {
  const { ticker, turn } = handTurned();
  let started = 0;
  let finish: () => void = () => undefined;
  const warned: string[] = [];

  const stop = pollWhileOpen(() => {
    started += 1;
    if (started === 1) {
      return new Promise<void>((resolve) => { finish = resolve; });
    }

    return Promise.reject(new Error('down'));
  }, 60_000, ticker, (message) => { warned.push(message); });
  turn();
  turn();
  assert.equal(started, 1, 'a slow server was asked twice at once');
  finish();
  await settle();
  turn();
  await settle();
  stop();

  assert.equal(started, 2);
  assert.equal(warned.length, 1, 'a tick that failed vanished without a word');
});

test('an opened page asks at once, not a minute later', () => {
  const { ticker } = handTurned();
  let ticks = 0;

  const stop = pollWhileOpen(async () => { ticks += 1; }, 60_000, ticker);
  stop();

  assert.equal(ticks, 1, 'the page opened with the sidebar closed and asked nothing for a minute');
});
