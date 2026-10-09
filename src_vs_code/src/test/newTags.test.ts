import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FORGET_AFTER_MS, seenNow } from '../newTags';

/**
 * The first-seen record behind the page's "new" marks (research/PLAN_one_model_catalog.md, E3.1): bounded by the listed
 * controls, pruned after 60 days, and never an error whatever was stored.
 */

test('a listed control is stamped the first time it is seen, and keeps that time afterwards', () => {
  const first = seenNow(undefined, ['model.thinking'], 1_000);
  const later = seenNow(first, ['model.thinking'], 5_000);

  assert.deepEqual(first, { 'model.thinking': 1_000 });
  assert.deepEqual(later, { 'model.thinking': 1_000 }, 'a later look does not move the clock');
});

test('the record holds only listed controls, and forgets an entry after 60 days', () => {
  const stored = { 'model.thinking': 1_000, 'model.gone': 1_000 };

  assert.deepEqual(seenNow(stored, ['model.thinking'], 2_000), { 'model.thinking': 1_000 }, 'an unlisted control is dropped');
  assert.deepEqual(seenNow(stored, ['model.thinking'], 1_000 + FORGET_AFTER_MS), {}, 'sixty days on it is forgotten');
});

test('whatever was stored reads as a record or as nothing — never an error', () => {
  for (const stored of [null, 'text', 42, ['a'], { 'model.thinking': 'yesterday' }, { 'model.thinking': Number.NaN }]) {
    assert.deepEqual(seenNow(stored, ['model.thinking'], 7), { 'model.thinking': 7 }, JSON.stringify(stored));
  }
});
