import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { type PanelState, settingsHtml } from '../panelView';
import { panelState, runPanel, withoutSeq } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { presses, runPageHtml } from './pageScriptHarness';

/**
 * E5.1 step 1 of todo/PLAN_one_model_catalog.md: the new page stops drawing the old pages' buttons. The gate's
 * "Edit commands…" and the stages' "Edit roles…" opened the commands page and the roles page — pages E5.1 deletes,
 * whose content is a place of THIS page now (Reviews › Commands, Reviews › Roles & prompts). So each is a jump: pressed,
 * the place it names is shown, the host is told the place as any tab press tells it, and nothing asks for a page.
 *
 * <p>Run against the DOM shim, and every node pressed is taken from the page as drawn (`pageTree.ts`), so a button
 * still wired to the old command posts that command here and the place stays hidden — the red this test was written
 * against.</p>
 */

function stateWith(): PanelState {
  return {
    ...panelState('reviewers'),
    commands: { rows: [], texts: {}, serverVersion: '', perSide: false },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

/** What the page's scripts select at load, so they bind to what the page drew. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page, opened on `place`, drawn and running. */
function pageAt(place: string) {
  const html = catalogHtml(stateWith(), 'test-nonce', place);
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });
  const paneOf = (at: string): PageNode => tree.one((node) => node.dataset.pane === at, `pane ${at}`);

  return { page, paneOf };
}

/** A button in a pane, by what it says — the way a person finds it. */
function buttonSaying(pane: PageNode, label: string): PageNode {
  return pane.one((node) => node.tagName === 'BUTTON' && node.text().trim() === label, `"${label}" button`);
}

test('the gate\'s "Edit commands…" opens Reviews › Commands on this page — no page is asked for', () => {
  const { page, paneOf } = pageAt('reviews/gate');

  bubbled(page, 'click', buttonSaying(paneOf('reviews/gate'), 'Edit commands…'));

  assert.equal(paneOf('reviews/commands').hidden, false, 'Commands was not opened');
  assert.equal(paneOf('reviews/gate').hidden, true, 'the gate stayed open');
  assert.deepEqual(presses(page), [{ type: 'tab', id: 'reviews/commands' }]);
});

/**
 * The CURRENT page has no place to jump to — it holds neither editor — so while it is still drawn (the preview switch
 * goes in E5.1 step 5) its two buttons keep opening the two pages. Run on the current page's own script, through the
 * buttons it drew, found by what they say.
 */
for (const [tab, label, command] of [['gate', 'Edit commands…', 'editCommands'], ['prompts', 'Edit roles…', 'editRoles']] as const) {
  test(`the current page's "${label}" still opens its page, which the current page has no place for`, () => {
    const state = stateWith();
    const page = runPanel(state, { html: settingsHtml(state, 'test-nonce', tab) });
    const button = page.commands.find((one) => one.textContent === label);
    assert.ok(button !== undefined, `the current page draws no "${label}" that posts a command`);

    button.fire('click');

    assert.deepEqual(page.posted.filter((one) => one['type'] === 'command').map(withoutSeq), [{ type: 'command', command, id: undefined }]);
  });
}

test('the stages\' "Edit roles…" opens Reviews › Roles & prompts on this page — no page is asked for', () => {
  const { page, paneOf } = pageAt('reviews/stages');

  bubbled(page, 'click', buttonSaying(paneOf('reviews/stages'), 'Edit roles…'));

  assert.equal(paneOf('reviews/roles').hidden, false, 'Roles & prompts was not opened');
  assert.equal(paneOf('reviews/stages').hidden, true, 'the stages stayed open');
  assert.deepEqual(presses(page), [{ type: 'tab', id: 'reviews/roles' }]);
});
