import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PanelState } from '../panelView';
import { everyPageHtml, paneIn } from './panelPages';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { CHAT_AUTO_SEND, ChatSettings, chatSettingsFrom } from '../chatSettings';
import { ModelPreset, PromptPreset } from '../chatPresets';
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

const chat: ChatSettings = {
  prompt: 'Explain',
  promptChoice: '',
  prompts: [],
  models: [],
  conflicts: [],
  unreadable: [],
  language: 'en',
  autoSend: 'keyboard',
  model: '',
  modelName: '',
};

/** A saved MODEL, which is what the chat's picker offers now — no reviewer anywhere near it. */
function saved(id: string, name: string, over: Partial<ModelPreset> = {}): ModelPreset {
  return {
    id, name, main: false, runtime: 'antigravity', model: 'gemini-3.7-flash-high',
    executablePath: '', baseUrl: '', ...over,
  };
}

function preset(id: string, name: string): PromptPreset {
  return { id, name, text: `${name} — the words that are actually sent`, main: id === 'p1' };
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
  cardPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  chat,
  ...over,
});

/**
 * Just the section, so an assertion cannot pass on something another section happens to render — the new page's Chat
 * tab (E4.6b; E5.1 step 3), which draws the current section's sending fields as they are.
 */
function chatSection(html: string): string {
  return paneIn(html, 'chat');
}

test('what to ask is CHOSEN from the saved prompts, not typed into a box that holds one', () => {
  // Step 5 of the presets plan, which shipped without this half: the CRUD lives in the tab, so the
  // sidebar's job shrank to picking one — and a box that edits a copy of a preset is a second place
  // for the same words to live, which is how the two of them start disagreeing.
  const body = chatSection(everyPageHtml(state({
    chat: { ...chat, prompts: [preset('p1', 'Explain'), preset('p2', 'What would you answer?')] },
  }), 'n0nce'));

  assert.doesNotMatch(body, /<textarea[^>]*data-setting="chatPrompt"/, 'the box is still there beside the picker');
  assert.match(body, /<select[^>]*data-setting="chatPromptChoice"/, 'there is no prompt picker');
  assert.ok(body.includes('>What would you answer?</option>'), 'a saved prompt is not on offer by name');
});

test('the picker names the prompts, and the hint shows the words that will actually be sent', () => {
  // "A prompt nobody can see is a prompt nobody corrects" is this section's own reason for existing.
  // The names are what a person chooses between; the TEXT is what is billed, so it stays visible.
  const body = chatSection(everyPageHtml(state({
    chat: { ...chat, prompt: 'Explain this <b>simply</b> & in short', prompts: [preset('p1', 'Short')] },
  }), 'n0nce'));

  assert.ok(body.includes('Explain this &lt;b&gt;simply&lt;/b&gt; &amp; in short'), 'the prompt reached the page raw');
});

test('the chosen prompt is the selected option, and the main one is what an empty choice means', () => {
  const chosen = chatSection(everyPageHtml(state({
    chat: { ...chat, promptChoice: 'p2', prompts: [preset('p1', 'A'), preset('p2', 'B')] },
  }), 'n0nce'));
  assert.match(chosen, /<option value="p2" selected>/, 'the saved choice is not the selected one');

  const unchosen = chatSection(everyPageHtml(state({
    chat: { ...chat, prompts: [preset('p1', 'A')] },
  }), 'n0nce'));
  assert.match(unchosen, /<option value=""[^>]*selected>[^<]*main/i, 'an unset choice looks like a broken control');
});

test('the answer language is its own control, and not the language of these pages', () => {
  // `coai.helpLanguage` is English on the owner's machine, which is exactly why borrowing it would
  // have delivered English explanations — the one thing this feature exists to avoid.
  const body = chatSection(everyPageHtml(state({ chat: { ...chat, language: 'ru' } }), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatLanguage"/);
  assert.match(body, /<option value="ru" selected>/, 'the chosen language is not the selected one');
});

test('who presses send is a choice, and its three values are all offered', () => {
  const body = chatSection(everyPageHtml(state(), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatAutoSend"/);
  // From the catalog, not retyped: a fourth value added to `CHAT_AUTO_SEND` and not rendered would
  // leave a hand-written list green while the control lost an option. (codex and gemini.)
  for (const value of CHAT_AUTO_SEND) {
    assert.ok(body.includes(`value="${value}"`), `${value} is not on offer`);
  }
  assert.match(body, /<option value="keyboard" selected>/, 'the default is not the selected one');
});

test('a saved model on a runtime the chat cannot speak to is named, not silently absent', () => {
  // The rule is unchanged; only what it is about. A person who saved a model and finds the picker
  // silently missing it cannot tell a bug from a policy.
  const body = chatSection(everyPageHtml(state({
    chat: { ...chat, models: [saved('m1', 'Fast'), saved('m2', 'On a local engine', { runtime: 'local' })] },
  }), 'n0nce'));

  assert.ok(body.includes('On a local engine'), 'the refused one is not mentioned at all');
  assert.match(body, /can only speak to/, 'it is mentioned without saying why it cannot answer');
});

test('a panel with no chat settings at all still renders the section, on the defaults', () => {
  // Sixteen test fixtures predate this field. Absent means the defaults, exactly as it does for the
  // Team servers beside it — the alternative was sixteen whole-file CRLF diffs nobody can review.
  const body = chatSection(everyPageHtml(state({ chat: undefined }), 'n0nce'));

  assert.ok(body.includes('<div class="hint">Explain</div>'), 'the default prompt is not what an absent setting shows');
});

test('the panel and the command read the same settings through the same reader', () => {
  // There is no extension-host harness here, so this is the closest a test can get to "what the
  // section shows is what the conversation will use": both sides are driven from ONE `settings.json`
  // and the rendered values are compared against the reader's own answer. (codex, the plan round.)
  const file: Record<string, unknown> = {
    chatPrompt: 'Объясни по-русски, коротко',
    chatLanguage: 'ru',
    chatAutoSend: 'always',
    chatModel: 'm2',
    chatModelPresets: [
      { id: 'm1', name: 'Architect', runtime: 'antigravity', model: 'gemini-3.7-flash-high' },
      { id: 'm2', name: 'Fast', runtime: 'antigravity', model: 'gemini-3.8-flash-low' },
    ],
  };
  const asCommandReadsIt = chatSettingsFrom((key) => file[key]);
  const body = chatSection(everyPageHtml(state({ chat: asCommandReadsIt }), 'n0nce'));

  assert.ok(body.includes(`<div class="hint">${escapeHtml(asCommandReadsIt.prompt)}</div>`), 'the section shows a different prompt');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.language}" selected>`), 'a different language is selected');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.autoSend}" selected>`), 'a different send rule is selected');
  // The model is a radio per row on Chat (E4.6b); the one the reader chose is the one checked.
  assert.ok(body.includes(`value="${asCommandReadsIt.model}" data-chp-main checked`), 'a different model is chosen');
});

test('a choice naming a prompt that was deleted is SHOWN as chosen, and as gone', () => {
  // The plan gate, from two vendors independently (local: Blocking, codex: Major): without this the
  // browser falls back to the first option, the section reads "The main one" while the setting still
  // holds a dead id, and the person sends words they did not choose without being told. The model
  // select has had exactly this option since three reviewers asked for it on one round; the prompt
  // picker shipped without it.
  const body = chatSection(everyPageHtml(state({
    chat: { ...chat, promptChoice: 'deleted-one', prompts: [preset('p1', 'A')] },
  }), 'n0nce'));
  const picker = body.slice(body.indexOf('data-setting="chatPromptChoice"'), body.indexOf('</select>'));

  assert.match(picker, /<option value="deleted-one"[^>]*selected[^>]*disabled|<option value="deleted-one"[^>]*disabled[^>]*selected/,
    'a deleted prompt is not shown as the chosen one');
  assert.ok(!/<option value=""[^>]*selected/.test(picker), 'the picker claims the main one is chosen while a dead id is saved');
});
