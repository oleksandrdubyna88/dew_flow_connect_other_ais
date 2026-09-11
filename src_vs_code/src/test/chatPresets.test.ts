import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  freshModelRow,
  freshPromptRow,
  mainPrompt,
  modelRowsAfterAdd,
  promptRowsAfterMain,
  presetById,
  reaskFrom,
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


/* ------------------------------------------------------------------------------------------------
 * `reaskFrom` — the RULE behind entry 24, and the part of it worth being exact about. SonarCloud
 * caught that it had none of its own: the tests covered the button that offers a re-ask and not the
 * function that decides what one consists of, which is the half a person's conversation depends on.
 * ---------------------------------------------------------------------------------------------- */

const CONVERSATION = [
  { role: 'you' as const, text: 'first question' },
  { role: 'model' as const, text: 'first answer', model: { id: 'antigravity' } },
  { role: 'you' as const, text: 'the question I want answered again' },
  { role: 'model' as const, text: 'the answer I did not like', model: { id: 'antigravity' } },
];

test('a re-ask keeps the question, drops the answer, and keeps everything before them', () => {
  const again = reaskFrom(CONVERSATION, 'claude');

  assert.strictEqual(again?.question, 'the question I want answered again');
  assert.deepStrictEqual(again?.said.map((message) => message.text), ['first question', 'first answer'],
    'the conversation behind the question was lost, or the rejected answer came with it');
});

test('there is nothing to re-ask while the same model is chosen', () => {
  // Pressing Enter on an empty box with the model unchanged does what it always did: nothing.
  assert.strictEqual(reaskFrom(CONVERSATION, 'antigravity'), undefined);
});

test('there is nothing to re-ask before an answer exists', () => {
  assert.strictEqual(reaskFrom([], 'claude'), undefined, 'an empty conversation offered a re-ask');
  assert.strictEqual(reaskFrom([{ role: 'you', text: 'asked' }], 'claude'), undefined,
    'a question nobody has answered yet offered a re-ask');
  assert.strictEqual(
    reaskFrom([{ role: 'you', text: 'asked' }, { role: 'you', text: 'asked again' }], 'claude'),
    undefined,
    'two questions in a row offered a re-ask');
});

test('an answer whose model was never recorded cannot be re-asked', () => {
  // A conversation restored from before the model was recorded on each answer. There is no way to
  // tell whether the model has changed, and guessing would re-ask something that was never asked
  // of anybody else.
  const older = [{ role: 'you' as const, text: 'q' }, { role: 'model' as const, text: 'a' }];

  assert.strictEqual(reaskFrom(older, 'claude'), undefined);
});

test('a re-ask needs a question with words in it', () => {
  const blank = [
    { role: 'you' as const, text: '   ' },
    { role: 'model' as const, text: 'an answer', model: { id: 'antigravity' } },
  ];

  assert.strictEqual(reaskFrom(blank, 'claude'), undefined, 'a re-ask of nothing was offered');
});

test('nothing is re-asked when no model is chosen at all', () => {
  assert.strictEqual(reaskFrom(CONVERSATION, ''), undefined);
});

test('the answer before last is kept, because the conversation is not the last exchange', () => {
  // Only ONE answer is dropped — the one being rejected. An earlier answer is part of what the next
  // model needs to make sense of the question.
  const again = reaskFrom(CONVERSATION, 'claude');

  assert.ok(again?.said.some((message) => message.text === 'first answer'),
    'an earlier answer was dropped along with the rejected one');
});

/**
 * A new row must be one its own reader will KEEP.
 *
 * <p>Reported 2026-09-11: *Add a model* did nothing. It was not doing nothing — it appended
 * `{ name: 'New model', provider: '', model: '' }` to `coai.chatModelPresets`, and
 * `chatModelPresetsFrom` drops any row without a provider, so the page re-read the list and showed
 * exactly what it showed before. Every press left a dead row in `settings.json` that nothing can
 * display, edit or remove. The prompt side does not have the defect because its seed is two real
 * values; the model side seeded the field its own reader refuses on.</p>
 */
test('a new model preset survives the reader that will render it', () => {
  const kept = chatModelPresetsFrom([freshModelRow([], 'agy')]);

  assert.strictEqual(kept.length, 1, 'the row a press of Add a model writes is dropped by the reader');
  assert.strictEqual(kept[0]!.provider, 'agy');
  assert.strictEqual(kept[0]!.name, 'New model');
});

test('a new prompt preset survives its reader too, which is why that button always worked', () => {
  const kept = chatPromptPresetsFrom([freshPromptRow([])]);

  assert.strictEqual(kept.length, 1);
  assert.strictEqual(kept[0]!.name, 'New prompt');
});

test('the reader still refuses a model row with no provider — the rule did not move', () => {
  // The fix is the SEED, not a loosened reader: a preset that names no row names nothing that can
  // answer, and the two button rows above the composer would render it as a button that does nothing.
  assert.deepStrictEqual(chatModelPresetsFrom([freshModelRow([], '')]), []);
});

test('two new rows in a row do not share an id', () => {
  const first = freshModelRow([], 'agy');
  const second = freshModelRow([first as { id: string }], 'agy');

  assert.notStrictEqual(first['id'], second['id'], 'a second press produced a row that shadows the first');
});

/**
 * The two decisions the HOST used to make inline, where no test could reach them.
 *
 * <p>The plan round said so in as many words (codex, Major): a helper test can pass while the
 * command is still wired to the old seed. So the decisions moved here, beside the readers whose
 * rules they have to respect.</p>
 */
test('adding a model prunes what the old defect wrote, and seeds a row the reader keeps', () => {
  const rows = [
    { id: 'dead-1', name: 'New model', provider: '', model: '' },
    { id: 'real', name: 'Mine', provider: 'agy', model: 'gemini-3.8-flash-low' },
    { id: 'dead-2', name: 'New model', provider: '', model: '' },
  ];

  const written = modelRowsAfterAdd(rows, 'agy');

  assert.notStrictEqual(written, undefined);
  assert.deepStrictEqual(written!.map((row: Record<string, unknown>) => row['id']).slice(0, 1), ['real'], 'the dead rows survived the write');
  assert.strictEqual(chatModelPresetsFrom(written!).length, 2, 'the row that was added is not one the reader keeps');
});

test('adding a model with nothing that can chat writes NOTHING, so the list is not touched', () => {
  // The alternative is the defect again: a write nobody can see. Refusing is the caller's cue to say
  // why, and saying nothing while writing is what this whole fix is about.
  assert.strictEqual(modelRowsAfterAdd([{ id: 'real', name: 'Mine', provider: 'agy', model: '' }], ''), undefined);
});

test('a list that is already clean is still written with its rows in order', () => {
  const rows = [{ id: 'a', name: 'A', provider: 'agy', model: '' }, { id: 'b', name: 'B', provider: 'codex', model: '' }];

  assert.deepStrictEqual(modelRowsAfterAdd(rows, 'agy')!.map((row: Record<string, unknown>) => row['id']).slice(0, 2), ['a', 'b']);
});

test('ticking one prompt as main unticks every other, in what is SAVED', () => {
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }, { id: 'b', name: 'B', text: 'b', main: false }];

  assert.deepStrictEqual(promptRowsAfterMain(rows, 'b', true).map((row: Record<string, unknown>) => row['main']), [false, true]);
});

test('unticking the only main one is refused, because something has to answer a capture', () => {
  // The plan round (gemini, Major): `onlyOneMain` marks nothing when nothing is ticked, and
  // `mainPrompt` then falls back to the FIRST prompt — so the page would show no tick while the first
  // prompt is quietly the one being sent. A checkbox that cannot be unticked says the truth instead.
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }, { id: 'b', name: 'B', text: 'b', main: false }];

  assert.deepStrictEqual(promptRowsAfterMain(rows, 'a', false).map((row: Record<string, unknown>) => row['main']), [true, false]);
});

test('unticking one that is not the main one changes nothing it should not', () => {
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }, { id: 'b', name: 'B', text: 'b', main: false }];

  assert.deepStrictEqual(promptRowsAfterMain(rows, 'b', false).map((row: Record<string, unknown>) => row['main']), [true, false]);
});

test('a main edit naming a prompt that is not there changes NOTHING', () => {
  // The code round, from codex and gemini independently: a webview message can name a row that was
  // removed in another window, and `rows.map(row => ({...row, main: row.id === id}))` then sets every
  // flag to false. The page shows no tick while `mainPrompt` falls back to the first prompt — the
  // exact state the untick refusal above exists to prevent, reached through a different door.
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }, { id: 'b', name: 'B', text: 'b', main: false }];

  assert.strictEqual(promptRowsAfterMain(rows, 'gone', true), rows, 'an unknown id rewrote the list');
  assert.strictEqual(promptRowsAfterMain(rows, 'gone', false), rows, 'an unknown id rewrote the list');
});

test('a refused untick returns the list ITSELF, so the caller can skip a write that changes nothing', () => {
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }];

  assert.strictEqual(promptRowsAfterMain(rows, 'a', false), rows, 'a no-op still produced a new list to write');
});

test('a model row cannot be built without the reviewer that answers it', () => {
  // The code round (codex): `freshPreset('model', rows)` was callable and defaulted the provider to
  // an empty string, which is the original defect available to the next caller. Two factories now,
  // and the model one cannot be called without the row it answers through.
  const row = freshModelRow([], 'agy');

  assert.strictEqual(chatModelPresetsFrom([row]).length, 1);
  assert.strictEqual(chatPromptPresetsFrom([freshPromptRow([])]).length, 1);
});
