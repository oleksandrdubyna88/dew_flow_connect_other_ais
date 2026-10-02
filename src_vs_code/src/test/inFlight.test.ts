import assert from 'node:assert/strict';
import { test } from 'node:test';

import { IDLE } from '../busySnapshot';
import { askOf, InFlight, settleAfterWrite, settleEverything, tracked } from '../inFlight';
import { askPerson } from '../personWait';

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

// ---- Waiting on the person (research/PLAN_busy_mark_pauses_while_you_type.md §3.2–§3.3) ----
// Asked for on 2026-10-02: an action that opens an input box was counted as running while the person typed into it,
// so the bar ran over their typing. Waiting on the person is not work: it leaves the count, and its time is not counted.

test('an operation waiting on the person leaves the count, and the wait is not counted in its age', () => {
  const time = clock();
  const flight = new InFlight<Slot>(time.now);
  const id = flight.start({ seq: 1, doc: 'd' }, new Slot('sidebar'));
  time.advance(200);

  assert.equal(flight.pause(id), true, 'the first prompt pauses it');
  assert.deepEqual(flight.snapshot(), IDLE, 'nothing is running while the box is open');
  time.advance(5_000);
  assert.equal(flight.resume(id), 200, 'answered: it has worked 200 ms, however long the person typed');
  time.advance(300);
  assert.deepEqual(flight.snapshot(), { count: 1, oldestMs: 500 });
});

test('two prompts open at once pause an operation once, and only the last answer resumes it', () => {
  const time = clock();
  const flight = new InFlight<Slot>(time.now);
  const id = flight.start({ seq: 1, doc: 'd' }, new Slot('sidebar'));

  assert.equal(flight.pause(id), true);
  assert.equal(flight.pause(id), false, 'already waiting: nothing new to say');
  time.advance(1_000);
  assert.equal(flight.resume(id), undefined, 'one box is still open');
  assert.deepEqual(flight.snapshot(), IDLE);
  assert.equal(flight.resume(id), 0, 'both answered: it has worked no time at all');
  assert.equal(flight.snapshot().count, 1);
});

test('two prompts one after the other: neither wait is counted, only the work around them', () => {
  // Plan round, gemini: a second prompt must not count the first one's typing as work.
  const time = clock();
  const flight = new InFlight<Slot>(time.now);
  const id = flight.start({ seq: 1, doc: 'd' }, new Slot('sidebar'));
  time.advance(100);
  flight.pause(id);
  time.advance(4_000);
  assert.equal(flight.resume(id), 100);
  time.advance(50);
  flight.pause(id);
  time.advance(6_000);

  assert.equal(flight.resume(id), 150, 'the URL box, then the key box: 100 ms + 50 ms of work, never ten seconds');
  assert.deepEqual(flight.snapshot(), { count: 1, oldestMs: 150 });
});

test('a prompt that outlives its operation can neither pause nor resume it back into the record', () => {
  const flight = new InFlight<Slot>(clock().now);
  const id = flight.start({ seq: 1, doc: 'd' }, new Slot('sidebar'));
  flight.finish(id);

  assert.equal(flight.pause(id), false);
  assert.equal(flight.resume(id), undefined);
  assert.deepEqual(flight.snapshot(), IDLE, 'a finished operation stays finished');
});

test('a tracked operation that asks the person tells its poster it is waiting, then working, and every page the count', async () => {
  const time = clock();
  const flight = new InFlight<Slot>(time.now);
  const sidebar = new Slot('sidebar');
  const settings = new Slot('settings');
  let answer: (typed: string) => void = () => undefined;
  const box = new Promise<string>((done) => { answer = done; });
  const running = tracked(flight, [sidebar, settings], sidebar, { seq: 4, doc: 'doc-a' }, async () => {
    time.advance(150);
    await askPerson(() => box);
    time.advance(50);
  });
  await new Promise((resolve) => { setImmediate(resolve); });

  assert.deepEqual(sidebar.posted, [
    { type: 'busy', count: 1, oldestMs: 0 },
    { type: 'waiting', seq: 4, doc: 'doc-a' },
    { type: 'busy', count: 0, oldestMs: 0 },
  ], 'the box is open: the poster stops its own clock, and no page counts it');
  assert.deepEqual(settings.posted.at(-1), { type: 'busy', count: 0, oldestMs: 0 }, 'the other page hears the count, not the seq');
  assert.equal(settings.posted.some((one) => one['type'] === 'waiting'), false);

  time.advance(10_000);
  answer('typed');
  await running;
  assert.deepEqual(sidebar.posted.slice(3), [
    { type: 'working', seq: 4, doc: 'doc-a', spentMs: 150 },
    { type: 'busy', count: 1, oldestMs: 150 },
    { type: 'settled', seq: 4, doc: 'doc-a', ok: true },
    { type: 'busy', count: 0, oldestMs: 0 },
  ], 'answered: working again from the 150 ms it had done, never the ten seconds of typing');
});

test('a prompt that fails resumes the operation before it settles, and the failure is said', async () => {
  const flight = new InFlight<Slot>(clock().now);
  const page = new Slot('sidebar');
  const errors: unknown[] = [];

  await tracked(flight, [page], page, { seq: 2, doc: 'd' }, async () => {
    await askPerson(() => Promise.reject(new Error('the box was torn down')));
  }, (error) => { errors.push(error); });

  assert.deepEqual(page.posted.map((one) => one['type']), ['busy', 'waiting', 'busy', 'working', 'busy', 'settled', 'busy']);
  assert.deepEqual(page.posted.at(-2), { type: 'settled', seq: 2, doc: 'd', ok: false });
  assert.match(String(errors[0]), /the box was torn down/u);
});
