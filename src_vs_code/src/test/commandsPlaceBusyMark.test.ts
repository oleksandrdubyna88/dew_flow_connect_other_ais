import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import { catalogHtml } from '../catalogPage';
import type { CommandRow } from '../commands';
import { PageClock, panelState } from './panelPageHarness';
import { type Page, runPageHtml } from './pageScriptHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';

/**
 * The gate's commands show that a structural change is working (research/PLAN_busy_marks_on_every_webview.md, E3) —
 * Reviews › Commands on the Settings page, which held these for the Gate commands tab until E5.1 step 4 of
 * research/PLAN_one_model_catalog.md deleted it.
 *
 * <p>Adding, removing, switching or restaging a command — and restoring a shipped text — re-reads every command file
 * (`texts()`) and redraws. Typing a text or a title is not marked: both settle on their own for 300 ms, and a bar over
 * the person's typing is the defect the operator ruled out. A page painted while work runs is the Settings document's
 * own (`busyMarkPage.test.ts`).</p>
 */

const docs: CommandRow = { id: 'custom-1', title: 'Docs', enabled: false, stage: 'any' };

const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

function running(): { readonly page: Page; readonly pane: PageNode; readonly clock: PageClock } {
  const clock = new PageClock();
  const state = { ...panelState('reviewers'), commands: { rows: [docs], texts: {}, serverVersion: '0.33.0', perSide: false } };
  const html = catalogHtml(state, 'test-nonce', 'reviews/commands');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), clock, { value: undefined });

  return { page, pane: tree.one((node) => node.dataset.pane === 'reviews/commands', 'Commands pane'), clock };
}

/** The last commands' edit of a type the page posted — the message, with its number if it carries one. */
function last(page: Page, type: string): Record<string, unknown> {
  const sent = page.posted.filter((m) => m['type'] === 'commands' && (m['edit'] as Record<string, unknown>)['type'] === type).at(-1);
  assert.ok(sent !== undefined, `the page posted no ${type}`);

  return sent;
}

/** A control of the one command's row, as the place drew it. */
function inRow(pane: PageNode, test: (node: PageNode) => boolean, what: string): PageNode {
  return pane.one((node) => node.dataset.cmdId === docs.id, 'the command\'s row').one(test, what);
}

test('switching a command on is numbered, and the bar shows from the half second until the host settles it', () => {
  const { page, pane, clock } = running();
  assert.ok(page.bar !== null, 'the page draws a busy bar');
  assert.equal(page.bar.hidden, true, 'drawn hidden');
  const toggle = inRow(pane, (node) => node.dataset.cmdField === 'enabled', 'its switch');
  toggle.checked = true;
  bubbled(page, 'change', toggle);

  const sent = last(page, 'switch');
  assert.equal(typeof sent['seq'], 'number');
  clock.advance(BUSY_AFTER_MS - 1);
  assert.equal(page.bar.hidden, true);
  clock.advance(1);
  assert.equal(page.bar.hidden, false);
  page.message({ type: 'settled', seq: sent['seq'], doc: sent['doc'], ok: true });
  assert.equal(page.bar.hidden, true);
});

test('adding, removing and restaging a command are numbered', () => {
  const { page, pane } = running();
  bubbled(page, 'click', pane.one((node) => node.dataset.cmdAdd !== undefined, 'Add a command'));
  bubbled(page, 'click', inRow(pane, (node) => node.dataset.cmdRemove !== undefined, 'its Remove'));
  const stage = inRow(pane, (node) => node.dataset.cmdField === 'stage', 'its stage');
  stage.value = 'code';
  bubbled(page, 'change', stage);

  assert.equal(typeof last(page, 'add')['seq'], 'number');
  assert.equal(typeof last(page, 'remove')['seq'], 'number');
  assert.equal(typeof last(page, 'restage')['seq'], 'number');
});

test('typing a text or a title is never numbered — typing is not the editor working', () => {
  const { page, pane } = running();
  const box = pane.one((node) => node.dataset.cmdText !== undefined && node.closest('[data-cmd-id]') === null, 'a shipped text');
  box.value = 'Work without asking.';
  bubbled(page, 'input', box);
  const title = inRow(pane, (node) => node.dataset.cmdField === 'title', 'its title');
  title.value = 'Docs, written';
  bubbled(page, 'input', title);

  assert.equal(last(page, 'text')['seq'], undefined);
  assert.equal(last(page, 'retitle')['seq'], undefined);
});
