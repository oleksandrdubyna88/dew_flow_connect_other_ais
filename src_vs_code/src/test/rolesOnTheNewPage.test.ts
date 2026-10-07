import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { composed, type RoleRow } from '../roles';
import { roleSwitchFollows } from '../rolesSwitch';
import { DEFAULTS, envBlock } from '../settingsShape';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * E4.3 of todo/PLAN_one_model_catalog.md: Roles & prompts on the new page — the Review roles tab's content drawn by the
 * panel, its edits posted as `roles` messages into the one editing core (`rolesHost.ts`), and ONE switch per role:
 * the catalog's `active` and the panel's `roleEnabled` read as one and written as one. Every control a test fires at
 * is taken from the page as drawn (`pageTree.ts`).
 */

function stateWith(rows: readonly RoleRow[] = [], roleEnabled: Readonly<Record<string, boolean>> = {}): PanelState {
  const base = panelState('reviewers');

  return {
    ...base,
    settings: { ...base.settings, roles: rows, roleEnabled: { ...base.settings.roleEnabled, ...roleEnabled } },
    roles: { rows, texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

/** What the page's scripts select at load, so they bind to what the page drew. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page on Roles & prompts, drawn and running. */
function rolesPage(state: PanelState = stateWith()) {
  const html = catalogHtml(state, 'test-nonce', 'reviews/roles');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  return { page, pane: tree.one((node) => node.dataset.pane === 'reviews/roles', 'Roles & prompts pane') };
}

/** One role's block, and a control in it. */
function roleBlock(pane: PageNode, id: string): PageNode {
  return pane.one((node) => node.tagName === 'DETAILS' && node.dataset.id === id, `role ${id}`);
}

test('every role is drawn on Roles & prompts', () => {
  const { pane } = rolesPage();
  const drawn = pane.find((node) => node.tagName === 'DETAILS' && node.dataset.id !== undefined).map((node) => node.dataset.id);

  assert.deepEqual(drawn, composed([]).map((role) => role.id));
});

test('a prompt typed on Roles & prompts is the roles\' edit — never read as a round pick', () => {
  const { page, pane } = rolesPage();
  const box = pane.one((node) => node.tagName === 'TEXTAREA' && node.dataset.field === 'text', 'prompt box');
  box.value = 'What does this break?';

  bubbled(page, 'input', box);
  bubbled(page, 'change', box);

  const types = page.posted.map((one) => one['type']);
  assert.ok(types.includes('roles'), 'the typed prompt was not posted as the roles\' edit');
  assert.ok(!types.includes('prompt'), 'the panel read a prompt box as a round pick and would store its text as one');
});

test('a code role switched off by EITHER switch is drawn off: one switch, read as one', () => {
  const { pane } = rolesPage(stateWith([], { Architecture: false }));
  const active = roleBlock(pane, 'Architecture').one((node) => node.dataset.field === 'active', 'Architecture\'s switch');

  assert.equal(active.checked, false);
});

test('Roles & prompts no longer says where it is', () => {
  // The pane's text after the page's script ran — decoded, as a person reads it — never the generated source (CodeRabbit on #688).
  assert.doesNotMatch(rolesPage().pane.text(), /Roles & prompts is still on the current Settings page/);
});

test('the roles\' controls post `roles` edits — a pick numbered, typing not — and report focus', () => {
  const { page, pane } = rolesPage();
  const block = roleBlock(pane, 'Architecture');
  const name = block.one((node) => node.dataset.field === 'name', 'the name box');
  name.value = 'Architecture, again';
  const active = block.one((node) => node.dataset.field === 'active', 'the switch');
  active.checked = false;
  const prompt = block.one((node) => node.dataset.rolePrompt !== undefined, 'a prompt');
  const text = prompt.one((node) => node.dataset.field === 'text', 'its box');
  text.value = 'What does this break?';
  const addPrompt = block.one((node) => node.dataset.addPrompt !== undefined, 'Add a prompt');

  bubbled(page, 'input', name);
  bubbled(page, 'change', active);
  bubbled(page, 'input', text);
  bubbled(page, 'click', addPrompt);
  bubbled(page, 'focusin', text);

  const roles = page.posted.filter((one) => one['type'] === 'roles');
  assert.deepEqual(roles.map((one) => one['edit']), [
    { type: 'edit', id: 'Architecture', field: 'name', value: 'Architecture, again' },
    { type: 'edit', id: 'Architecture', field: 'active', value: false },
    { type: 'editPrompt', id: 'Architecture', promptId: prompt.dataset.rolePrompt, field: 'text', value: 'What does this break?' },
    { type: 'addPrompt', id: 'Architecture' },
  ]);
  assert.deepEqual(roles.map((one) => one['seq'] !== undefined), [false, true, false, true], 'typing is numbered, or a pick is not');
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: `roles|Architecture|${prompt.dataset.rolePrompt}|text`, editing: true }]);
});

test('the panel switch follows the catalog switch once it has landed — and only then, and only for a switched role', () => {
  const off: readonly RoleRow[] = [{ id: 'Architecture', active: false }];

  assert.deepEqual(roleSwitchFollows(off, 'Architecture', false, { UxDxPerformance: false }), { UxDxPerformance: false, Architecture: false });
  assert.equal(roleSwitchFollows([], 'Architecture', false, {}), undefined, 'the catalog write was refused or has not landed');
  assert.equal(roleSwitchFollows([{ id: 'PlanCritique', active: false }], 'PlanCritique', false, {}), undefined, 'the plan role has no second switch');
  assert.equal(roleSwitchFollows([{ id: 'Architecture', active: false }], 'Architecture', false, { Architecture: false }), undefined, 'nothing to change');
});

test('a role switched off is off for EVERY server: the catalog row for one before 0.18.13, both channels after it', () => {
  const settings = { ...DEFAULTS, roles: [{ id: 'Architecture', active: false }], roleEnabled: { ...DEFAULTS.roleEnabled, Architecture: false } };
  const env = envBlock(settings);

  assert.match(env['COAI_ROLES'] ?? '', /"id":"Architecture","active":false/, 'a server below ROLE_SWITCH_SINCE reads only the catalog row');
  assert.equal(env['COAI_ENABLED_ARCHITECTURE'], 'false', 'a server at or above it reads COAI_ENABLED_*');
});
