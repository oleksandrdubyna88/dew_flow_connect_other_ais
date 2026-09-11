import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  EditorText,
  WHOLE_FILE_LIMIT,
  confirmWholeFile,
  passageFromEditor,
} from '../editorPassage';
import {
  CLAUDE_PANEL_VIEW_TYPE,
  TabSnapshot,
  isClaudeSessionTab,
  isOrdinaryEditorTab,
  sourceSession,
} from '../sessionKey';

/**
 * The second door: Ctrl+Alt+A and the right-click item on an ordinary file.
 *
 * <p>Asked for as *"хочу чтоб можно было через Ctrl+Alt+A (и правой кнопкой чат) в обычных окнах
 * тоже вызывать. например на md файлах, cs файлах"*.</p>
 */

const editor = (over: Partial<EditorText> = {}): EditorText => ({
  whole: 'the whole document\nand a second line',
  selected: '',
  name: 'notes.md',
  ...over,
});

const tab = (over: Partial<TabSnapshot> = {}): TabSnapshot => ({
  key: {},
  label: 'notes.md',
  viewType: '',
  scheme: 'file',
  ...over,
});

// ---------------------------------------------------------------------------------------------
// What the passage IS.
// ---------------------------------------------------------------------------------------------

test('a selection is the passage', () => {
  const passage = passageFromEditor(editor({ selected: 'the paragraph in question' }));

  assert.deepStrictEqual(passage, { ok: true, text: 'the paragraph in question', whole: false });
});

test('nothing selected sends the whole file — the operator chose that over a refusal', () => {
  const passage = passageFromEditor(editor());

  assert.strictEqual(passage.ok && passage.whole, true, 'an empty selection did not fall back to the file');
  assert.strictEqual(passage.ok && passage.text, 'the whole document\nand a second line');
});

test('a stray drag is not a choice of passage', () => {
  // Whitespace selected by accident must not become the question — it would open a conversation
  // about nothing and bill for it.
  const passage = passageFromEditor(editor({ selected: '   \n  ' }));

  assert.strictEqual(passage.ok && passage.whole, true, 'whitespace was taken as the passage');
});

test('no editor, and an empty one, are refusals that say what to do', () => {
  const none = passageFromEditor(undefined);
  const empty = passageFromEditor(editor({ whole: '\n  \n' }));

  assert.strictEqual(none.ok, false);
  assert.match(none.ok === false ? none.refusal : '', /Open a file/, 'the refusal does not say what to do');
  assert.strictEqual(empty.ok, false);
  assert.match(empty.ok === false ? empty.refusal : '', /notes\.md is empty/, 'the refusal does not name the file');
});

test('a big file is ASKED about, and a small one is not', () => {
  const big = editor({ whole: 'x'.repeat(WHOLE_FILE_LIMIT + 1) });

  assert.strictEqual(confirmWholeFile(editor()), '', 'an ordinary file asked for permission');
  assert.match(confirmWholeFile(big), /notes\.md/, 'the question does not name the file');
  assert.match(confirmWholeFile(big), /KB/, 'the question does not say how much it is');
});

// ---------------------------------------------------------------------------------------------
// WHICH tabs are eligible, and that the two doors do not overlap.
// ---------------------------------------------------------------------------------------------

test('a file and an unsaved buffer are ordinary editors; nothing else is', () => {
  assert.strictEqual(isOrdinaryEditorTab(tab({ scheme: 'file' })), true);
  assert.strictEqual(isOrdinaryEditorTab(tab({ scheme: 'untitled' })), true, 'an unsaved buffer was refused');
  // By SCHEME, so an Output pane, a settings editor and a git revision are excluded structurally
  // rather than by a list of names somebody has to keep up to date.
  for (const scheme of ['output', 'vscode-settings', 'git', '']) {
    assert.strictEqual(isOrdinaryEditorTab(tab({ scheme })), false, `${scheme} was taken for a file`);
  }
});

test('the two doors do not overlap', () => {
  const claude = tab({ viewType: CLAUDE_PANEL_VIEW_TYPE, scheme: '' });
  const file = tab();

  assert.strictEqual(isClaudeSessionTab(claude), true);
  assert.strictEqual(isOrdinaryEditorTab(claude), false, 'the file door would take a Claude panel');
  assert.strictEqual(isClaudeSessionTab(file), false, 'the Claude door would take a file');
});

test('the file door keeps every rule the Claude door keeps', () => {
  // The point of widening `sourceSession` rather than writing a second one: two files named
  // README.md in two folders collide exactly the way two tabs named `main` do.
  const one = tab({ label: 'README.md' });
  const two = tab({ label: 'README.md' });
  const known = [{ key: one.key, label: 'README.md' }];

  assert.deepStrictEqual(
    sourceSession(one, [one, two], known, isOrdinaryEditorTab),
    { kind: 'existing', key: one.key, label: 'README.md' },
    'a second capture from the same file did not continue its conversation',
  );
  // The namesake whose own tab is still open must NOT be re-keyed onto — that is the silent
  // wrong-conversation this module exists to prevent.
  assert.deepStrictEqual(
    sourceSession(two, [one, two], known, isOrdinaryEditorTab),
    { kind: 'new', key: two.key, label: 'README.md' },
    'a namesake file stole the other one\'s conversation',
  );
});

test('the Claude door is still the default, so nothing written before this moved', () => {
  const claude = tab({ viewType: CLAUDE_PANEL_VIEW_TYPE, scheme: '' });

  assert.deepStrictEqual(sourceSession(claude, [claude], []), { kind: 'new', key: claude.key, label: 'notes.md' });
  assert.strictEqual(sourceSession(tab(), [tab()], []), undefined, 'a file reached the Claude door by default');
});

// ---------------------------------------------------------------------------------------------
// The manifest is part of the feature.
// ---------------------------------------------------------------------------------------------

test('the chord and the menu item reach an ordinary editor', () => {
  // Every test above can pass while `package.json` still gates both doors on Claude's panel — and
  // then nothing at all happens on a .md file. (codex, the plan round.)
  const manifest = JSON.parse(readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: {
      keybindings: { command: string; when: string }[];
      menus: Record<string, { command: string; when?: string }[]>;
    };
  };
  const chords = manifest.contributes.keybindings.filter((one) => one.command === 'coai.chatWithOtherAi');
  const editorMenu = manifest.contributes.menus['editor/context'] ?? [];

  assert.ok(
    chords.some((one) => one.when.includes('editorTextFocus')),
    'the chord is still bound only inside the assistant panel',
  );
  assert.ok(
    editorMenu.some((one) => one.command === 'coai.chatWithOtherAi'),
    'the right-click item is offered nowhere but the panel',
  );
  assert.ok(
    chords.some((one) => one.when.includes('claudeVSCodePanel')),
    'the panel lost the chord it already had',
  );
});
