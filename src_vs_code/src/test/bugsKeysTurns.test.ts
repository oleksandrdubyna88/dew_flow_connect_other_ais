import assert from 'node:assert/strict';
import { test } from 'node:test';

import { Turns } from '../bugsKeysTurns';

/**
 * One action at a time, and a page that is never left with every control disabled.
 *
 * <p>The second half is the defect this module was extracted for. The Users tab disables everything
 * while a request is in flight — Refresh included, deliberately, because a second Issue is a second
 * live key — and the panel then cleared its own flag in a `finally` with nothing painting again. So
 * the page a person was looking at stayed disabled for ever: no button on it worked, and only
 * closing the tab and opening it again helped. `bugzReviewPanel.ts` carries a comment about being
 * caught by exactly this, which is why the flag and the repaint now live in ONE place rather than in
 * every panel that has controls. (Code round 2, codex.)</p>
 */

interface Watched {
  readonly turns: Turns;
  /** Mutable, because the ACTIONS write to it too: what ran, and in which order. */
  readonly log: string[];
  readonly busyWhenStarted: readonly boolean[];
  readonly busyWhenSettled: readonly boolean[];
  readonly heard: readonly unknown[];
}

function watched(settle: () => Promise<void> = () => Promise.resolve()): Watched {
  const log: string[] = [];
  const busyWhenStarted: boolean[] = [];
  const busyWhenSettled: boolean[] = [];
  const heard: unknown[] = [];
  const turns: Turns = new Turns({
    started: async () => {
      busyWhenStarted.push(turns.busy);
      await Promise.resolve();
    },
    settled: async () => {
      busyWhenSettled.push(turns.busy);
      log.push('settled');
      await settle();
    },
    failed: async (reason) => {
      heard.push(reason);
      log.push('failed');
      await Promise.resolve();
    },
  });

  return { turns, log, busyWhenStarted, busyWhenSettled, heard };
}

/** Everything already queued, run. A turn begins in a microtask, not in the call that asks for it. */
const flush = (): Promise<void> => new Promise<void>((resolve) => { setImmediate(resolve); });

/** A promise somebody else finishes, so a test can hold an action open and look around it. */
function held(): { readonly promise: Promise<void>; finish: () => void } {
  let finish = (): void => undefined;
  const promise = new Promise<void>((resolve) => {
    finish = (): void => { resolve(); };
  });

  return { promise, finish };
}

test('a second action waits for the first to finish', async () => {
  const { turns, log } = watched();
  const first = held();

  const one = turns.run(async () => {
    log.push('first in');
    await first.promise;
    log.push('first out');
  });
  const two = turns.run(async () => {
    log.push('second in');
    await Promise.resolve();
  });

  await flush();
  assert.deepEqual(log, ['first in'], 'the second action began before the first was done');
  first.finish();
  await Promise.all([one, two]);

  assert.deepEqual(log, ['first in', 'first out', 'settled', 'second in', 'settled']);
});

test('the controls are taken at the start and given back at the end', async () => {
  const { turns, busyWhenStarted, busyWhenSettled } = watched();
  const running = held();

  const one = turns.run(async () => {
    assert.equal(turns.busy, true, 'the page is not told that anything is happening');
    await running.promise;
  });

  await flush();
  assert.equal(turns.busy, true);
  running.finish();
  await one;

  assert.deepEqual(busyWhenStarted, [true],
    'nothing said anything was happening while the action ran, so a second press queues a second one');
  assert.deepEqual(busyWhenSettled, [false], 'the repaint was told the page is still busy');
  assert.equal(turns.busy, false, 'the tab is left disabled with nothing able to press it');
});

test('an action that throws still gives the controls back, and says what happened', async () => {
  const { turns, log, heard } = watched();

  await turns.run(() => Promise.reject(new Error('the server hung up')));

  assert.deepEqual(log, ['failed', 'settled'], 'a failure leaves the page disabled for ever');
  assert.match(String(heard[0]), /the server hung up/u);
  assert.equal(turns.busy, false);
});

test('a repaint that itself fails does not end the panel', async () => {
  // Painting is a webview write and a webview can be disposed mid-flight. A coordinator whose chain
  // is poisoned by one rejection would silently never run another action.
  const { turns, log } = watched(() => Promise.reject(new Error('the webview is disposed')));

  await turns.run(() => Promise.resolve());
  await turns.run(async () => { log.push('the next action'); await Promise.resolve(); });

  assert.ok(log.includes('the next action'), 'one failed repaint stopped every later action');
});

test('a reporter that itself fails does not end the panel either', async () => {
  const log: string[] = [];
  const turns = new Turns({
    started: () => Promise.resolve(),
    settled: () => Promise.resolve(),
    failed: () => Promise.reject(new Error('the message could not be shown')),
  });

  await turns.run(() => Promise.reject(new Error('the first failure')));
  await turns.run(async () => { log.push('the next action'); await Promise.resolve(); });

  assert.deepEqual(log, ['the next action']);
});

test('what run returns is over when that action is over', async () => {
  // `show()` awaits it before revealing the tab: a reveal that overtook the first draw would show
  // an empty panel.
  const { turns, log } = watched();

  await turns.run(async () => { await Promise.resolve(); log.push('drawn'); });

  assert.deepEqual(log, ['drawn', 'settled']);
});
