import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import { type BusySnapshot } from '../busySnapshot';
import type { CommandRow } from '../commands';
import { commandsHtml, type CommandsPageState } from '../commandsPage';
import { PageClock } from './panelPageHarness';
import { Node, type Page, runPageHtml } from './rolesPageHarness';

/**
 * The gate commands tab shows that a structural change is working (research/PLAN_busy_marks_on_every_webview.md, E3).
 *
 * <p>Adding, removing, switching or restaging a command — and restoring a shipped text — re-reads every command file
 * (`texts()`) and redraws the tab. Typing a text or a title is not marked: both settle on their own for 300 ms, and a bar
 * over the person's typing is the defect the operator ruled out.</p>
 */

const docs: CommandRow = { id: 'custom-1', title: 'Docs', enabled: false, stage: 'any' };

function running(busy?: BusySnapshot): { readonly page: Page; readonly clock: PageClock } {
  const clock = new PageClock();
  const state: CommandsPageState = {
    rows: [docs], texts: {}, serverVersion: '0.33.0', perSide: false, ...(busy === undefined ? {} : { busy }),
  };

  return { page: runPageHtml(commandsHtml(state, 'test-nonce'), {}, clock), clock };
}

/** A control inside the command's row, as the page's `closest('[data-id]')` finds its row. */
function inRow(dataset: Record<string, string>, tagName: string): Node {
  const row = new Node({ id: docs.id }, 'DIV');
  const control = new Node(dataset, tagName);
  control.parent = row;

  return control;
}

function last(page: Page, type: string): Record<string, unknown> {
  const sent = page.posted.filter((m) => m['type'] === type).at(-1);
  assert.ok(sent !== undefined, `the page posted no ${type}`);

  return sent;
}

test('switching a command on is numbered, and the bar shows from the half second until the host settles it', () => {
  const { page, clock } = running();
  assert.ok(page.bar !== null, 'the page draws a busy bar');
  assert.equal(page.bar.hidden, true, 'drawn hidden');
  const toggle = inRow({ field: 'enabled' }, 'INPUT');
  toggle.type = 'checkbox';
  toggle.checked = true;
  page.fire('change', toggle);

  const sent = last(page, 'switch');
  assert.equal(typeof sent['seq'], 'number');
  clock.advance(BUSY_AFTER_MS - 1);
  assert.equal(page.bar.hidden, true);
  clock.advance(1);
  assert.equal(page.bar.hidden, false);
  page.message({ type: 'settled', seq: sent['seq'], doc: sent['doc'], ok: true });
  assert.equal(page.bar.hidden, true);
});

test('adding and removing a command are numbered', () => {
  const { page } = running();
  page.fire('click', new Node({ add: '1' }, 'BUTTON'));
  page.fire('click', inRow({ remove: '1' }, 'BUTTON'));

  assert.equal(typeof last(page, 'add')['seq'], 'number');
  assert.equal(typeof last(page, 'remove')['seq'], 'number');
});

test('typing a text or a title is never numbered — typing is not the tab working', () => {
  const { page } = running();
  const box = new Node({ text: 'command-autonomy' }, 'TEXTAREA');
  box.value = 'Work without asking.';
  page.fire('input', box);
  const title = inRow({ field: 'title' }, 'INPUT');
  title.value = 'Docs, written';
  page.fire('input', title);

  assert.equal(last(page, 'text')['seq'], undefined);
  assert.equal(last(page, 'retitle')['seq'], undefined);
});

test('a tab repainted while a change runs shows the bar after what is LEFT of the delay', () => {
  const { page, clock } = running({ count: 1, oldestMs: 300 });

  clock.advance(BUSY_AFTER_MS - 300 - 1);
  assert.equal(page.bar?.hidden, true);
  clock.advance(1);
  assert.equal(page.bar?.hidden, false);
  assert.deepEqual(page.posted.at(-1), { type: 'ready' }, 'and the page asks what is running, last');
});
