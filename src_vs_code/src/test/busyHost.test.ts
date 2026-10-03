import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BusyHost } from '../busyHost';
import { askPerson } from '../personWait';

/**
 * The busy mark's host half for a webview that is not the panel (todo/PLAN_busy_marks_on_every_webview.md §4.2).
 *
 * <p>Asked for on 2026-10-02 — a progress mark everywhere an action takes longer than half a second — and built on
 * 2026-10-03 for the other webviews. One `BusyHost` per webview: the same `InFlight`, `tracked()` and `askOf()` the panel
 * uses, so the rules that already hold there (settled in a `finally`, waiting on the person is not work) hold here by
 * construction rather than by copy.</p>
 */

/** A webview as far as the busy mark uses one: somewhere to post, and a record of what was posted. */
class Webview {
  readonly posted: Record<string, unknown>[] = [];

  post(message: unknown): void {
    this.posted.push(message as Record<string, unknown>);
  }
}

function clock(start = 1_000): { readonly now: () => number; readonly advance: (ms: number) => void } {
  let at = start;

  return { now: () => at, advance: (ms) => { at += ms; } };
}

test('a numbered post is tracked: announced, run, settled to its page, and the count falls back', async () => {
  const page = new Webview();
  const host = new BusyHost(page, clock().now);
  let release: () => void = () => undefined;
  const running = host.track({ type: 'command', seq: 3, doc: 'd1' }, () => new Promise<void>((done) => { release = done; }));

  assert.deepEqual(page.posted, [{ type: 'busy', count: 1, oldestMs: 0 }]);
  assert.deepEqual(host.snapshot(), { count: 1, oldestMs: 0 }, 'what a repaint paints while it runs');
  release();
  await running;

  assert.deepEqual(page.posted.slice(1), [{ type: 'settled', seq: 3, doc: 'd1', ok: true }, { type: 'busy', count: 0, oldestMs: 0 }]);
});

test('an unnumbered post still runs, and marks nothing', async () => {
  const page = new Webview();
  const host = new BusyHost(page, clock().now);
  let ran = false;

  await host.track({ type: 'command' }, async () => { ran = true; });

  assert.equal(ran, true, 'a page from before the mark is still served');
  assert.deepEqual(page.posted, []);
});

test('work that throws settles not ok, and the failure is said', async () => {
  const page = new Webview();
  const errors: unknown[] = [];
  const host = new BusyHost(page, clock().now, (error) => { errors.push(error); });

  await host.track({ type: 'decide', seq: 1, doc: 'd' }, () => Promise.reject(new Error('the server refused the write')));

  assert.deepEqual(page.posted.at(-2), { type: 'settled', seq: 1, doc: 'd', ok: false });
  assert.match(String(errors[0]), /the server refused the write/u);
});

test('a page that says it is ready is told what is running, and nothing else is consumed', async () => {
  const time = clock();
  const page = new Webview();
  const host = new BusyHost(page, time.now);
  const running = host.track({ type: 'command', seq: 1, doc: 'old' }, () => new Promise<void>(() => undefined));
  time.advance(300);

  assert.equal(host.heard({ type: 'ready' }), true);
  assert.deepEqual(page.posted.at(-1), { type: 'busy', count: 1, oldestMs: 300 }, 'a fresh document draws after what is LEFT of the delay');
  assert.equal(host.heard({ type: 'command', command: 'export' }), false, 'every other message is the panel’s own');
  void running;
});

test('a prompt opened by the work pauses it, as on the panel', async () => {
  const page = new Webview();
  const host = new BusyHost(page, clock().now);
  let answer: (value: string) => void = () => undefined;
  const box = new Promise<string>((done) => { answer = done; });
  const running = host.track({ type: 'command', seq: 2, doc: 'd' }, async () => { await askPerson(() => box); });
  await new Promise((resolve) => { setImmediate(resolve); });

  assert.deepEqual(page.posted.slice(1), [{ type: 'waiting', seq: 2, doc: 'd' }, { type: 'busy', count: 0, oldestMs: 0 }]);
  answer('picked');
  await running;
  assert.deepEqual(page.posted.at(-2), { type: 'settled', seq: 2, doc: 'd', ok: true });
});

test('closing the webview settles everything still running as not done', () => {
  const page = new Webview();
  const host = new BusyHost(page, clock().now);
  void host.track({ type: 'command', seq: 5, doc: 'd' }, () => new Promise<void>(() => undefined));

  host.dispose();

  assert.deepEqual(page.posted.at(-1), { type: 'settled', seq: 5, doc: 'd', ok: false });
  assert.deepEqual(host.snapshot(), { count: 0, oldestMs: 0 });
});
