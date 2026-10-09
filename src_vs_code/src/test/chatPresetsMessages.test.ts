import assert from 'node:assert/strict';
import { test } from 'node:test';
import { vendorOfPreset } from '../chatPresets';
import { editRepaints, editedRows, presetEdit, presetSettlesAs } from '../chatPresetsMessages';

/**
 * What a message from a page that edits the presets MEANS, decided without a host — the same split `chatMessages.ts`
 * makes for the chat page and for the same reason: the module that maps a webview message to an action is otherwise the
 * one no unit test can reach.
 *
 * <p>These sat beside the Chat presets tab's markup tests until E5.1 step 4 of research/PLAN_one_model_catalog.md deleted
 * the tab; the parser is the one Chat on the Settings page posts through (`chatTabEmbed.ts`), and what the tab DREW is
 * asked of that place in `chatOnTheNewPage.test.ts`.</p>
 */

test('an edit names a list, an id, a field and a value', () => {
  assert.deepStrictEqual(
    presetEdit({ type: 'edit', list: 'prompt', id: 'p1', field: 'name', value: 'Explain it' }),
    { kind: 'edit', list: 'prompt', id: 'p1', field: 'name', value: 'Explain it' },
  );
  // BOTH lists carry the tick now: the prompts' says which one a capture sends with, the models'
  // which one it opens on. It used to be refused for the model list, because no model row had one.
  assert.deepStrictEqual(
    presetEdit({ type: 'edit', list: 'model', id: 'm1', field: 'main', value: true }),
    { kind: 'edit', list: 'model', id: 'm1', field: 'main', value: true },
  );
  // And it is still a BOOLEAN on both. A string through the same field would be written straight
  // into the rule that decides which row is the one.
  assert.deepStrictEqual(
    presetEdit({ type: 'edit', list: 'model', id: 'm1', field: 'main', value: 'yes' }),
    { kind: 'ignore' },
  );
});

test('a field this page does not have is not an edit', () => {
  for (const field of ['id', 'constructor', '__proto__', '', 'nope']) {
    assert.deepStrictEqual(presetEdit({ type: 'edit', list: 'prompt', id: 'p', field, value: 'x' }),
      { kind: 'ignore' }, `${field} was accepted as a field`);
  }
});

test('add and remove name their list, and nothing else', () => {
  assert.deepStrictEqual(presetEdit({ type: 'add', list: 'model' }), { kind: 'add', list: 'model' });
  assert.deepStrictEqual(presetEdit({ type: 'remove', list: 'prompt', id: 'p2' }),
    { kind: 'remove', list: 'prompt', id: 'p2' });
  assert.deepStrictEqual(presetEdit({ type: 'remove', list: 'prompt' }), { kind: 'ignore' });
  assert.deepStrictEqual(presetEdit({ type: 'add', list: 'nonsense' }), { kind: 'ignore' });
});

test('a message this page does not understand is ignored, not guessed at', () => {
  for (const message of [undefined, null, {}, { type: 'edit' }, 'text', { type: 'nope' }]) {
    assert.deepStrictEqual(presetEdit(message), { kind: 'ignore' });
  }
});

/**
 * Which edits the page must be REDRAWN after.
 *
 * <p>Reported 2026-09-11, with a screenshot of two prompts both ticked as main: *"логично, что мейн
 * может быть один. а оно дает 2 галочки поставить"*. What is SAVED was already right —
 * ticking one row unticks the others before the write — but an edit deliberately does not repaint,
 * because a repaint under a caret is the defect the sidebar's own prompt box had. A checkbox has no
 * caret, and its whole effect is on OTHER rows, so it was the one edit that had to be redrawn and
 * was not. The page therefore showed a state the file never held.</p>
 *
 * <p>Decided here rather than in the page's script: unticking the siblings in the DOM as well would
 * be the same rule written twice, and the second copy is the one that drifts.</p>
 */
test('ticking the main one is redrawn, because its effect is on the rows it is not in', () => {
  assert.strictEqual(editRepaints({ kind: 'edit', list: 'prompt', id: 'p1', field: 'main', value: true }), true);
});

test('typing is NOT redrawn, which is the rule this exception is carved out of', () => {
  for (const field of ['name', 'text', 'provider', 'model', 'startingPrompt'] as const) {
    assert.strictEqual(
      editRepaints({ kind: 'edit', list: 'prompt', id: 'p1', field, value: 'x' }),
      false,
      `${field} redraws the page under a caret`,
    );
  }
});

test('unticking is redrawn too, so the list cannot be left showing none', () => {
  assert.strictEqual(editRepaints({ kind: 'edit', list: 'prompt', id: 'p1', field: 'main', value: false }), true);
});

// Adding and removing are NOT decided here any more. The code round was right that a whole-command
// policy in the page parser, consulted by the host for edits only, is a rule with a test and no
// effect: changing what it says about `add` would move a test and nothing else. Whether an add
// redraws depends on whether it was refused, which only the host knows. (gemini, the code round.)

test('an edit naming a row that is not in the list is a no-op the host can see', () => {
  // CodeRabbit on PR #198: `edited` mapped over the rows and produced a NEW array with identical
  // content, and the host's reference check read that as a change and wrote the file back. The same
  // class as the `main` no-ops the code round found, through the field that is not `main`.
  const rows = [{ id: 'a', name: 'A', text: 'a', main: true }];

  assert.strictEqual(editedRows(rows, { kind: 'edit', list: 'prompt', id: 'gone', field: 'name', value: 'x' }), rows);
  assert.notStrictEqual(editedRows(rows, { kind: 'edit', list: 'prompt', id: 'a', field: 'name', value: 'B' }), rows);
});

test('a recorded preset id resolves to the vendor whose row it belongs in', () => {
  // A chat records the id of the model PRESET in force, because a preset is what a conversation is
  // switched between - and a preset id is a generated string that means nothing beside a list of
  // vendors. The preset knows its runtime, which IS the vendor.
  const vendorOf = vendorOfPreset([
    { id: 'preset-mtwxbymp-4', name: 'Gemini high', runtime: 'antigravity', model: 'gemini-3.8-flash-high', main: true, executablePath: '', baseUrl: '' },
    { id: 'preset-mtwtqr0p-3', name: 'GPT sol', runtime: 'codex', model: 'gpt-5.6-sol', main: false, executablePath: '', baseUrl: '' },
  ]);

  assert.strictEqual(vendorOf('preset-mtwxbymp-4'), 'antigravity');
  assert.strictEqual(vendorOf('preset-mtwtqr0p-3'), 'codex');
  // A preset can be deleted, and the honest answer for a row recorded under one that is gone is the
  // id that was written down - never a guess, and never an empty cell.
  assert.strictEqual(vendorOf('preset-deleted'), 'preset-deleted');
  assert.strictEqual(vendorOf(''), '');
});

test('a typed field settles under its own key; a tick, a pick or a press goes straight through (E4.6b)', () => {
  for (const field of ['name', 'text', 'startingPrompt'] as const) {
    assert.strictEqual(presetSettlesAs({ kind: 'edit', list: 'prompt', id: 'p1', field, value: 'x' }), `prompt/p1/${field}`);
  }
  assert.notStrictEqual(
    presetSettlesAs({ kind: 'edit', list: 'model', id: 'a', field: 'name', value: 'x' }),
    presetSettlesAs({ kind: 'edit', list: 'model', id: 'b', field: 'name', value: 'x' }),
    'two rows typed in one after the other would store only the last',
  );
  for (const command of [
    { kind: 'edit', list: 'prompt', id: 'p1', field: 'main', value: true },
    { kind: 'edit', list: 'model', id: 'm1', field: 'model', value: 'opus' },
    { kind: 'add', list: 'prompt' },
    { kind: 'remove', list: 'model', id: 'm1' },
  ] as const) {
    assert.strictEqual(presetSettlesAs(command), undefined, `${command.kind} waited to settle`);
  }
});
