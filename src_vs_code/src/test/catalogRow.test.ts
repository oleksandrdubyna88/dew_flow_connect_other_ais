import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogRefusal, MAX_PROMPT_BYTES, MAX_ROWS, reviewsAnything, shownOnTheOldPage } from '../catalogRules';
import { DEFAULT_VENDORS, Vendor, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';

/**
 * A reviewer row grows into a catalog row (PLAN_one_model_catalog.md D1, E1.1): a name, the non-review
 * features it may serve, a system prompt, its own time limit and a chat starting prompt — each only
 * when said, so every row written before the catalog stays byte-identical.
 */

const CODEX = { id: 'codex', runtime: 'codex', model: 'gpt-x' };

test('a row keeps every catalog field it said', () => {
  const [row] = vendorsFrom([{
    ...CODEX,
    name: '  Codex for consults ',
    uses: ['consultant', 'chat'],
    systemPrompt: 'Answer tersely.',
    timeoutMinutes: 30,
    chatStartingPrompt: 'Review this:',
  }]);

  assert.equal(row?.name, 'Codex for consults');
  assert.deepEqual(row?.uses, ['consultant', 'chat']);
  assert.equal(row?.systemPrompt, 'Answer tersely.');
  assert.equal(row?.timeoutMinutes, 30);
  assert.equal(row?.chatStartingPrompt, 'Review this:');
});

test('a row written before the catalog parses to exactly what it parsed to before', () => {
  const [row] = vendorsFrom([CODEX]);

  for (const field of ['name', 'uses', 'systemPrompt', 'timeoutMinutes', 'chatStartingPrompt']) {
    assert.equal(field in (row ?? {}), false, `${field} appeared on a row that never said it`);
  }
});

test('uses keeps only the features it knows, once each, in the catalog order', () => {
  const [row] = vendorsFrom([{ ...CODEX, uses: ['chat', 'review', 'security', 'chat', 7, 'bugz'] }]);

  assert.deepEqual(row?.uses, ['security', 'chat', 'bugz']);
});

test('an empty uses list is absent, not an empty array', () => {
  const [row] = vendorsFrom([{ ...CODEX, uses: ['nonsense'] }]);

  assert.equal('uses' in (row ?? {}), false);
});

test('an api row keeps its own reviewMinutes and never a timeoutMinutes', () => {
  const [row] = vendorsFrom([{ id: 'qwen', runtime: 'api', model: 'qwen-max', timeoutMinutes: 30, reviewMinutes: 20 }]);

  assert.equal('timeoutMinutes' in (row ?? {}), false);
  assert.equal(row?.reviewMinutes, 20);
});

test('a time limit the box would not accept is absent', () => {
  for (const timeoutMinutes of [0, -1, 2.5, 1441, '30']) {
    const [row] = vendorsFrom([{ ...CODEX, timeoutMinutes }]);
    assert.equal('timeoutMinutes' in (row ?? {}), false, `${String(timeoutMinutes)} was kept`);
  }
});

test('the new fields do not cross to coai-mcp: the wire is a whitelist', () => {
  const plain = vendorsFrom([CODEX]);
  const widened = vendorsFrom([{ ...CODEX, name: 'C', uses: ['security'], systemPrompt: 'x', timeoutMinutes: 9, chatStartingPrompt: 'y' }]);

  assert.equal(vendorsEnv(widened), vendorsEnv(plain));
});

test('a catalog of more than 64 rows is refused, naming the limit', () => {
  const rows: Vendor[] = Array.from({ length: MAX_ROWS + 1 }, (_, i) => ({ ...DEFAULT_VENDORS[0]!, id: `r${i}` }));

  assert.match(catalogRefusal(rows), /65 models.*at most 64/u);
  assert.equal(catalogRefusal(rows.slice(0, MAX_ROWS)), '');
});

test('a system prompt over 8 KiB is refused, counted in bytes, naming the row', () => {
  const ascii = 'a'.repeat(MAX_PROMPT_BYTES);
  // Eight thousand characters of a two-byte letter is sixteen thousand bytes, which is what crosses.
  const cyrillic = 'ж'.repeat(MAX_PROMPT_BYTES / 2 + 1);
  const row = DEFAULT_VENDORS[0]!;

  assert.equal(catalogRefusal([{ ...row, systemPrompt: ascii }]), '');
  assert.match(catalogRefusal([{ ...row, systemPrompt: cyrillic }]), new RegExp(`${row.id}.*8192 bytes`, 'u'));
});

test('a chat starting prompt has the same limit', () => {
  const row = DEFAULT_VENDORS[0]!;

  assert.match(catalogRefusal([{ ...row, chatStartingPrompt: 'a'.repeat(MAX_PROMPT_BYTES + 1) }]), /chat starting prompt/u);
});

test('the old Settings page shows every reviewer and hides a row that exists only for its uses', () => {
  const row = DEFAULT_VENDORS[0]!;
  const off = { ...row, plan: false, code: false, document: false };

  assert.equal(shownOnTheOldPage(row), true);
  assert.equal(shownOnTheOldPage(off), true, 'a reviewer switched off everywhere is still the person’s own row');
  assert.equal(shownOnTheOldPage({ ...off, uses: ['consultant'] }), false, 'a migrated consultant is not a reviewer');
  assert.equal(shownOnTheOldPage({ ...row, uses: ['qconsult'] }), true, 'a reviewer that also answers questions');
});

test('a row that reviews nothing is told apart from one that reviews a stage', () => {
  const row = DEFAULT_VENDORS[0]!;

  assert.equal(reviewsAnything(row), true);
  assert.equal(reviewsAnything({ ...row, plan: false, code: false, document: false }), false);
  assert.equal(reviewsAnything({ ...row, plan: false, code: false }), false, 'absent document follows the plan tick');
  assert.equal(reviewsAnything({ ...row, plan: false, code: false, document: true }), true);
  assert.equal(reviewsAnything({ ...row, runtime: 'codex', plan: false, code: false, document: false, feature: true }), true);
});

test('the prompt limit is 8192 bytes — the number coai-mcp refuses past too (CatalogLimits.MaxPromptBytes)', () => {
  // Pinned on both halves rather than one reading the other's source: changing one alone is a red test here or there.
  assert.equal(MAX_PROMPT_BYTES, 8192);
});
