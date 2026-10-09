import assert from 'node:assert/strict';
import { test } from 'node:test';
import { MAX_PROMPT_BYTES, MAX_ROWS } from '../catalogRules';
import { addRefusal, rowWriteRefusal } from '../catalogWriteRules';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';

/**
 * What a write of one catalog row may not do, decided BEFORE it is saved (research/PLAN_one_model_catalog.md E3.2): the
 * row limits (`catalogRefusal`, `effortRefusal`) were called by the migration alone, so a page could store an 9 KB
 * prompt or an effort its runtime refuses. Epic 3's plan round: the 64-row cap is checked only where a row is ADDED,
 * so a catalog already past it (a hand-edited file) can always be edited back under it.
 */

const row = (id: string, runtime: Vendor['runtime'], extra: Partial<Vendor> = {}): Vendor => ({
  ...DEFAULT_VENDORS[0]!, id, runtime, enabled: true, plan: true, code: true, ...extra,
});

test('a system prompt past 8 KiB — counted in UTF-8 bytes — is refused by name; at the limit it is taken', () => {
  const rows = [row('codex', 'codex')];

  assert.match(rowWriteRefusal(rows, 'codex', 'systemPrompt', 'x'.repeat(MAX_PROMPT_BYTES + 1)), /8193 bytes.*at most 8192/);
  assert.equal(rowWriteRefusal(rows, 'codex', 'systemPrompt', 'é'.repeat(MAX_PROMPT_BYTES / 2)), '', '4096 two-byte characters are 8192 bytes');
});

test('a CLI row\'s effort is judged by its runtime; an api or Team server row is judged elsewhere', () => {
  const rows = [row('claude', 'claude'), row('codex', 'codex'), row('agy', 'antigravity'), row('qwen', 'api'), row('srv', 'remote')];

  assert.equal(rowWriteRefusal(rows, 'claude', 'effort', 'high'), '');
  assert.match(rowWriteRefusal(rows, 'claude', 'effort', 'turbo'), /'turbo' is not an effort claude accepts/);
  assert.match(rowWriteRefusal(rows, 'codex', 'effort', 'high'), /has not been measured/);
  assert.match(rowWriteRefusal(rows, 'agy', 'effort', 'high'), /antigravity takes no effort/);
  assert.equal(rowWriteRefusal(rows, 'qwen', 'effort', 'high'), '', 'an api row is judged against its probe report on its own path');
  assert.equal(rowWriteRefusal(rows, 'srv', 'effort', 'high'), '', 'a Team server judges a remote row and says what it dropped');
  assert.equal(rowWriteRefusal(rows, 'codex', 'effort', ''), '', 'back to the default is always allowed');
});

test('the last model switched on for a review stage cannot be switched off or untick that stage', () => {
  const rows = [row('codex', 'codex'), row('claude', 'claude', { plan: false })];

  assert.match(rowWriteRefusal(rows, 'codex', 'enabled', false), /codex is the only model switched on for plan review/);
  assert.match(rowWriteRefusal(rows, 'codex', 'plan', false), /only model switched on for plan review/);
  assert.equal(rowWriteRefusal(rows, 'codex', 'code', false), '', 'claude still reviews code');
  assert.equal(rowWriteRefusal(rows, 'claude', 'enabled', false), '', 'claude is not the last for anything');
});

test('a write that is not one of these is never refused here', () => {
  assert.equal(rowWriteRefusal([row('codex', 'codex')], 'codex', 'model', 'gpt-6'), '');
  assert.equal(rowWriteRefusal([row('codex', 'codex')], 'nobody', 'enabled', false), '', 'an unknown row is the write path\'s own question');
});

test('the row cap is checked where a row is ADDED — and never on an edit of a catalog already past it', () => {
  const full = Array.from({ length: MAX_ROWS }, (_, at) => row(`r${at}`, 'codex'));
  const over = [...full, row('extra', 'codex')];

  assert.match(addRefusal(full), /at most 64/);
  assert.equal(addRefusal(full.slice(1)), '');
  assert.equal(rowWriteRefusal(over, 'r0', 'model', 'gpt-6'), '', 'an edit is never refused for the count');
});
