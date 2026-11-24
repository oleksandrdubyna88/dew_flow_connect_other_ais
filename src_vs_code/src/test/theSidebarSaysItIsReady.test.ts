import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import { measureMark, recordMarksWith } from '../measureMarks';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The sidebar's `ready` acknowledgement, which is where the cold-start measurement's M2 ends (v2 extension.md § 2; epic E2 story S2).
 *
 * <p><b>RUN, not read.</b> The page is the sidebar's own script, run on the page harness every page test shares (`.agents/PROJECT.md`: a webview
 * page is tested by running it). If the post were deleted from the script this test would see no `ready` at all, and M2 would have no end: the
 * measurement would fail with "no ready acknowledgement", which is the point of requiring one.</p>
 */

test('the sidebar page posts exactly one ready, as its last post, after every handler is bound', () => {
  const state = panelState('server');
  const page = runPanel(state, { html: panelHtml(state, 'test-nonce') });
  const ready = page.posted.filter((one) => one['type'] === 'ready');

  assert.equal(ready.length, 1, 'one acknowledgement per document');
  assert.deepEqual(page.posted.at(-1), ready[0], 'ready is the last thing the page says: the document is built before it speaks');
  assert.deepEqual(Object.keys(ready[0] ?? {}).filter((key) => key !== 'type'), [], 'the host reads the type only; the post carries nothing else to go stale');
  assert.doesNotThrow(() => { page.deliver({ type: 'busy' }); }, 'the page already listens when it says it is ready');
});

test('the old code\'s mark of a cold-start moment is nothing without the shell\'s recorder, and the recorder\'s while one is installed', () => {
  const heard: string[] = [];
  measureMark('ready');
  recordMarksWith((name) => { heard.push(name); });
  try {
    measureMark('ready');
    measureMark('firstData');
  } finally {
    recordMarksWith(undefined);
  }
  measureMark('ready');

  assert.deepEqual(heard, ['ready', 'firstData']);
});
