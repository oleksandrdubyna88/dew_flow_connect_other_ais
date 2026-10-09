import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CATALOG_CSS } from '../catalogCss';
import { catalogHtml } from '../catalogPage';
import type { ModelPreset, PromptPreset } from '../chatPresets';
import type { ChatSettings } from '../chatSettings';
import { SHIPPED_COMMANDS, type CommandRow } from '../commands';
import { type PanelState } from '../panelView';
import { composed } from '../roles';
import { DEFAULT_SECURITY } from '../securityLane';
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

/** Every prompt the shipped roles carry — what Roles & prompts draws with no role of the person's own. */
const PROMPTS = composed([]).reduce((sum, role) => sum + (role.prompts ?? []).length, 0);

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

/** What repeats on each place the operator named — the things that flow into the columns. */
const CARDS: readonly { readonly place: string; readonly what: string; readonly least: number; readonly card: (node: PageNode, pane: PageNode) => boolean }[] = [
  { place: 'reviews/stages', what: 'role box', least: 8, card: (node) => node.tagName === 'DIV' && has(node, 'role') },
  // A role spans the page and its PROMPTS flow: a stage often holds one role, which two columns of roles would leave
  // as narrow as before. Every prompt of every role the page draws.
  { place: 'reviews/roles', what: 'role\'s prompt', least: PROMPTS, card: (node) => node.tagName === 'DIV' && node.dataset.rolePrompt !== undefined },
  { place: 'reviews/prompts', what: 'role box', least: 8, card: (node) => node.tagName === 'DIV' && has(node, 'role') },
  // The gate is one decision per field, and nothing repeats but the fields themselves.
  { place: 'reviews/gate', what: 'setting', least: 4, card: (node, pane) => has(node, 'field') && !insideA(node, 'field', pane) },
  { place: 'reviews/commands', what: 'command', least: 1 + SHIPPED_COMMANDS.length, card: (node) => node.tagName === 'SECTION' && has(node, 'command') },
  // The two numbers, thirteen shipped prompt cards and the two pairs.
  {
    place: 'security',
    what: 'number, prompt card or pair',
    least: 2 + DEFAULT_SECURITY.prompts.length + 2,
    card: (node) => node.tagName === 'FIELDSET'
      || (node.tagName === 'LABEL' && node.find((one) => one.dataset.securityField === 'threshold' || one.dataset.securityField === 'maxRounds').length > 0),
  },
  {
    place: 'chat',
    what: 'model, sending field or prompt preset',
    // Two models, the three sending fields, two presets.
    least: 7,
    // A model's block and a preset carry their id; a stranded choice or a conflict is a warning, and spans both columns.
    card: (node, pane) => (node.tagName === 'DIV' && node.dataset.chpId !== undefined && (has(node, 'block') || has(node, 'preset')))
      || (has(node, 'field') && !insideA(node, 'block', pane) && !insideA(node, 'preset', pane) && !insideA(node, 'field', pane)),
  },
];

test('each named tab lays its repeated cards out in the columns Models uses', () => {
  for (const spec of CARDS) {
    const pane = paneOn(spec.place);
    const cards = pane.find((node) => spec.card(node, pane));
    // The count first, so a fixture that draws nothing fails here rather than passing a loop over nothing.
    assert.ok(spec.least > 0, `the fixture: ${spec.place} expects no ${spec.what}s at all`);
    assert.ok(cards.length >= spec.least, `${spec.place} draws ${cards.length} ${spec.what}s, the fixture expects ${spec.least} or more`);
    for (const card of cards) {
      const parent = card.parent as PageNode;
      assert.ok(has(parent, COLUMNS), `a ${spec.what} on ${spec.place} sits in <${parent.tagName.toLowerCase()} class="${parent.className}">, not in the columns`);
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
  const found: { media: string; selector: string; body: string }[] = [];
  const flat = css.replace(/\/\*[\s\S]*?\*\//gu, '');
  for (const media of flat.matchAll(/@media\s*([^{]+)\{((?:[^{}]*\{[^{}]*\})*)\s*\}/gu)) {
    for (const rule of media[2]!.matchAll(/([^{}]+)\{([^{}]*)\}/gu)) {
      found.push({ media: media[1]!.trim(), selector: rule[1]!.trim(), body: rule[2]!.trim() });
    }
  }
  const outside = flat.replace(/@media\s*[^{]+\{(?:[^{}]*\{[^{}]*\})*\s*\}/gu, '');
  for (const rule of outside.matchAll(/([^{}]+)\{([^{}]*)\}/gu)) {
    found.push({ media: '', selector: rule[1]!.trim(), body: rule[2]!.trim() });
  }

  return found;
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
