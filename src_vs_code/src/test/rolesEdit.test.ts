import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { MAX_ACTIVE_PER_STAGE, PLAN_STAGE, RESULT_STAGE, composed, isActive, type RoleRow } from '../roles';
import { promptBelongsTo, rowsAfter, type RowsOutcome } from '../rolesEdit';
import type { RolesCommand } from '../rolesPage';

/**
 * What one command does to the rows — the decisions the HOST used to make inline.
 *
 * <p><b>Why this module exists at all.</b> "Remove this role" did nothing for a whole plan, and no
 * test could have caught it: the rule lived in a private function of a webview host, which this
 * extension does not unit-test anywhere. The defect was not the missing branch, it was the missing
 * SEAM — four reviewers found it by reading, which is not a process. So the rules moved out here,
 * where a test can reach every one of them, and the host kept only what a host can do: read a
 * setting, write one, write a file, repaint.</p>
 *
 * <p>The other half of the same defect was the shape of the answer. `undefined` meant both "this
 * command changes nothing" and "this command is refused", so the refusal path and the removal path
 * were one branch and removal took the refusal's behaviour. The outcome is a union now; nothing can
 * confuse the three again.</p>
 */

const shipped = BUILTIN_ROLES[0]!;
const code = BUILTIN_ROLES.filter((r) => r.stage !== PLAN_STAGE);

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [{ id: 'requirements-general', label: 'General' }, { id: 'requirements-edges', label: 'Edges' }],
};

/** The rows of a successful outcome, or a failure naming what came back instead. */
function rowsOf(outcome: RowsOutcome): readonly RoleRow[] {
  assert.strictEqual(outcome.kind, 'rows', `expected rows, got ${JSON.stringify(outcome)}`);

  return (outcome as { rows: readonly RoleRow[] }).rows;
}

// ---------- removing a role ----------

test('removing a role a person added takes it out of the rows', () => {
  // It did NOT, for the whole of this plan: the command returned the same `undefined` that means
  // "refused", the caller read it as "write nothing", and the button was decoration.
  const after = rowsOf(rowsAfter([mine], { kind: 'remove', id: mine.id }));

  assert.deepStrictEqual(after, [], 'the row is gone');
});

test('removing a role names the prompt files that must go with it', () => {
  // Its bodies live in `<dataDir>/prompts/<id>.md`, which no row points at any more. Left behind,
  // they come BACK: the next role called "Requirements" generates the same id, generates the same
  // prompt id, and opens with text the person believed they had deleted.
  const outcome = rowsAfter([mine], { kind: 'remove', id: mine.id });

  assert.strictEqual(outcome.kind, 'rows');
  assert.deepStrictEqual(
    [...(outcome as { forget: readonly string[] }).forget].sort(),
    ['requirements-edges', 'requirements-general'],
  );
});

test('a role this product ships cannot be removed', () => {
  const outcome = rowsAfter([], { kind: 'remove', id: shipped.id });

  assert.strictEqual(outcome.kind, 'refused');
  assert.match((outcome as { why: string }).why, /ships/);
});

test('removing a role nobody has changes nothing', () => {
  assert.deepStrictEqual(rowsAfter([mine], { kind: 'remove', id: 'NoSuchRole' }), { kind: 'unchanged' });
});

// ---------- what a row may say about a role this product ships ----------

test('a shipped role cannot be renamed, restaged or reclassified through a command either', () => {
  // The page renders those controls read-only. This is the twin: a page is not a boundary, and the
  // webview can post whatever it likes.
  for (const field of ['name', 'stage', 'programmingTask'] as const) {
    const value = field === 'programmingTask' ? false : 'Mine';
    const outcome = rowsAfter([], { kind: 'edit', id: shipped.id, field, value });

    assert.strictEqual(outcome.kind, 'refused', field);
  }
});

test('a shipped role can still be switched off, because the server reads that one', () => {
  // A CODE role: the plan stage ships exactly one, so switching that one off is the empty-stage
  // refusal below rather than this rule.
  const after = rowsOf(rowsAfter([], { kind: 'edit', id: code[0]!.id, field: 'active', value: false }));
  const row = after.find((r) => r.id === code[0]!.id)!;

  assert.strictEqual(row.active, false, 'and an override row was created to hold it');
});

test('the one role this product ships in the plan stage cannot be switched off alone', () => {
  const outcome = rowsAfter([], { kind: 'edit', id: shipped.id, field: 'active', value: false });

  assert.strictEqual(outcome.kind, 'refused', 'it would leave the plan stage with no reviewer');
});

test('a role a person added takes every edit', () => {
  const after = rowsOf(rowsAfter([mine], { kind: 'edit', id: mine.id, field: 'name', value: 'Mine' }));

  assert.strictEqual(after[0]!.name, 'Mine');
});

// ---------- the cap, on the host as on the page ----------

test('a sixth role in a stage is refused rather than saved as active', () => {
  const refused = rowsAfter(fullStage(), { kind: 'edit', id: 'Spare', field: 'active', value: true });

  assert.strictEqual(refused.kind, 'refused');
  assert.match((refused as { why: string }).why, /five/i);
});

test('switching on a role that is already on is not a sixth', () => {
  const rows = fullStage();
  const outcome = rowsAfter(rows, { kind: 'edit', id: 'Extra0', field: 'active', value: true });

  assert.strictEqual(outcome.kind, 'rows');
});

/** The result stage with exactly five active roles in it, and a sixth switched off. */
function fullStage(): readonly RoleRow[] {
  const off: RoleRow[] = code.map((r) => ({ id: r.id, active: false }));
  const five: RoleRow[] = Array.from({ length: MAX_ACTIVE_PER_STAGE }, (_, i) => ({
    id: `Extra${i}`, stage: RESULT_STAGE, active: true,
  }));

  return [...off, ...five, { id: 'Spare', stage: RESULT_STAGE, active: false }];
}

test('adding a role into a full stage stores it switched off rather than lying about it', () => {
  // `added()` always wrote `active: true`. With five already on, the server caps and the person is
  // looking at a role whose box is ticked and which is in no round.
  const off = code.map((r) => ({ id: r.id, active: false }));
  const five = Array.from({ length: MAX_ACTIVE_PER_STAGE }, (_, i) => ({
    id: `Extra${i}`, stage: RESULT_STAGE, active: true,
  }));
  const after = rowsOf(rowsAfter([...off, ...five], { kind: 'add' }));
  const added = after[after.length - 1]!;

  assert.strictEqual(isActive(added), false);
});

test('adding a role into a stage with room stores it switched on', () => {
  const after = rowsOf(rowsAfter([], { kind: 'add' }));

  assert.strictEqual(isActive(after[after.length - 1]!), true);
});

test('the last role active in a stage cannot be switched off', () => {
  const off = code.slice(1).map((r) => ({ id: r.id, active: false }));
  const outcome = rowsAfter(off, { kind: 'edit', id: code[0]!.id, field: 'active', value: false });

  assert.strictEqual(outcome.kind, 'refused');
  assert.match((outcome as { why: string }).why, /no reviewer|last|only/i);
});

// ---------- prompts ----------

test('removing a prompt a person added names its file too', () => {
  const outcome = rowsAfter([mine], { kind: 'removePrompt', id: mine.id, promptId: 'requirements-edges' });
  const after = rowsOf(outcome);

  assert.deepStrictEqual(after[0]!.prompts!.map((p) => p.id), ['requirements-general']);
  assert.deepStrictEqual((outcome as { forget: readonly string[] }).forget, ['requirements-edges']);
});

test('a prompt this product ships cannot be removed', () => {
  const outcome = rowsAfter([], { kind: 'removePrompt', id: shipped.id, promptId: shipped.prompts[0]!.id });

  assert.strictEqual(outcome.kind, 'refused');
});

test('a prompt this product ships cannot be relabelled', () => {
  // Its label keys nothing, but the picker shows it and the help articles name it. The page draws
  // that input read-only; this is the twin.
  const outcome = rowsAfter([], {
    kind: 'editPrompt', id: shipped.id, promptId: shipped.prompts[0]!.id, field: 'label', value: 'Mine',
  });

  assert.strictEqual(outcome.kind, 'refused');
});

test('a prompt body is never a row, whoever asks', () => {
  // `text` goes to a file. A command carrying one must not put a paragraph of prose into the setting.
  const outcome = rowsAfter([mine], {
    kind: 'editPrompt', id: mine.id, promptId: 'requirements-general', field: 'text', value: 'a paragraph',
  });

  assert.deepStrictEqual(outcome, { kind: 'unchanged' });
});

test('adding a prompt gives it an id nothing else is using', () => {
  const after = rowsOf(rowsAfter([mine], { kind: 'addPrompt', id: mine.id }));
  const ids = after[0]!.prompts!.map((p) => p.id);

  assert.strictEqual(ids.length, 3);
  assert.strictEqual(new Set(ids).size, 3);
});

// ---------- what belongs to the host ----------

test('the commands only a host can carry out change no rows', () => {
  const untouched: readonly RolesCommand[] = [
    { kind: 'ignore' },
    { kind: 'zoom', delta: 1 },
    { kind: 'restorePrompt', id: shipped.id, promptId: shipped.prompts[0]!.id },
  ];

  for (const command of untouched) {
    assert.deepStrictEqual(rowsAfter([mine], command), { kind: 'unchanged' }, command.kind);
  }
});

// ---------- an id that reaches a PATH belongs to the role that claims it ----------

/**
 * `promptFile` refuses anything that is not a slug, which is what keeps a write inside the prompts
 * directory. This is the narrower question it cannot answer: whether the id belongs to the role the
 * message names — so a webview cannot write over an unrelated role's override by naming it.
 */
test('a prompt id belongs to the role that actually has it', () => {
  const all = composed([mine]);

  assert.ok(promptBelongsTo(all, mine.id, 'requirements-general'));
  assert.ok(promptBelongsTo(all, shipped.id, shipped.prompts[0]!.id), 'a shipped prompt belongs to its shipped role');
});

test('a prompt id belonging to another role is not this role’s to write', () => {
  const all = composed([mine]);

  assert.ok(!promptBelongsTo(all, shipped.id, 'requirements-general'));
  assert.ok(!promptBelongsTo(all, mine.id, shipped.prompts[0]!.id));
});

test('a prompt id nothing has at all belongs to nobody', () => {
  assert.ok(!promptBelongsTo(composed([mine]), mine.id, 'invented'));
  assert.ok(!promptBelongsTo(composed([mine]), 'NoSuchRole', 'requirements-general'));
});
