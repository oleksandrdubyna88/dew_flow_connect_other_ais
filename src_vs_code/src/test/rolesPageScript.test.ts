import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { CUSTOM_ROLES_SINCE, type RolesPageState } from '../rolesPage';
import { RESULT_STAGE, type RoleRow } from '../roles';
import { Node, type Page, runRolesPage } from './rolesPageHarness';

/**
 * The roles page's own script, RUN.
 *
 * <p><b>Why this file exists and a text scan does not do.</b> Every other test of this page asserts
 * over the assembled string — that a `data-remove` attribute is present, that an input is `readonly`.
 * None of them can see whether the script's delegated selectors actually MATCH those attributes. A
 * script can hold a listener for every event, post a message for every button, and still name a
 * selector nothing in the document has: the string contains everything it is supposed to contain and
 * the page does nothing. The artefact is correct as text and wrong as a program.</p>
 *
 * <p>That is not hypothetical here. "Remove this role" did nothing for the whole of this plan —
 * the defect was in the host rather than in this script, but no test on this side could have told the
 * two apart, because none of them ran anything. So the script is executed against a synthetic
 * document and the messages it posts are read, which is the arrangement
 * `thePromptBoxRemembers.test.ts` already uses for the panel and
 * `common/generated-code-tests.md` now requires of every executable artefact.</p>
 *
 * <p>The context is explicit and small — five names, no ambient environment, no filesystem, no
 * network — and the script is synchronous, so it needs no deadline of its own.</p>
 */

const shipped = BUILTIN_ROLES.find((r) => r.stage !== 'plan')!;

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
};

const state = (over: Partial<RolesPageState> = {}): RolesPageState => ({
  rows: [mine],
  texts: {},
  serverVersion: CUSTOM_ROLES_SINCE,
  perSide: false,
  uiScale: 0,
  ...over,
});

/** Run the roles page's script and collect everything it posts.
 *
 * <p>The shim and the runner live in {@link ./rolesPageHarness}, extracted there when
 * `editRolesInTabs.test.ts` needed the same thing: a second DOM shim is two shims that drift,
 * and the one thing a shim must be is the same for everybody asserting against it.</p>
 */
function run(over: Partial<RolesPageState> = {}): Page {
  return runRolesPage(state(over));
}

// ---------- the buttons ----------

test('clicking Remove on a role posts a remove for THAT role', () => {
  // The defect this whole module was written after. The host is what ignored the message, but
  // nothing on this side could have told a message never sent from one sent and dropped.
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const button = new Node({ remove: 'Requirements' }, 'BUTTON').under(role);

  page.fire('click', button);

  assert.deepStrictEqual(page.posted, [{ type: 'remove', id: 'Requirements' }]);
});

test('clicking Add a role posts an add and nothing else', () => {
  const page = run();

  page.fire('click', new Node({ add: 'role' }, 'BUTTON'));

  assert.deepStrictEqual(page.posted, [{ type: 'add' }]);
});

test('clicking Add a prompt names the role it was pressed in', () => {
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');

  page.fire('click', new Node({ addPrompt: 'Requirements' }, 'BUTTON').under(role));

  assert.deepStrictEqual(page.posted, [{ type: 'addPrompt', id: 'Requirements' }]);
});

test('clicking Remove on a prompt names the role AND the prompt', () => {
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const prompt = new Node({ prompt: 'requirements-general' }, 'DIV').under(role);

  page.fire('click', new Node({ removePrompt: 'requirements-general' }, 'BUTTON').under(prompt));

  assert.deepStrictEqual(page.posted, [
    { type: 'removePrompt', id: 'Requirements', promptId: 'requirements-general' },
  ]);
});

test('clicking Restore on a shipped prompt names the role AND the prompt', () => {
  const page = run();
  const role = new Node({ id: shipped.id }, 'DETAILS');
  const prompt = new Node({ prompt: shipped.prompts[0]!.id }, 'DIV').under(role);

  page.fire('click', new Node({ restore: shipped.prompts[0]!.id }, 'BUTTON').under(prompt));

  assert.deepStrictEqual(page.posted, [
    { type: 'restorePrompt', id: shipped.id, promptId: shipped.prompts[0]!.id },
  ]);
});

// ---------- the fields ----------

test('typing a role name posts an edit carrying the text', () => {
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const input = new Node({ field: 'name' }).under(role);
  input.value = 'Requirements we wrote';

  page.fire('input', input);

  assert.deepStrictEqual(page.posted, [
    { type: 'edit', id: 'Requirements', field: 'name', value: 'Requirements we wrote' },
  ]);
});

test('typing in a prompt body posts an editPrompt, not an edit', () => {
  // The two are a different destination entirely: one is a setting, the other a file.
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const prompt = new Node({ prompt: 'requirements-general' }, 'DIV').under(role);
  const box = new Node({ field: 'text' }, 'TEXTAREA').under(prompt);
  box.value = 'Whether the requirements are met.';

  page.fire('input', box);

  assert.deepStrictEqual(page.posted, [{
    type: 'editPrompt', id: 'Requirements', promptId: 'requirements-general',
    field: 'text', value: 'Whether the requirements are met.',
  }]);
});

test('ticking Active posts a BOOLEAN, which is what the parser will accept', () => {
  // `roleEdit` refuses a string for a flag. A script that posted `"true"` would be refused at the
  // boundary and the tick would do nothing — which is a text scan's blind spot exactly.
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const box = new Node({ field: 'active' }).under(role);
  box.type = 'checkbox';
  box.checked = false;

  page.fire('change', box);

  assert.deepStrictEqual(page.posted, [
    { type: 'edit', id: 'Requirements', field: 'active', value: false },
  ]);
});

test('choosing a stage posts the value the select holds', () => {
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const select = new Node({ field: 'stage' }, 'SELECT').under(role);
  select.value = 'plan';

  page.fire('change', select);

  assert.deepStrictEqual(page.posted, [
    { type: 'edit', id: 'Requirements', field: 'stage', value: 'plan' },
  ]);
});

// ---------- what it must NOT do ----------

test('a control outside every role posts nothing at all', () => {
  const page = run();

  page.fire('input', new Node({ field: 'name' }));

  assert.deepStrictEqual(page.posted, [], 'there is no role for it to belong to');
});

test('typing in a checkbox-less control does not fire on change', () => {
  // `change` is for the tick and the select only; a text field posting on both would post twice.
  const page = run();
  const role = new Node({ id: 'Requirements' }, 'DETAILS');
  const input = new Node({ field: 'name' }).under(role);

  page.fire('change', input);

  assert.deepStrictEqual(page.posted, []);
});
