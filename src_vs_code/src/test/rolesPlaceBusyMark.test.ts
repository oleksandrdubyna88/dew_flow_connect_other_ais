import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import { type BusySnapshot } from '../busySnapshot';
import { RESULT_STAGE, type RoleRow } from '../roles';
import { PageClock } from './panelPageHarness';
import { type Page } from './pageScriptHarness';
import { bubbled, type PageNode } from './pageTree';
import { roleBlockIn, runRolesPlace } from './rolesPlaceHarness';

/**
 * The roles' editor shows that a structural change is working (research/PLAN_busy_marks_on_every_webview.md, E3) —
 * Reviews › Roles & prompts on the Settings page, which held these for the Review roles tab until E5.1 step 4 of
 * todo/PLAN_one_model_catalog.md deleted it.
 *
 * <p>Adding or removing a role or a prompt, restoring one, or switching a role on re-reads every prompt file and redraws
 * — with nothing on screen meanwhile. Typing is not marked: a field settles for 300 ms by design, and a bar over the
 * person's own typing is the 0.61.0 defect the operator ruled out ("пока печатаю не считаем").</p>
 */

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
};

function running(busy?: BusySnapshot): { readonly page: Page; readonly pane: PageNode; readonly clock: PageClock } {
  const clock = new PageClock();
  const { page, pane } = runRolesPlace({ rows: [mine], ...(busy === undefined ? {} : { busy }) }, clock);

  return { page, pane, clock };
}

/** The last roles' edit of a type the page posted — the message, with its number if it carries one. */
function last(page: Page, type: string): Record<string, unknown> {
  const sent = page.posted.filter((m) => m['type'] === 'roles' && (m['edit'] as Record<string, unknown>)['type'] === type).at(-1);
  assert.ok(sent !== undefined, `the page posted no ${type}`);

  return sent;
}

function field(pane: PageNode, name: string): PageNode {
  return roleBlockIn(pane, mine.id).one((node) => node.dataset.field === name && node.closest('[data-role-prompt]') === null, `the ${name} field`);
}

test('adding a role is numbered, and the bar shows from the half second until the host settles it', () => {
  const { page, pane, clock } = running();
  assert.ok(page.bar !== null, 'the page draws a busy bar');
  assert.equal(page.bar.hidden, true, 'drawn hidden');
  bubbled(page, 'click', pane.one((node) => node.tagName === 'BUTTON' && node.dataset.add === 'role', 'Add a role'));

  const sent = last(page, 'add');
  assert.equal(typeof sent['seq'], 'number');
  clock.advance(BUSY_AFTER_MS - 1);
  assert.equal(page.bar.hidden, true);
  clock.advance(1);
  assert.equal(page.bar.hidden, false);
  page.message({ type: 'settled', seq: sent['seq'], doc: sent['doc'], ok: true });
  assert.equal(page.bar.hidden, true);
});

test('switching a role on is numbered: it reads every prompt before it redraws', () => {
  const { page, pane } = running();
  const toggle = field(pane, 'active');
  toggle.checked = false;
  bubbled(page, 'change', toggle);

  assert.equal(typeof last(page, 'edit')['seq'], 'number');
});

test('typing in a field is never numbered — typing is not the editor working', () => {
  const { page, pane } = running();
  const typed = field(pane, 'name');
  typed.value = 'Requirements we really wrote';
  bubbled(page, 'input', typed);

  assert.equal(last(page, 'edit')['seq'], undefined);
});

test('the page repainted while a change runs shows the bar after what is LEFT of the delay', () => {
  const { page, clock } = running({ count: 1, oldestMs: 300 });

  clock.advance(BUSY_AFTER_MS - 300 - 1);
  assert.equal(page.bar?.hidden, true);
  clock.advance(1);
  assert.equal(page.bar?.hidden, false, 'work already running is not given a fresh delay');
  assert.deepEqual(page.posted.at(-1), { type: 'ready' }, 'and the page asks what is running, last');
});
