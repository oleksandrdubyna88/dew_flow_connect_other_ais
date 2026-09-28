import assert from 'node:assert/strict';
import { test } from 'node:test';
import { REPAINT_HOLD_MS } from '../panelView';
import { anyHeld, SurfaceSlot, type SurfaceView } from '../surfaceSlot';

/**
 * What belongs to ONE webview the panel paints: the held view, what was painted into it, and whether
 * somebody is typing in it (`surfaceSlot.ts`, `todo/PLAN_settings_page.md` F2, F3, F8).
 */

interface FakeView extends SurfaceView {
  readonly writes: string[];
  readonly posted: unknown[];
}

function fakeView(options: { throwOnWrite?: Error; rejectPost?: Error } = {}): FakeView {
  const writes: string[] = [];
  const posted: unknown[] = [];

  return {
    writes,
    posted,
    webview: {
      set html(value: string) {
        if (options.throwOnWrite !== undefined) {
          throw options.throwOnWrite;
        }
        writes.push(value);
      },
      get html(): string {
        return writes.at(-1) ?? '';
      },
      postMessage(message: unknown): Thenable<boolean> {
        posted.push(message);

        return options.rejectPost === undefined ? Promise.resolve(true) : Promise.reject(options.rejectPost);
      },
    },
  };
}

test('a view VS Code creates again is painted, even when nothing drawn changed (F2)', () => {
  const slot = new SurfaceSlot<FakeView>();
  const first = fakeView();
  const second = fakeView();

  slot.attach(first);
  assert.equal(slot.paint('k', () => '<p>page</p>'), 'painted');
  slot.attach(second);

  assert.equal(slot.paint('k', () => '<p>page</p>'), 'painted', 'the new document is empty, so a patch would land in nothing');
  assert.deepEqual(second.writes, ['<p>page</p>']);
});

test('the same key into the same view is a patch, and the page is not even built', () => {
  const slot = new SurfaceSlot<FakeView>();
  slot.attach(fakeView());
  slot.paint('k', () => 'x');

  assert.equal(slot.paint('k', () => { throw new Error('built for nothing'); }), 'patch');
});

test('a late disposal of a REPLACED view keeps the live view\'s edit hold (F3)', () => {
  let now = 1_000;
  const slot = new SurfaceSlot<FakeView>(() => now);
  const replaced = fakeView();
  const live = fakeView();
  slot.attach(replaced);
  slot.attach(live);
  slot.paint('before', () => 'before');
  slot.edited(true, 'model|codex||', 3, 3);

  slot.detach(replaced);
  now += 1_000;

  assert.deepEqual(slot.focus(), { id: 'model|codex||', start: 3, end: 3 }, 'the caret of the page still open was forgotten');
  assert.equal(slot.paint('after', () => 'after'), 'patch', 'a paint landed under a control somebody is typing in');
  assert.equal(slot.view, live);
});

test('disposing the view it holds lets go of it and of its edit hold', () => {
  const slot = new SurfaceSlot<FakeView>();
  const view = fakeView();
  slot.attach(view);
  slot.edited(true, 'x|||', 0, 0);

  slot.detach(view);

  assert.equal(slot.view, undefined);
  assert.equal(slot.focus(), undefined);
  assert.equal(slot.paint('k', () => 'x'), 'gone');
});

test('a paint waits while somebody types, and lands once the hold runs out or focus leaves', () => {
  let now = 0;
  const slot = new SurfaceSlot<FakeView>(() => now);
  slot.attach(fakeView());
  now = 10;
  assert.equal(slot.edited(true, 'a|||', 1, 1), false);

  assert.equal(slot.paint('k1', () => 'one'), 'patch');
  now = 10 + REPAINT_HOLD_MS;
  assert.equal(slot.paint('k1', () => 'one'), 'painted', 'the hold is capped, so a focus nobody left cannot freeze the page');

  assert.equal(slot.edited(true, 'a|||', 2, 2), false);
  assert.equal(slot.edited(false, '', 0, 0), true, 'focus leaving is when the withheld paint may land');
  assert.equal(slot.paint('k2', () => 'two'), 'painted');
});

test('moving between controls does not renew the hold, or tabbing through a page withholds a paint forever', () => {
  let now = 10;
  const slot = new SurfaceSlot<FakeView>(() => now);
  slot.attach(fakeView());
  slot.edited(true, 'a|||', 0, 0);
  now = 10 + REPAINT_HOLD_MS - 1;
  slot.edited(true, 'b|||', 4, 4);
  now = 10 + REPAINT_HOLD_MS;

  assert.equal(slot.paint('k', () => 'x'), 'painted', 'the second focus restarted the clock');
  assert.deepEqual(slot.focus(), { id: 'b|||', start: 4, end: 4 }, 'the caret must be the control the person is in NOW');
});

test('the key is recorded only after the write succeeded', () => {
  const slot = new SurfaceSlot<FakeView>();
  slot.attach(fakeView({ throwOnWrite: new Error('Webview is disposed') }));

  assert.equal(slot.paint('k', () => 'x'), 'gone');
  const next = fakeView();
  slot.attach(next);
  assert.equal(slot.paint('k', () => 'x'), 'painted');
});

test('a write that fails for another reason is not swallowed', () => {
  const slot = new SurfaceSlot<FakeView>();
  slot.attach(fakeView({ throwOnWrite: new Error('something else broke') }));

  assert.throws(() => slot.paint('k', () => 'x'), /something else broke/);
});

test('a forced repaint writes the same page again — a refused write snapping back', () => {
  const slot = new SurfaceSlot<FakeView>();
  const view = fakeView();
  slot.attach(view);
  slot.paint('k', () => 'x');

  slot.forceRepaint();

  assert.equal(slot.paint('k', () => 'x'), 'painted');
  assert.equal(view.writes.length, 2);
});

test('a message reaches the held view, and a disposal on the way is dropped quietly', async () => {
  const slot = new SurfaceSlot<FakeView>();
  const view = fakeView({ rejectPost: new Error('Webview is disposed') });
  slot.attach(view);
  const errors: unknown[] = [];
  const original = console.error;
  console.error = (...args: unknown[]) => { errors.push(args); };
  try {
    slot.post({ type: 'copied', id: 'p1' });
    await new Promise((resolve) => setImmediate(resolve));
  } finally {
    console.error = original;
  }

  assert.deepEqual(view.posted, [{ type: 'copied', id: 'p1' }]);
  assert.deepEqual(errors, []);
});

test('any held surface counts, not only the sidebar — so a probe for the other page is not cancelled (F8)', () => {
  const sidebar = new SurfaceSlot<FakeView>();
  const other = new SurfaceSlot<FakeView>();

  assert.equal(anyHeld([sidebar, other]), false);
  other.attach(fakeView());
  assert.equal(anyHeld([sidebar, other]), true, 'the sidebar is closed, the other page is open, and a probe it needs was cancelled');
});
