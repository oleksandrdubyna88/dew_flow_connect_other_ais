import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { composed, type RoleRow } from '../roles';
import { roleSwitchFollows } from '../rolesSwitch';
import { DEFAULTS, envBlock } from '../settingsShape';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * E4.3 of todo/PLAN_one_model_catalog.md: Roles & prompts on the new page — the Review roles tab's content drawn by the
 * panel, its edits posted as `roles` messages into the one editing core (`rolesHost.ts`), and ONE switch per role:
 * the catalog's `active` and the panel's `roleEnabled` read as one and written as one.
 */

function stateWith(rows: readonly RoleRow[] = [], roleEnabled: Readonly<Record<string, boolean>> = {}): PanelState {
  const base = panelState('reviewers');

  return {
    ...base,
    settings: { ...base.settings, roles: rows, roleEnabled: { ...base.settings.roleEnabled, ...roleEnabled } },
    roles: { rows, texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

/** The Roles & prompts pane, cut at the next tab panel. */
function rolesPane(state: PanelState): string {
  const body = catalogBody(state);
  const start = body.indexOf('id="cpane-reviews-roles"');
  const next = body.indexOf('role="tabpanel"', body.indexOf('>', start));

  return body.slice(start, next < 0 ? body.length : next);
}

test('every role is drawn on Roles & prompts, its prompts NOT marked as round pickers', () => {
  const pane = rolesPane(stateWith());

  for (const role of composed([])) {
    assert.match(pane, new RegExp(`data-id="${role.id}"`), `${role.id} is not drawn`);
  }
  assert.match(pane, /data-role-prompt="/);
  assert.doesNotMatch(pane, /data-prompt="/, 'the panel\'s script reads data-prompt as a round pick, and would post a prompt\'s text as one');
  assert.doesNotMatch(pane, /data-tab="(plan|code|documents|feature)"/, 'a second tab strip inside a place the page\'s own strip already selects');
  assert.match(pane, /data-add="role"/);
});

test('a code role switched off by EITHER switch is drawn off: one switch, read as one', () => {
  const pane = rolesPane(stateWith([], { Architecture: false }));
  const block = pane.slice(pane.indexOf('data-id="Architecture"'));
  const active = /<input type="checkbox" data-field="active"[^>]*>/.exec(block)?.[0] ?? '';

  assert.ok(active.length > 0);
  assert.doesNotMatch(active, /checked/);
});

test('Roles & prompts no longer says where it is', () => {
  const body = catalogBody(stateWith());

  assert.doesNotMatch(body, /Roles &amp; prompts is still on the current Settings page/);
});

test('the roles\' controls post `roles` edits — a pick numbered, typing not — and report focus', () => {
  const role = new Node({ id: 'Architecture' }, 'DETAILS');
  const name = new Node({ field: 'name' }, 'INPUT').under(role);
  name.value = 'Architecture, again';
  const active = new Node({ field: 'active' }, 'INPUT').under(role);
  active.type = 'checkbox';
  active.checked = true;
  const prompt = new Node({ rolePrompt: 'architecture-universal' }, 'DIV').under(role);
  const text = new Node({ field: 'text' }, 'TEXTAREA').under(prompt);
  text.value = 'What does this break?';
  const addPrompt = new Node({ addPrompt: 'Architecture' }, 'BUTTON');
  const page = runPageHtml(catalogHtml(stateWith(), 'test-nonce', 'reviews/roles'), {}, undefined, { value: undefined });

  page.fire('input', name);
  page.fire('change', active);
  page.fire('input', text);
  page.fire('click', addPrompt);
  page.fire('focusin', text);

  const roles = page.posted.filter((one) => one['type'] === 'roles');
  assert.deepEqual(roles.map((one) => one['edit']), [
    { type: 'edit', id: 'Architecture', field: 'name', value: 'Architecture, again' },
    { type: 'edit', id: 'Architecture', field: 'active', value: true },
    { type: 'editPrompt', id: 'Architecture', promptId: 'architecture-universal', field: 'text', value: 'What does this break?' },
    { type: 'addPrompt', id: 'Architecture' },
  ]);
  assert.deepEqual(roles.map((one) => one['seq'] !== undefined), [false, true, false, true], 'typing is numbered, or a pick is not');
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: 'roles|Architecture|architecture-universal|text', editing: true }]);
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
