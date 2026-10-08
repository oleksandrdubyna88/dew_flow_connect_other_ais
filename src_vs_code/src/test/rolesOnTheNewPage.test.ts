import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { composed, type RoleRow } from '../roles';
import { roleBlock as drawRoleBlock, roleBlockOptions } from '../rolesBlocks';
import { rowsAfter } from '../rolesEdit';
import { roleSwitchFollows } from '../rolesSwitch';
import { DEFAULTS, envBlock } from '../settingsShape';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * E4.3 of todo/PLAN_one_model_catalog.md: Roles & prompts on the new page — the Review roles tab's content drawn by the
 * panel, its edits posted as `roles` messages into the one editing core (`rolesHost.ts`), and ONE switch per role:
 * the catalog's `active` and the panel's `roleEnabled` read as one and written as one. Every control a test fires at
 * is taken from the page as drawn (`pageTree.ts`).
 */

function stateWith(rows: readonly RoleRow[] = [], roleEnabled: Readonly<Record<string, boolean>> = {}): PanelState {
  const base = panelState('reviewers');

  return {
    ...base,
    settings: { ...base.settings, roles: rows, roleEnabled: { ...base.settings.roleEnabled, ...roleEnabled } },
    roles: { rows, texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

/** What the page's scripts select at load, so they bind to what the page drew. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page on Roles & prompts, drawn and running. */
function rolesPage(state: PanelState = stateWith()) {
  const html = catalogHtml(state, 'test-nonce', 'reviews/roles');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  return { page, pane: tree.one((node) => node.dataset.pane === 'reviews/roles', 'Roles & prompts pane') };
}

/** One role's block, and a control in it. */
function roleBlock(pane: PageNode, id: string): PageNode {
  return pane.one((node) => node.tagName === 'DETAILS' && node.dataset.id === id, `role ${id}`);
}

test('every role is drawn on Roles & prompts', () => {
  const { pane } = rolesPage();
  const drawn = pane.find((node) => node.tagName === 'DETAILS' && node.dataset.id !== undefined).map((node) => node.dataset.id);

  assert.deepEqual(drawn, composed([]).map((role) => role.id));
});

test('a prompt typed on Roles & prompts is the roles\' edit — never read as a round pick', () => {
  const { page, pane } = rolesPage();
  const box = pane.one((node) => node.tagName === 'TEXTAREA' && node.dataset.field === 'text', 'prompt box');
  box.value = 'What does this break?';

  bubbled(page, 'input', box);
  bubbled(page, 'change', box);

  const types = page.posted.map((one) => one['type']);
  assert.ok(types.includes('roles'), 'the typed prompt was not posted as the roles\' edit');
  assert.ok(!types.includes('prompt'), 'the panel read a prompt box as a round pick and would store its text as one');
});

test('a code role switched off by EITHER switch is drawn off: one switch, read as one', () => {
  const { pane } = rolesPage(stateWith([], { Architecture: false }));
  const active = roleBlock(pane, 'Architecture').one((node) => node.dataset.field === 'active', 'Architecture\'s switch');

  assert.equal(active.checked, false);
});

test('Roles & prompts no longer says where it is', () => {
  // The pane's text after the page's script ran — decoded, as a person reads it — never the generated source (CodeRabbit on #688).
  assert.doesNotMatch(rolesPage().pane.text(), /Roles & prompts is still on the current Settings page/);
});

test('the roles\' controls post `roles` edits — a pick numbered, typing not — and report focus', () => {
  const { page, pane } = rolesPage();
  const block = roleBlock(pane, 'Architecture');
  const name = block.one((node) => node.dataset.field === 'name', 'the name box');
  name.value = 'Architecture, again';
  const active = block.one((node) => node.dataset.field === 'active', 'the switch');
  active.checked = false;
  const prompt = block.one((node) => node.dataset.rolePrompt !== undefined, 'a prompt');
  const text = prompt.one((node) => node.dataset.field === 'text', 'its box');
  text.value = 'What does this break?';
  const addPrompt = block.one((node) => node.dataset.addPrompt !== undefined, 'Add a prompt');

  bubbled(page, 'input', name);
  bubbled(page, 'change', active);
  bubbled(page, 'input', text);
  bubbled(page, 'click', addPrompt);
  bubbled(page, 'focusin', text);

  const roles = page.posted.filter((one) => one['type'] === 'roles');
  assert.deepEqual(roles.map((one) => one['edit']), [
    { type: 'edit', id: 'Architecture', field: 'name', value: 'Architecture, again' },
    { type: 'edit', id: 'Architecture', field: 'active', value: false },
    { type: 'editPrompt', id: 'Architecture', promptId: prompt.dataset.rolePrompt, field: 'text', value: 'What does this break?' },
    { type: 'addPrompt', id: 'Architecture' },
  ]);
  assert.deepEqual(roles.map((one) => one['seq'] !== undefined), [false, true, false, true], 'typing is numbered, or a pick is not');
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: `roles|Architecture|${prompt.dataset.rolePrompt}|text`, editing: true }]);
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

/** The rows and the panel's switches a bucket is drawn and guarded with. */
interface Switches {
  readonly rows: readonly RoleRow[];
  readonly roleEnabled: Readonly<Record<string, boolean>>;
}

/**
 * One bucket with exactly one role ON by the one rule (`rolesSwitch.switchedOn`) — and the same bucket with another
 * role ON beside it, which is what proves the refusal is about the count and not a switch that never moves.
 */
interface LastOn {
  readonly bucket: string;
  readonly last: string;
  readonly alone: Switches;
  readonly another: Switches;
}

/** A role of the person's own, active in the catalog — the second role of a bucket the shipped roles hold one of. */
function mine(id: string, stage: string): RoleRow {
  return { id, name: id, stage, programmingTask: true, active: true, prompts: [{ id: id.toLowerCase(), label: 'General' }] };
}

/**
 * EVERY bucket a round runs (E5.1b, and the plan round's finding: the guard is tested in each, not only the code bucket
 * the current page guards). In each the other role is active in the CATALOG but switched off by the panel's switch —
 * except the plan stage, whose roles have no second switch, so its other role is off in the catalog.
 */
const LAST_ON: readonly LastOn[] = [
  {
    bucket: 'plan',
    last: 'PlanCritique',
    alone: { rows: [{ ...mine('MyPlan', 'plan'), active: false }], roleEnabled: {} },
    another: { rows: [mine('MyPlan', 'plan')], roleEnabled: {} },
  },
  {
    bucket: 'code',
    last: 'Architecture',
    alone: {
      rows: [{ id: 'SecurityReliability', active: false }, { id: 'UxDxPerformance', active: false }],
      roleEnabled: { Conventions: false },
    },
    another: { rows: [{ id: 'SecurityReliability', active: false }, { id: 'UxDxPerformance', active: false }], roleEnabled: {} },
  },
  { bucket: 'documents', last: 'DocumentSummary', alone: { rows: [], roleEnabled: { DocumentReview: false } }, another: { rows: [], roleEnabled: {} } },
  {
    bucket: 'feature',
    last: 'FeatureReview',
    alone: { rows: [mine('MyFeature', 'feature')], roleEnabled: { MyFeature: false } },
    another: { rows: [mine('MyFeature', 'feature')], roleEnabled: {} },
  },
];

/** The last-standing role's switch on the new page, as drawn — and whether the page says why it cannot move. */
function lastSwitch(switches: Switches, id: string): { readonly disabled: boolean; readonly said: boolean } {
  const block = roleBlock(rolesPage(stateWith(switches.rows, switches.roleEnabled)).pane, id);
  const active = block.one((node) => node.dataset.field === 'active', `${id}'s switch`);

  return { disabled: active.disabled, said: /The only role still active in this stage/.test(block.text()) };
}

/** What the host's guard does with switching that role off. */
function hostSwitchOff(switches: Switches, id: string): string {
  return rowsAfter(switches.rows, { kind: 'edit', id, field: 'active', value: false }, new Set(), {}, switches.roleEnabled).kind;
}

for (const { bucket, last, alone, another } of LAST_ON) {
  test(`the ${bucket} bucket's last role ON cannot be switched off on the new page — a role off by the panel's switch is not ON`, () => {
    assert.deepEqual(lastSwitch(alone, last), { disabled: true, said: true },
      `${last} could be switched off while every other ${bucket} role is off, so the ${bucket} stage would run no role`);
    assert.deepEqual(lastSwitch(another, last), { disabled: false, said: false }, `${last} could not be switched off with another ${bucket} role ON`);
  });

  test(`the host refuses to switch off the ${bucket} bucket's last role ON — the twin of the page's disabled switch`, () => {
    assert.equal(hostSwitchOff(alone, last), 'refused', `the host stored ${last} switched off, leaving the ${bucket} stage with no role ON`);
    assert.equal(hostSwitchOff(another, last), 'rows', `the host refused ${last} with another ${bucket} role ON`);
  });
}

/** A host file's text, between two markers — a host runs only inside an editor, so its wiring is pinned by source. */
function sourceBetween(file: string, from: string, to: string): string {
  const source = readFileSync(join(__dirname, '..', '..', 'src', file), 'utf8');

  return source.slice(source.indexOf(from), source.indexOf(to));
}

test('the new page\'s role edits reach the host guard with the panel\'s switches, read when the queue applies them', () => {
  // The rule above is `rowsAfter`'s; these pin that the new page's edits are handed to it WITH the switches — the
  // panel passes a reader to the one queue, and the queue reads it for a row edit and for a removal alike.
  assert.match(sourceBetween('panelProvider.ts', 'private async roleEdited(', 'private roleSwitches('), /queueRoleEdit\(command, \(\) => this\.roleSwitches\(\)\)/u,
    'the new page queues its role edits without the panel\'s switches, so the host counts the catalog alone');
  assert.match(sourceBetween('rolesHost.ts', 'async function store(', 'async function removeRole('), /rowsAfter\([^;]*, roleEnabled\(\)\)/u,
    'a row edit is guarded without the switches it was queued with');
  assert.match(sourceBetween('rolesHost.ts', 'async function removeConfirmed(', '/** The override files'), /rowsAfter\([^;]*, roleEnabled\(\)\)/u,
    'a removal is guarded without the switches it was queued with');
});

// ---------------------------------------------------------------- E5.1b's code round, finding 0

/** A document role of the person's own — the documents bucket's second role, as `mine` is the others'. */
function mineDocument(id: string): RoleRow {
  return { ...mine(id, 'result'), programmingTask: false };
}

/**
 * A role of the person's OWN as the last one ON of its bucket — the only kind a page offers a stage move and a removal
 * (a shipped role's stage is fixed and it has no Remove). Every shipped role of the bucket is off: by the panel's switch,
 * or, in the plan stage, in the catalog.
 */
const OWN_LAST_ON: readonly LastOn[] = [
  {
    bucket: 'plan',
    last: 'MyPlan',
    alone: { rows: [mine('MyPlan', 'plan'), { id: 'PlanCritique', active: false }], roleEnabled: {} },
    another: { rows: [mine('MyPlan', 'plan')], roleEnabled: {} },
  },
  {
    bucket: 'code',
    last: 'MyCode',
    alone: { rows: [mine('MyCode', 'result')], roleEnabled: { Conventions: false, Architecture: false, SecurityReliability: false, UxDxPerformance: false } },
    another: { rows: [mine('MyCode', 'result')], roleEnabled: {} },
  },
  {
    bucket: 'documents',
    last: 'MyDoc',
    alone: { rows: [mineDocument('MyDoc')], roleEnabled: { DocumentReview: false, DocumentSummary: false } },
    another: { rows: [mineDocument('MyDoc')], roleEnabled: {} },
  },
  {
    bucket: 'feature',
    last: 'MyFeature',
    alone: { rows: [mine('MyFeature', 'feature')], roleEnabled: { FeatureReview: false } },
    another: { rows: [mine('MyFeature', 'feature')], roleEnabled: {} },
  },
];

/** What the new page lets a person do to a role besides its switch: the stages it offers, and whether Remove posts. */
function movesOf(switches: Switches, id: string): { readonly stages: readonly string[]; readonly removeDisabled: boolean; readonly removed: boolean } {
  const { page, pane } = rolesPage(stateWith(switches.rows, switches.roleEnabled));
  const block = roleBlock(pane, id);
  const stage = block.one((node) => node.dataset.field === 'stage', `${id}'s stage`);
  const remove = block.one((node) => node.dataset.remove === id, `${id}'s Remove`);

  bubbled(page, 'click', remove);

  return {
    stages: stage.find((node) => node.tagName === 'OPTION' && !node.disabled).map((node) => node.value),
    removeDisabled: remove.disabled,
    removed: page.posted.some((one) => one['type'] === 'roles' && isRemoval(one['edit'])),
  };
}

// ---------------------------------------------------------------- E5.1b's code round, finding 1

/** A role list whose index reads are counted — what "once per page, not once per block" is measured by (as R7's record). */
function counted(rows: readonly RoleRow[]): { readonly list: readonly RoleRow[]; readonly reads: () => number } {
  let reads = 0;
  const list = new Proxy([...rows], {
    get(target, key, receiver): unknown {
      if (typeof key === 'string' && /^\d+$/u.test(key)) {
        reads += 1;
      }

      return Reflect.get(target, key, receiver);
    },
  });

  return { list, reads: () => reads };
}

test('a page of roles counts its buckets once, not once per block — 1000 roles are not a million reads (finding 1)', () => {
  // Half ON and half OFF, so both counts a block asks — the last-ON refusal and the five-per-bucket room — are asked.
  const rows = composed(Array.from({ length: 1000 }, (_, index) => ({ ...mine(`Mine${index}`, 'result'), active: index % 2 === 0 })));
  const { list, reads } = counted(rows);
  const options = roleBlockOptions(list, 'data-role-prompt', { Architecture: false });

  for (const role of rows) {
    drawRoleBlock(role, {}, options);
  }

  assert.ok(reads() <= 10 * rows.length, `${rows.length} blocks read the role list ${reads()} times — every block rescanned it`);
});

/** Whether a posted roles edit is a removal — read from the message as it arrives, never asserted into a type. */
function isRemoval(edit: unknown): boolean {
  return typeof edit === 'object' && edit !== null && 'type' in edit && edit.type === 'remove';
}

for (const { bucket, last, alone, another } of OWN_LAST_ON) {
  test(`the ${bucket} bucket's last role ON is offered no other stage and no removal on the new page — refused there, not late by the host`, () => {
    const stage = composed(alone.rows).find((role) => role.id === last)?.stage ?? '';

    assert.deepEqual(movesOf(alone, last), { stages: [stage], removeDisabled: true, removed: false },
      `${last}, the last ${bucket} role ON, could be moved to another stage or removed on the page`);
    assert.deepEqual(movesOf(another, last), { stages: ['plan', 'result', 'feature'], removeDisabled: false, removed: true },
      `${last} could not be moved or removed with another ${bucket} role ON`);
  });
}
