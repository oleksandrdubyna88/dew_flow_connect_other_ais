import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import type { CommandRow } from '../commands';
import { SHIPPED_COMMANDS } from '../commands';
import { type PanelState } from '../panelView';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * E4.4 of todo/PLAN_one_model_catalog.md: Reviews › Commands on the new page — the Gate commands tab's own blocks, drawn
 * by the panel, their edits posted as `commands` messages into the one editing core (`commandsHost.ts`). The page also
 * draws the roles, so the two never read each other's controls.
 */

const mine: CommandRow = { id: 'cmd-ab12', title: 'Run the linter', stage: 'code', enabled: true };

function stateWith(rows: readonly CommandRow[] = [mine]): PanelState {
  return {
    ...panelState('reviewers'),
    commands: { rows, texts: {}, serverVersion: '', perSide: false },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

function commandsPane(state: PanelState): string {
  const body = catalogBody(state);
  const start = body.indexOf('id="cpane-reviews-commands"');
  const next = body.indexOf('role="tabpanel"', body.indexOf('>', start));

  return body.slice(start, next < 0 ? body.length : next);
}

test('Commands draws your commands and every shipped text, in attributes of its own', () => {
  const pane = commandsPane(stateWith());

  assert.match(pane, /data-cmd-id="cmd-ab12"/);
  for (const one of SHIPPED_COMMANDS) {
    assert.match(pane, new RegExp(`data-cmd-file="${one.id}"`), `${one.id} is not drawn`);
  }
  assert.doesNotMatch(pane, /data-(field|text|remove|restore|add|file)[=\s>]/, 'an attribute the roles\' wiring reads too');
});

test('no place of the new page says it is still on the current page', () => {
  assert.doesNotMatch(catalogBody(stateWith()), /is still on the current Settings page/);
});

/** A command row as the page holds it, with its title, switch and text. */
function commandNodes() {
  const row = new Node({ cmdId: 'cmd-ab12' }, 'SECTION');
  const title = new Node({ cmdField: 'title' }, 'INPUT').under(row);
  title.value = 'Run the linter, twice';
  const on = new Node({ cmdField: 'enabled' }, 'INPUT').under(row);
  on.type = 'checkbox';
  on.checked = false;
  const text = new Node({ cmdText: 'command-cmd-ab12' }, 'TEXTAREA').under(row);
  text.value = 'Run npm run lint.';

  return { row, title, on, text };
}

test('the commands\' controls post `commands` edits — a pick numbered, typing not — and report focus', () => {
  const { title, on, text } = commandNodes();
  const add = new Node({ cmdAdd: '' }, 'BUTTON');
  const restore = new Node({ cmdRestore: 'gate-proceed' }, 'BUTTON');
  const page = runPageHtml(catalogHtml(stateWith(), 'test-nonce', 'reviews/commands'), {}, undefined, { value: undefined });

  page.fire('input', title);
  page.fire('change', on);
  page.fire('input', text);
  page.fire('click', add);
  page.fire('click', restore);
  page.fire('focusin', text);

  const sent = page.posted.filter((one) => one['type'] === 'commands');
  assert.deepEqual(sent.map((one) => one['edit']), [
    { type: 'retitle', id: 'cmd-ab12', value: 'Run the linter, twice' },
    { type: 'switch', id: 'cmd-ab12', value: false },
    { type: 'text', fileId: 'command-cmd-ab12', value: 'Run npm run lint.' },
    { type: 'add' },
    { type: 'restore', fileId: 'gate-proceed' },
  ]);
  assert.deepEqual(sent.map((one) => one['seq'] !== undefined), [false, true, false, true, true]);
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: 'commands|command-cmd-ab12|text', editing: true }]);
});

test('the roles and the commands never read each other\'s controls', () => {
  const { title } = commandNodes();
  const role = new Node({ id: 'Architecture' }, 'DETAILS');
  const name = new Node({ field: 'name' }, 'INPUT').under(role);
  const page = runPageHtml(catalogHtml(stateWith(), 'test-nonce', 'reviews/commands'), {}, undefined, { value: undefined });

  page.fire('input', title);
  page.fire('input', name);

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'roles' || one['type'] === 'commands').map((one) => one['type']), ['commands', 'roles']);
});
