import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import {
  MAX_ACTIVE_PER_STAGE,
  PLAN_STAGE,
  RESULT_STAGE,
  activeCount,
  builtInFor,
  composed,
  idFor,
  isActive,
  isBuiltIn,
  promptIdFor,
  promptIdsInUse,
  rolesFrom,
  stageOf,
} from '../roles';

/**
 * The role rows a person configures, and the rules the page must not offer to break.
 *
 * <p>Every rule here has a twin in `RoleComposition` on the server, which is the boundary that
 * actually holds. These exist so the PAGE never offers an action the server would refuse — a
 * checkbox that saves and then does nothing is worse than one that is disabled with a reason.</p>
 */

const none = new Set<string>();

test('an array of rows survives a round trip through JSON unchanged', () => {
  // The setting IS the wire format. Anything this drops on the way back is a field the server
  // reads and the panel would silently stop sending.
  const rows = [
    { id: 'Requirements', name: 'Requirements we wrote', stage: RESULT_STAGE, programmingTask: true, active: true,
      prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }] },
    { id: 'Brief', name: 'The brief', stage: PLAN_STAGE },
  ];

  assert.deepStrictEqual(rolesFrom(JSON.parse(JSON.stringify(rows))), rows);
});

test('a row that says nothing beyond its id keeps saying nothing', () => {
  // The override case: a row naming a built-in must be able to leave every field alone. A parser
  // that filled in defaults here would switch a role off by reading it.
  assert.deepStrictEqual(rolesFrom([{ id: 'Architecture' }]), [{ id: 'Architecture' }]);
});

test('a row this build cannot use is dropped, and the others survive', () => {
  const rows = rolesFrom([
    null,
    'not a row',
    { id: '' },
    { id: '1bad' },
    { id: 'my-role' },
    { id: 'Requirements', name: 'Kept' },
    { id: 'requirements', name: 'The same id in another case' },
  ]);

  assert.deepStrictEqual(rows, [{ id: 'Requirements', name: 'Kept' }]);
});

test('anything that is not an array at all is no roles, rather than a crash', () => {
  for (const raw of [undefined, null, 42, 'roles', {}]) {
    assert.deepStrictEqual(rolesFrom(raw), []);
  }
});

test('a prompt whose id is not a slug is dropped, and the role keeps the rest', () => {
  const rows = rolesFrom([
    { id: 'Requirements', prompts: [
      { id: 'Requirements-General' },
      { id: '../../escaped' },
      { id: 'con' },
      { id: 'requirements-general', label: 'General' },
    ] },
  ]);

  assert.deepStrictEqual(rows, [{ id: 'Requirements', prompts: [{ id: 'requirements-general', label: 'General' }] }]);
});

test('an id is latin, starts with a letter, and carries no hyphen', () => {
  // It becomes COAI_ROUNDS_<ID>, and COAI_ROUNDS_MY-ROLE is not a name a POSIX shell can export.
  for (const name of ['Requirements we wrote', 'my role', '2nd opinion', 'a-b-c']) {
    assert.match(idFor(name, none), /^[A-Za-z][A-Za-z0-9_]*$/, name);
  }
});

test('a name this build cannot spell in latin still gets an id', () => {
  // A name in Cyrillic is a name, not a mistake. The NAME still says what the person meant.
  for (const name of ['Требования', '要件', '']) {
    assert.match(idFor(name, none), /^Role[0-9]*$/, name);
  }
});

test('two roles named the same get different ids', () => {
  const first = idFor('Requirements', none);
  const second = idFor('Requirements', new Set([first.toLowerCase()]));

  assert.notStrictEqual(first, second);
  assert.match(second, /^[A-Za-z][A-Za-z0-9_]*$/);
});

test('a new role never takes a shipped id, whatever its case', () => {
  for (const name of ['Architecture', 'architecture', 'Conventions']) {
    assert.strictEqual(isBuiltIn(idFor(name, none)), false, name);
  }
});

test('a prompt id is unique across the whole catalog, not just its role', () => {
  // One prompt id names one text for the whole product: it is the file under <dataDir>/prompts/,
  // so two roles claiming one id would be two roles sharing one override.
  const taken = promptIdsInUse([]);

  assert.ok(taken.has('architecture'), 'the shipped ids are in use');
  assert.ok(!taken.has('requirements-general'));

  const id = promptIdFor('Requirements', 'General', taken);
  assert.match(id, /^[a-z0-9][a-z0-9-]*$/);
  assert.ok(!taken.has(id));
});

test('a prompt label this build cannot slug still gets an id under its role', () => {
  const id = promptIdFor('Requirements', 'Требования', promptIdsInUse([]));

  assert.match(id, /^[a-z0-9][a-z0-9-]*$/);
  assert.ok(id.startsWith('requirements'));
});

test('a prompt id already taken is stepped past, not handed out twice', () => {
  // The collision path, exercised deliberately: the first version of this file asserted only that a
  // FRESH id came back unused, which is true whatever the uniqueness code does — breaking the check
  // left every assertion green. Two labels that slug the same is the ordinary way a person hits it.
  const taken = new Set(promptIdsInUse([]));
  const first = promptIdFor('Requirements', 'General', taken);
  taken.add(first);
  const second = promptIdFor('Requirements', 'General', taken);

  assert.notStrictEqual(second, first);
  assert.ok(!taken.has(second));
  assert.match(second, /^[a-z0-9][a-z0-9-]*$/);
});

test('a prompt id that collides with a SHIPPED one is stepped past too', () => {
  // One prompt id names one text for the whole product, so a person adding "Architecture" to their
  // own role must not land on the shipped `architecture` and share its override file.
  const id = promptIdFor('Arch', 'itecture', promptIdsInUse([]));

  assert.ok(!promptIdsInUse([]).has(id), id);
});

test('a stage is the row’s, else the built-in’s, else the result stage', () => {
  assert.strictEqual(stageOf({ id: 'Brief', stage: PLAN_STAGE }), PLAN_STAGE);
  assert.strictEqual(stageOf({ id: 'Architecture' }), RESULT_STAGE);
  assert.strictEqual(stageOf({ id: 'PlanCritique' }), PLAN_STAGE);
  assert.strictEqual(stageOf({ id: 'Requirements' }), RESULT_STAGE);
});

test('a row that says nothing about being active is active', () => {
  assert.strictEqual(isActive({ id: 'Architecture' }), true);
  assert.strictEqual(isActive({ id: 'Architecture', active: false }), false);
});

test('the composed picture is the shipped five, edited, then the ones a person added', () => {
  const rows = composed([
    { id: 'Architecture', name: 'Our architecture' },
    { id: 'Requirements', name: 'Requirements we wrote', stage: RESULT_STAGE },
  ]);

  assert.deepStrictEqual(
    rows.map((r) => r.id),
    [...BUILTIN_ROLES.map((r) => r.id), 'Requirements'],
  );
  assert.strictEqual(rows.find((r) => r.id === 'Architecture')?.name, 'Our architecture');
  assert.strictEqual(rows.find((r) => r.id === 'Conventions')?.name, 'Conventions', 'untouched by any row');
});

test('a prompt a person adds to a built-in is appended, never replacing the shipped ones', () => {
  const role = composed([{ id: 'Architecture', prompts: [{ id: 'arch-our-layering', label: 'Our layering' }] }])
    .find((r) => r.id === 'Architecture');
  const shipped = builtInFor('Architecture')!;

  assert.deepStrictEqual(
    role?.prompts?.map((p) => p.id),
    [...shipped.prompts.map((p) => p.id), 'arch-our-layering'],
  );
});

test('a row that repeats a shipped prompt id does not double it', () => {
  const role = composed([{ id: 'Architecture', prompts: [{ id: 'architecture', label: 'Mine' }] }])
    .find((r) => r.id === 'Architecture');

  assert.strictEqual(role?.prompts?.filter((p) => p.id === 'architecture').length, 1);
});

test('the active count is over the composed picture, which is what the server caps', () => {
  const code = activeCount([], RESULT_STAGE);
  assert.strictEqual(code, BUILTIN_ROLES.filter((r) => r.stage === RESULT_STAGE).length);

  assert.strictEqual(activeCount([{ id: 'Architecture', active: false }], RESULT_STAGE), code - 1);
  assert.strictEqual(
    activeCount([{ id: 'Requirements', stage: RESULT_STAGE, prompts: [{ id: 'requirements-general' }] }], RESULT_STAGE),
    code + 1,
  );
});

test('the cap is the number the server caps at', () => {
  assert.strictEqual(MAX_ACTIVE_PER_STAGE, 5);
});
