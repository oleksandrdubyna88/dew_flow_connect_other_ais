import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { SHIPPED_COMMANDS, fileIdOf, type CommandRow } from '../commands';
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
    // An ENABLED one: a Restore with nothing to restore is drawn disabled, and the roles' script posts nothing for a
    // disabled button, as a browser sends it no click (E5.1b's code round, finding 0).
    [roles.one((node) => !node.disabled && (node.dataset.restore !== undefined || node.dataset.addPrompt !== undefined), 'a role button'), 'click'],
  ];

  for (const [node, kind] of presses) {
    bubbled(page, kind, node);
  }

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'roles' || one['type'] === 'commands').map((one) => one['type']),
    ['commands', 'commands', 'roles', 'roles']);
});

// ---------- what the Gate commands tab held, asked of this place since E5.1 step 4 deleted the tab ----------

/** The place's state with these commands' texts and this server — the rows are {@link mine}. */
function withTexts(texts: Readonly<Record<string, string>>, serverVersion = ''): PanelState {
  const base = stateWith();

  return { ...base, commands: { rows: [mine], texts, serverVersion, perSide: false } };
}

/** The Commands pane's markup, as drawn. */
const paneHtml = (state: PanelState): string => {
  const html = catalogHtml(state, 'test-nonce', 'reviews/commands');
  const open = html.indexOf('data-pane="reviews/commands"');
  assert.notEqual(open, -1, 'the page draws no Commands pane');

  return html.slice(open, html.indexOf('data-pane=', open + 1) === -1 ? html.length : html.indexOf('data-pane=', open + 1));
};

test('Remove posts the removal of THAT command, and a tick or a stage posts once, on change — never again on input', () => {
  const { page, pane } = commandsPage();
  const row = yourCommand(pane);
  const on = row.one((node) => node.dataset.cmdField === 'enabled', 'its switch');
  on.checked = false;
  const stage = row.one((node) => node.tagName === 'SELECT' && node.dataset.cmdField === 'stage', 'its stage');
  stage.value = 'plan';

  bubbled(page, 'input', on);
  bubbled(page, 'change', on);
  bubbled(page, 'input', stage);
  bubbled(page, 'change', stage);
  bubbled(page, 'click', row.one((node) => node.dataset.cmdRemove !== undefined, 'its Remove'));

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'commands').map((one) => one['edit']), [
    { type: 'switch', id: 'cmd-ab12', value: false },
    { type: 'restage', id: 'cmd-ab12', value: 'plan' },
    { type: 'remove', id: 'cmd-ab12' },
  ]);
});

test('each shipped text shows its marker, its shipped words faintly, and Restore only when overridden', () => {
  const plain = paneHtml(withTexts({}));
  const rewritten = paneHtml(withTexts({ 'command-autonomy': 'Mine.', 'command-preamble': '  \n' }));

  assert.match(plain, /<b>Work AUTONOMOUSLY\. <\/b>/u);
  assert.match(plain, /placeholder="Say that you are working autonomously/u);
  assert.match(plain, /The server fills in <code>\{scope\}<\/code>/u);
  assert.doesNotMatch(plain, /data-cmd-restore=/u, 'a Restore is offered with nothing to restore');
  assert.match(rewritten, /data-cmd-restore="command-autonomy"/u);
  assert.doesNotMatch(rewritten, /data-cmd-restore="command-preamble"/u, 'a blank file is no override, as the server reads it');
});

test('a title or a text holding markup is drawn as text', () => {
  // A text can hold `</textarea><script>` — it is the person's own words, pasted from anywhere. (codex, the plan round.)
  const state = withTexts({ [fileIdOf('cmd-ab12')]: '</textarea><script>alert(2)</script>', 'command-autonomy': '</textarea><img onerror=x>' });
  const html = paneHtml({ ...state, commands: { ...state.commands!, rows: [{ ...mine, title: '"><script>alert(1)</script>' }] } });

  assert.doesNotMatch(html, /<script>alert/u);
  assert.doesNotMatch(html, /<img/u);
  assert.match(html, /&lt;\/textarea&gt;&lt;script&gt;alert\(2\)/u);
});

test('an older server is told, on the place, that it ignores the commands', () => {
  assert.match(paneHtml(withTexts({}, '0.32.0')), /class="stale"/u);
  assert.doesNotMatch(paneHtml(withTexts({}, '0.33.0')), /class="stale"/u);
});
