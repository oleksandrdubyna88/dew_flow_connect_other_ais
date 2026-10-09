import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FEATURE_STAGE, RESULT_STAGE, type RoleRow } from '../roles';
import { rowsAfter } from '../rolesEdit';
import { roleEdit } from '../rolesMessages';
import { bubbled, pageTree, type PageNode } from './pageTree';
import { roleBlockIn, roleEditsOf, rolesPlaceHtml, runRolesPlace, type RolesPlaceState } from './rolesPlaceHarness';

/**
 * A role of one's own can be put in the FEATURE stage from the roles' editor (story S3.3 of
 * `todo/PLAN_feature_review.md`: "the roles page offers the stage").
 *
 * <p>S2.1 gave the feature stage a tab and a bucket, and the server composes a person's feature-stage role
 * (`RoleStages.All`) — but the Stage select offered Plan review and Code review only, so a feature-stage
 * row was drawn with the first option showing and could never be put there from the page at all.</p>
 *
 * <p>These held the Review roles tab until E5.1 step 4 of todo/PLAN_one_model_catalog.md deleted it; they ask the same
 * of Reviews › Roles & prompts on the Settings page, which draws the same blocks. Choosing is run through the page's
 * own script and the host's own edit, and the place re-drawn from what the host stored.</p>
 */

const TEXTS = { 'requirements-general': 'Is every requirement met?' };

const state = (rows: readonly RoleRow[]): RolesPlaceState => ({ rows, texts: TEXTS });

const mine = (stage: string): RoleRow => ({
  id: 'Requirements', name: 'Requirements we wrote', stage, active: false,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
});

/** The place's pane, parsed. */
const paneOf = (rows: readonly RoleRow[]): PageNode => pageTree(rolesPlaceHtml(state(rows)));

/** One role's Stage select as drawn. */
const stageSelectOf = (pane: PageNode, id: string): PageNode =>
  roleBlockIn(pane, id).one((node) => node.tagName === 'SELECT' && node.dataset.field === 'stage', `${id}'s Stage select`);

/** One role's Stage select: every option's value, and which one is selected. */
function stageOptions(pane: PageNode, id: string): { values: readonly string[]; selected: readonly string[] } {
  const options = stageSelectOf(pane, id).find((node) => node.tagName === 'OPTION');

  return {
    values: options.map((one) => one.attrs['value'] ?? ''),
    selected: options.filter((one) => 'selected' in one.attrs).map((one) => one.attrs['value'] ?? ''),
  };
}

/** The heading of the stage group a role's block is drawn in. */
function stageOf(pane: PageNode, id: string): string {
  const group = pane.find((node) => node.className === 'role-stage')
    .find((section) => section.find((node) => node.tagName === 'DETAILS' && node.dataset.id === id).length > 0);
  assert.ok(group !== undefined, `${id} is drawn outside every stage`);

  return group.one((node) => node.tagName === 'H3', 'stage heading').text();
}

test('the Stage select offers the feature stage', () => {
  const { values } = stageOptions(paneOf([mine(RESULT_STAGE)]), 'Requirements');

  assert.ok(values.includes(FEATURE_STAGE), `the select offers only ${values.join(', ')}`);
});

test('a feature-stage role is drawn with the feature stage selected, under the feature stage', () => {
  const pane = paneOf([mine(FEATURE_STAGE)]);

  assert.deepEqual(stageOptions(pane, 'Requirements').selected, [FEATURE_STAGE],
    'the select shows another stage than the one the role is in');
  assert.equal(stageOf(pane, 'Requirements'), 'Feature stage');
});

test('choosing it on the page moves the role to the feature stage, and the place draws it there', () => {
  const before = [mine(RESULT_STAGE)];
  const { page, pane } = runRolesPlace(state(before));
  assert.ok(stageOptions(pane, 'Requirements').values.includes(FEATURE_STAGE), 'nothing on the page can choose the feature stage');
  const select = stageSelectOf(pane, 'Requirements');
  select.value = FEATURE_STAGE;

  bubbled(page, 'change', select);

  const edits = roleEditsOf(page);
  assert.equal(edits.length, 1, 'one change is one edit');
  const outcome = rowsAfter(before, roleEdit(edits[0]), new Set(), TEXTS);
  assert.equal(outcome.kind, 'rows', `the host refused the move: ${JSON.stringify(outcome)}`);
  const after = outcome.kind === 'rows' ? outcome.rows : [];
  assert.equal(after.find((row) => row.id === 'Requirements')?.stage, FEATURE_STAGE);
  assert.equal(stageOf(paneOf(after), 'Requirements'), 'Feature stage');
});
