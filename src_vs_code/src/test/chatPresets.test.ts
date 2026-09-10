import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  mainPrompt,
  presetById,
} from '../chatPresets';

/**
 * The two lists a person builds, read from settings they can edit by hand.
 *
 * <p>Everything here is a BOUNDARY: `settings.json` is a file somebody edits with an editor, and a
 * preset list is the first thing in this feature that a person composes rather than picks from a
 * catalog this product shipped. So a malformed entry is dropped rather than trusted, and the whole
 * list is never lost because one row of it was wrong — losing a person's saved prompts because they
 * mistyped one is the failure this file exists to prevent.</p>
 */

test('a prompt preset needs a name and a text, and anything else is dropped', () => {
  const presets = chatPromptPresetsFrom([
    { id: 'a', name: 'Explain', text: 'Explain this' },
    { id: 'b', name: '', text: 'no name' },
    { id: 'c', name: 'no text', text: '   ' },
    { id: 'd', name: 'Review', text: 'What is wrong with it?' },
    'not an object',
    null,
  ]);

  assert.deepStrictEqual(presets.map((preset) => preset.name), ['Explain', 'Review'],
    'a malformed row took the good ones with it');
});

test('exactly one prompt is the main one, and the first claim wins', () => {
  const presets = chatPromptPresetsFrom([
    { id: 'a', name: 'A', text: 'a' },
    { id: 'b', name: 'B', text: 'b', main: true },
    { id: 'c', name: 'C', text: 'c', main: true },
  ]);

  assert.deepStrictEqual(presets.map((preset) => preset.main), [false, true, false],
    'two prompts claimed to be the main one');
  assert.strictEqual(mainPrompt(presets)?.name, 'B');
});

test('a list with no main one has its first entry standing in', () => {
  // The main prompt is what a trigger that sends BY ITSELF uses, so there must always be one while
  // there is any prompt at all — otherwise the keybinding path has nothing to send.
  const presets = chatPromptPresetsFrom([{ id: 'a', name: 'A', text: 'a' }, { id: 'b', name: 'B', text: 'b' }]);

  assert.strictEqual(mainPrompt(presets)?.name, 'A');
  assert.strictEqual(mainPrompt([]), undefined, 'an empty list invented a prompt');
});

test('the prompt somebody already typed becomes their first preset, ticked', () => {
  // THE migration that must not be got wrong. `coai.chatPrompt` is a single string a person has been
  // editing since the feature shipped; it is their prompt, and this is the release that stops
  // reading it. Dropping it would delete something they wrote.
  const presets = chatPromptPresetsFrom(undefined, 'explain it to somebody who knows the language');

  assert.strictEqual(presets.length, 1);
  assert.strictEqual(presets[0]?.text, 'explain it to somebody who knows the language');
  assert.strictEqual(presets[0]?.main, true, 'the migrated prompt is not the main one');
  assert.ok((presets[0]?.name ?? '').length > 0, 'the migrated prompt has no name to show on a button');
});

test('a saved list is never overwritten by the old single prompt', () => {
  // The migration happens once, in the sense that matters: a person who HAS presets has already
  // moved, and re-adding the old string every read would resurrect a prompt they deleted.
  const presets = chatPromptPresetsFrom([{ id: 'a', name: 'Mine', text: 'mine' }], 'the old one');

  assert.deepStrictEqual(presets.map((preset) => preset.text), ['mine']);
});

test('a model preset names a provider, and one without is not a model preset', () => {
  const presets = chatModelPresetsFrom([
    { id: 'a', name: 'Fast', provider: 'antigravity', model: 'gemini-3.8-flash' },
    { id: 'b', name: 'Nameless provider', model: 'x' },
    { id: 'c', name: '', provider: 'claude' },
    { id: 'd', name: 'Deep', provider: 'claude', model: 'opus', startingPrompt: 'Think hard' },
  ]);

  assert.deepStrictEqual(presets.map((preset) => preset.name), ['Fast', 'Deep']);
  assert.strictEqual(presets[1]?.startingPrompt, 'Think hard');
});

test('a model preset may name no model, which means whatever the row is set to', () => {
  // The same meaning `modelId` has everywhere else in this feature — a row without a model chosen
  // answers with the one it is configured to.
  const presets = chatModelPresetsFrom([{ id: 'a', name: 'Whatever', provider: 'claude' }]);

  assert.strictEqual(presets.length, 1);
  assert.strictEqual(presets[0]?.model, '');
});

test('ids are made where they are missing, and never collide', () => {
  // A person editing settings.json by hand will not write ids. The buttons need one to name the
  // preset a click chose, and two presets sharing one would make a click ambiguous.
  const presets = chatPromptPresetsFrom([
    { name: 'A', text: 'a' },
    { name: 'B', text: 'b' },
    { id: 'a', name: 'C', text: 'c' },
    { id: 'a', name: 'D', text: 'd' },
  ]);

  const ids = presets.map((preset) => preset.id);
  assert.strictEqual(new Set(ids).size, ids.length, 'two presets share an id, so a click is ambiguous');
  assert.ok(ids.every((id) => id.length > 0), 'a preset has no id for a button to name');
});

test('presetById finds one by its id and refuses anything else', () => {
  const presets = chatPromptPresetsFrom([{ id: 'a', name: 'A', text: 'a' }]);

  assert.strictEqual(presetById(presets, 'a')?.name, 'A');
  assert.strictEqual(presetById(presets, 'b'), undefined);
  assert.strictEqual(presetById(presets, ''), undefined);
});

test('a name long enough to fill the row is cut, and the text is not', () => {
  // The name goes on a button in a row of buttons; the text is what a model is asked. One has a
  // width to respect and the other has meaning to keep.
  const presets = chatPromptPresetsFrom([{ id: 'a', name: 'x'.repeat(300), text: 'y'.repeat(9_000) }]);

  assert.ok((presets[0]?.name.length ?? 0) <= 60, 'a name of any length would break the row');
  assert.strictEqual(presets[0]?.text.length, 9_000, 'the prompt itself was truncated');
});
