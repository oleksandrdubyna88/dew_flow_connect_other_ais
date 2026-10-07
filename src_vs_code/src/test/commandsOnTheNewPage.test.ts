import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { SHIPPED_COMMANDS, type CommandRow } from '../commands';
import { type PanelState } from '../panelView';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * E4.4 of todo/PLAN_one_model_catalog.md: Reviews › Commands on the new page — the Gate commands tab's own blocks, drawn
 * by the panel, their edits posted as `commands` messages into the one editing core (`commandsHost.ts`). The page also
 * draws the roles, so the two never read each other's controls. Every control fired at is taken from the page as drawn.
 */

/** A shipped text the person has rewritten — the one that offers Restore. */
const REWRITTEN = SHIPPED_COMMANDS[0]!.id;
const mine: CommandRow = { id: 'cmd-ab12', title: 'Run the linter', stage: 'code', enabled: true };

function stateWith(rows: readonly CommandRow[] = [mine]): PanelState {
  return {
    ...panelState('reviewers'),
    commands: { rows, texts: { [REWRITTEN]: 'Go on.' }, serverVersion: '', perSide: false },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page on Commands, drawn and running — with the whole drawn tree, the roles' pane included. */
function commandsPage(state: PanelState = stateWith()) {
  const html = catalogHtml(state, 'test-nonce', 'reviews/commands');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  return { page, tree, pane: tree.one((node) => node.dataset.pane === 'reviews/commands', 'Commands pane') };
}

function yourCommand(pane: PageNode): PageNode {
  return pane.one((node) => node.dataset.cmdId === 'cmd-ab12', 'your command');
}

test('Commands draws your commands and every shipped text', () => {
  const { pane } = commandsPage();

  assert.deepEqual(pane.find((node) => node.dataset.cmdId !== undefined).map((node) => node.dataset.cmdId), ['cmd-ab12']);
  assert.deepEqual(pane.find((node) => node.dataset.cmdFile !== undefined).map((node) => node.dataset.cmdFile), SHIPPED_COMMANDS.map((one) => one.id));
});

test('no place of the new page says it is still on the current page', () => {
  assert.doesNotMatch(catalogBody(stateWith()), /is still on the current Settings page/);
});

test('the commands\' controls post `commands` edits — a pick numbered, typing not — and report focus', () => {
  const { page, pane } = commandsPage();
  const row = yourCommand(pane);
  const title = row.one((node) => node.dataset.cmdField === 'title', 'its title');
  title.value = 'Run the linter, twice';
  const on = row.one((node) => node.dataset.cmdField === 'enabled', 'its switch');
  on.checked = false;
  const text = row.one((node) => node.dataset.cmdText !== undefined, 'its text');
  text.value = 'Run npm run lint.';
  const add = pane.one((node) => node.dataset.cmdAdd !== undefined, 'Add a command');
  const restore = pane.one((node) => node.dataset.cmdRestore === REWRITTEN, 'Restore on a rewritten text');

  bubbled(page, 'input', title);
  bubbled(page, 'change', on);
  bubbled(page, 'input', text);
  bubbled(page, 'click', add);
  bubbled(page, 'click', restore);
  bubbled(page, 'focusin', text);

  const sent = page.posted.filter((one) => one['type'] === 'commands');
  assert.deepEqual(sent.map((one) => one['edit']), [
    { type: 'retitle', id: 'cmd-ab12', value: 'Run the linter, twice' },
    { type: 'switch', id: 'cmd-ab12', value: false },
    { type: 'text', fileId: text.dataset.cmdText, value: 'Run npm run lint.' },
    { type: 'add' },
    { type: 'restore', fileId: REWRITTEN },
  ]);
  assert.deepEqual(sent.map((one) => one['seq'] !== undefined), [false, true, false, true, true]);
  const focus = page.posted.filter((one) => one['type'] === 'focus').map(({ id, editing }) => ({ id, editing }));
  assert.deepEqual(focus, [{ id: `commands|${text.dataset.cmdText}|text`, editing: true }]);
});

test('the roles and the commands never read each other\'s controls — each press posts its own edit alone', () => {
  const { page, tree, pane } = commandsPage();
  const command = yourCommand(pane);
  const roles = tree.one((node) => node.dataset.pane === 'reviews/roles', 'Roles & prompts pane');
  const presses: readonly [PageNode, string][] = [
    [command.one((node) => node.dataset.cmdField === 'title', 'a command title'), 'input'],
    [command.one((node) => node.dataset.cmdRemove !== undefined, 'a command\'s Remove'), 'click'],
    [roles.one((node) => node.dataset.field === 'name', 'a role name'), 'input'],
    [roles.one((node) => node.dataset.restore !== undefined || node.dataset.addPrompt !== undefined, 'a role button'), 'click'],
  ];

  for (const [node, kind] of presses) {
    bubbled(page, kind, node);
  }

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'roles' || one['type'] === 'commands').map((one) => one['type']),
    ['commands', 'commands', 'roles', 'roles']);
});
