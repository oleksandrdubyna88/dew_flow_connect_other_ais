import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { RESULT_STAGE, isActive, whyNotAskable, type RoleRow } from '../roles';
import { rowsAfter, type RowsOutcome } from '../rolesEdit';
import { CUSTOM_ROLES_SINCE, rolesHtml, type RolesPageState } from '../rolesPage';
import { Node, runRolesPage } from './rolesPageHarness';

/**
 * Issue #338: a role could be switched on, counted, and never asked. Every new role was created with a
 * prompt that had no text and switched on when its stage had room; the server then skipped it every
 * round, in one line naming a generated id and a file path. A role is now never switched on without a
 * question to ask, and one that cannot be asked says so — by the name the person gave it.
 */

const mine: RoleRow = {
  id: 'Role2',
  name: 'Моя роль',
  stage: RESULT_STAGE,
  active: false,
  prompts: [{ id: 'role2-general', label: 'General' }, { id: 'role2-edges', label: 'Edges' }],
};

function rowsOf(outcome: RowsOutcome): readonly RoleRow[] {
  assert.strictEqual(outcome.kind, 'rows', `expected rows, got ${JSON.stringify(outcome)}`);

  return (outcome as { rows: readonly RoleRow[] }).rows;
}

function whyOf(outcome: RowsOutcome): string {
  assert.strictEqual(outcome.kind, 'refused', `expected a refusal, got ${JSON.stringify(outcome)}`);

  return (outcome as { why: string }).why;
}

// ---------- whyNotAskable ----------

test('a role this product ships can always be asked: its prompts are in the binary', () => {
  assert.equal(whyNotAskable({ id: BUILTIN_ROLES[0]!.id }, {}), '');
});

test('a role of one’s own whose first prompt has text can be asked', () => {
  assert.equal(whyNotAskable(mine, { 'role2-general': 'Is the requirement met?' }), '');
});

test('a role of one’s own whose first prompt has no text cannot, and is named by its NAME', () => {
  const why = whyNotAskable(mine, {});

  assert.ok(why.includes('Моя роль'), why);
  assert.ok(why.includes('General'), why);
  assert.equal(why.includes('Role2'), false, `the generated id is not what the person called it: ${why}`);
  assert.equal(why.includes('role2-general'), false, why);
});

test('blank text is no text, as it is on the server', () => {
  assert.notEqual(whyNotAskable(mine, { 'role2-general': '  \r\n\t ' }), '');
});

test('the FIRST prompt is the one asked: text on another prompt does not make the role askable', () => {
  assert.notEqual(whyNotAskable(mine, { 'role2-edges': 'Edge cases?' }), '');
});

test('a role of one’s own with no prompt at all cannot be asked, and says so without inventing a label', () => {
  const why = whyNotAskable({ ...mine, prompts: [] }, {});

  assert.ok(why.includes('Моя роль'), why);
  assert.equal(why.includes('undefined'), false, why);
});

test('a role with no name falls back to its id rather than to nothing', () => {
  const { name: _unnamed, ...nameless } = mine;

  assert.ok(whyNotAskable(nameless, {}).includes('Role2'));
});

// ---------- the rules ----------

test('a new role is created switched off, even with room in its stage: it has nothing to ask yet', () => {
  const after = rowsOf(rowsAfter([], { kind: 'add' }));

  assert.strictEqual(isActive(after[after.length - 1]!), false);
});

test('switching on a role whose question has no text is refused, naming the role', () => {
  const why = whyOf(rowsAfter([mine], { kind: 'edit', id: 'Role2', field: 'active', value: true }, new Set(), {}));

  assert.ok(why.includes('Моя роль'), why);
});

test('switching on a role whose question has text is stored', () => {
  const after = rowsOf(rowsAfter(
    [mine], { kind: 'edit', id: 'Role2', field: 'active', value: true }, new Set(), { 'role2-general': 'Is it met?' },
  ));

  assert.strictEqual(isActive(after.find((r) => r.id === 'Role2')!), true);
});

test('switching a role OFF is never refused for having no text', () => {
  const on = { ...mine, active: true };
  const outcome = rowsAfter([on], { kind: 'edit', id: 'Role2', field: 'active', value: false }, new Set(), {});

  assert.notStrictEqual(outcome.kind, 'refused');
});

test('a shipped role is switched on without any text of its own', () => {
  const shipped = BUILTIN_ROLES.find((r) => r.stage === RESULT_STAGE)!;
  const outcome = rowsAfter(
    [{ id: shipped.id, active: false }], { kind: 'edit', id: shipped.id, field: 'active', value: true }, new Set(), {},
  );

  assert.notStrictEqual(outcome.kind, 'refused');
});

// ---------- the page ----------

const state = (over: Partial<RolesPageState> = {}): RolesPageState => ({
  rows: [],
  texts: {},
  serverVersion: CUSTOM_ROLES_SINCE,
  perSide: false,
  uiScale: 0,
  ...over,
});

function block(html: string, id: string): string {
  const start = html.indexOf(`data-id="${id}"`);
  assert.notStrictEqual(start, -1, `${id} is not on the page`);
  const next = html.indexOf('<details class="role', start + 1);

  return html.slice(start, next === -1 ? html.length : next);
}

test('an ACTIVE role that cannot be asked is marked on the page, by name', () => {
  // The routes the refusal cannot see: a text erased after the switch, a hand-edited settings file, a
  // deleted prompt file. Without this the only signal was one line in a round reply.
  const html = rolesHtml(state({ rows: [{ ...mine, active: true }] }), 'n');
  const own = block(html, 'Role2');

  assert.match(own, /a round that asks its first prompt will skip it/);
  assert.ok(own.includes('Моя роль'), own);
});

test('the mark is absent when the question has text, and when the role is off', () => {
  const texted = rolesHtml(state({ rows: [{ ...mine, active: true }], texts: { 'role2-general': 'Is it met?' } }), 'n');
  const off = rolesHtml(state({ rows: [mine] }), 'n');

  assert.doesNotMatch(block(texted, 'Role2'), /a round that asks its first prompt will skip it/);
  assert.doesNotMatch(block(off, 'Role2'), /a round that asks its first prompt will skip it/);
});

// ---------- one press, one command ----------

test('a press on a role’s Active box asks the host ONCE, although the browser fires input and change', () => {
  // Our own code review: both listeners sent a checkbox, so one press was two commands. It hardly
  // mattered while a refusal needed five roles already on; now that switching on a role with no
  // question is refused as a matter of course, it was two reads of every prompt, two refusals and two
  // notices for one click.
  const page = runRolesPage(state({ rows: [mine] }));
  const role = new Node({ id: 'Role2' }, 'DETAILS');
  const box = new Node({ field: 'active' });
  box.type = 'checkbox';
  box.checked = true;
  box.parent = role;

  page.fire('input', box);
  page.fire('change', box);

  assert.equal(page.posted.filter((one) => one['field'] === 'active').length, 1, JSON.stringify(page.posted));
});

test('a typed field is still sent as it is typed', () => {
  const page = runRolesPage(state({ rows: [mine] }));
  const role = new Node({ id: 'Role2' }, 'DETAILS');
  const name = new Node({ field: 'name' });
  name.value = 'Моя роль, renamed';
  name.parent = role;

  page.fire('input', name);

  assert.equal(page.posted.filter((one) => one['field'] === 'name').length, 1, JSON.stringify(page.posted));
});

// ---------- the sentences, at their edges (the code round) ----------

test('a prompt id that is also an Object property name is read as no text, not as a function', () => {
  // `texts['constructor']` on a plain object is Object's own constructor; `.trim()` on it threw, and
  // the whole page render with it. A hand-edited settings file can name a prompt anything. (gemini)
  const odd: RoleRow = { ...mine, prompts: [{ id: 'constructor', label: 'General' }] };

  assert.notEqual(whyNotAskable(odd, {}), '');
  assert.equal(whyNotAskable(odd, { constructor: 'Is it met?' }), '');
});

test('a cleared name or label falls back to the id rather than to empty quotation marks', () => {
  const why = whyNotAskable({ ...mine, name: '  ', prompts: [{ id: 'role2-general', label: '' }] }, {});

  assert.equal(why.includes('“”'), false, why);
  assert.ok(why.includes('Role2') && why.includes('role2-general'), why);
});

test('a role with no prompt is told to add one, not to write in a box it does not have', () => {
  const noPrompt = { ...mine, prompts: [] };
  const refusal = rowsAfter([noPrompt], { kind: 'edit', id: 'Role2', field: 'active', value: true }, new Set(), {});
  const hint = block(rolesHtml(state({ rows: [{ ...noPrompt, active: true }] }), 'n'), 'Role2');

  assert.match(whyOf(refusal), /Add a prompt/);
  assert.doesNotMatch(whyOf(refusal), /box under it/);
  assert.match(hint, /Add a prompt/);
  assert.doesNotMatch(hint, /box under it/);
});

test('a role with an empty prompt is told to write in its box, once', () => {
  const refusal = whyOf(rowsAfter([mine], { kind: 'edit', id: 'Role2', field: 'active', value: true }, new Set(), {}));

  assert.equal(refusal.split('box under it').length - 1, 1, refusal);
});
