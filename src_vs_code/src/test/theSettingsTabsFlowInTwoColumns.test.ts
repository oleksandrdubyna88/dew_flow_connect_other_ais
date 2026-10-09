import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CATALOG_CSS } from '../catalogCss';
import { catalogHtml } from '../catalogPage';
import type { ModelPreset, PromptPreset } from '../chatPresets';
import type { ChatSettings } from '../chatSettings';
import { SHIPPED_COMMANDS, type CommandRow } from '../commands';
import { chatProviderListFor, chatSendingFields, gateBody, type PanelState } from '../panelView';
import { FEATURE_CODE, PLAN_CODE, RESULT_CODE, RESULT_DOCUMENT, bucketOf, composed } from '../roles';
import { DEFAULT_SECURITY } from '../securityLane';
import { promptsInOrder } from '../securityLaneState';
import { panelState } from './panelPageHarness';
import { pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * The Settings page's tabs lay their cards out the way Models does (operator, 2026-10-09, extension 0.65.0): two columns
 * on a wide editor, one on a narrow one — Reviews (every sub-tab but Limits), Security lane and Chat. ONE mechanism, the
 * class Models' cards carry, never a second grid beside it.
 *
 * <p>Read off the page as drawn and run (`pageTree.ts`, `runPageHtml`): each place's repeated cards must be the CHILDREN
 * of that class, which a substring of the source cannot see — a class written next to the cards rather than around them
 * reads the same to a search. The breakpoint itself is CSS, which no script runs, so it is pinned on the stylesheet.</p>
 */

/** The shared class: what Models' cards sit in, and every other place's repeated cards. */
const COLUMNS = 'card-columns';

const classes = (node: PageNode): readonly string[] => node.className.split(/\s+/u).filter((one) => one.length > 0);
const has = (node: PageNode, name: string): boolean => classes(node).includes(name);
/** Whether an ancestor of `node` (below `top`) carries the class — so a field inside a card is not a card of its own. */
const insideA = (node: PageNode, name: string, top: PageNode): boolean => {
  for (let at = node.parent as PageNode | undefined; at !== undefined && at !== top; at = at.parent as PageNode | undefined) {
    if (has(at, name)) {
      return true;
    }
  }

  return false;
};

const chatModel = (id: string, name: string): ModelPreset => ({
  id, name, runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '',
});
const chatPrompt = (id: string, name: string, main = false): PromptPreset => ({ id, name, text: `${name}, the words`, main });

const CHAT: ChatSettings = {
  prompt: 'Explain, the words', promptChoice: '', prompts: [chatPrompt('p1', 'Explain', true), chatPrompt('p2', 'Review')],
  models: [chatModel('chat-deep', 'Deep'), chatModel('chat-fast', 'Fast')], conflicts: [], unreadable: [], language: 'en',
  autoSend: 'keyboard', model: 'chat-deep', modelName: '',
};

const MINE: CommandRow = { id: 'cmd-ab12', title: 'Run the linter', stage: 'code', enabled: true };

/** Every place with something to repeat: the roles and commands read, two chat models, two prompt presets, two pairs. */
function fullState(): PanelState {
  const base = panelState('reviewers');

  return {
    ...base,
    settings: {
      ...base.settings,
      securityLane: { ...DEFAULT_SECURITY, runs: [{ vendor: 'codex', prompt: 'redteam-general' }, { vendor: 'claude', prompt: 'redteam-general' }] },
    },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
    commands: { rows: [MINE], texts: {}, serverVersion: '', perSide: false },
    chat: CHAT,
  };
}

const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The page opened on `place`, drawn and running; the place's pane, as drawn. */
function paneOn(place: string): PageNode {
  const html = catalogHtml(fullState(), 'test-nonce', place);
  const tree = pageTree(html);
  runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });
  const pane = tree.one((node) => node.dataset.pane === place, `a pane for ${place}`);
  assert.equal(pane.hidden, false, `the page did not open ${place}`);

  return pane;
}

/** The top-level fields a builder draws, read off its own output — the count a place that wraps it must show. */
const fieldsOf = (html: string): number => pageTree(html).children.filter((node) => has(node, 'field')).length;

/** The role boxes Stages and Prompts per round draw: one per role in the four buckets `promptsBody` groups. */
const STAGED = [PLAN_CODE, RESULT_CODE, RESULT_DOCUMENT, FEATURE_CODE];
const roleBoxes = (state: PanelState): number => composed(state.settings.roles).filter((role) => STAGED.includes(bucketOf(role))).length;

/** The lane's two numbers, by the field each writes. */
const LANE_NUMBERS = ['threshold', 'maxRounds'];

/** A model's block or a preset: it carries its id. A stranded choice or a conflict is a warning, and spans both columns. */
const chatCard = (node: PageNode, kind: string): boolean => node.tagName === 'DIV' && node.dataset.chpId !== undefined && has(node, kind);

/**
 * Every family of repeated cards the operator's places draw, each counted against its OWN source of truth — the state,
 * the shipped catalogue or the builder's own output — so a family that draws nothing is a red test by name, not a loop
 * over nothing that a "every card has the right parent" assertion would pass.
 */
const FAMILIES: readonly {
  readonly place: string;
  readonly family: string;
  readonly expected: (state: PanelState) => number;
  readonly card: (node: PageNode, pane: PageNode) => boolean;
}[] = [
  { place: 'reviews/stages', family: 'Stages role boxes', expected: roleBoxes, card: (node) => node.tagName === 'DIV' && has(node, 'role') },
  { place: 'reviews/prompts', family: 'Prompts per round role boxes', expected: roleBoxes, card: (node) => node.tagName === 'DIV' && has(node, 'role') },
  // A role spans the page and its PROMPTS flow: a stage often holds one role, which two columns of roles would leave
  // as narrow as before.
  {
    place: 'reviews/roles',
    family: 'Roles & prompts prompt cards',
    expected: (state) => composed(state.roles?.rows ?? []).reduce((sum, role) => sum + (role.prompts ?? []).length, 0),
    card: (node) => node.tagName === 'DIV' && node.dataset.rolePrompt !== undefined,
  },
  // The gate is one decision per field, and nothing repeats but the fields themselves.
  { place: 'reviews/gate', family: 'The gate fields', expected: (state) => fieldsOf(gateBody(state)), card: (node, pane) => has(node, 'field') && !insideA(node, 'field', pane) },
  {
    place: 'reviews/commands', family: 'Commands — Yours', expected: (state) => state.commands?.rows.length ?? 0,
    card: (node) => node.tagName === 'SECTION' && node.dataset.cmdId !== undefined,
  },
  {
    place: 'reviews/commands', family: 'Commands — Shipped', expected: () => SHIPPED_COMMANDS.length,
    card: (node) => node.tagName === 'SECTION' && node.dataset.cmdFile !== undefined,
  },
  {
    place: 'security', family: 'Security lane numbers', expected: () => LANE_NUMBERS.length,
    card: (node) => node.tagName === 'LABEL' && node.find((one) => LANE_NUMBERS.includes(one.dataset.securityField ?? '')).length > 0,
  },
  {
    place: 'security', family: 'Security lane prompt cards', expected: (state) => promptsInOrder(state.settings.securityLane).length,
    card: (node) => node.tagName === 'FIELDSET' && has(node, 'seclane-prompt'),
  },
  {
    place: 'security', family: 'Security lane pairs', expected: (state) => state.settings.securityLane.runs.length,
    card: (node) => node.tagName === 'FIELDSET' && !has(node, 'seclane-prompt'),
  },
  {
    place: 'chat', family: 'Chat models a chat opens on',
    expected: (state) => chatProviderListFor(CHAT, state).providers.filter((one) => CHAT.models.some((model) => model.id === one.id)).length,
    card: (node) => chatCard(node, 'block'),
  },
  {
    place: 'chat', family: 'Chat sending fields', expected: () => fieldsOf(chatSendingFields(CHAT)),
    card: (node, pane) => has(node, 'field') && !insideA(node, 'block', pane) && !insideA(node, 'preset', pane) && !insideA(node, 'field', pane),
  },
  { place: 'chat', family: 'Chat prompt presets', expected: () => CHAT.prompts.length, card: (node) => chatCard(node, 'preset') },
];

test('each named tab draws every family of its repeated cards, all of it, in the columns Models uses', () => {
  const state = fullState();
  for (const spec of FAMILIES) {
    const pane = paneOn(spec.place);
    const cards = pane.find((node) => spec.card(node, pane));
    const expected = spec.expected(state);
    // The count first — against the family's own source — so a family that draws nothing, or loses one, fails here by
    // name rather than passing the parent check below over fewer cards.
    assert.ok(expected > 0, `the fixture: ${spec.family} has nothing to draw`);
    assert.equal(cards.length, expected, `${spec.family} on ${spec.place}: the page drew ${cards.length}, its source has ${expected}`);
    for (const card of cards) {
      const parent = card.parent as PageNode;
      assert.ok(has(parent, COLUMNS), `one of the ${spec.family} on ${spec.place} sits in <${parent.tagName.toLowerCase()} class="${parent.className}">, not in the columns`);
    }
  }
});

test('Limits stays one column — five numbers do not need two', () => {
  const pane = paneOn('reviews/limits');

  assert.ok(pane.find((node) => node.dataset.setting !== undefined || node.tagName === 'INPUT').length >= 5, 'the fixture: Limits draws its five numbers');
  assert.deepEqual(pane.find((node) => has(node, COLUMNS)).map((node) => node.tagName), [], 'Limits drew a column grid');
});

test('Models\' cards sit in the same columns — one mechanism, not a copy', () => {
  const pane = paneOn('models');
  const cards = pane.one((node) => has(node, 'cards'), 'cards container on Models');

  assert.ok(has(cards, COLUMNS), `Models' cards sit in class="${cards.className}"`);
});

/** The rules of a stylesheet at its top level and inside each `@media`, as selector → body, with the media they sit in. */
function rulesOf(css: string): readonly { readonly media: string; readonly selector: string; readonly body: string }[] {
  const flat = css.replace(/\/\*[\s\S]*?\*\//gu, '');
  const rulesIn = (block: string, media: string) => [...block.matchAll(/([^{}]+)\{([^{}]*)\}/gu)]
    .map((rule) => ({ media, selector: rule[1]!.trim(), body: rule[2]!.trim() }));
  const inMedia = [...flat.matchAll(/@media\s*([^{]+)\{((?:[^{}]*\{[^{}]*\})*)\s*\}/gu)]
    .flatMap((media) => rulesIn(media[2]!, media[1]!.trim()));
  const outside = flat.replace(/@media\s*[^{]+\{(?:[^{}]*\{[^{}]*\})*\s*\}/gu, '');

  return [...inMedia, ...rulesIn(outside, '')];
}

test('the stylesheet has ONE two-column rule, on the shared class, at Models\' breakpoint — and lifts the narrow column there', () => {
  const rules = rulesOf(CATALOG_CSS);
  const twoColumns = rules.filter((rule) => /grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/u.test(rule.body) && rule.media.includes('min-width'));
  const base = rules.filter((rule) => rule.media === '' && rule.selector === `.catalog .${COLUMNS}`);

  assert.deepEqual(twoColumns.map((rule) => [rule.media, rule.selector]), [['(min-width: 1100px)', `.catalog .${COLUMNS}`]]);
  assert.equal(base.length, 1, `one base rule for .${COLUMNS}`);
  assert.match(base[0]!.body, /display:\s*grid/u);
  assert.match(base[0]!.body, /grid-template-columns:\s*minmax\(0,\s*1fr\)/u);
  // The sections were written for 760 px; on a wide editor a place that has columns may use the width they need.
  assert.ok(rules.some((rule) => rule.media === '(min-width: 1100px)' && rule.selector === `.catalog .moved:has(.${COLUMNS})` && /max-width:\s*none/u.test(rule.body)),
    'the narrow column is not lifted where a place lays out in columns');
});

/**
 * Whether a selector of class compounds joined by descendant spaces (`.catalog .card .block`) matches `node` — the shape
 * every rule this check reads has. Anything else (a tag, a combinator, an attribute, a pseudo-class) is not this
 * matcher's to judge and answers no.
 */
function matches(node: PageNode, selector: string): boolean {
  const parts = selector.trim().split(/\s+/u);
  if (!parts.every((part) => /^(\.[\w-]+)+$/u.test(part))) {
    return false;
  }
  const fits = (at: PageNode, part: string): boolean => part.split('.').filter((one) => one.length > 0).every((name) => has(at, name));
  if (!fits(node, parts[parts.length - 1]!)) {
    return false;
  }
  let wanted = parts.length - 2;
  for (let at = node.parent as PageNode | undefined; at !== undefined && wanted >= 0; at = at.parent as PageNode | undefined) {
    wanted = fits(at, parts[wanted]!) ? wanted - 1 : wanted;
  }

  return wanted < 0;
}

/** The top-level rules that draw a top border, one entry per selector of a selector list. */
const borderTopRules = (): readonly string[] => rulesOf(CATALOG_CSS)
  .filter((rule) => rule.media === '' && /border-top:/u.test(rule.body))
  .flatMap((rule) => rule.selector.split(','))
  .map((selector) => selector.trim());

test('a Chat model block wears no Models card rule — the top border is a Models card block\'s alone', () => {
  const chat = paneOn('chat');
  const block = chat.one((node) => chatCard(node, 'block'), 'a model block on Chat');
  const models = paneOn('models');
  const cardBlock = models.one((node) => has(node, 'block') && insideA(node, 'card', models), 'a block inside a Models card');

  // The positive half first: the rule is alive and still dresses the card block it was written for.
  assert.ok(borderTopRules().some((selector) => matches(cardBlock, selector)), 'no top-border rule reaches a Models card block any more');
  assert.deepEqual(borderTopRules().filter((selector) => matches(block, selector)), [], 'a Models card rule reaches the Chat tab\'s model block');
});
