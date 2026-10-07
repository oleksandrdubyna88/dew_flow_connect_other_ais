import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import { type BusySnapshot } from '../busySnapshot';
import { RESULT_STAGE, type RoleRow } from '../roles';
import { type RolesPageState } from '../rolesPage';
import { CUSTOM_ROLES_SINCE } from '../rolesBlocks';
import { PageClock } from './panelPageHarness';
import { runRolesPage } from './rolesPageHarness';
import { Node, type Page } from './pageScriptHarness';

/**
 * The roles tab shows that a structural change is working (research/PLAN_busy_marks_on_every_webview.md, E3).
 *
 * <p>Adding or removing a role or a prompt, restoring one, or switching a role on re-reads every prompt file and redraws
 * the tab — with nothing on screen meanwhile. Typing is not marked: a field settles for 300 ms by design, and a bar over
 * the person's own typing is the 0.61.0 defect the operator ruled out ("пока печатаю не считаем").</p>
 */

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [{ id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' }],
};

function running(busy?: BusySnapshot): { readonly page: Page; readonly clock: PageClock } {
  const clock = new PageClock();
  const state: RolesPageState = {
    rows: [mine], texts: {}, serverVersion: CUSTOM_ROLES_SINCE, perSide: false, uiScale: 0,
    ...(busy === undefined ? {} : { busy }),
  };

  return { page: runRolesPage(state, {}, clock), clock };
}

/** A field inside a role's block, as the page's `closest('[data-id]')` finds its role. */
function fieldOf(field: string, kind: 'text' | 'checkbox'): Node {
  const role = new Node({ id: mine.id }, 'DIV');
  const input = new Node({ field }, 'INPUT');
  input.type = kind;
  input.parent = role;

  return input;
}

function last(page: Page, type: string): Record<string, unknown> {
  const sent = page.posted.filter((m) => m['type'] === type).at(-1);
  assert.ok(sent !== undefined, `the page posted no ${type}`);

  return sent;
}

test('adding a role is numbered, and the bar shows from the half second until the host settles it', () => {
  const { page, clock } = running();
  assert.ok(page.bar !== null, 'the page draws a busy bar');
  assert.equal(page.bar.hidden, true, 'drawn hidden');
  page.fire('click', new Node({ add: 'role' }, 'BUTTON'));

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
  const { page } = running();
  const toggle = fieldOf('active', 'checkbox');
  toggle.checked = true;
  page.fire('change', toggle);

  assert.equal(typeof last(page, 'edit')['seq'], 'number');
});

test('typing in a field is never numbered — typing is not the tab working', () => {
  const { page } = running();
  const typed = fieldOf('name', 'text');
  typed.value = 'Requirements we really wrote';
  page.fire('input', typed);

  assert.equal(last(page, 'edit')['seq'], undefined);
});

test('a tab repainted while a change runs shows the bar after what is LEFT of the delay', () => {
  const { page, clock } = running({ count: 1, oldestMs: 300 });

  clock.advance(BUSY_AFTER_MS - 300 - 1);
  assert.equal(page.bar?.hidden, true);
  clock.advance(1);
  assert.equal(page.bar?.hidden, false, 'work already running is not given a fresh delay');
  assert.deepEqual(page.posted.at(-1), { type: 'ready' }, 'and the page asks what is running, last');
});
