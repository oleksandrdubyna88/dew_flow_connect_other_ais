import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import {
  FEATURE_CODE,
  FEATURE_DOCUMENT,
  FEATURE_STAGE,
  MAX_ACTIVE_PER_BUCKET,
  MAX_ROLE_ID_LENGTH,
  PLAN_CODE,
  PLAN_DOCUMENT,
  PLAN_STAGE,
  RESULT_CODE,
  RESULT_DOCUMENT,
  RESULT_STAGE,
  activeCount,
  bucketOf,
  builtInFor,
  composed,
  idFor,
  isActive,
  isBuiltIn,
  promptIdFor,
  promptIdsInUse,
  rolesFrom,
  stageOf,
  type RoleRow,
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
  // The row naming Architecture does NOT rename it: the server takes only the switch and the extra
  // prompts from a row that names a built-in, so a panel that drew "Our architecture" would be
  // drawing a role no round has ever heard of. See the test below that states the rule on its own.
  assert.strictEqual(rows.find((r) => r.id === 'Architecture')?.name, 'Architecture');
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
  const code = activeCount([], RESULT_CODE);
  assert.strictEqual(
    code, BUILTIN_ROLES.filter((r) => r.stage === RESULT_STAGE && r.programmingTask).length);

  assert.strictEqual(activeCount([{ id: 'Architecture', active: false }], RESULT_CODE), code - 1);
  assert.strictEqual(
    activeCount([{ id: 'Requirements', stage: RESULT_STAGE, prompts: [{ id: 'requirements-general' }] }], RESULT_CODE),
    code + 1,
  );
});

test('the cap is the number the server caps at', () => {
  assert.strictEqual(MAX_ACTIVE_PER_BUCKET, 5);
});

// ---------- a row naming a built-in is an OVERRIDE, and the SERVER decides what it may override ----------

/**
 * `RoleComposition.Overridden` takes exactly two things from a row that names a shipped role: its
 * `Active`, and the prompts it adds. Its own comment says why about the rest — *"Id, name, stage and
 * kind are NEVER taken from the row. A built-in cannot be renamed — its id keys settings, session
 * files and every row of the rounds database."*
 *
 * <p>So a panel that applied a name anyway would draw "Mine" on a page whose every round then said
 * "Architecture": the two halves disagreeing about one role, which is the whole defect the shared
 * wire format exists to prevent. Found on this plan's code round by three reviewers at once.</p>
 */
test('a row cannot rename, restage or reclassify a role this product ships', () => {
  const shipped = BUILTIN_ROLES[0]!;
  const other = shipped.stage === PLAN_STAGE ? RESULT_STAGE : PLAN_STAGE;
  const drawn = composed([
    { id: shipped.id, name: 'Mine', stage: other, programmingTask: !shipped.programmingTask },
  ])[0]!;

  assert.strictEqual(drawn.name, shipped.name, 'the name is the seed’s, as the server reads it');
  assert.strictEqual(drawn.stage, shipped.stage, 'and so is the stage');
  assert.strictEqual(drawn.programmingTask, shipped.programmingTask, 'and so is the kind');
});

test('the switch is the one field a row may say about a role this product ships', () => {
  const shipped = BUILTIN_ROLES[0]!;

  assert.strictEqual(isActive(composed([{ id: shipped.id, active: false }])[0]!), false,
    'the server takes Active from the row, so the panel must draw it');
});

test('a role a person added keeps every field they wrote', () => {
  // The refusal above is about SHIPPED ids only: a role of their own is theirs to name and stage.
  const drawn = composed([{ id: 'Requirements', name: 'Mine', stage: PLAN_STAGE, programmingTask: false }]);
  const one = drawn.find((r) => r.id === 'Requirements')!;

  assert.strictEqual(one.name, 'Mine');
  assert.strictEqual(one.stage, PLAN_STAGE);
  assert.strictEqual(one.programmingTask, false);
});

// ---------- the setting IS the wire format, so nothing may be dropped on the way back ----------

test('a field this build does not know survives the round trip', () => {
  // The panel rewrites the whole array on every edit. A field the SERVER gains before the extension
  // does would be silently deleted by the first keystroke on the roles page — which is exactly how
  // `remoteVendor` was lost by three releases of this extension, one 400 at a time.
  const kept = rolesFrom([{ id: 'Requirements', name: 'Mine', kind: 'document' }])[0]!;

  assert.strictEqual((kept as unknown as Record<string, unknown>)['kind'], 'document');
  assert.strictEqual(kept.name, 'Mine', 'and the fields it does know are still read');
});

test('a prompt field this build does not know survives too', () => {
  const kept = rolesFrom([{ id: 'Requirements', prompts: [{ id: 'requirements-general', weight: 3 }] }])[0]!;

  assert.strictEqual((kept.prompts![0] as unknown as Record<string, unknown>)['weight'], 3);
});

test('a polluting key is not a field, however the row spells it', () => {
  const row = JSON.parse('{"id":"Requirements","__proto__":{"polluted":"yes"},"constructor":"no"}') as unknown;
  const kept = rolesFrom([row])[0]!;

  assert.strictEqual(({} as Record<string, unknown>)['polluted'], undefined, 'nothing reached Object.prototype');
  assert.ok(!Object.prototype.hasOwnProperty.call(kept, '__proto__'), 'and the key was not carried over');
  assert.ok(!Object.prototype.hasOwnProperty.call(kept, 'constructor'));
});

// ---------- an id becomes an environment variable, so it has a length ----------

test('a generated id stays short enough to be an environment variable', () => {
  // A role id becomes `COAI_ROUNDS_<ID>`, and a name pasted from a document is a name a person can
  // write. An id longer than the variable can be is a budget that reads from the settings file and
  // never from the block they paste — working in one of the two places, which is worse than neither.
  const id = idFor('Requirements '.repeat(40), none);

  assert.ok(id.length <= MAX_ROLE_ID_LENGTH, `a ${id.length}-character id`);
  assert.ok(id.length > 0);
});

test('two long names that start alike still get two different ids', () => {
  const first = idFor('Requirements '.repeat(40), none);
  const second = idFor('Requirements '.repeat(40), new Set([first.toLowerCase()]));

  assert.notStrictEqual(second, first);
  assert.ok(second.length <= MAX_ROLE_ID_LENGTH, `a ${second.length}-character id`);
});

// ---------- the composed catalog is one shape, not two ----------

/**
 * A built-in came out of `composed()` with every field filled in and a person's own role came out
 * exactly as it was stored — so `role.active` was a boolean on one and `undefined` on the other, and
 * anything reading it directly would read a live custom role as switched off. Every caller was
 * carrying its own `?? true`, which is the definition of a leaky shape. (gemini, second code round.)
 */
test('a role a person added comes out of the catalog as complete as a shipped one', () => {
  const bare = composed([{ id: 'Requirements' }]).find((r) => r.id === 'Requirements')!;

  assert.strictEqual(bare.active, true);
  assert.strictEqual(bare.stage, RESULT_STAGE);
  assert.strictEqual(bare.programmingTask, true);
  assert.strictEqual(bare.name, 'Requirements', 'named by its id when it has no name');
  assert.deepStrictEqual(bare.prompts, []);
});

test('the catalog fills nothing in that the row already said', () => {
  const said = composed([
    { id: 'Brief', name: 'The brief', stage: PLAN_STAGE, programmingTask: false, active: false,
      prompts: [{ id: 'brief-general', label: 'General' }] },
  ]).find((r) => r.id === 'Brief')!;

  assert.strictEqual(said.name, 'The brief');
  assert.strictEqual(said.stage, PLAN_STAGE);
  assert.strictEqual(said.programmingTask, false);
  assert.strictEqual(said.active, false);
  assert.deepStrictEqual(said.prompts, [{ id: 'brief-general', label: 'General' }]);
});

test('a field the catalog does not know survives being composed', () => {
  const kept = composed(rolesFrom([{ id: 'Requirements', kind: 'document' }]))
    .find((r) => r.id === 'Requirements')!;

  assert.strictEqual((kept as unknown as Record<string, unknown>)['kind'], 'document');
});

// ---------- the five-active limit means the same thing in both halves (plan 4) ----------

test('the count is per BUCKET, so five code roles do not block a document one', () => {
  // The server has counted per bucket since plan 1 (`active[role.Bucket]`) and this side counted
  // per STAGE. It was invisible while document roles ran in nothing; the day review_document
  // shipped it became step 2 of the manager's story — five shipped code roles active, and the page
  // refusing to switch on a single document role that the server would have accepted.
  const doc: RoleRow = {
    id: 'Requirements', stage: RESULT_STAGE, programmingTask: false,
    prompts: [{ id: 'requirements-general' }],
  };

  assert.strictEqual(activeCount([doc], RESULT_CODE), activeCount([], RESULT_CODE),
    'adding a document role changes no code count');
  assert.strictEqual(activeCount([doc], RESULT_DOCUMENT), activeCount([], RESULT_DOCUMENT) + 1);
});

test('a shipped document role is in the document bucket and no other', () => {
  assert.ok(activeCount([], RESULT_DOCUMENT) >= 1, 'the product ships document roles since plan 4');
  assert.strictEqual(
    activeCount([], RESULT_CODE) + activeCount([], RESULT_DOCUMENT) + activeCount([], PLAN_CODE) + activeCount([], FEATURE_CODE),
    BUILTIN_ROLES.length,
    'every shipped role is in exactly one bucket',
  );
});

// ---------- the feature stage is a bucket of its own (S2.1 of the feature-review plan) ----------

test('a shipped feature role is in the feature bucket, and in no code round', () => {
  assert.strictEqual(activeCount([], FEATURE_CODE), 1, 'the product ships one feature role');
  const feature = composed([]).find((r) => r.id === 'FeatureReview')!;

  assert.strictEqual(bucketOf(feature), FEATURE_CODE);
  assert.notStrictEqual(bucketOf(feature), RESULT_CODE, 'a feature role reads an outline, never a diff');
});

test('bucketOf answers every stage by name, and a feature row of either kind', () => {
  assert.strictEqual(bucketOf({ id: 'Seams', stage: FEATURE_STAGE }), FEATURE_CODE,
    'absent means a programming task, as the server defaults it');
  assert.strictEqual(bucketOf({ id: 'Seams', stage: FEATURE_STAGE, programmingTask: false }), FEATURE_DOCUMENT,
    'stored, composed, counted, run by nothing — like plan:document');
  assert.strictEqual(bucketOf({ id: 'Perf', stage: RESULT_STAGE }), RESULT_CODE);
  assert.strictEqual(bucketOf({ id: 'Brief', stage: PLAN_STAGE }), PLAN_CODE);
});

test('a stage this build does not know composes into the result bucket, never into a bucket nothing draws', () => {
  // `bucketOf` was a template string cast to the union: `${stage}:code` compiled for ANY stage word,
  // so a row whose stage was misspelt — or written by a newer extension — got a bucket no section
  // draws and no count sees, and the row vanished from the page without a word. The server drops such
  // a row with a sentence; the page has to draw it somewhere until that sentence arrives.
  assert.strictEqual(bucketOf({ id: 'Odd', stage: 'sometime' }), RESULT_CODE);
  assert.strictEqual(bucketOf({ id: 'Odd', stage: 'sometime', programmingTask: false }), RESULT_DOCUMENT);
  assert.strictEqual(activeCount([{ id: 'Odd', stage: 'sometime', prompts: [{ id: 'odd-general' }] }], RESULT_CODE),
    activeCount([], RESULT_CODE) + 1, 'and it is counted where it is drawn');
});

test('the feature bucket has its own five', () => {
  const five = ['A', 'B', 'C', 'D', 'E'].map((id) => ({ id, stage: FEATURE_STAGE, prompts: [{ id: `${id.toLowerCase()}-general` }] }));

  assert.strictEqual(activeCount(five, FEATURE_CODE), 6, 'the shipped one and five of a person\'s own');
  assert.strictEqual(activeCount(five, RESULT_CODE), activeCount([], RESULT_CODE), 'and the code count is untouched');
});

test('a row that is not a programming task is in a document bucket', () => {
  assert.strictEqual(
    bucketOf({ id: 'Requirements', stage: RESULT_STAGE, programmingTask: false }), RESULT_DOCUMENT);
  assert.strictEqual(
    bucketOf({ id: 'Brief', stage: PLAN_STAGE, programmingTask: false }), PLAN_DOCUMENT);
  assert.strictEqual(bucketOf({ id: 'Perf', stage: RESULT_STAGE }), RESULT_CODE,
    'absent means a programming task, as the server defaults it');
});

test('a row cannot move a shipped role out of its bucket', () => {
  // The server never takes stage or kind from a row naming a built-in, and `composed` mirrors that.
  const moved = composed([{ id: 'Architecture', stage: PLAN_STAGE, programmingTask: false }])
    .find((r) => r.id === 'Architecture');

  assert.strictEqual(bucketOf(moved!), RESULT_CODE);
});
