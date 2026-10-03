import assert from 'node:assert/strict';
import { test } from 'node:test';

import { rankChoices, SEARCH_FROM_OPTIONS } from '../selectSearch';

/**
 * The ranking behind a model list's search box, as a value (research/PLAN_model_search_and_busy_marks.md §3.2).
 *
 * <p>The same function is embedded into the page by its source text, so what is asserted here is what the page runs;
 * `selectSearchPage.test.ts` runs the page itself and `bundledPage.test.ts` the minified bundle.</p>
 */

/** OpenRouter's own shape — `vendor/model`, with `:` variants — in the endpoint's order, which is not alphabetical. */
const LISTED = [
  'apodex/apodex-1.1-mini:free',
  'openai/gpt-6.1-sol',
  'anthropic/claude-sonnet-5.5',
  'anthropic/claude-sonnet-5.5:batch',
  'z-ai/glm-5.3-prime',
  'anthropic/claude-opus-5.5',
  'openai/gpt-6-luna',
  'opus-community/opus-7b',
].map((id) => ({ value: id, text: id }));

const ids = (ranked: readonly number[]): readonly string[] => ranked.map((index) => LISTED[index]!.value);

test('an id that STARTS with the query comes first, then one where a segment does, then one that only contains it', () => {
  const ranked = ids(rankChoices('opus', LISTED));

  // `opus-community/…` starts with it; `anthropic/claude-opus-5.5` has a segment starting with it after a `-`.
  assert.deepEqual(ranked, ['opus-community/opus-7b', 'anthropic/claude-opus-5.5']);
});

test('a segment boundary is any of / - . : and a space', () => {
  const choices = ['a/beta', 'a-beta', 'a.beta', 'a:beta', 'a beta', 'abeta'].map((id) => ({ value: id, text: id }));

  assert.deepEqual(rankChoices('beta', choices), [0, 1, 2, 3, 4, 5], 'five segment starts in order, then the one that only contains it');
  assert.deepEqual(rankChoices('bet', [{ value: 'abeta', text: 'abeta' }, { value: 'x/beta', text: 'x/beta' }]), [1, 0],
    'containing is the last tier, whatever its position in the list');
});

test('inside a tier the list keeps its own order', () => {
  assert.deepEqual(ids(rankChoices('claude', LISTED)), [
    'anthropic/claude-sonnet-5.5', 'anthropic/claude-sonnet-5.5:batch', 'anthropic/claude-opus-5.5',
  ]);
});

test('several words must ALL match, and the first word decides the tier', () => {
  assert.deepEqual(ids(rankChoices('claude batch', LISTED)), ['anthropic/claude-sonnet-5.5:batch']);
  assert.deepEqual(ids(rankChoices('sonnet nothing-like-this', LISTED)), []);
  assert.deepEqual(ids(rankChoices('batch anthropic', LISTED)), ['anthropic/claude-sonnet-5.5:batch']);
});

test('case is ignored on both sides', () => {
  assert.deepEqual(ids(rankChoices('LUNA', LISTED)), ['openai/gpt-6-luna']);
  assert.deepEqual(rankChoices('luna', [{ value: 'gpt-6-luna', text: 'GPT-6 Luna' }]), [0]);
});

test('the label counts as much as the id', () => {
  const choices = [{ value: 'gpt-6-astra', text: 'GPT-6 Astra' }, { value: 'x', text: 'Something Astral' }];

  assert.deepEqual(rankChoices('astra', choices), [0, 1], 'a word of the label starts a segment too');
  assert.deepEqual(rankChoices('something', choices), [1], 'a label that starts with it ranks as a prefix');
});

test('an empty or blank query is every choice, in its own order', () => {
  assert.deepEqual(rankChoices('', LISTED), LISTED.map((_, index) => index));
  assert.deepEqual(rankChoices('   ', LISTED), LISTED.map((_, index) => index));
});

test('nothing matching is an empty ranking, never an error', () => {
  assert.deepEqual(rankChoices('zzz', LISTED), []);
  assert.deepEqual(rankChoices('opus', []), []);
});

test('characters that mean something to a regular expression are plain text', () => {
  const choices = [{ value: 'a.b', text: 'a.b' }, { value: 'axb', text: 'axb' }, { value: 'c(1)', text: 'c(1)' }];

  assert.deepEqual(rankChoices('a.b', choices), [0], 'a dot is a dot');
  assert.deepEqual(rankChoices('(1', choices), [2]);
});

test('the threshold is fifteen options', () => {
  assert.equal(SEARCH_FROM_OPTIONS, 15);
});

test('the function can be embedded by its source text: no backtick, and no name from outside itself', () => {
  const source = rankChoices.toString();

  assert.ok(!source.includes('`'), 'a backtick would end the template literal the page is built in');
  // Run the TEXT, in a scope that has nothing else in it: a reference to an outer helper throws here.
  const standalone = new Function(`return (${source});`)() as typeof rankChoices;
  assert.deepEqual(standalone('opus', LISTED), rankChoices('opus', LISTED));
});
