import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatPresetsHtml, editRepaints, editedRows, presetEdit } from '../chatPresetsPage';

/**
 * The tab where a person keeps their prompts and their models.
 *
 * <p>Everything on it is text THEY wrote, which makes the escaping here the same question it is on
 * the chat page and a different one from the rest of the panel: elsewhere a label comes from a
 * catalog this product shipped or a server it talked to, and here it comes from the person in front
 * of it — who can perfectly well paste a `&lt;script&gt;` into a name to see what happens.</p>
 */

const PROMPTS = [
  { id: 'p1', name: 'Explain', text: 'Explain this', main: true },
  { id: 'p2', name: 'Review', text: 'What is wrong with it?', main: false },
];

const MODELS = [
  { id: 'm1', name: 'Fast', provider: 'antigravity', model: 'gemini-3.8-flash' },
  { id: 'm2', name: 'Deep', provider: 'claude', model: 'opus', startingPrompt: 'Think hard' },
];

const PROVIDERS = [
  { id: 'antigravity', label: 'antigravity', caption: 'local', models: [{ id: 'gemini-3.8-flash', label: 'Flash' }] },
  { id: 'claude', label: 'claude', caption: 'local', models: [{ id: 'opus', label: 'Opus' }] },
];

test('both lists are on the page, each entry named', () => {
  const html = chatPresetsHtml({ prompts: PROMPTS, models: MODELS, providers: PROVIDERS, uiScale: 0 }, 'n0nce');

  assert.match(html, /Explain/);
  assert.match(html, /Review/);
  assert.match(html, /Fast/);
  assert.match(html, /Deep/);
});

test('the prompt editor is large, because reading a long prompt is the point', () => {
  // The operator asked for it in as many words: "окно промта большое, что б можно было легко
  // читать". A prompt is often a paragraph and sometimes several.
  const html = chatPresetsHtml({ prompts: PROMPTS, models: [], providers: [], uiScale: 0 }, 'n0nce');

  assert.match(html, /<textarea[^>]*data-field="text"[^>]*rows="(1[2-9]|[2-9]\d)"/,
    'the prompt box is not big enough to read a prompt in');
});

test('exactly one prompt is ticked as the main one', () => {
  const html = chatPresetsHtml({ prompts: PROMPTS, models: [], providers: [], uiScale: 0 }, 'n0nce');
  const ticked = [...html.matchAll(/data-field="main"[^>]*checked/g)];

  assert.strictEqual(ticked.length, 1, 'the main prompt is not exactly one');
});

test('a model preset offers the providers, and the chosen one is marked', () => {
  const html = chatPresetsHtml({ prompts: [], models: MODELS, providers: PROVIDERS, uiScale: 0 }, 'n0nce');

  assert.match(html, /<option value="claude" selected>/, 'the chosen provider is not marked');
  assert.match(html, /<option value="opus" selected>/, 'the chosen model is not marked');
});

test('each list can be added to and each entry removed', () => {
  const html = chatPresetsHtml({ prompts: PROMPTS, models: MODELS, providers: PROVIDERS, uiScale: 0 }, 'n0nce');

  assert.match(html, /data-add="prompt"/, 'there is no way to add a prompt');
  assert.match(html, /data-add="model"/, 'there is no way to add a model');
  assert.strictEqual((html.match(/data-remove="/g) ?? []).length, 4, 'not every entry can be removed');
});

test('everything a person typed is escaped, in a name and in a prompt alike', () => {
  const html = chatPresetsHtml(
    {
      prompts: [{ id: 'p', name: '<img src=x onerror=alert(1)>', text: '</textarea><script>alert(1)</script>', main: true }],
      models: [{ id: 'm', name: '"><b>', provider: 'x', model: '' }],
      providers: [],
      uiScale: 0,
    },
    'n0nce',
  );

  // The page's OWN markup is not the question — this asserts on what the person typed. An earlier
  // version matched `<b>` anywhere and caught the page's own emphasis in a sentence about the main
  // prompt, which is exactly the kind of assertion that has to be narrowed rather than the code.
  assert.doesNotMatch(html, /<img/i, 'a name reached the page as markup');
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/, 'the name was dropped rather than shown');
  assert.doesNotMatch(html, /value="">\s*<b>/, 'a name broke out of its attribute');
  assert.doesNotMatch(html, /<\/textarea><script>/i, 'a prompt closed its own textarea');
  assert.match(html, /&lt;\/textarea&gt;/, 'the prompt was dropped rather than shown');
});

test('an empty page says what to do rather than showing nothing', () => {
  const html = chatPresetsHtml({ prompts: [], models: [], providers: [], uiScale: 0 }, 'n0nce');

  assert.match(html, /data-add="prompt"/, 'an empty page offers no way to start');
  assert.doesNotMatch(html, /data-remove=/, 'an empty page drew a row to remove');
});

/* ------------------------------------------------------------------------------------------------
 * `presetEdit` — what a message from the page MEANS, decided without a host, the same split
 * `chatMessages.ts` makes for the chat page and for the same reason: the module that maps a webview
 * message to an action is otherwise the one no unit test can reach.
 * ---------------------------------------------------------------------------------------------- */

test('an edit names a list, an id, a field and a value', () => {
  assert.deepStrictEqual(
    presetEdit({ type: 'edit', list: 'prompt', id: 'p1', field: 'name', value: 'Explain it' }),
    { kind: 'edit', list: 'prompt', id: 'p1', field: 'name', value: 'Explain it' },
  );
  assert.deepStrictEqual(
    presetEdit({ type: 'edit', list: 'model', id: 'm1', field: 'main', value: true }),
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
