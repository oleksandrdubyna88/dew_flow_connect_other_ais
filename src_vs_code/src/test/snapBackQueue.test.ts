import assert from 'node:assert/strict';
import { test } from 'node:test';

import { afterTheWrite, saveOrSnapBack } from '../refusedWrite';
import { WriteQueue } from '../writeQueue';

/**
 * A refused write's repaint runs AFTER the write, and the queue keeps moving (the follow-up to PR #561).
 *
 * <p>Found by review on the dropdown follow-up: the snap-back awaited `render()` from inside the queued write,
 * and `render()` waits for the queue — which is that very write. The panel froze until the window was
 * reloaded, and every later setting write queued behind it and never ran. Run here against the REAL queue.</p>
 */

/** Resolves with what the queue did, or rejects when it did not settle in time — a deadlock, not a slowness. */
async function settledWithin(queue: WriteQueue, ms: number): Promise<void> {
  let timer: NodeJS.Timeout | undefined;
  const late = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`the write queue did not settle in ${ms} ms — a write is waiting on itself`)), ms);
  });
  try {
    await Promise.race([queue.settled(), late]);
  } finally {
    clearTimeout(timer);
  }
}

test('a refused dropdown repaints after its write, and the next write still runs', async () => {
  const queue = new WriteQueue();
  const happened: string[] = [];
  // The repaint the host hands in: `render()` waits for the queue before it reads anything.
  const render = async () => {
    await queue.settled();
    happened.push('painted');
  };

  queue.enqueue(async () => {
    await saveOrSnapBack(async () => false, afterTheWrite(render), 'continue', 'select');
    happened.push('refused write done');
  });
  queue.enqueue(async () => {
    happened.push('next write');
  });

  await settledWithin(queue, 1000);
  // The repaint was started, not awaited: give it the turn it waits for.
  await queue.settled();
  await new Promise((resolve) => setImmediate(resolve));

  assert.deepEqual(happened, ['refused write done', 'next write', 'painted'],
    'the repaint must come after the writes it waits for, and must not hold them up');
});

test('a write appended while the queue was being waited for is waited for too', async () => {
  // The render after a blur would otherwise read the configuration a moment before the write it followed.
  const queue = new WriteQueue();
  const happened: string[] = [];
  let release: (() => void) | undefined;
  const started = new Promise<void>((begun) => {
    queue.enqueue(() => new Promise<void>((resolve) => { release = resolve; begun(); }));
  });

  const waited = queue.settled().then(() => { happened.push('settled'); });
  await started;
  queue.enqueue(async () => { happened.push('appended write'); });
  release!();
  await settledWithin(queue, 1000);
  await waited;

  assert.deepEqual(happened, ['appended write', 'settled'], 'the wait ended on a snapshot and missed the write after it');
});

test('a repaint that fails after the write is said, never an unhandled rejection', async () => {
  // Started and not awaited, so nothing upstream can catch it — the queue has already moved on.
  const said: unknown[] = [];
  const original = console.error;
  console.error = (...args: unknown[]) => { said.push(...args); };
  try {
    await afterTheWrite(async () => { throw new Error('the view went away'); })();
    await new Promise((resolve) => setImmediate(resolve));
  } finally {
    console.error = original;
  }

  assert.ok(said.some((one) => one instanceof Error && one.message === 'the view went away'),
    'a failed repaint vanished — or became an unhandled rejection the host may die of');
});

test('the queue still runs writes in order, and a failed one does not stop the rest', async () => {
  const queue = new WriteQueue();
  const happened: string[] = [];
  queue.enqueue(async () => { happened.push('one'); });
  queue.enqueue(async () => { throw new Error('refused'); });
  queue.enqueue(async () => { happened.push('three'); });

  await settledWithin(queue, 1000);

  assert.deepEqual(happened, ['one', 'three']);
});
