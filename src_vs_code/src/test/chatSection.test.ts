import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PanelState, panelHtml } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { CHAT_AUTO_SEND, ChatSettings, chatSettingsFrom } from '../chatSettings';
import { escapeHtml } from '../escapeHtml';

/**
 * The `Chat other AIs` section: the four settings, edited where everything else is edited.
 *
 * <p>Asked for in the owner's own words — *"в меню справа нужно добавть мультилайн инпут, где чел
 * вводит промт, с которым скопированное должно уезжать в выбранную ии, по умолчанию одно слово
 * Explain"* and *"какую модель исп, аналогично ревьюверам"*. Until this section existed the four
 * `coai.chat*` keys could only be changed by hand in `settings.json`, which is where settings go
 * to be forgotten.</p>
 *
 * <p>The tests are on the MARKUP rather than on a `vscode` call, exactly like every other section
 * here: what the panel renders is the whole of what it decides, and the write path behind
 * `data-setting` is one generic route that `settingWrite.test.ts` already covers.</p>
 */

const chat: ChatSettings = { prompt: 'Explain', language: 'en', autoSend: 'keyboard', model: '' };

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'antigravity',
    runtime: 'antigravity',
    model: 'gemini-3.7-flash-high',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['chat'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  chat,
  ...over,
});

/** Just the section, so an assertion cannot pass on something another section happens to render. */
function chatSection(html: string): string {
  const start = html.indexOf('data-section="chat"');
  assert.ok(start > 0, 'the panel renders no Chat other AIs section at all');
  const end = html.indexOf('</details>', start);

  return html.slice(start, end);
}

test('the prompt is a MULTILINE box, because the prompt is not always one word', () => {
  // The default is one word — `Explain` — precisely so that nobody has to maintain it. What is
  // asked for is the room to write a paragraph when the one word is not enough.
  const body = chatSection(panelHtml(state(), 'n0nce'));

  assert.match(body, /<textarea[^>]*data-setting="chatPrompt"/, 'the prompt is not a textarea');
  assert.ok(body.includes('>Explain</textarea>'), 'the box does not hold the prompt it will send');
});

test('a prompt somebody typed comes back, escaped rather than executed', () => {
  const body = chatSection(panelHtml(state({
    chat: { ...chat, prompt: 'Explain this <b>simply</b> & in short' },
  }), 'n0nce'));

  assert.ok(body.includes('Explain this &lt;b&gt;simply&lt;/b&gt; &amp; in short'), 'the prompt reached the page raw');
});

test('the answer language is its own control, and not the language of these pages', () => {
  // `coai.helpLanguage` is English on the owner's machine, which is exactly why borrowing it would
  // have delivered English explanations — the one thing this feature exists to avoid.
  const body = chatSection(panelHtml(state({ chat: { ...chat, language: 'ru' } }), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatLanguage"/);
  assert.match(body, /<option value="ru" selected>/, 'the chosen language is not the selected one');
});

test('who presses send is a choice, and its three values are all offered', () => {
  const body = chatSection(panelHtml(state(), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatAutoSend"/);
  // From the catalog, not retyped: a fourth value added to `CHAT_AUTO_SEND` and not rendered would
  // leave a hand-written list green while the control lost an option. (codex and gemini.)
  for (const value of CHAT_AUTO_SEND) {
    assert.ok(body.includes(`value="${value}"`), `${value} is not on offer`);
  }
  assert.match(body, /<option value="keyboard" selected>/, 'the default is not the selected one');
});

test('the model list is the models that can actually answer, with its own model named', () => {
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'second', model: 'gemini-3.7-pro' })],
  }), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatModel"/);
  assert.ok(body.includes('gemini-3.7-flash-high'), 'a row is offered without saying which model it is');
  assert.ok(body.includes('gemini-3.7-pro'), 'the second row is missing');
});

test('an unset model reads as the first one offered, not as an empty box', () => {
  const body = chatSection(panelHtml(state({ vendors: [vendor()] }), 'n0nce'));

  assert.match(body, /<option value=""[^>]*selected>[^<]*first/i, 'an empty setting looks like a broken control');
});

test('a reviewer the chat cannot speak to is named in the section, not silently absent', () => {
  // The same rule the command follows: a person who configured `codex` and finds it missing from a
  // picker cannot tell a bug from a policy.
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'codex', runtime: 'codex' })],
  }), 'n0nce'));

  assert.ok(body.includes('codex'), 'the refused reviewer is not mentioned at all');
  assert.match(body, /can only speak to/, 'it is mentioned without saying why it cannot answer');
});

test('a panel with no chat settings at all still renders the section, on the defaults', () => {
  // Sixteen test fixtures predate this field. Absent means the defaults, exactly as it does for the
  // Team servers beside it — the alternative was sixteen whole-file CRLF diffs nobody can review.
  const body = chatSection(panelHtml(state({ chat: undefined }), 'n0nce'));

  assert.ok(body.includes('>Explain</textarea>'), 'the default prompt is not what an absent setting shows');
});

test('a model the settings NAME and which cannot answer is shown as chosen, and as unable', () => {
  // Otherwise the browser selects the first option and the panel quietly reads "the first one that
  // can answer" while `settings.json` says `codex` — the section describing a state that is not the
  // one the command will refuse. Disabled, so it cannot be re-picked once it is left. (codex,
  // gemini and local, one finding from three directions.)
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'codex', runtime: 'codex' })],
    chat: { ...chat, model: 'codex' },
  }), 'n0nce'));

  assert.match(body, /<option value="codex"[^>]*selected[^>]*disabled|<option value="codex"[^>]*disabled[^>]*selected/,
    'the configured model is not shown as the chosen one');
  assert.ok(!/<option value=""[^>]*selected/.test(body), 'the panel claims no model is chosen while one is');
});

test('the panel and the command read the same settings through the same reader', () => {
  // There is no extension-host harness here, so this is the closest a test can get to "what the
  // section shows is what the conversation will use": both sides are driven from ONE `settings.json`
  // and the rendered values are compared against the reader's own answer. (codex, the plan round.)
  const file: Record<string, unknown> = {
    chatPrompt: 'Объясни по-русски, коротко',
    chatLanguage: 'ru',
    chatAutoSend: 'always',
    chatModel: 'second',
  };
  const asCommandReadsIt = chatSettingsFrom((key) => file[key]);
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'second' })],
    chat: asCommandReadsIt,
  }), 'n0nce'));

  assert.ok(body.includes(`>${escapeHtml(asCommandReadsIt.prompt)}</textarea>`), 'the box shows a different prompt');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.language}" selected>`), 'a different language is selected');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.autoSend}" selected>`), 'a different send rule is selected');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.model}" selected>`), 'a different model is selected');
});
