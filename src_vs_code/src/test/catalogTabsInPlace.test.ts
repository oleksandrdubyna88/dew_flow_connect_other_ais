import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { gateBody, limitsSection, serverBody, sideBody, teamServersSection, type PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

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
 * The old sections that move as they are, each with the place that holds it and the builder BOTH pages draw it with:
 * every Settings section but the reviewers (now Models), the prompts (split), the keys (counted over every row, below),
 * and the three that pick from the catalog since E4.2 — the consultant, the question consultant and the Security lane
 * (consultantPicks, qconsultRowPicks and securityPicks tests) — and Chat, drawn from the rows ticked Chat with its
 * prompt presets inline since E4.6b (chatOnTheNewPage.test.ts).
 *
 * <p>LISTED, not read off the old page's `PANEL_SECTIONS` as it was until E5's prerequisite (b): E5.1 deletes that
 * list, and an inventory derived from it would have shrunk to nothing with it and passed, checking no section at all.</p>
 */
const MOVED_WHOLE: readonly { readonly title: string; readonly place: string; readonly body: (state: PanelState) => string }[] = [
  { title: 'The gate', place: 'reviews/gate', body: gateBody },
  { title: 'Limits', place: 'reviews/limits', body: limitsSection },
  { title: 'Team servers', place: 'setup/team', body: teamServersSection },
  { title: 'This side', place: 'setup/side', body: sideBody },
  { title: 'MCP server', place: 'setup/mcp', body: serverBody },
];

/** What the page's scripts select at load, so they bind to what the page drew. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** A drawn subtree's controls and named elements, in order — what a section IS, beyond its words. */
const signature = (node: PageNode): string =>
  node.all().map((one) => one.dataset.setting ?? (one.id.length > 0 ? `#${one.id}` : '')).filter((one) => one.length > 0).join('|');

test('every old section that moves whole is drawn in its place, by its own builder', () => {
  // Read off the page AFTER its script ran, inside the place's own pane — a section moved into a later pane goes red
  // (CodeRabbit on #688: a search of the source past the pane's start would have stayed green).
  const state = panelState('reviewers');
  const html = catalogHtml(state, 'test-nonce', 'models');
  const tree = pageTree(html);
  runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  let checked = 0;
  for (const spec of MOVED_WHOLE) {
    const place = spec.place;
    const pane = tree.one((node) => node.dataset.pane === place, `a pane for ${place}`);
    const builder = pageTree(spec.body(state));
    const said = builder.text().replace(/\s+/gu, ' ').trim();
    assert.ok(said.length > 0 || signature(builder).length > 0, `the fixture: ${spec.title} draws nothing to look for`);
    assert.ok(pane.text().replace(/\s+/gu, ' ').includes(said), `${spec.title}'s words are not in ${place}`);
    assert.ok(signature(pane).includes(signature(builder)), `${spec.title}'s controls are not in ${place}, in its order`);
    checked += 1;
  }
  // The count, so a list emptied or cut down — by hand or by a refactor — is a red test rather than a loop over nothing.
  assert.equal(checked, 5, 'five old sections move whole: the gate, limits, team servers, this side and the MCP server');
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
