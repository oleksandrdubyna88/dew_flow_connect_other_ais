import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FEATURE_STAGE, RESULT_STAGE, type RoleRow } from '../roles';
import { rowsAfter } from '../rolesEdit';
import { CUSTOM_ROLES_SINCE, rolesHtml, type RolesPageState } from '../rolesPage';
import { roleEdit } from '../rolesMessages';
import { Node, runRolesPage, presses } from './rolesPageHarness';

/**
 * A role of one's own can be put in the FEATURE stage from the roles page (story S3.3 of
 * `todo/PLAN_feature_review.md`: "rolesPage.ts offers the stage").
 *
 * <p>S2.1 gave the feature stage a tab and a bucket, and the server composes a person's feature-stage role
 * (`RoleStages.All`) — but the Stage select offered Plan review and Code review only, so a feature-stage
 * row was drawn with the first option showing and could never be put there from the page at all.</p>
 *
 * <p>The options are read as the PARSED form of the markup — they are not a program, so there is nothing
 * to run — and choosing one is then run through the page's own script and the host's own edit, and the
 * page re-rendered from what the host stored.</p>
 */

const state = (rows: readonly RoleRow[]): RolesPageState => ({
  rows, texts: { 'requirements-general': 'Is every requirement met?' }, serverVersion: CUSTOM_ROLES_SINCE, perSide: false, uiScale: 0,
});

const mine = (stage: string): RoleRow => ({
  id: 'Requirements', name: 'Requirements we wrote', stage, active: false,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
});

/** One role's Stage select as rendered: every option's value, and which one is selected. */
function stageSelect(html: string, id: string): { values: readonly string[]; selected: readonly string[] } {
  const start = html.indexOf(`data-id="${id}"`);
  assert.notEqual(start, -1, `${id} is not on the page`);
  const open = html.indexOf('<select data-field="stage"', start);
  const close = html.indexOf('</select>', open);
  assert.ok(open > start && close > open, `${id} has no Stage select`);
  const options = [...html.slice(open, close).matchAll(/<option value="([^"]*)"([^>]*)>/g)];

  return {
    values: options.map((one) => one[1]!),
    selected: options.filter((one) => /\sselected\b/.test(one[2]!)).map((one) => one[1]!),
  };
}

/** The section a role's block is drawn in. */
function sectionOf(html: string, id: string): string {
  const at = html.indexOf(`data-id="${id}"`);
  const sections = [...html.matchAll(/<section id="section-([a-z]+)"/g)].filter((one) => one.index! < at);
  assert.ok(sections.length > 0, `${id} is drawn outside every section`);

  return sections.at(-1)![1]!;
}

test('the Stage select offers the feature stage', () => {
  const { values } = stageSelect(rolesHtml(state([mine(RESULT_STAGE)]), 'n0nce'), 'Requirements');

  assert.ok(values.includes(FEATURE_STAGE), `the select offers only ${values.join(', ')}`);
});

test('a feature-stage role is drawn with the feature stage selected, under the feature tab', () => {
  const html = rolesHtml(state([mine(FEATURE_STAGE)]), 'n0nce');

  assert.deepEqual(stageSelect(html, 'Requirements').selected, [FEATURE_STAGE],
    'the select shows another stage than the one the role is in');
  assert.equal(sectionOf(html, 'Requirements'), 'feature');
});

test('choosing it on the page moves the role to the feature stage, and the page draws it there', () => {
  const before = [mine(RESULT_STAGE)];
  const offered = stageSelect(rolesHtml(state(before), 'n0nce'), 'Requirements').values;
  assert.ok(offered.includes(FEATURE_STAGE), 'nothing on the page can choose the feature stage');

  const page = runRolesPage(state(before));
  const select = new Node({ field: 'stage' }, 'SELECT').under(new Node({ id: 'Requirements' }, 'DETAILS'));
  select.value = offered.find((value) => value === FEATURE_STAGE)!;
  page.fire('change', select);

  assert.equal(presses(page).length, 1, 'one change is one edit');
  const outcome = rowsAfter(before, roleEdit(presses(page)[0]), new Set(), state(before).texts);
  assert.equal(outcome.kind, 'rows', `the host refused the move: ${JSON.stringify(outcome)}`);
  const after = outcome.kind === 'rows' ? outcome.rows : [];
  assert.equal(after.find((row) => row.id === 'Requirements')?.stage, FEATURE_STAGE);
  assert.equal(sectionOf(rolesHtml(state(after), 'n0nce'), 'Requirements'), 'feature');
});
