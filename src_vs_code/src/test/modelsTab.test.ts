import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { modelsTabHtml } from '../modelsTab';
import { skewSaid } from '../modelCard';
import { cardContextFor, type PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { click, lastWrite, panelState, runPanel, withoutSeq } from './panelPageHarness';
import { Node, runPageHtml } from './pageScriptHarness';

/**
 * The Models tab, RUN (todo/PLAN_one_model_catalog.md E3.2): one card per catalog row, every control writing that row
 * through the panel's one vendor write, the uses ticked by command, remove asked first and locked for the last model of
 * a review stage, the filters narrowing the cards in the page, and each runtime's effort and thinking said as the shared
 * file decides (D4, D12).
 */

const codex = (extra: Partial<Vendor> = {}): Vendor => ({ ...DEFAULT_VENDORS[0]!, ...extra });
const claude = (extra: Partial<Vendor> = {}): Vendor => ({ ...DEFAULT_VENDORS[0]!, id: 'claude', runtime: 'claude', model: 'opus', ...extra });

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return { ...panelState('reviewers'), vendors, ...overrides };
}

/** What a write says — its key, value and row — without the routing fields every write carries. */
function said(write: Record<string, unknown>): Record<string, unknown> {
  return { key: write['key'], value: write['value'], vendor: write['vendor'] };
}

function run(state: PanelState): ReturnType<typeof runPanel> {
  return runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });
}

test('one card per catalog row, each control writing ITS row', () => {
  const page = run(stateWith([codex(), claude()]));
  const effort = page.controls.find((one) => one.dataset['setting'] === 'effort' && one.dataset['vendor'] === 'claude')!;

  effort.value = 'high';
  effort.fire('change');

  assert.equal(page.html.match(/data-model-card="/g)?.length, 2);
  assert.deepEqual(said(lastWrite(page)), { key: 'effort', value: 'high', vendor: 'claude' });
});

test('a system prompt being typed is saved when the box is left — a page switch never loses the draft', () => {
  const page = run(stateWith([codex(), claude()]));
  const prompt = page.controls.find((one) => one.dataset['setting'] === 'systemPrompt' && one.dataset['vendor'] === 'codex')!;

  prompt.value = 'cite every line';
  prompt.fire('input');
  prompt.fire('focusout');

  assert.deepEqual(said(lastWrite(page)), { key: 'systemPrompt', value: 'cite every line', vendor: 'codex' });
});

test('a use is ticked by command; one the runtime cannot take is drawn off and says why', () => {
  const html = modelsTabHtml(stateWith([codex(), claude()]));
  const page = run(stateWith([codex(), claude()]));

  click(page, 'toggleUse', 'codex|consultant');

  assert.deepEqual(page.posted.filter((m) => m['type'] === 'command').map(withoutSeq).at(-1), { type: 'command', command: 'toggleUse', id: 'codex|consultant' });
  assert.doesNotMatch(html, /data-id="codex\|bugz"/, 'Bugz is not offered on a CLI row, so it carries no command');
  assert.match(html, /Not offered: Bugz ranking — Bugz ranks with a model on this machine/);
});

test('remove asks first — removeModel, never a command sent on the first click', () => {
  const html = modelsTabHtml(stateWith([codex(), claude()]));

  assert.match(html, /data-asks="removeModel" data-id="claude"/);
  assert.doesNotMatch(html, /data-command="removeModel"/);
  assert.doesNotMatch(html, /data-command="removeVendor"/, 'the current page\'s modal remove would ask a second time');
});

test('the last model switched on for a review stage cannot be switched off, unticked or removed — and says why', () => {
  const html = modelsTabHtml(stateWith([codex(), claude({ plan: false })]));
  const codexCard = html.slice(html.indexOf('data-model-card="codex"'), html.indexOf('data-model-card="claude"'));

  assert.match(codexCard, /data-setting="enabled" data-vendor="codex" checked disabled title="the only model switched on for plan review"/);
  assert.match(codexCard, /<input type="checkbox" data-setting="plan" data-vendor="codex" checked disabled>/);
  assert.match(codexCard, /data-asks="removeModel" data-id="codex"[^>]* disabled title="the only model switched on for plan review"/);
});

test('effort and thinking as the RUNTIME takes them: claude lists levels, codex says it is unmeasured, neither has a thinking switch', () => {
  const html = modelsTabHtml(stateWith([codex({ effort: 'high' }), claude()]));
  const claudeCard = html.slice(html.indexOf('data-model-card="claude"'));
  const codexCard = html.slice(html.indexOf('data-model-card="codex"'), html.indexOf('data-model-card="claude"'));

  assert.match(claudeCard, /data-setting="effort" data-vendor="claude"><option value="" selected>Default — the vendor decides<\/option><option value="low">/);
  assert.match(codexCard, /'high' is kept and not applied/);
  assert.doesNotMatch(codexCard, /data-setting="effort"/, 'an unmeasured effort is not offered as a choice');
  assert.match(claudeCard, /Thinking: no switch — Claude’s depth is its effort/);
});

test('what this side\'s coai-mcp ignores is said on the card — by capability, and not before the binary has answered', () => {
  const vendors = [codex({ systemPrompt: 'terse' }), claude()];
  const older = modelsTabHtml(stateWith(vendors, { server: { kind: 'known', version: '0.43.0', remembered: false, updateOffered: false }, serverFeatures: ['bugzRuntime'] }));
  const cold = modelsTabHtml(stateWith(vendors, { server: { kind: 'known', version: '0.43.0', remembered: false, updateOffered: false }, serverFeatures: undefined }));

  assert.match(older, /<p class="skew">this side's coai-mcp does not take its system prompt yet/);
  assert.doesNotMatch(cold, /class="skew"/, 'a cold start is not an older binary');
});

/** The cards and chips as nodes carrying the data the page's filters read. */
function filterNodes(html: string): { cards: Node[]; chips: Node[]; empty: Node } {
  const cards = [...html.matchAll(/<article class="card[^"]*"[^>]*>/g)].map(([tag]) => new Node(Object.fromEntries(
    [...tag.matchAll(/data-([a-z-]+)="([^"]*)"/g)].map(([, name, value]) => [name!.replace(/-([a-z])/g, (_m, c: string) => c.toUpperCase()), value!]),
  ), 'ARTICLE'));
  const chips = [...html.matchAll(/data-chip="([^"]+)" data-value="([^"]*)"/g)].map(([, chip, value]) => new Node({ chip: chip!, value: value! }, 'BUTTON'));

  return { cards, chips, empty: new Node({}, 'DIV') };
}

test('a chip narrows the cards in the page, a second press clears it, and nothing is written', () => {
  const state = stateWith([codex(), claude()]);
  const html = catalogHtml(state, 'test-nonce', 'models');
  const { cards, chips, empty } = filterNodes(html);
  const page = runPageHtml(html, { '[data-model-card]': cards, '[data-chip]': chips, '[data-models-empty]': [empty] });
  const runsOnClaude = chips.find((chip) => chip.dataset['chip'] === 'runtime' && chip.dataset['value'] === 'claude')!;

  page.fire('click', runsOnClaude);
  assert.deepEqual(cards.filter((card) => !card.hidden).map((card) => card.dataset['modelCard']), ['claude']);
  assert.equal(runsOnClaude.attributes['aria-pressed'], 'true');

  page.fire('click', runsOnClaude);
  assert.deepEqual(cards.filter((card) => !card.hidden).map((card) => card.dataset['modelCard']), ['codex', 'claude']);
  assert.deepEqual(page.posted.filter((m) => m['type'] === 'setting' || m['type'] === 'command'), [], 'filtering stores nothing');
});

test('a switched-off model is hidden until "show switched-off" is ticked', () => {
  const state = stateWith([codex(), claude({ enabled: false })]);
  const html = catalogHtml(state, 'test-nonce', 'models');
  const { cards, chips, empty } = filterNodes(html);
  const showOff = new Node({}, 'INPUT');
  Object.assign(showOff, { id: 'model-show-off', checked: true });
  const page = runPageHtml(html, { '[data-model-card]': cards, '[data-chip]': chips, '[data-models-empty]': [empty] });

  assert.deepEqual(cards.filter((card) => !card.hidden).map((card) => card.dataset['modelCard']), ['codex']);
  page.fire('change', showOff);
  assert.deepEqual(cards.filter((card) => !card.hidden).map((card) => card.dataset['modelCard']), ['codex', 'claude']);
});

test('an api row on a server too old for api rows says why its card is switched off, as the current page does', () => {
  // The epics 4–5 cadence consultation, §7 parity: the card turned its stage boxes off for the note but never said it.
  const api = codex({ id: 'grok', runtime: 'api', model: 'grok-4', baseUrl: 'https://api.x.example/v1', enabled: true });
  const state = stateWith([api], { server: { kind: 'known', version: '0.36.0', remembered: false, updateOffered: false } });
  const context = cardContextFor(state)(api);

  assert.ok(context.apiNote.length > 0, 'the fixture: the current page has a note for this row');
  assert.ok(skewSaid(api, { installed: true, features: [] }, context.apiNote).includes(context.apiNote), 'the card does not say why');
  assert.deepEqual(skewSaid(api, { installed: true, features: [] }, ''), [], 'a server that takes api rows draws no note');
});
