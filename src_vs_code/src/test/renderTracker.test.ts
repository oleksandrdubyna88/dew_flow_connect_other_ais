import assert from 'node:assert/strict';
import { test } from 'node:test';

import { RenderTracker } from '../renderTracker';

/**
 * The panel's renders, numbered (research/PLAN_model_search_and_busy_marks.md §3.14, as shipped).
 *
 * <p>A write's busy mark settles once a render that STARTED after the write was queued has finished — that render read
 * the written value. The renders themselves run side by side: the first version ran them one at a time, and the E3
 * review found that a render stalled on an un-timed network fetch then held up every paint after it — the reported
 * freeze, made longer by its fix. The final E3 round added three more: a stalled render must not hold a write's mark
 * once a LATER render has carried it; a write must not start a render of its own when the configuration listener's is
 * about to start; and a render that a newer one has already painted over is superseded.</p>
 */

/** Renders that each wait until the test finishes them, in any order; the numbers they were started under. */
function gated(): {
  readonly work: (number: number) => Promise<void>;
  readonly finish: (index: number) => void;
  readonly fail: (index: number, error: Error) => void;
  readonly started: () => number;
  readonly numbers: () => readonly number[];
} {
  const runs: { resolve: () => void; reject: (error: Error) => void }[] = [];
  const numbers: number[] = [];
  const work = (number: number): Promise<void> => new Promise<void>((resolve, reject) => {
    numbers.push(number);
    runs.push({ resolve, reject });
  });

  return {
    work,
    finish: (index) => { runs[index]?.resolve(); },
    fail: (index, error) => { runs[index]?.reject(error); },
    started: () => runs.length,
    numbers: () => [...numbers],
  };
}

const settle = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });
const wait = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

test('a slow render never holds up the next one', async () => {
  // The E3 review's case: render 1 is stuck on a fetch to GitHub through a slow proxy; a model is picked meanwhile.
  const render = gated();
  const renders = new RenderTracker(render.work);
  void renders.run();
  const second = renders.run();
  await settle();
  assert.equal(render.started(), 2, 'the second render starts at once, beside the stalled one');

  render.finish(1);
  await second;
});

test('each render is handed its own number', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  void renders.run();
  void renders.run();
  await settle();

  assert.deepEqual(render.numbers(), [1, 2]);
});

test('runAfter shares a render that started after the mark, and answers at once after one finished', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  const mark = renders.mark();
  const listener = renders.run(); // the configuration listener's render, started after the write was queued
  const writer = renders.runAfter(mark);
  await settle();
  assert.equal(render.started(), 1, 'the write shares the listener’s render instead of starting a second');

  render.finish(0);
  await Promise.all([listener, writer]);
  await renders.runAfter(mark);
  assert.equal(render.started(), 1, 'a finished render that started after the mark is enough');
});

test('a write waiting on a stalled render is settled by a LATER render that carried it', async () => {
  // Final E3 round (gemini): the waiter was bound to the oldest post-mark render, so a stalled one held the mark
  // after a newer render had already painted the written value.
  const render = gated();
  const renders = new RenderTracker(render.work);
  const mark = renders.mark();
  void renders.run(); // render 1, after the mark — stalls
  let settled = false;
  const writer = renders.runAfter(mark).then(() => { settled = true; });
  void renders.run(); // render 2, after the mark too — finishes
  await settle();
  render.finish(1);
  await settle();

  assert.equal(settled, true, 'render 2 read the written value and painted it; render 1 stalling must not hold the mark');
  await writer;
});

test('runAfter does not count a render that started before the mark', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  void renders.run();
  const mark = renders.mark();
  let settled = false;
  const writer = renders.runAfter(mark).then(() => { settled = true; });
  await settle();
  assert.equal(render.started(), 2, 'a render begun before the write may have read the old value, so another starts');

  render.finish(0);
  await settle();
  assert.equal(settled, false, 'the earlier render finishing does not settle the write');
  render.finish(1);
  await writer;
});

test('runAfter with nothing after the mark starts a render', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  const waiting = renders.runAfter(renders.mark());
  await settle();

  assert.equal(render.started(), 1);
  render.finish(0);
  await waiting;
});

test('with a grace period, a render that starts within it is shared — the write starts none of its own', async () => {
  // Final E3 round (gemini): the write's settle started a render before the configuration listener's arrived, so every
  // write cost two full renders. The listener's render arrives within a turn or two; the grace waits for it.
  const render = gated();
  const renders = new RenderTracker(render.work, 50);
  const writer = renders.runAfter(renders.mark());
  await wait(5);
  assert.equal(render.started(), 0, 'nothing is started while the listener’s render may still come');
  void renders.run(); // the configuration listener
  await wait(80);

  assert.equal(render.started(), 1, 'one render, shared — not two');
  render.finish(0);
  await writer;
});

test('with a grace period, a render IS started when none comes', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work, 20);
  const writer = renders.runAfter(renders.mark());
  await wait(60);

  assert.equal(render.started(), 1, 'a write that changed nothing still settles on a render');
  render.finish(0);
  await writer;
});

test('a failed render rejects a waiting write only when no other render after its mark is still running', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  const mark = renders.mark();
  void renders.run().catch(() => undefined); // render 1 — will fail
  void renders.run(); // render 2 — still running
  let outcome = 'waiting';
  const writer = renders.runAfter(mark).then(() => { outcome = 'settled'; }, () => { outcome = 'rejected'; });
  await settle();
  render.fail(0, new Error('the ledger could not be read'));
  await settle();
  assert.equal(outcome, 'waiting', 'render 2 may still carry the write');

  render.finish(1);
  await writer;
  assert.equal(outcome, 'settled');
});

test('a failed render with nothing else running rejects the waiting write, and a later one is still counted', async () => {
  const render = gated();
  const renders = new RenderTracker(render.work);
  const mark = renders.mark();
  const failing = renders.run();
  const writer = renders.runAfter(mark);
  await settle();
  render.fail(0, new Error('the ledger could not be read'));

  await assert.rejects(failing, /the ledger could not be read/u);
  await assert.rejects(writer, /the ledger could not be read/u, 'a write waiting on it is told it failed');
  const next = renders.runAfter(mark);
  await settle();
  assert.equal(render.started(), 2, 'a failed render is not a finished one, so another is started');
  render.finish(1);
  await next;
});

test('a render a newer one has already finished is superseded, so its paint can be skipped', async () => {
  // Final E3 round (gemini, twice): renders run side by side, so a slow older render could paint its stale state
  // over the newer one's.
  const render = gated();
  const renders = new RenderTracker(render.work);
  void renders.run();
  void renders.run();
  await settle();
  assert.equal(renders.superseded(1), false);

  render.finish(1);
  await settle();
  assert.equal(renders.superseded(1), true, 'render 2 has painted; render 1 painting now would put the old state back');
  assert.equal(renders.superseded(2), false);
});

test('a render that throws before it awaits anything is still a failed render, never a wedged tracker', async () => {
  // Own review of E3: an async `execute` around a work that threw SYNCHRONOUSLY ran its `finally` before the run was
  // recorded, and left a rejected promise recorded as running for good.
  let calls = 0;
  const renders = new RenderTracker(() => {
    calls += 1;
    if (calls === 1) {
      throw new Error('thrown before any await');
    }
    return Promise.resolve();
  });

  await assert.rejects(renders.run(), /thrown before any await/u);
  await renders.run();
  await renders.runAfter(renders.mark());
  assert.equal(calls, 3, 'every later call ran');
});
