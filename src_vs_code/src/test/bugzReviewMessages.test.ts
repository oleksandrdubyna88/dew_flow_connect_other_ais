import assert from 'node:assert/strict';
import test from 'node:test';

import { asReviewMessage } from '../bugzReviewMessages';

/**
 * The bugs page's message guard, RUN — moved out of the panel (issue #487) so it could be. What arrives
 * from a webview is only the page's while nothing has gone wrong.
 */

test('a row\'s CoAI: choose names a whole, non-negative row id, or it is nothing', () => {
  assert.deepEqual(asReviewMessage({ type: 'choose', id: 7 }), { type: 'choose', id: 7 });
  assert.deepEqual(asReviewMessage({ type: 'choose', id: '7' }), { type: 'choose', id: 7 }, 'the page posts numbers, but a string of one is one');
  for (const id of [-1, 1.5, undefined, 'x']) {
    assert.equal(asReviewMessage({ type: 'choose', id }), undefined, `id ${String(id)} names no row`);
  }
});

test('the moved guard still answers every kind it answered in the panel', () => {
  assert.deepEqual(asReviewMessage({ type: 'openAt', id: 3 }), { type: 'openAt', id: 3 });
  assert.deepEqual(asReviewMessage({ type: 'decide', ids: [1, 'x', 2], keep: 1 }), { type: 'decide', ids: [1, 2], keep: 1 });
  assert.deepEqual(asReviewMessage({ type: 'draft', id: 2, text: 'hi' }), { type: 'draft', id: 2, text: 'hi' });
  assert.deepEqual(asReviewMessage({ type: 'zoom', delta: 1 }), { type: 'zoom', delta: 1 });
  assert.equal(asReviewMessage({ type: 'zoom', delta: Number.NaN }), undefined);
  assert.equal(asReviewMessage({ type: '__proto__' }), undefined);
  assert.equal(asReviewMessage(null), undefined);
  assert.equal(asReviewMessage({ type: 'ready' }), undefined);
});
