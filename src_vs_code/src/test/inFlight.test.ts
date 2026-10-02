import assert from 'node:assert/strict';
import { test } from 'node:test';

import { askOf, IDLE, InFlight, settleAfterWrite, settleEverything, tracked } from '../inFlight';

/**
 * What the pages posted and the host has not finished (research/PLAN_model_search_and_busy_marks.md §3.8–§3.10).
 *
 * <p>The page's busy mark is drawn from this: an entry per operation, removed in a `finally` however the work ended,
 * and every change announced to every page — so a mark can neither stick on finished work nor vanish from work still
 * running.</p>
 */

/** A page slot as far as tracking uses one: somewhere to post, and a record of what was posted. */
class Slot {
  readonly posted: Record<string, unknown>[] = [];

  constructor(readonly name: string) {}

  post(message: unknown): void {
    this.posted.push(message as Record<string, unknown>);
  }
}

function clock(start = 1_000): { readonly now: () => number; readonly advance: (ms: number) => void } {
  let at = start;

  return { now: () => at, advance: (ms) => { at += ms; } };
}

test('an empty record is idle: nothing in flight, no age', () => {
  assert.deepEqual(new InFlight<Slot>(clock().now).snapshot(), IDLE);
  assert.deepEqual(IDLE, { count: 0, oldestMs: 0 });
});

test('the snapshot counts what is in flight and how old the OLDEST of it is', () => {
  const time = clock();
  const flight = new InFlight<Slot>(time.now);
  const sidebar = new Slot('sidebar');
  const first = flight.start({ seq: 1, doc: 'd' }, sidebar);
  time.advance(300);
  flight.start({ seq: 2, doc: 'd' }, sidebar);
  time.advance(100);

  assert.deepEqual(flight.snapshot(), { count: 2, oldestMs: 400 });
  flight.finish(first);
  assert.deepEqual(flight.snapshot(), { count: 1, oldestMs: 100 });
});

test('a tracked operation announces itself to every page, and its end to every page, and settles only its poster', async () => {
  const flight = new InFlight<Slot>(clock().now);
  const sidebar = new Slot('sidebar');
  const settings = new Slot('settings');
  let release: () => void = () => undefined;
  const running = tracked(flight, [sidebar, settings], settings, { seq: 7, doc: 'doc-a' }, () => new Promise<void>((done) => { release = done; }));

  assert.deepEqual(sidebar.posted, [{ type: 'busy', count: 1, oldestMs: 0 }], 'the start reaches the page that did not post it');
  assert.deepEqual(settings.posted, [{ type: 'busy', count: 1, oldestMs: 0 }]);
  release();
  await running;

  assert.deepEqual(settings.posted.slice(1), [{ type: 'settled', seq: 7, doc: 'doc-a', ok: true }, { type: 'busy', count: 0, oldestMs: 0 }]);
  assert.deepEqual(sidebar.posted.slice(1), [{ type: 'busy', count: 0, oldestMs: 0 }], 'only the poster hears its own seq');
  assert.deepEqual(flight.snapshot(), IDLE);
});

test('work that throws still settles — not ok — and leaves nothing behind', async () => {
  const flight = new InFlight<Slot>(clock().now);
  const page = new Slot('sidebar');
  const errors: unknown[] = [];

  await tracked(flight, [page], page, { seq: 3, doc: 'doc-b' }, () => Promise.reject(new Error('the write was refused')), (error) => { errors.push(error); });

  assert.deepEqual(page.posted.at(-2), { type: 'settled', seq: 3, doc: 'doc-b', ok: false });
  assert.deepEqual(page.posted.at(-1), { type: 'busy', count: 0, oldestMs: 0 });
  assert.equal(flight.snapshot().count, 0, 'the mark cannot stick on work that ended');
  assert.match(String(errors[0]), /the write was refused/u, 'and the failure is said, never swallowed');
});

test('ending the panel settles everything still running as not ok, and empties the record', () => {
  const flight = new InFlight<Slot>(clock().now);
  const sidebar = new Slot('sidebar');
  const settings = new Slot('settings');
  flight.start({ seq: 1, doc: 'x' }, sidebar);
  flight.start({ seq: 2, doc: 'y' }, settings);

  settleEverything(flight);

  assert.deepEqual(sidebar.posted, [{ type: 'settled', seq: 1, doc: 'x', ok: false }]);
  assert.deepEqual(settings.posted, [{ type: 'settled', seq: 2, doc: 'y', ok: false }]);
  assert.deepEqual(flight.snapshot(), IDLE);
});

test('finishing an operation twice is harmless, and the second finish answers nothing', () => {
  const flight = new InFlight<Slot>(clock().now);
  const id = flight.start({ seq: 1, doc: 'd' }, new Slot('sidebar'));

  assert.equal(flight.finish(id)?.seq, 1);
  assert.equal(flight.finish(id), undefined);
  assert.equal(flight.snapshot().count, 0, 'never below zero');
});

test('a write settles only after the queue has written it AND a render that started after the mark has finished', async () => {
  // Own review of E3: this order lived as two lines in PanelProvider, and deleting either left the suite green.
  const steps: string[] = [];
  let releaseQueue: () => void = () => undefined;
  let releaseRender: () => void = () => undefined;
  const writes = { settled: () => new Promise<void>((done) => { steps.push('queue waited'); releaseQueue = done; }) };
  let started = 4;
  const renders = {
    mark: () => { steps.push(`mark ${started}`); return started; },
    runAfter: (mark: number) => new Promise<void>((done) => { steps.push(`render after ${mark}`); releaseRender = done; }),
  };

  const settle = settleAfterWrite(writes, renders);
  started = 9; // renders that start between the queueing and the settle must still count
  let done = false;
  const running = settle().then(() => { done = true; });
  await new Promise((resolve) => { setImmediate(resolve); });
  assert.deepEqual(steps, ['mark 4', 'queue waited'], 'the mark is taken when the write is queued, not later');

  releaseQueue();
  await new Promise((resolve) => { setImmediate(resolve); });
  assert.deepEqual(steps, ['mark 4', 'queue waited', 'render after 4'], 'then a render after THAT mark is awaited');
  assert.equal(done, false, 'written is not settled: the paint has not happened');
  releaseRender();
  await running;
  assert.equal(done, true);
});

test('a page that cannot be posted to does not stop the others hearing that the work ended', async () => {
  // Final E3 round (gemini): a Settings tab closed mid-action made its post throw inside the `finally`, and the
  // sidebar never heard the count fall — its bar stayed up for good.
  const flight = new InFlight<Slot>(clock().now);
  const closed = new class extends Slot {
    override post(): void {
      throw new Error('Webview is disposed');
    }
  }('settings');
  const sidebar = new Slot('sidebar');
  const errors: unknown[] = [];

  await tracked(flight, [closed, sidebar], closed, { seq: 1, doc: 'gone' }, () => Promise.resolve(), (error) => { errors.push(error); });

  assert.deepEqual(sidebar.posted.at(-1), { type: 'busy', count: 0, oldestMs: 0 }, 'the surviving page is told nothing runs');
  assert.equal(flight.snapshot().count, 0);
  assert.ok(errors.some((error) => /Webview is disposed/u.test(String(error))), 'and the failed post is said, not swallowed');

  flight.start({ seq: 2, doc: 'gone' }, closed);
  flight.start({ seq: 3, doc: 'live' }, sidebar);
  settleEverything(flight, (error) => { errors.push(error); });
  assert.deepEqual(sidebar.posted.at(-1), { type: 'settled', seq: 3, doc: 'live', ok: false }, 'ending the panel reaches every page it can');
});

test('only a positive whole seq with a document id is a numbered ask', () => {
  assert.deepEqual(askOf({ seq: 3, doc: 'page-1' }), { seq: 3, doc: 'page-1' });
  assert.equal(askOf({}), undefined, 'a page from before the busy mark');
  assert.equal(askOf({ seq: 3 }), undefined, 'no document, no way to tell whose');
  assert.equal(askOf({ seq: 0, doc: 'p' }), undefined);
  assert.equal(askOf({ seq: 1.5, doc: 'p' }), undefined);
  assert.equal(askOf({ seq: '3', doc: 'p' }), undefined, 'a message from the webview is checked where it is read');
  assert.equal(askOf({ seq: 3, doc: '' }), undefined);
});
