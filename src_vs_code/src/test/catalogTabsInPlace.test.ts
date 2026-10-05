import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { OLD_TAB_PLACES } from '../catalogPlaces';
import { BLANK_REGIONS } from '../panelSurface';
import { PANEL_SECTIONS, type PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './rolesPageHarness';

/**
 * E4.1 of todo/PLAN_one_model_catalog.md: every old Settings section is drawn in the sub-tab that owns it on the new
 * page — by its own builder, never a copy — and each feature tab names the rows ticked for it, with the way to change
 * them on Models. Read off the page as drawn (`pageTree.ts`), and run through its own script.
 */

/** The pane that holds a place, as drawn. */
function paneOf(state: PanelState, place: string): PageNode {
  return pageTree(catalogBody(state)).one((node) => node.dataset.pane === place, `a pane for ${place}`);
}

/**
 * The old sections that move as they are: every Settings section but the reviewers (now Models), the prompts (split),
 * the keys (counted over every row), and the three that pick from the catalog since E4.2 — the consultant, the question
 * consultant and the Security lane (consultantPicks, qconsultRowPicks and securityPicks tests).
 */
const PICKING = ['consultant', 'questionconsultant', 'securityLane'];
const MOVED_WHOLE = PANEL_SECTIONS.filter((spec) => spec.surface === 'settings' && !['reviewers', 'prompts', 'keys', ...PICKING].includes(spec.id));

test('every old section that moves whole is drawn in its place, by its own builder', () => {
  const state = panelState('reviewers');
  const body = catalogBody(state);

  for (const spec of MOVED_WHOLE) {
    const place = OLD_TAB_PLACES[spec.id];
    assert.ok(place !== undefined, `${spec.id} has no place on the new page`);
    // The builder's own output, byte for byte, inside the place's pane: drawn by it, not by a copy of it.
    const start = body.indexOf(`data-pane="${place}"`);
    assert.ok(start >= 0 && body.indexOf(spec.body(state, BLANK_REGIONS), start) > start, `${spec.title} is not drawn in ${place}`);
  }
});

test('the prompts section is split: the switches and budgets on Stages, the round pickers on Prompts per round', () => {
  const state = panelState('reviewers');
  const stages = paneOf(state, 'reviews/stages');
  const prompts = paneOf(state, 'reviews/prompts');

  assert.ok(stages.find((node) => node.dataset.setting === 'rounds').length > 0, 'Stages draws no rounds');
  // One switch per role (E4.3): it lives on Roles & prompts, so Stages draws the role's state in words, not a second tick.
  assert.deepEqual(stages.find((node) => node.dataset.setting === 'roleEnabled'), []);
  assert.deepEqual(stages.find((node) => node.dataset.prompt !== undefined), [], 'a round picker drawn twice on one page is two controls for one setting');
  assert.ok(prompts.find((node) => node.dataset.prompt !== undefined && node.dataset.round === '1').length > 0, 'Prompts per round draws no picker');
  assert.deepEqual(prompts.find((node) => node.dataset.setting === 'rounds'), []);
});

test('on the running page a round picker writes a round pick, and each control is there once', () => {
  const state = panelState('reviewers');
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'reviews/prompts') });
  const keys = page.prompts.map((one) => `${one.dataset['prompt']}/${one.dataset['round']}`);
  assert.deepEqual(keys, [...new Set(keys)], 'one round of one role has two pickers');
  const rounds = page.controls.filter((one) => one.dataset['setting'] === 'rounds').map((one) => one.dataset['role']);
  assert.deepEqual(rounds, [...new Set(rounds)], 'one role has two rounds boxes');

  const picker = page.prompts[0]!;
  picker.value = picker.options.at(-1)?.value ?? '';
  picker.fire('change');

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'prompt').map((one) => one['role']), [picker.dataset['prompt']]);
});

test('no id is drawn twice on the new page — a label would name the wrong control', () => {
  const ids = pageTree(catalogBody(panelState('reviewers'))).find((node) => node.id.length > 0).map((node) => node.id);
  const twice = ids.filter((id, at) => ids.indexOf(id) !== at);

  assert.deepEqual([...new Set(twice)], []);
});

test('no place still says where it is (Roles & prompts since E4.3, Commands since E4.4)', () => {
  const tree = pageTree(catalogBody(panelState('reviewers')));
  const saying = tree.find((node) => node.dataset.pane !== undefined && node.text().includes('is still on the current Settings page'));

  assert.deepEqual(saying.map((node) => node.dataset.pane), []);
});

test('the keys are counted across every row, not only the reviewers', () => {
  const endpoint: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-router', runtime: 'api', baseUrl: 'https://example.test/v1', plan: false, code: false, uses: ['consultant'] };
  const state: PanelState = { ...panelState('reviewers'), vendors: DEFAULT_VENDORS, catalogRows: [...DEFAULT_VENDORS, endpoint] };

  assert.match(paneOf(state, 'setup/keys').text(), /consult-router/);
});

/** A feature tab's "used by" strip, as drawn. */
function strip(state: PanelState, place: string): string {
  return paneOf(state, place).one((node) => node.dataset.usedBy !== undefined, `the strip on ${place}`).text();
}

test('a feature tab names the rows ticked for it, and a row ticked for something else is not named', () => {
  const asks: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-claude', plan: false, code: false, uses: ['consultant'] };
  const chats: Vendor = { ...DEFAULT_VENDORS[1]!, id: 'chat-fast', plan: false, code: false, uses: ['chat'] };
  const state = { ...panelState('reviewers'), catalogRows: [...DEFAULT_VENDORS, asks, chats] };

  assert.match(strip(state, 'consultants/consultant'), /consult-claude/);
  assert.doesNotMatch(strip(state, 'consultants/consultant'), /chat-fast/);
  assert.match(strip(state, 'chat'), /chat-fast/);
});

test('a feature tab with nothing ticked says so, and the consultant says who answers until then (D2)', () => {
  const state = { ...panelState('reviewers'), catalogRows: DEFAULT_VENDORS };

  assert.match(strip(state, 'security'), /No model is ticked for the security lane yet/);
  assert.match(strip(state, 'consultants/consultant'), /each caller asks the pair it ships with/);
});

test('"Change on Models" opens Models narrowed to that use, in the page alone', () => {
  const html = catalogHtml(panelState('reviewers'), 'test-nonce', 'chat');
  const tree = pageTree(html);
  const saved = { value: undefined as unknown };
  const page = runPageHtml(html, selectorsOf(tree, ['[data-tab]', '[data-pane]']), undefined, saved);
  const change = tree.one((node) => node.dataset.modelsUses === 'chat', 'Change on Models on the chat tab');

  bubbled(page, 'click', change);

  assert.equal((saved.value as { models?: { uses?: string } }).models?.uses, 'chat');
  assert.equal(tree.one((node) => node.dataset.pane === 'models', 'the Models pane').hidden, false, 'Models is not shown');
  assert.equal(tree.one((node) => node.dataset.pane === 'chat', 'the Chat pane').hidden, true);
  assert.deepEqual(page.posted.filter((one) => one['type'] === 'tab').map((one) => one['id']), ['models'], 'the host holds the place it opened');
});
