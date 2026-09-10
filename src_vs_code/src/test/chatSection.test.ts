import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PanelState, panelHtml } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { CHAT_AUTO_SEND, ChatSettings, chatSettingsFrom } from '../chatSettings';
import { PromptPreset } from '../chatPresets';
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
  language: 'en',
  autoSend: 'keyboard',
  model: '',
  modelName: '',
};

function preset(id: string, name: string): PromptPreset {
  return { id, name, text: `${name} — the words that are actually sent`, main: id === 'p1' };
}

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

test('what to ask is CHOSEN from the saved prompts, not typed into a box that holds one', () => {
  // Step 5 of the presets plan, which shipped without this half: the CRUD lives in the tab, so the
  // sidebar's job shrank to picking one — and a box that edits a copy of a preset is a second place
  // for the same words to live, which is how the two of them start disagreeing.
  const body = chatSection(panelHtml(state({
    chat: { ...chat, prompts: [preset('p1', 'Explain'), preset('p2', 'What would you answer?')] },
  }), 'n0nce'));

  assert.doesNotMatch(body, /<textarea[^>]*data-setting="chatPrompt"/, 'the box is still there beside the picker');
  assert.match(body, /<select[^>]*data-setting="chatPromptChoice"/, 'there is no prompt picker');
  assert.ok(body.includes('>What would you answer?</option>'), 'a saved prompt is not on offer by name');
});

test('the picker names the prompts, and the hint shows the words that will actually be sent', () => {
  // "A prompt nobody can see is a prompt nobody corrects" is this section's own reason for existing.
  // The names are what a person chooses between; the TEXT is what is billed, so it stays visible.
  const body = chatSection(panelHtml(state({
    chat: { ...chat, prompt: 'Explain this <b>simply</b> & in short', prompts: [preset('p1', 'Short')] },
  }), 'n0nce'));

  assert.ok(body.includes('Explain this &lt;b&gt;simply&lt;/b&gt; &amp; in short'), 'the prompt reached the page raw');
});

test('the chosen prompt is the selected option, and the main one is what an empty choice means', () => {
  const chosen = chatSection(panelHtml(state({
    chat: { ...chat, promptChoice: 'p2', prompts: [preset('p1', 'A'), preset('p2', 'B')] },
  }), 'n0nce'));
  assert.match(chosen, /<option value="p2" selected>/, 'the saved choice is not the selected one');

  const unchosen = chatSection(panelHtml(state({
    chat: { ...chat, prompts: [preset('p1', 'A')] },
  }), 'n0nce'));
  assert.match(unchosen, /<option value=""[^>]*selected>[^<]*main/i, 'an unset choice looks like a broken control');
});

test('the section opens the tab where prompts and models are actually edited', () => {
  // The CRUD tab shipped with no way into it but the command palette — `coai.editChatPresets` was
  // registered, absent from every menu, and named in no view. A command nobody can reach is a
  // feature nobody has.
  const body = chatSection(panelHtml(state(), 'n0nce'));

  assert.match(body, /data-command="editChatPresets"/, 'there is no way into the presets tab');
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

test('the panel asks for a PROVIDER and then one of its models, as the tab already does', () => {
  // The DoD of `PLAN_provider_then_model.md` says "in the tab and in the panel". The tab got the
  // pair; the panel kept the flat list of rows, and the plan recorded no deviation saying so.
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'second', model: 'gemini-3.7-pro' })],
    chat: { ...chat, model: 'second' },
  }), 'n0nce'));

  assert.match(body, /<select[^>]*data-setting="chatModel"/, 'there is no provider select');
  assert.match(body, /<select[^>]*data-setting="chatModelName"/, 'there is no model select beside it');
  assert.match(body, /<option value="second" selected>/, 'the chosen provider is not the selected one');
});

test('the model select offers the models of the CHOSEN provider, and nobody else\'s', () => {
  // The pair is checked as a pair everywhere else in this feature — `resolveChatPick` refuses a
  // model the provider does not offer — so a picker that offered every provider's models would be
  // offering combinations the command will then refuse by name.
  const body = chatSection(panelHtml(state({
    vendors: [
      vendor({ id: 'agy-row', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }),
      vendor({ id: 'claude-row', runtime: 'claude', model: 'claude-opus-5' }),
    ],
    chat: { ...chat, model: 'claude-row' },
  }), 'n0nce'));
  const models = body.slice(body.indexOf('data-setting="chatModelName"'));

  assert.ok(models.includes('claude-opus-5'), 'the chosen provider\'s own model is not offered');
  assert.ok(!models.includes('gemini-3.7-flash-high'), 'another provider\'s model is offered under this one');
});

test('an unset model name reads as the row\'s own model, not as an empty box', () => {
  const body = chatSection(panelHtml(state({ vendors: [vendor()] }), 'n0nce'));
  const models = body.slice(body.indexOf('data-setting="chatModelName"'));

  assert.match(models, /<option value=""[^>]*selected>/, 'an unset model name looks like a broken control');
});

test('a reviewer the chat cannot speak to is named in the section, not silently absent', () => {
  // The same rule the command follows: a person who configured `codex` and finds it missing from a
  // picker cannot tell a bug from a policy.
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'my-local', runtime: 'local' })],
  }), 'n0nce'));

  assert.ok(body.includes('my-local'), 'the refused reviewer is not mentioned at all');
  assert.match(body, /can only speak to/, 'it is mentioned without saying why it cannot answer');
});

test('a panel with no chat settings at all still renders the section, on the defaults', () => {
  // Sixteen test fixtures predate this field. Absent means the defaults, exactly as it does for the
  // Team servers beside it — the alternative was sixteen whole-file CRLF diffs nobody can review.
  const body = chatSection(panelHtml(state({ chat: undefined }), 'n0nce'));

  assert.ok(body.includes('<div class="hint">Explain</div>'), 'the default prompt is not what an absent setting shows');
});

test('a model the settings NAME and which cannot answer is shown as chosen, and as unable', () => {
  // Otherwise the browser selects the first option and the panel quietly reads "the first one that
  // can answer" while `settings.json` says `codex` — the section describing a state that is not the
  // one the command will refuse. Disabled, so it cannot be re-picked once it is left. (codex,
  // gemini and local, one finding from three directions.)
  const body = chatSection(panelHtml(state({
    vendors: [vendor(), vendor({ id: 'my-local', runtime: 'local' })],
    chat: { ...chat, model: 'my-local' },
  }), 'n0nce'));

  assert.match(body, /<option value="my-local"[^>]*selected[^>]*disabled|<option value="my-local"[^>]*disabled[^>]*selected/,
    'the configured model is not shown as the chosen one');
  // Scoped to the PROVIDER select: the prompt picker and the model select each carry an empty
  // option of their own now, and both are legitimately selected here.
  const providers = body.slice(body.indexOf('data-setting="chatModel"'), body.indexOf('data-setting="chatModelName"'));

  assert.ok(!/<option value=""[^>]*selected/.test(providers), 'the panel claims no model is chosen while one is');
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

  assert.ok(body.includes(`<div class="hint">${escapeHtml(asCommandReadsIt.prompt)}</div>`), 'the section shows a different prompt');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.language}" selected>`), 'a different language is selected');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.autoSend}" selected>`), 'a different send rule is selected');
  assert.ok(body.includes(`<option value="${asCommandReadsIt.model}" selected>`), 'a different model is selected');
});
