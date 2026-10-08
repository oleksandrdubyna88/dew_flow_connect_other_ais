import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { RESULT_STAGE, type RoleRow } from '../roles';
import { STOOD_DOWN, type Tombstone } from '../roleDeletion';
import { Node, presses } from './pageScriptHarness';
import { bubbled, type PageNode } from './pageTree';
import { roleBlockIn, roleEditsOf, runRolesPlace, type RolesPlace, type RolesPlaceState } from './rolesPlaceHarness';

/**
 * The roles' editor's script, RUN — Reviews › Roles & prompts on the Settings page.
 *
 * <p><b>Why this file exists and a text scan does not do.</b> Every other test of the roles asserts over the assembled
 * string — that a `data-remove` attribute is present, that an input is `readonly`. None of them can see whether the
 * script's delegated selectors actually MATCH those attributes. A script can hold a listener for every event, post a
 * message for every button, and still name a selector nothing in the document has: the string contains everything it is
 * supposed to contain and the page does nothing. "Remove this role" did nothing for a whole plan once, and no test on
 * this side could have told a message never sent from one sent and dropped.</p>
 *
 * <p>These pressed the Review roles tab's own script until E5.1 step 4 of todo/PLAN_one_model_catalog.md deleted the
 * tab; each now presses the control the place DREW (`pageTree.ts`), so a selector that matches nothing drawn is red.</p>
 */

const shipped = BUILTIN_ROLES.find((r) => r.stage !== 'plan')!;

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
};

/** The place with one role of the person's own beside the shipped ones. */
function run(over: RolesPlaceState = {}): RolesPlace {
  return runRolesPlace({ rows: [mine], ...over });
}

/** A button the place drew, matched by an attribute of its own. */
function button(pane: PageNode, attribute: string, value: string): PageNode {
  return pane.one((node) => node.tagName === 'BUTTON' && node.dataset[attribute] === value, `button ${attribute}=${value}`);
}

// ---------- the deletions the server has not been told about ----------

const stuck = (over: Partial<Tombstone> = {}): Tombstone => ({
  roleId: 'Role2',
  name: 'Role 2',
  promptIds: ['role2-general'],
  askedAt: '2026-09-18T12:00:00.000Z',
  nonce: 'n1',
  reason: 'the settings could not be written',
  failedAt: '2026-09-18T12:00:01.000Z',
  ...over,
} as Tombstone);

test('pressing Finish the deletion anyway posts it for THAT role', () => {
  // RUN, not read. The first version of this asserted that `data-finish="Role2"` appears in the markup, and both button
  // handlers could have been deleted while it stayed green. (codex, the code round, Blocking.)
  const { page, pane } = run({ stranded: [stuck()] });

  bubbled(page, 'click', button(pane, 'finish', 'Role2'));

  assert.deepEqual(roleEditsOf(page), [{ type: 'finishDeletion', id: 'Role2' }], 'the control is drawn and pressing it reaches nobody');
});

test('pressing Reload Window posts the reload, and nothing about a role', () => {
  const { page, pane } = run({ stranded: [stuck({ reason: STOOD_DOWN })] });

  bubbled(page, 'click', button(pane, 'reload', '1'));

  assert.deepEqual(roleEditsOf(page), [{ type: 'reloadWindow' }]);
});

test('a press somewhere else in the section posts nothing at all', () => {
  // The companion every delegated handler needs: a listener on the document that answered every press would post on the
  // name beside the buttons just as happily.
  const { page, pane } = run({ stranded: [stuck()] });
  const name = pane.one((node) => node.tagName === 'B' && node.text() === 'Role 2', 'the stuck role\'s name');

  bubbled(page, 'click', name);

  assert.deepEqual(presses(page), []);
});

// ---------- the buttons ----------

test('clicking Remove on a role posts a remove for THAT role', () => {
  const { page, pane } = run();

  bubbled(page, 'click', button(pane, 'remove', 'Requirements'));

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'remove', id: 'Requirements' }]);
});

test('clicking Add a role posts an add and nothing else', () => {
  const { page, pane } = run();

  bubbled(page, 'click', button(pane, 'add', 'role'));

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'add' }]);
});

test('clicking Add a prompt names the role it was pressed in', () => {
  const { page, pane } = run();

  bubbled(page, 'click', button(roleBlockIn(pane, 'Requirements'), 'addPrompt', 'Requirements'));

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'addPrompt', id: 'Requirements' }]);
});

test('clicking Remove on a prompt names the role AND the prompt', () => {
  const { page, pane } = run();

  bubbled(page, 'click', button(roleBlockIn(pane, 'Requirements'), 'removePrompt', 'requirements-general'));

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'removePrompt', id: 'Requirements', promptId: 'requirements-general' }]);
});

test('clicking Restore on a shipped prompt names the role AND the prompt', () => {
  // Restore is drawn disabled until the shipped prompt has been rewritten, so the place is drawn with a rewrite.
  const promptId = shipped.prompts[0]!.id;
  const { page, pane } = run({ texts: { [promptId]: 'in our words' } });

  bubbled(page, 'click', button(roleBlockIn(pane, shipped.id), 'restore', promptId));

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'restorePrompt', id: shipped.id, promptId }]);
});

// ---------- the fields ----------

/** A field of a role's own (not of a prompt), as the place drew it. */
function roleField(pane: PageNode, field: string): PageNode {
  return roleBlockIn(pane, 'Requirements').one((node) => node.dataset.field === field && node.closest('[data-role-prompt]') === null,
    `the ${field} field of Requirements`);
}

test('typing a role name posts an edit carrying the text', () => {
  const { page, pane } = run();
  const input = roleField(pane, 'name');
  input.value = 'Requirements we wrote';

  bubbled(page, 'input', input);

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'edit', id: 'Requirements', field: 'name', value: 'Requirements we wrote' }]);
});

test('typing in a prompt body posts an editPrompt, not an edit', () => {
  // The two are a different destination entirely: one is a setting, the other a file.
  const { page, pane } = run();
  const box = roleBlockIn(pane, 'Requirements').one((node) => node.tagName === 'TEXTAREA' && node.dataset.field === 'text', 'the prompt box');
  box.value = 'Whether the requirements are met.';

  bubbled(page, 'input', box);

  assert.deepStrictEqual(roleEditsOf(page), [{
    type: 'editPrompt', id: 'Requirements', promptId: 'requirements-general', field: 'text', value: 'Whether the requirements are met.',
  }]);
});

test('ticking Active posts a BOOLEAN, which is what the parser will accept', () => {
  // `roleEdit` refuses a string for a flag. A script that posted `"true"` would be refused at the boundary and the tick
  // would do nothing — which is a text scan's blind spot exactly.
  const { page, pane } = run();
  const box = roleField(pane, 'active');
  box.checked = false;

  bubbled(page, 'change', box);

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'edit', id: 'Requirements', field: 'active', value: false }]);
});

test('choosing a stage posts the value the select holds', () => {
  const { page, pane } = run();
  const select = roleField(pane, 'stage');
  select.value = 'plan';

  bubbled(page, 'change', select);

  assert.deepStrictEqual(roleEditsOf(page), [{ type: 'edit', id: 'Requirements', field: 'stage', value: 'plan' }]);
});

// ---------- what it must NOT do ----------

test('a control outside every role posts nothing at all', () => {
  const { page } = run();

  page.fire('input', new Node({ field: 'name' }));

  assert.deepStrictEqual(presses(page), [], 'there is no role for it to belong to');
});

test('typing in a checkbox-less control does not fire on change', () => {
  // `change` is for the tick and the select only; a text field posting on both would post twice.
  const { page, pane } = run();

  bubbled(page, 'change', roleField(pane, 'name'));

  assert.deepStrictEqual(roleEditsOf(page), []);
});
