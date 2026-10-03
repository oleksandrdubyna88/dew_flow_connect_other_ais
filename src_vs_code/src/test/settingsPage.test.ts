import assert from 'node:assert/strict';
import { test } from 'node:test';
import { type PanelState, settingsHtml, settingsSections } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { nextSettingsTab } from '../settingsPage';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * The Settings tab, RUN: its strip, its panes, its keys (`research/PLAN_settings_page.md`, S3).
 *
 * <p>The page is the panel's own document with the Settings page's script appended, so what is run
 * here is the real script — the shared half binds nothing in this fixture, and the tab half is what
 * every assertion watches. Tabs and panes are built from the REGISTRY, never typed: a hand list here
 * would stop covering the eleventh tab the day it is added.</p>
 */

const state = (): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [],
  agyModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: [],
  usage: [],
  usageWindow: 'week',
  latestServerVersion: '',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
});

const IDS = settingsSections().map((section) => section.id);

interface Running {
  readonly tabs: readonly Node[];
  readonly panes: readonly Node[];
  readonly page: ReturnType<typeof runPageHtml>;
}

/** The page's script over one tab node and one pane node per registry section, in one strip. */
/**
 * The strip's tabs and the panes, read OUT OF the page's own markup — so a tab or a pane that lost the
 * attribute the script selects by is a page these tests cannot run, rather than one they run anyway on
 * nodes typed by hand. Each pane keeps the `hidden` it was drawn with.
 */
function nodesOf(html: string): { tabs: Node[]; panes: Node[] } {
  const strip = new Node({ strip: 'settings' }, 'DIV');
  const tabs = [...html.matchAll(/<button type="button" role="tab"[^>]* data-tab="([^"]+)"/g)]
    .map((found) => new Node({ tab: found[1]! }, 'BUTTON').under(strip));
  const panes = [...html.matchAll(/<section [^>]*data-pane="([^"]+)"[^>]*>/g)].map((found) => {
    const pane = new Node({ pane: found[1]!, section: found[1]! }, 'SECTION');
    pane.hidden = / hidden>$/.test(found[0]);
    return pane;
  });
  assert.equal(tabs.length, IDS.length, 'the markup does not draw a tab per section');
  assert.equal(panes.length, IDS.length, 'the markup does not draw a pane per section');

  return { tabs, panes };
}

function run(heldTab: string): Running {
  const html = settingsHtml(state(), 'test-nonce', heldTab);
  const { tabs, panes } = nodesOf(html);
  const page = runPageHtml(html, {
    '[data-tab]': tabs,
    '[data-pane]': panes,
  });

  return { tabs, panes, page };
}

function shown(running: Running): readonly string[] {
  return running.panes.filter((pane) => !pane.hidden).map((pane) => pane.dataset['pane'] ?? '');
}

function selected(running: Running): readonly string[] {
  return running.tabs.filter((tab) => tab.attributes['aria-selected'] === 'true').map((tab) => tab.dataset['tab'] ?? '');
}

test('the page opens on the tab the host holds, and shows only its pane', () => {
  const running = run('limits');

  assert.deepEqual(shown(running), ['limits']);
  assert.deepEqual(selected(running), ['limits']);
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'tab'), [], 'opening is not a choice to report back');
});

test('an id the page has no tab for opens the first tab, never an empty page', () => {
  const running = run('noSuchTab');

  assert.deepEqual(shown(running), [IDS[0]]);
});

test('pressing a tab shows exactly its pane, marks exactly it, and tells the host', () => {
  const running = run(IDS[0]!);
  const gate = running.tabs[IDS.indexOf('gate')]!;

  running.page.fire('click', gate);

  assert.deepEqual(shown(running), ['gate'], 'a press showed some other set of panes');
  assert.deepEqual(selected(running), ['gate']);
  assert.equal(gate.className, 'tab on');
  for (const tab of running.tabs) {
    assert.equal(tab.attributes['tabindex'], tab === gate ? '0' : '-1', 'exactly one tab is in the Tab order');
  }
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'tab'), [{ type: 'tab', id: 'gate' }]);
});

test('the host telling the page a tab selects it without being told back', () => {
  const running = run(IDS[0]!);

  running.page.message({ type: 'showTab', id: 'server' });

  assert.deepEqual(shown(running), ['server']);
  assert.deepEqual(running.page.posted.filter((m) => m['type'] === 'tab'), [], 'an echo would make the host and the page argue');
});

test('the arrow keys move along the strip and wrap; Home and End go to its ends', () => {
  const running = run(IDS[IDS.length - 1]!);
  const last = running.tabs[running.tabs.length - 1]!;

  const right = running.page.fire('keydown', last, { key: 'ArrowRight' });
  assert.equal(right.defaultPrevented, true);
  assert.deepEqual(shown(running), [IDS[0]], 'ArrowRight on the last tab did not wrap to the first');
  assert.equal(running.tabs[0]!.focused, true, 'focus did not follow the selection');

  running.page.fire('keydown', running.tabs[0]!, { key: 'ArrowLeft' });
  assert.deepEqual(shown(running), [IDS[IDS.length - 1]], 'ArrowLeft on the first tab did not wrap to the last');

  running.page.fire('keydown', running.tabs[3]!, { key: 'Home' });
  assert.deepEqual(shown(running), [IDS[0]]);
  running.page.fire('keydown', running.tabs[3]!, { key: 'End' });
  assert.deepEqual(shown(running), [IDS[IDS.length - 1]]);
});

test('a key the strip does not own is left alone', () => {
  const running = run('limits');
  const limits = running.tabs[IDS.indexOf('limits')]!;

  const typed = running.page.fire('keydown', limits, { key: 'a' });
  const tabbed = running.page.fire('keydown', limits, { key: 'Tab' });

  assert.equal(typed.defaultPrevented, false);
  assert.equal(tabbed.defaultPrevented, false, 'Tab must still leave the strip');
  assert.deepEqual(shown(running), ['limits']);
});

test('the strip and the panes are wired for a screen reader, both ways', () => {
  const html = settingsHtml(state(), 'n', '');
  assert.match(html, /<div class="tabs" role="tablist" aria-label="Settings" data-strip="settings">/);
  for (const id of IDS) {
    const button = new RegExp(`<button type="button" role="tab" id="tab-${id}" aria-controls="pane-${id}"`);
    const pane = new RegExp(`<section id="pane-${id}" class="pane sec-${id}" role="tabpanel" aria-labelledby="tab-${id}" tabindex="0" data-section="${id}" data-pane="${id}" hidden>`);
    assert.match(html, button, `the ${id} tab does not name the pane it controls`);
    assert.match(html, pane, `the ${id} pane is not labelled by its tab`);
  }
});

test('the markup chooses no tab — the held tab is the script\'s, so a press can never move the paint key', () => {
  const html = settingsHtml(state(), 'n', 'gate');
  const body = html.slice(html.indexOf('<body>'), html.indexOf('<script'));

  assert.doesNotMatch(body, /aria-selected="true"|class="tab on"/, 'a tab drawn as chosen puts the held tab into the key');
});

test('a requested tab is held only when the page has it', () => {
  assert.equal(nextSettingsTab('gate', 'limits', IDS), 'limits');
  assert.equal(nextSettingsTab('gate', 'noSuchTab', IDS), 'gate', 'a stale id moved the page');
  assert.equal(nextSettingsTab('gate', undefined, IDS), 'gate', 'the title bar passes nothing');
  assert.equal(nextSettingsTab('gate', { fsPath: '/x' }, IDS), 'gate', 'a URI is not a tab');
});

test('a repaint under a control someone is typing in puts the caret back, in the tab it is in', () => {
  // The panel's shared script restores the caret; on this page every pane is drawn hidden until the
  // page's own script opens the held tab — so the order of the two is the whole defect. Restored first,
  // the control is in a pane that is not rendered, a browser refuses the focus, and the next keystrokes
  // go nowhere. The shim refuses it the same way (rolesPageHarness `Node.focus`).
  const html = settingsHtml({ ...state(), focus: { id: 'model|codex||', start: 1, end: 1 } }, 'n', 'reviewers');
  const { tabs, panes } = nodesOf(html);
  assert.ok(panes.every((pane) => pane.hidden), 'the page is drawn with every pane hidden, which is the case this is about');
  const reviewers = panes[IDS.indexOf('reviewers')]!;
  const box = new Node({ setting: 'model', vendor: 'codex' }, 'INPUT').under(reviewers);
  runPageHtml(html, {
    '[data-tab]': tabs,
    '[data-pane]': panes,
    '[data-setting]': [box],
  });

  assert.equal(reviewers.hidden, false, 'the held tab was not opened');
  assert.equal(box.focused, true, 'the caret was put back before its pane was shown, so the control never got it');
});
