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

test('every kind the page sends is parsed to its shape, and each one\'s junk is dropped', () => {
  assert.deepEqual(asReviewMessage({ type: 'expand', id: 4, open: true }), { type: 'expand', ids: [4], open: true });
  assert.deepEqual(asReviewMessage({ type: 'expandAll', ids: [4, 5], open: 'yes' }), { type: 'expandAll', ids: [4, 5], open: false },
    'only a real true opens');
  assert.deepEqual(asReviewMessage({ type: 'tab', strip: 'lang', key: 'ts' }), { type: 'tab', strip: 'lang', key: 'ts' });
  assert.deepEqual(asReviewMessage({ type: 'realText', on: true }), { type: 'realText', on: true });
  assert.deepEqual(asReviewMessage({ type: 'fetchReal', id: 3, generation: 'g1' }), { type: 'fetchReal', id: 3, generation: 'g1' });
  assert.equal(asReviewMessage({ type: 'fetchReal', id: 3, generation: 7 }), undefined, 'a generation that is not a string');
  assert.equal(asReviewMessage({ type: 'fetchReal', id: 1.5, generation: 'g1' }), undefined);
  assert.deepEqual(asReviewMessage({ type: 'openCall', at: 'x#1' }), { type: 'openCall', at: 'x#1' });
  assert.equal(asReviewMessage({ type: 'openCall', at: 1 }), undefined);
  assert.deepEqual(asReviewMessage({ type: 'comment', id: 2, text: 'why' }), { type: 'comment', id: 2, text: 'why' });
  assert.equal(asReviewMessage({ type: 'comment', id: 2 }), undefined, 'String(undefined) must never become somebody\'s comment');
  assert.equal(asReviewMessage({ type: 'draft', id: -2, text: 'x' }), undefined);
  for (const type of ['openCurrent', 'openTree', 'calls'] as const) {
    assert.deepEqual(asReviewMessage({ type, id: 9 }), { type, id: 9 });
    assert.equal(asReviewMessage({ type, id: -9 }), undefined);
  }
  assert.deepEqual(asReviewMessage({ type: 'tone', delta: -1 }), { type: 'tone', delta: -1 });
  assert.equal(asReviewMessage({ type: 'tone', delta: 'x' }), undefined);
  assert.equal(asReviewMessage({ type: 'constructor' }), undefined, 'nothing on a prototype answers');
  assert.equal(asReviewMessage({ type: 'toString' }), undefined);
  assert.equal(asReviewMessage({}), undefined);
  assert.equal(asReviewMessage('decide'), undefined);
});
