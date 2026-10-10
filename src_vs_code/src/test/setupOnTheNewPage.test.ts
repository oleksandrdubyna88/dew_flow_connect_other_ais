import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { DataLocation } from '../dataDir';
import { watchedDirs } from '../escalationDirs';
import type { PanelState } from '../panelView';
import type { TeamServerState } from '../teamServerView';
import { type Page, click, panelState, pressCommand, runPanel, work } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * Setup › Team servers and Setup › MCP server on the NEW Settings page, RUN — every button of both places pressed
 * through the page's own script and the message it posts asserted (research/PLAN_one_model_catalog.md E5.1b).
 *
 * <p><b>Why these two, and why now.</b> Before E5.1 step 5 deletes the current page, every place of the new page has to
 * have a test that runs it and works a control, or a control wired on the current page alone would go with it unseen.
 * Every other place had one; these two had none, because their controls are buttons and the tests that sweep the
 * settings controls see only `data-setting`. A button is asserted by what pressing it POSTS — a template literal
 * contains every button it was meant to contain, wired or not (`.agents/PROJECT.md`).</p>
 *
 * <p>Each button is also found inside its own place's pane, so a press proves the button is where a person looks for
 * it, not merely somewhere on a page that draws every place at once.</p>
 */

/** A Team server row: signed in, or not. */
function teamServer(id: string, email: string): TeamServerState {
  return { server: { id, name: `Server ${id}`, url: `https://${id}.example.com` }, email, problem: '', stale: false };
}

/** A data folder this window reads, with a side — what draws the folder's change and move buttons. */
const WHERE: DataLocation = {
  directory: '/srv/coai/windows',
  side: 'windows',
  ignoredSide: '',
  alsoWatched: [],
  refusal: '',
  notes: [],
  env: { COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' },
  source: 'this side',
};

/**
 * Everything both places can draw a button for: a signed-out and a signed-in Team server, an installed MCP server with
 * an update offered, the data folder, and a VERIFIED move of it — the one state the delete is offered in.
 */
function everyButton(over: Partial<PanelState> = {}): PanelState {
  return panelState('reviewers', {
    teamServers: [teamServer('ts-out', ''), teamServer('ts-in', 'me@example.com')],
    server: { kind: 'known', version: '0.43.0', remembered: false, updateOffered: true },
    latestServerVersion: '0.44.2',
    storage: WHERE,
    lastDataMove: { from: 'C:\\old\\coai', to: 'Z:\\coai', verified: true },
    ...over,
  });
}

/** One button of a place: how a person presses it, and the command and id the press must post. */
interface Press {
  readonly place: 'setup/team' | 'setup/mcp';
  readonly button: string;
  readonly command: string;
  /** The button's `data-id`, or `undefined` for a button that has none — pressed by its command alone. */
  readonly id: string | undefined;
}

const PRESSES: readonly Press[] = [
  { place: 'setup/team', button: 'Add a Team server', command: 'addTeamServer', id: undefined },
  { place: 'setup/team', button: 'Sign in', command: 'signInTeamServer', id: 'ts-out' },
  { place: 'setup/team', button: 'Sign out', command: 'signOutTeamServer', id: 'ts-in' },
  { place: 'setup/team', button: 'Remove (signed out)', command: 'removeTeamServer', id: 'ts-out' },
  { place: 'setup/team', button: 'Remove (signed in)', command: 'removeTeamServer', id: 'ts-in' },
  { place: 'setup/mcp', button: 'Update coai-mcp', command: 'installServer', id: undefined },
  { place: 'setup/mcp', button: 'Check again', command: 'checkForUpdate', id: undefined },
  { place: 'setup/mcp', button: 'Change where your data lives…', command: 'changeDataDirectory', id: undefined },
  { place: 'setup/mcp', button: 'Move what is here to another folder…', command: 'moveDataDirectory', id: undefined },
  { place: 'setup/mcp', button: 'Delete the old folder…', command: 'deleteOldDataFolder', id: '' },
];

/** The new page on a place, drawn and running. */
function opened(place: string, state: PanelState = everyButton()): { readonly page: Page; readonly html: string } {
  const html = catalogHtml(state, 'test-nonce', place);

  return { page: runPanel(state, { html }), html };
}

/** Whether the place's own pane draws this command's button with this id (or none). */
function inPane(html: string, { place, command, id }: Press): boolean {
  const pane = pageTree(html).one((node) => node.dataset.pane === place, `the ${place} pane`);

  return pane.find((node) => node.dataset.command === command && node.dataset.id === id).length === 1;
}

/** A press as a person makes it: by command and id, or by command alone for a button with no id. */
function press(page: Page, { command, id }: Press): void {
  if (id === undefined) {
    pressCommand(page, command);

    return;
  }
  click(page, command, id);
}

for (const one of PRESSES) {
  test(`${one.place}: pressing ${one.button} posts ${one.command}${one.id === undefined ? '' : ` for ${one.id === '' ? 'no id' : one.id}`}`, () => {
    const { page, html } = opened(one.place);
    assert.ok(inPane(html, one), `${one.place} draws no ${one.button} button of its own`);

    press(page, one);

    assert.deepEqual(work(page).map((sent) => [sent['type'], sent['command'], sent['id']]), [['command', one.command, one.id]],
      `pressing ${one.button} did not post ${one.command} — and only it`);
  });
}

test('setup/mcp offers no Install while the installed server is the newest published', () => {
  const { html } = opened('setup/mcp', everyButton({ server: { kind: 'known', version: '0.44.2', remembered: false, updateOffered: false } }));

  assert.equal(inPane(html, PRESSES.find((one) => one.command === 'installServer')!), false, 'an Install drawn over a server that is up to date');
});

test('setup/mcp offers no delete of the old folder after a move that did not verify — it may hold what the copy lost', () => {
  const { html } = opened('setup/mcp', everyButton({ lastDataMove: { from: 'C:\\old\\coai', to: 'Z:\\coai', verified: false } }));

  assert.equal(inPane(html, PRESSES.find((one) => one.command === 'deleteOldDataFolder')!), false, 'a delete offered over an unverified copy');
});

test('setup/mcp says a watched folder of the other OS is the other side\'s, quietly — never the refusal tone', () => {
  // todo/PLAN_paths_per_side.md E1.3: the list is shared with the WSL window, where `/home/...` is right.
  const alsoWatched = watchedDirs('C:\\Own', ['/home/user/.local/share/coai-mcp', 'C:\\Elsewhere'], 'win32');
  const { html } = opened('setup/mcp', everyButton({ storage: { ...WHERE, alsoWatched } }));
  const pane = pageTree(html).one((node) => node.dataset.pane === 'setup/mcp', 'the setup/mcp pane');
  const line = pane.one((node) => node.tagName === 'DIV' && node.text().startsWith('/home/user/.local/share/coai-mcp'), 'line for the WSL folder');

  assert.deepEqual(line.className.split(' ').sort(), ['hint', 'watched-other-side'], 'drawn in another tone than the quiet hint');
  assert.match(line.text(), /the other side's data folder: a window on that side answers its questions, and this Windows side skips it/);
  assert.equal(pane.find((node) => node.className.split(' ').includes('stale')).length, 0, 'something in this pane is drawn as a refusal');
  assert.ok(pane.find((node) => node.className === 'status' && node.text() === 'C:\\Elsewhere').length === 1, 'this side\'s folder is not drawn plainly beside it');
});
