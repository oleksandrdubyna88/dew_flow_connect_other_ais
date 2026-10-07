import assert from 'node:assert/strict';
import { test } from 'node:test';

import { rolesFieldOf } from '../rolesMessages';

/**
 * Which roles commands are typing — debounced by `settledWrites` — and which are a pick, applied at once
 * (research/PLAN_busy_marks_on_every_webview.md, E3 code round).
 *
 * <p>A pick is numbered for the busy mark. Settled like typing, a stage pick waited 300 ms before it even started, which
 * the bar then counted, and it skipped the drain that stores pending typing first.</p>
 */

test('typing in a role or prompt field settles under its own key', () => {
  assert.equal(rolesFieldOf({ kind: 'edit', id: 'R', field: 'name', value: 'Requirements' }), 'R/name');
  assert.equal(rolesFieldOf({ kind: 'editPrompt', id: 'R', promptId: 'p', field: 'text', value: 'Check it.' }), 'R/p/text');
});

test('a stage picked from its select is a pick, not typing, and is not settled', () => {
  assert.equal(rolesFieldOf({ kind: 'edit', id: 'R', field: 'stage', value: 'code' }), undefined);
});

test('a switch is a pick too', () => {
  assert.equal(rolesFieldOf({ kind: 'edit', id: 'R', field: 'active', value: true }), undefined);
});
