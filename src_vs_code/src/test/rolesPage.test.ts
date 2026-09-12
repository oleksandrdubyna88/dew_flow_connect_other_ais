import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { escapeHtml } from '../webviewHtml';
import { PLAN_STAGE, RESULT_STAGE, type RoleRow } from '../roles';
import { CUSTOM_ROLES_SINCE, canActivate, canDeactivate, isShippedPrompt, roleEdit, rolesHtml, tooOldFor, type RolesPageState } from '../rolesPage';

/**
 * The roles page: what it draws, and what it refuses to offer.
 *
 * <p>Assertions are on the MARKUP and on the pure parser, which is the house style for a page module
 * — the host that writes a setting has no unit test anywhere in this extension, and the structural
 * scans cover its obligations instead.</p>
 */

const state = (over: Partial<RolesPageState> = {}): RolesPageState => ({
  rows: [],
  texts: {},
  serverVersion: CUSTOM_ROLES_SINCE,
  perSide: false,
  uiScale: 0,
  ...over,
});

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
};

/** The page between one role's block and the next, so an assertion cannot drift into another. */
function block(html: string, id: string): string {
  const start = html.indexOf(`data-id="${id}"`);
  assert.notStrictEqual(start, -1, `${id} is not on the page`);
  const next = html.indexOf('<details class="role', start + 1);

  return html.slice(start, next === -1 ? html.length : next);
}

test('every shipped role is on the page, under its own name', () => {
  const html = rolesHtml(state(), 'n0nce');

  for (const role of BUILTIN_ROLES) {
    assert.ok(html.includes(`data-id="${role.id}"`), role.id);
    // The ESCAPED name: `Security & reliability` reaches the page as `Security &amp; reliability`,
    // and a test that looked for the raw text would quietly stop checking the moment one of these
    // names gained a character that has to be escaped.
    assert.ok(block(html, role.id).includes(escapeHtml(role.name)), `${role.id} is drawn as ${role.name}`);
  }
});

test('a role a person added is drawn beside them', () => {
  const html = rolesHtml(state({ rows: [mine] }), 'n0nce');

  assert.ok(block(html, 'Requirements').includes('Requirements we wrote'));
  assert.ok(!block(html, 'Requirements').includes('shipped'), 'and is not labelled as one of ours');
});

test('a shipped role cannot be renamed away or removed', () => {
  // Its id keys settings, open sessions and every round already recorded. The page does not offer
  // it; RoleComposition refuses it again, because a page is not a boundary.
  const one = block(rolesHtml(state(), 'n0nce'), 'Architecture');

  assert.ok(one.includes('badge">shipped'), 'it says so');
  assert.ok(!one.includes('data-remove="Architecture"'), 'no Remove button');
  assert.ok(one.includes('<select data-field="stage" disabled'), 'and its stage is fixed');
});

test('a shipped prompt may be rewritten and restored, never removed', () => {
  const html = rolesHtml(state({ texts: { architecture: 'in our words' } }), 'n0nce');
  const one = block(html, 'Architecture');

  assert.ok(one.includes('data-restore="architecture"'), 'Restore is offered');
  assert.ok(!one.includes('data-remove-prompt="architecture"'), 'Remove is not');
  assert.ok(one.includes('in our words'), 'and the text a person wrote is in the box');
});

test('Restore is disabled while a shipped prompt has never been rewritten', () => {
  const one = block(rolesHtml(state(), 'n0nce'), 'Architecture');

  assert.match(one, /data-restore="architecture" disabled/);
});

test('a prompt a person added to their own role can be removed', () => {
  const one = block(rolesHtml(state({ rows: [mine] }), 'n0nce'), 'Requirements');

  assert.ok(one.includes('data-remove-prompt="requirements-general"'));
  assert.ok(one.includes('data-remove="Requirements"'), 'and so can the role');
});

test('a sixth active role in a stage cannot be ticked, and the page says why', () => {
  // The server caps at five and names what it capped, so nothing is at risk except the truth: a
  // page that let somebody tick a sixth and then showed them five would be lying about what it saved.
  const rows: RoleRow[] = [
    { id: 'One', stage: RESULT_STAGE, prompts: [{ id: 'one-general' }] },
    { id: 'Two', stage: RESULT_STAGE, active: false, prompts: [{ id: 'two-general' }] },
  ];
  const html = rolesHtml(state({ rows }), 'n0nce');

  assert.match(block(html, 'Two'), /data-field="active" disabled/);
  assert.ok(block(html, 'Two').includes('Five roles are already active'));
  assert.doesNotMatch(block(html, 'One'), /data-field="active"[^>]* disabled/, 'one already on stays on');
});

test('the last active role in a stage cannot be switched off, and the page says why', () => {
  // A stage with nothing in it produces a round with no reviewer, which the session counts as
  // unresolved and never lets a person retry. The server refuses such a round with a sentence;
  // refusing it here, where the pointer is, beats refusing it after somebody has waited.
  const off = { active: false };
  const rows: RoleRow[] = [
    { id: 'Conventions', ...off },
    { id: 'SecurityReliability', ...off },
    { id: 'UxDxPerformance', ...off },
  ];
  const html = rolesHtml(state({ rows }), 'n0nce');

  assert.match(block(html, 'Architecture'), /data-field="active"[^>]* disabled/, 'the only one left');
  assert.ok(block(html, 'Architecture').includes('The only role still active in this stage'));
  assert.strictEqual(canDeactivate(rows, { id: 'Architecture' }), false);
});

test('the plan stage has the same rule, which is what it never had before', () => {
  // The sidebar draws no switch on a plan role at all, so this page is where a plan role is
  // switched off — and where the last one is refused.
  assert.strictEqual(canDeactivate([], { id: 'PlanCritique' }), false, 'the only shipped plan role');

  const withMine: RoleRow[] = [{ id: 'Brief', stage: PLAN_STAGE, prompts: [{ id: 'brief-general' }] }];
  assert.strictEqual(canDeactivate(withMine, { id: 'PlanCritique' }), true, 'once a second one exists');
});

test('a role already active is never refused its own switch', () => {
  // Otherwise the fifth role could be switched on and never off again.
  const rows: RoleRow[] = [{ id: 'One', stage: RESULT_STAGE, prompts: [{ id: 'one-general' }] }];

  assert.strictEqual(canActivate(rows, { id: 'Architecture' }), true);
});

test('the plan stage and the code stage are drawn apart', () => {
  const html = rolesHtml(state({ rows: [{ id: 'Brief', stage: PLAN_STAGE, prompts: [{ id: 'brief-general' }] }] }), 'n0nce');

  assert.ok(html.indexOf('data-id="Brief"') > html.indexOf('<h2>Plan review</h2>'));
  assert.ok(html.indexOf('data-id="Brief"') < html.indexOf('<h2>Code review</h2>'));
  assert.ok(html.indexOf('data-id="Architecture"') > html.indexOf('<h2>Code review</h2>'));
});

test('a role that is not a programming task says it takes part in no round yet', () => {
  const rows: RoleRow[] = [{ id: 'Brief', stage: RESULT_STAGE, programmingTask: false, prompts: [{ id: 'brief-general' }] }];

  assert.ok(block(rolesHtml(state({ rows }), 'n0nce'), 'Brief').includes('takes part in no round yet'));
});

test('the page escapes what a person typed, wherever they typed it', () => {
  const rows: RoleRow[] = [{
    id: 'Evil',
    name: '<img src=x onerror=alert(1)>',
    prompts: [{ id: 'evil-general', label: '<script>a</script>', purpose: '"><b>b' }],
  }];
  const html = rolesHtml(state({ rows, texts: { 'evil-general': '</textarea><script>c</script>' } }), 'n0nce');

  assert.doesNotMatch(html, /<img src=x/);
  assert.doesNotMatch(html, /<script>a<\/script>/);
  assert.doesNotMatch(html, /<script>c<\/script>/);
  assert.match(html, /&lt;img src=x/);
});

test('the banner appears only for a KNOWN server older than the release that reads roles', () => {
  const rows = [mine];

  assert.ok(tooOldFor('0.18.17', rows).includes('0.18.17'), 'older says so');
  assert.strictEqual(tooOldFor(CUSTOM_ROLES_SINCE, rows), '', 'the one that reads them says nothing');
  assert.strictEqual(tooOldFor('0.20.1', rows), '', 'nor does a later one');
  assert.strictEqual(tooOldFor('', rows), '', 'a server nobody has installed is not behind');
  assert.strictEqual(tooOldFor('0.18.17', []), '', 'and with no roles of your own there is nothing to warn about');
});

test('a shipped prompt is known by role and id together', () => {
  assert.strictEqual(isShippedPrompt('Architecture', 'architecture'), true);
  assert.strictEqual(isShippedPrompt('Architecture', 'requirements-general'), false);
  assert.strictEqual(isShippedPrompt('Requirements', 'architecture'), false, 'not by prompt id alone');
});

test('the page says when the roles belong to one side', () => {
  assert.ok(rolesHtml(state({ perSide: true }), 'n0nce').includes('for this side'));
  assert.ok(!rolesHtml(state(), 'n0nce').includes('for this side'));
});

test('every message the page can post is parsed into a command', () => {
  assert.deepStrictEqual(roleEdit({ type: 'add' }), { kind: 'add' });
  assert.deepStrictEqual(roleEdit({ type: 'remove', id: 'Requirements' }), { kind: 'remove', id: 'Requirements' });
  assert.deepStrictEqual(roleEdit({ type: 'addPrompt', id: 'Requirements' }), { kind: 'addPrompt', id: 'Requirements' });
  assert.deepStrictEqual(
    roleEdit({ type: 'edit', id: 'Requirements', field: 'name', value: 'New' }),
    { kind: 'edit', id: 'Requirements', field: 'name', value: 'New' },
  );
  assert.deepStrictEqual(
    roleEdit({ type: 'edit', id: 'Requirements', field: 'active', value: false }),
    { kind: 'edit', id: 'Requirements', field: 'active', value: false },
  );
  assert.deepStrictEqual(
    roleEdit({ type: 'editPrompt', id: 'R', promptId: 'r-general', field: 'text', value: 'ask this' }),
    { kind: 'editPrompt', id: 'R', promptId: 'r-general', field: 'text', value: 'ask this' },
  );
  assert.deepStrictEqual(
    roleEdit({ type: 'removePrompt', id: 'R', promptId: 'r-general' }),
    { kind: 'removePrompt', id: 'R', promptId: 'r-general' },
  );
  assert.deepStrictEqual(
    roleEdit({ type: 'restorePrompt', id: 'R', promptId: 'r-general' }),
    { kind: 'restorePrompt', id: 'R', promptId: 'r-general' },
  );
  assert.deepStrictEqual(roleEdit({ type: 'zoom', delta: 1 }), { kind: 'zoom', delta: 1 });
});

test('a field this page does not have is not an edit', () => {
  // Checked against a LIST of names rather than with `in`, so no key of Object.prototype is a field.
  for (const field of ['__proto__', 'constructor', 'id', 'prompts', 'toString']) {
    assert.deepStrictEqual(roleEdit({ type: 'edit', id: 'R', field, value: 'x' }), { kind: 'ignore' }, field);
  }
});

test('a value of the wrong type is not an edit either', () => {
  assert.deepStrictEqual(roleEdit({ type: 'edit', id: 'R', field: 'name', value: true }), { kind: 'ignore' });
  assert.deepStrictEqual(roleEdit({ type: 'edit', id: 'R', field: 'active', value: 'yes' }), { kind: 'ignore' });
});

test('a message with no id, or no message at all, is ignored', () => {
  for (const raw of [undefined, null, 'edit', 42, {}, { type: 'edit' }, { type: 'remove', id: '' }]) {
    assert.deepStrictEqual(roleEdit(raw), { kind: 'ignore' }, JSON.stringify(raw));
  }
});
