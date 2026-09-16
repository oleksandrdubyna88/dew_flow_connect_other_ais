import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PANEL_COMMANDS, VSCODE_COMMAND_FOR, panelHtml, staticKey, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULT_VENDORS } from '../vendors';
import type { Phrase } from '../phrases';
import { phrasesFrom } from '../phrases';
import { DEFAULTS } from '../settingsShape';
import { phrasesHtml } from '../phrasesPage';

/**
 * The Phrases section: the half of this feature a person actually touches.
 *
 * <p>The section is the feature. `coai.editChatPresets` shipped registered, in no menu and named in
 * no view, and the deviation recorded against it is the operator saying they read the sidebar as the
 * feature and could not find any of it. So *Edit phrases…* being reachable from here is a test, not
 * a hope.</p>
 */

const PHRASES = phrasesFrom([
  { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy' },
  { id: 'b', name: 'Check', text: 'check that it works' },
]);

const state = (phrases: readonly Phrase[] = PHRASES): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  agyModels: [],
  codexModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['phrases'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  phrases,
});

/** The section alone, so an assertion cannot pass on something another section rendered. */
function phrasesSection(html: string): string {
  const from = html.indexOf('data-section="phrases"');
  assert.ok(from > -1, 'there is no Phrases section in the panel');

  return html.slice(from, html.indexOf('</details>', from));
}

test('every phrase is a button, labelled with its name', () => {
  const section = phrasesSection(panelHtml(state(), 'NONCE'));

  assert.match(section, /data-command="copyPhrase"[^>]*data-id="a"/, 'the first phrase has no button');
  assert.match(section, /data-command="copyPhrase"[^>]*data-id="b"/, 'the second phrase has no button');
  assert.match(section, />Ship it</, 'the button is not labelled with the name of the phrase');
});

test('a button carries the id and never the words', () => {
  const section = phrasesSection(panelHtml(state(), 'NONCE'));

  assert.ok(!section.includes('make a pr, accept it, deploy'.slice(0, 12) + '"'),
    'the phrase itself was written into the button, so it now exists in two places that can disagree');
  assert.match(section, /data-id="a"/, 'the button does not carry the id the host looks up');
});

test('hovering a phrase shows more than its first line, so two of them can be told apart', () => {
  const twins = phrasesFrom([
    { id: 'a', name: 'Deploy', text: 'ship it\nto staging' },
    { id: 'b', name: 'Deploy', text: 'ship it\nto production' },
  ]);
  const section = phrasesSection(panelHtml(state(twins), 'NONCE'));

  assert.match(section, /to staging/, 'the tooltip stops at the first line, so two phrases look identical');
  assert.match(section, /to production/, 'the second phrase cannot be told from the first');
});

test('a phrase containing markup is shown as text in the panel too', () => {
  const nasty = phrasesFrom([{ id: 'x', name: '<img src=x onerror=alert(1)>', text: '"><script>alert(2)</script>' }]);
  const section = phrasesSection(panelHtml(state(nasty), 'NONCE'));

  assert.ok(!section.includes('<img src=x'), 'a phrase name was written into the panel as markup');
  assert.ok(!section.includes('<script>alert(2)'), 'a phrase body was written into the panel as markup');
  assert.match(section, /&lt;img/, 'the name is not shown to the person who wrote it');
});

test('an empty list says what the section is for, and still offers the way in', () => {
  const section = phrasesSection(panelHtml(state([]), 'NONCE'));

  assert.match(section, /No phrases yet/, 'an empty section renders blank, which reads as broken');
  assert.match(section, /data-command="editPhrases"/, 'an empty section offers no way to make the first phrase');
});

test('the section is a way into the tab that edits the list', () => {
  // The presets tab shipped reachable only from the command palette. Twice guarded there; once here.
  const section = phrasesSection(panelHtml(state(), 'NONCE'));

  assert.match(section, /data-command="editPhrases"/, 'there is no way into the phrases tab');
});

test('both commands the section posts are declared, and the tab opener is a real command', () => {
  assert.ok((PANEL_COMMANDS as readonly string[]).includes('copyPhrase'), 'copyPhrase is not declared');
  assert.ok((PANEL_COMMANDS as readonly string[]).includes('editPhrases'), 'editPhrases is not declared');
  assert.equal(VSCODE_COMMAND_FOR.editPhrases, 'coai.editPhrases', 'the section opens the wrong command');
});

test('editing a phrase repaints a panel that is already open', () => {
  // Without `state.phrases` in staticKey the section is frozen for the life of the panel while the
  // tab underneath it works perfectly — the bug two other entries in that list were added for.
  const before = staticKey(state());
  const renamed = phrasesFrom([
    { id: 'a', name: 'Ship it now', text: 'make a pr, accept it, deploy' },
    { id: 'b', name: 'Check', text: 'check that it works' },
  ]);
  const rewritten = phrasesFrom([
    { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy, and watch the logs' },
    { id: 'b', name: 'Check', text: 'check that it works' },
  ]);

  assert.notEqual(staticKey(state(renamed)), before, 'renaming a phrase leaves the open panel showing the old name');
  assert.notEqual(staticKey(state(rewritten)), before, 'rewriting a phrase leaves the open panel showing the old words');
  assert.notEqual(staticKey(state([])), before, 'removing every phrase leaves the buttons on screen');
});

test('the button says Copied only when the host says the write landed', () => {
  const page = panelHtml(state(), 'NONCE');

  assert.match(page, /message\?\.type === 'copied'/, 'the page never hears that a copy landed');
  assert.match(page, /'Copied'/, 'no button ever confirms itself');
  assert.ok(!/data-command="copyPhrase"[^>]*onclick/.test(page), 'the button changes itself on the click, before the write');
});

// ---------- the button and the box are the same colour (issue #295) ----------

test('a phrase is the same colour on its button as in the editor', () => {
  // THE assertion that guards this feature. Each surface builds its own allocator from its own
  // state, so a per-surface test can be green while the two disagree — which is the defect the
  // allocator exists to prevent and the lesson from the Consultant change immediately before this
  // one. (gemini, the plan round.)
  const page = phrasesHtml({ rows: PHRASES.map((p) => ({ id: p.id, name: p.name, text: p.text })), uiScale: 1 }, 'NONCE');
  const panel = panelHtml(state(), 'n0nce');

  for (const phrase of PHRASES) {
    const inEditor = new RegExp(`data-id="${phrase.id}" style="border-left-color:([^"]+)"`).exec(page);
    const onButton = new RegExp(`data-command="copyPhrase" data-id="${phrase.id}"[^>]*style="border-left-color:([^"]+)"`).exec(panel);

    assert.ok(inEditor, `${phrase.id} has no colour in the editor`);
    assert.ok(onButton, `${phrase.id}'s button has no colour`);
    assert.equal(onButton[1], inEditor[1], `${phrase.id} is one colour in the editor and another on its button`);
  }
});

test('a phrase keeps its colour when the two surfaces are one phrase apart', () => {
  // The editor and the sidebar are separate webviews refreshed on their own clocks, so one can hold
  // a list the other has not caught up with. A shared phrase must not change colour because of a
  // phrase it is not. (gemini, the plan round — the half of that finding that was right.)
  const shared = PHRASES[0]!;
  const withExtra = [...PHRASES, ...phrasesFrom([{ id: 'c', name: 'Later', text: 'and again' }])];

  const small = panelHtml(state(PHRASES), 'n0nce');
  const large = panelHtml(state(withExtra), 'n0nce');

  const colourIn = (html: string): string => {
    const found = new RegExp(`data-command="copyPhrase" data-id="${shared.id}"[^>]*style="border-left-color:([^"]+)"`).exec(html);
    assert.ok(found, `${shared.id} has no colour`);

    return found[1]!;
  };

  assert.equal(colourIn(large), colourIn(small), 'a phrase changed colour because another one was added beside it');
});

test('a phrase button is a frame with an edge, not a bare button', () => {
  const css = panelHtml(state(), 'n0nce').split('<style>')[1]!.split('</style>')[0]!.replace(/\s+/gu, ' ');
  const rule = css.split('.phrases .run {')[1]?.split('}')[0] ?? '';

  assert.ok(rule.length > 0, 'the .phrases .run rule is gone');
  assert.match(rule, /border-left: 3px solid/, 'the button has no left edge, so its inline colour paints nothing');
});
