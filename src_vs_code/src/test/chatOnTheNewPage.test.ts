import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import type { ModelPreset, PromptPreset } from '../chatPresets';
import { presetEdit } from '../chatPresetsMessages';
import type { ChatSettings } from '../chatSettings';
import { chatProviderListFor, type PanelState } from '../panelView';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './rolesPageHarness';

/**
 * E4.6b of todo/PLAN_one_model_catalog.md: Chat on the new page — which model a chat opens on (the rows ticked Chat that
 * can answer), what it sends with and how, and the prompt presets inline, edited through the presets' own messages into
 * the one editing core (`chatPresetsHost.ts`). The page also draws the roles and the commands, so none of the three
 * reads another's controls. Every control fired at is taken from the page as drawn.
 */

const model = (id: string, name: string, extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id, name, runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '', ...extra,
});

const prompt = (id: string, name: string, main = false): PromptPreset => ({ id, name, text: `${name}, the words`, main });

/** Two rows that can answer, and one on a runtime the chat cannot speak to. */
const MODELS = [
  model('chat-deep', 'Deep', { startingPrompt: 'You review APIs.' }),
  model('chat-fast', 'Fast', { model: 'sonnet' }),
  model('local-x', 'On my GPU', { runtime: 'local' }),
];

const CHAT: ChatSettings = {
  prompt: 'Explain, the words', promptChoice: '', prompts: [prompt('p1', 'Explain', true), prompt('p2', 'Review')],
  models: MODELS, language: 'en', autoSend: 'keyboard', model: 'chat-deep', modelName: '',
};

function stateWith(chat: Partial<ChatSettings> = {}, over: Partial<PanelState> = {}): PanelState {
  return {
    ...panelState('reviewers'),
    chat: { ...CHAT, ...chat },
    commands: { rows: [], texts: {}, serverVersion: '', perSide: false },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
    ...over,
  };
}

const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page on Chat, drawn and running — with the whole drawn tree, the roles' and commands' panes included. */
function chatPage(state: PanelState = stateWith()) {
  const html = catalogHtml(state, 'test-nonce', 'chat');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  return { page, tree, pane: tree.one((node) => node.dataset.pane === 'chat', 'Chat pane') };
}

const radios = (pane: PageNode): readonly PageNode[] => pane.find((node) => node.dataset.chpMain !== undefined);
/** A model's or a prompt's block — its Remove button carries the id too, so the block is the DIV. */
const rowOf = (pane: PageNode, id: string): PageNode => pane.one((node) => node.dataset.chpId === id && node.tagName === 'DIV', `the block of ${id}`);

test('one radio per model that can answer, the saved one checked, and the one that cannot is named with why', () => {
  const { pane } = chatPage();

  assert.deepEqual(radios(pane).map((node) => node.attrs['value']), ['chat-deep', 'chat-fast']);
  assert.deepEqual(radios(pane).filter((node) => node.checked).map((node) => node.attrs['value']), ['chat-deep']);
  const refused = chatProviderListFor(CHAT, stateWith()).refused;
  assert.ok(refused.length > 0, 'the fixture has no model the chat cannot speak to');
  for (const one of refused) {
    assert.ok(pane.text().includes(one.reason), `the refusal of ${one.id} is not on the page`);
  }
});

test('each model that can answer shows what the composer opens with, in a box of its own', () => {
  const { pane } = chatPage();
  const box = rowOf(pane, 'chat-deep').one((node) => node.dataset.chpField === 'startingPrompt', 'its Opens with box');

  assert.equal(box.text(), 'You review APIs.');
});

test('a saved choice that cannot answer is drawn chosen and disabled, with the reason — never another model', () => {
  const { pane } = chatPage(stateWith({ model: 'gone-row' }));
  const stranded = radios(pane).filter((node) => node.checked);

  assert.deepEqual(stranded.map((node) => [node.attrs['value'], node.attrs['disabled'] !== undefined]), [['gone-row', true]]);
  assert.match(pane.text(), /gone-row/u);
  assert.match(pane.text(), /never quietly sent to a different one/u);
});

test('with nothing saved, the model ticked main is the one chosen; with none, the page says which answers', () => {
  const ticked = chatPage(stateWith({ model: '', models: [MODELS[0]!, { ...MODELS[1]!, main: true }] })).pane;
  assert.deepEqual(radios(ticked).filter((node) => node.checked).map((node) => node.attrs['value']), ['chat-fast']);

  const none = chatPage(stateWith({ model: '' })).pane;
  assert.deepEqual(radios(none).filter((node) => node.checked), []);
  assert.match(none.text(), /the first that can answer — Deep/u);
});

test('the sending fields are the current page\'s own, and the model picker is not drawn twice', () => {
  const { pane } = chatPage();
  const settings = pane.find((node) => node.dataset.setting !== undefined).map((node) => node.dataset.setting);

  assert.deepEqual(settings, ['chatPromptChoice', 'chatLanguage', 'chatAutoSend']);
  assert.equal(pane.find((node) => node.dataset.command === 'editChatPresets').length, 0, 'the presets are edited here now');
});

test('the prompt presets are drawn inline, each with its name, main tick, Remove and words', () => {
  const { pane } = chatPage();
  const prompts = pane.find((node) => node.tagName === 'DIV' && node.dataset.chpId !== undefined && node.find((field) => field.dataset.chpList === 'prompt').length > 0);

  assert.deepEqual(prompts.map((node) => node.dataset.chpId), ['p1', 'p2']);
  assert.equal(rowOf(pane, 'p1').one((node) => node.dataset.chpField === 'main', 'the main tick').checked, true);
});

test('the chat\'s controls post the presets\' own edits — a pick numbered, typing not — and report focus', () => {
  const { page, pane } = chatPage();
  const fast = radios(pane).find((node) => node.attrs['value'] === 'chat-fast')!;
  fast.checked = true;
  const opens = rowOf(pane, 'chat-deep').one((node) => node.dataset.chpField === 'startingPrompt', 'Opens with');
  opens.value = 'You review schemas.';
  const name = rowOf(pane, 'p2').one((node) => node.dataset.chpField === 'name', 'a prompt name');
  name.value = 'Review it';
  const main = rowOf(pane, 'p2').one((node) => node.dataset.chpField === 'main', 'a prompt main tick');
  main.checked = true;
  const add = pane.one((node) => node.dataset.chpAdd === 'prompt', 'Add a prompt');
  const remove = rowOf(pane, 'p2').one((node) => node.dataset.chpRemove === 'prompt', 'a prompt Remove');

  bubbled(page, 'change', fast);
  bubbled(page, 'input', opens);
  bubbled(page, 'input', name);
  bubbled(page, 'change', main);
  bubbled(page, 'click', add);
  bubbled(page, 'click', remove);
  bubbled(page, 'focusin', name);

  const sent = page.posted.filter((one) => one['type'] === 'chatPresets');
  assert.deepEqual(sent.map((one) => one['edit']), [
    { type: 'edit', list: 'model', id: 'chat-fast', field: 'main', value: true },
    { type: 'edit', list: 'model', id: 'chat-deep', field: 'startingPrompt', value: 'You review schemas.' },
    { type: 'edit', list: 'prompt', id: 'p2', field: 'name', value: 'Review it' },
    { type: 'edit', list: 'prompt', id: 'p2', field: 'main', value: true },
    { type: 'add', list: 'prompt' },
    { type: 'remove', list: 'prompt', id: 'p2' },
  ]);
  assert.deepEqual(sent.map((one) => one['seq'] !== undefined), [true, false, false, true, true, true]);
  // Every one of them is a message the presets' own reader understands — the page invents no dialect of its own.
  assert.deepEqual(sent.map((one) => presetEdit(one['edit']).kind), ['edit', 'edit', 'edit', 'edit', 'add', 'remove']);
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: 'chatPresets|p2|name', editing: true }]);
});

test('the chat, the roles and the commands never read each other\'s controls', () => {
  const { page, tree, pane } = chatPage();
  const roles = tree.one((node) => node.dataset.pane === 'reviews/roles', 'Roles & prompts pane');
  const commands = tree.one((node) => node.dataset.pane === 'reviews/commands', 'Commands pane');
  const presses: readonly [PageNode, string][] = [
    [rowOf(pane, 'p1').one((node) => node.dataset.chpField === 'name', 'a prompt name'), 'input'],
    [rowOf(pane, 'p1').one((node) => node.dataset.chpRemove !== undefined, 'a prompt Remove'), 'click'],
    [roles.one((node) => node.dataset.field === 'name', 'a role name'), 'input'],
    [commands.one((node) => node.dataset.cmdAdd !== undefined, 'Add a command'), 'click'],
  ];

  for (const [node, kind] of presses) {
    bubbled(page, kind, node);
  }

  assert.deepEqual(page.posted.filter((one) => ['roles', 'commands', 'chatPresets'].includes(String(one['type']))).map((one) => one['type']),
    ['chatPresets', 'chatPresets', 'roles', 'commands']);
});

test('a side that keeps its own settings says the chat models are saved for it', () => {
  assert.doesNotMatch(chatPage().pane.text(), /for this side of the machine/u);
  assert.match(chatPage(stateWith({}, { perSide: true })).pane.text(), /for this side of the machine/u);
});
