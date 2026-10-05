import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { CATALOG_TABS } from '../catalogPlaces';
import { settingsHtml } from '../panelView';
import { click, panelState, runPanel, withoutSeq } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * The new Settings page, RUN (todo/PLAN_one_model_catalog.md, E3.1): six tabs and their sub-tabs, one place held by
 * the host, the one confirm dialog, and the way back to the current page. Every assertion watches the page's own
 * script over nodes read OUT OF its own markup.
 */

const state = () => panelState('reviewers');

/** A dialog as the page's script uses one: it opens modally and closes. */
class Dialog extends Node {
  open = false;
  textContent = '';

  showModal(): void {
    this.open = true;
  }

  close(): void {
    this.open = false;
  }
}

interface Running {
  readonly tabs: readonly Node[];
  readonly panes: readonly Node[];
  readonly page: ReturnType<typeof runPageHtml>;
  readonly dialog: Dialog;
  readonly go: Dialog;
  readonly keep: Dialog;
}

/** Every strip's tabs, each under a node for its strip — so the arrow keys move within ONE strip, as on the page. */
function tabsOf(html: string): Node[] {
  return [...html.matchAll(/<div class="tabs" role="tablist"[^>]* data-strip="([^"]+)">([\s\S]*?)<\/div>/g)].flatMap(([, strip, buttons]) => {
    const parent = new Node({ strip: strip! }, 'DIV');

    return [...buttons!.matchAll(/data-tab="([^"]+)"/g)].map((found) => new Node({ tab: found[1]! }, 'BUTTON').under(parent));
  });
}

function panesOf(html: string): Node[] {
  return [...html.matchAll(/<(?:section|div) [^>]*data-pane="([^"]+)"[^>]*>/g)].map((found) => {
    const pane = new Node({ pane: found[1]! }, 'SECTION');
    pane.hidden = / hidden>$/.test(found[0]);

    return pane;
  });
}

function run(place: string, asks: readonly Node[] = []): Running {
  const html = catalogHtml(state(), 'test-nonce', place);
  const tabs = tabsOf(html);
  const panes = panesOf(html);
  const dialog = new Dialog({}, 'DIALOG');
  const go = new Dialog({}, 'BUTTON');
  const keep = new Dialog({}, 'BUTTON');
  const page = runPageHtml(html, {
    '[data-tab]': tabs,
    '[data-pane]': panes,
    '#confirm-dialog': [dialog],
    '#confirm-title': [new Dialog({}, 'H2')],
    '#confirm-body': [new Dialog({}, 'P')],
    '#confirm-go': [go],
    '#confirm-keep': [keep],
    '[data-asks]': asks,
  });

  return { tabs, panes, page, dialog, go, keep };
}

function shown(running: Running): readonly string[] {
  return running.panes.filter((pane) => !pane.hidden).map((pane) => pane.dataset['pane'] ?? '');
}

function told(running: Running): readonly unknown[] {
  return running.page.posted.filter((m) => m['type'] === 'tab').map((m) => m['id']);
}

test('the body draws the six tabs and every sub-tab, place-neutral — no place in the paint key', () => {
  const body = catalogBody(state());

  for (const tab of CATALOG_TABS) {
    assert.match(body, new RegExp(`data-tab="${tab.id}"`), `no tab for ${tab.id}`);
    for (const sub of tab.subs) {
      assert.match(body, new RegExp(`data-tab="${tab.id}/${sub.id}"`), `no sub-tab for ${tab.id}/${sub.id}`);
    }
  }
  assert.doesNotMatch(body, /aria-selected="true"/, 'a tab drawn selected would put the place into the paint key');
  assert.equal(panesOf(body).every((pane) => pane.hidden), true, 'every pane is drawn hidden');
});

test('the page opens on the place the host holds: its tab, its sub-tab, and only their panes', () => {
  const running = run('reviews/gate');

  assert.deepEqual(shown(running), ['reviews', 'reviews/gate']);
  assert.deepEqual(told(running), [], 'opening is not a choice to report back');
});

test('a top tab opens its first sub-tab, and the host is told the whole place', () => {
  const running = run('models');

  running.page.fire('click', running.tabs.find((tab) => tab.dataset['tab'] === 'setup')!);

  assert.deepEqual(shown(running), ['setup', 'setup/keys']);
  assert.deepEqual(told(running), ['setup/keys']);
});

test('a top tab reopens the sub-tab last open under it', () => {
  const running = run('setup/keys');
  const tab = (key: string): Node => running.tabs.find((one) => one.dataset['tab'] === key)!;

  running.page.fire('click', tab('setup/mcp'));
  running.page.fire('click', tab('models'));
  running.page.fire('click', tab('setup'));

  assert.deepEqual(shown(running), ['setup', 'setup/mcp']);
  assert.deepEqual(told(running), ['setup/mcp', 'models', 'setup/mcp']);
});

test('the host telling the page a place selects it without being told back', () => {
  const running = run('models');

  running.page.message({ type: 'showTab', id: 'consultants/qconsult' });

  assert.deepEqual(shown(running), ['consultants', 'consultants/qconsult']);
  assert.deepEqual(told(running), []);
});

test('a place the page does not know opens the first tab, never an empty page', () => {
  assert.deepEqual(shown(run('nowhere/at-all')), ['models']);
});

test('the arrow keys move within ONE strip: a sub-tab moves to the next sub-tab, never to a top tab', () => {
  const running = run('reviews/stages');
  const stages = running.tabs.find((tab) => tab.dataset['tab'] === 'reviews/stages')!;

  running.page.fire('keydown', stages, { key: 'ArrowRight' });

  assert.deepEqual(shown(running), ['reviews', 'reviews/roles']);
});

test('a button that asks opens the one dialog and sends nothing until the action button is pressed', () => {
  const asks = new Node({ asks: 'removeVendor', id: 'codex-2', askTitle: 'Remove codex-2?', askBody: 'b', askAction: 'Remove', askDanger: 'true' }, 'BUTTON');
  const running = run('models', [asks]);

  running.page.fire('click', asks);
  assert.equal(running.dialog.open, true, 'the dialog did not open');
  assert.equal(running.go.textContent, 'Remove');
  assert.equal(running.go.className, 'primary danger');
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'command'), [], 'nothing is sent on the first click');

  running.go.click();

  assert.equal(running.dialog.open, false);
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'command').map(withoutSeq), [{ type: 'command', command: 'removeVendor', id: 'codex-2' }]);
});

test('Keep it closes the dialog and sends nothing', () => {
  const asks = new Node({ asks: 'removeVendor', id: 'codex-2', askTitle: 't', askBody: 'b', askAction: 'Remove', askDanger: 'true' }, 'BUTTON');
  const running = run('models', [asks]);

  running.page.fire('click', asks);
  running.keep.click();
  running.go.click();

  assert.equal(running.dialog.open, false);
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'command'), [], 'a kept action is not sent by a later press');
});

test('the new page offers the way back, and the current page offers the new one', () => {
  const back = runPanel(state(), { html: catalogHtml(state(), 'test-nonce', 'models') });
  click(back, 'settingsPreview', 'off');
  assert.deepEqual(back.posted.filter((m) => m['type'] === 'command').map(withoutSeq).at(-1), { type: 'command', command: 'settingsPreview', id: 'off' });

  const current = runPanel(state(), { html: settingsHtml(state(), 'test-nonce', 'reviewers') });
  click(current, 'settingsPreview', 'on');
  assert.deepEqual(current.posted.filter((m) => m['type'] === 'command').map(withoutSeq).at(-1), { type: 'command', command: 'settingsPreview', id: 'on' });
});
