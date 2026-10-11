import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { PanelState } from '../panelView';
import { settingMessageFrom, settingWrite } from '../settingsShape';
import { sideSeenKey, sidesIn, type SeenSide, type SidesView } from '../sidesSeen';
import { seenDay } from '../sidesSeenView';
import { type Page, click, lastWrite, panelState, runPanel, work } from './panelPageHarness';
import { pageTree, type PageNode } from './pageTree';

/**
 * Setup › This side lists the sides this profile has been used on, with **Forget** and the path-fields switch
 * (todo/PLAN_paths_per_side.md E2.4) — the new Settings page RUN through its own script, the list read off the page as
 * drawn (`pageTree`), and every press asserted by what it POSTS (`.agents/PROJECT.md`: never a regex over the html).
 *
 * <p>Times come from one fixed `NOW`; the page is handed it in the view, so nothing here reads the clock.</p>
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 10, 12, 0, 0);
const HERE = 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais';
const WSL = 'wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';

function seen(side: string, family: SeenSide['family'], label: string, daysAgo: number): SeenSide {
  return { side, family, label, firstSeen: NOW - 60 * DAY, lastSeen: NOW - daysAgo * DAY };
}

/** A local Windows window that has seen itself and, two days ago, a WSL window of the same profile. */
function view(over: Partial<SidesView> = {}): SidesView {
  return {
    here: { key: HERE, family: 'windows', label: 'Windows' },
    seen: [seen(HERE, 'windows', 'Windows', 0), seen(WSL, 'posix', 'WSL: Ubuntu', 2)],
    mode: 'auto',
    now: NOW,
    ...over,
  };
}

function opened(sides: SidesView | undefined): { readonly page: Page; readonly pane: PageNode } {
  const state: PanelState = panelState('reviewers', sides === undefined ? {} : { sides });
  const html = catalogHtml(state, 'test-nonce', 'setup/side');

  return {
    page: runPanel(state, { html }),
    pane: pageTree(html).one((node) => node.dataset.pane === 'setup/side', 'the setup/side pane'),
  };
}

/** The rows of the list as drawn: each side's key, and its cells' text. */
function rows(pane: PageNode): readonly (readonly string[])[] {
  return pane.find((node) => node.tagName === 'TR' && node.dataset.side !== undefined)
    .map((row) => [row.dataset.side!, ...row.children.map((cell) => cell.text().replace(/\s+/gu, ' ').trim())]);
}

test('two sides seen draw two rows — name, the paths they spell, the day last seen — this window first and marked', () => {
  const { pane } = opened(view());

  assert.deepEqual(rows(pane), [
    [HERE, 'Windows (this window)', 'Windows', seenDay(NOW), ''],
    [WSL, 'WSL: Ubuntu', 'WSL / Linux', seenDay(NOW - 2 * DAY), 'Forget'],
  ]);
});

test('Forget posts the side\'s key — and this window\'s own row offers none', () => {
  const { page, pane } = opened(view());

  assert.deepEqual(pane.find((node) => node.dataset.command === 'forgetSide').map((node) => node.dataset.id), [WSL]);
  click(page, 'forgetSide', WSL);

  assert.deepEqual(work(page).map((sent) => [sent['type'], sent['command'], sent['id']]), [['command', 'forgetSide', WSL]]);
  assert.equal(sideSeenKey(WSL).endsWith(WSL), true, 'the premise: the key posted is the one the registry stores the side under');
});

test('a local window names itself by its operating system, never "this machine"', () => {
  const { pane } = opened(view());
  const said = pane.text().replace(/\s+/gu, ' ');

  assert.ok(said.includes('This side is Windows.'), `the side is not named: ${said.slice(0, 200)}`);
  assert.equal(said.includes('this machine'), false, 'a local window still calls itself "this machine"');
});

test('the switch writes coai.pathFieldsPerSide as a plain, shared setting', () => {
  const { page } = opened(view());
  const always = page.controls.filter((one) => one.dataset['setting'] === 'pathFieldsPerSide' && one.value === 'always');
  assert.equal(always.length, 1, 'the page draws no Always two choice');

  always[0]!.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), { kind: 'plain', key: 'pathFieldsPerSide', value: 'always' });
});

test('the page says what the switch decides today, and why', () => {
  const words = (sides: SidesView): string => opened(sides).pane.text().replace(/\s+/gu, ' ');

  assert.ok(words(view()).includes('two fields — Windows and WSL / Linux have both been seen within 90 days'));
  assert.ok(words(view({ seen: [seen(HERE, 'windows', 'Windows', 0)] })).includes('one field — only Windows has been seen within 90 days'));
  assert.ok(words(view({ seen: [seen(HERE, 'windows', 'Windows', 0)], mode: 'always' })).includes('two fields — Always two is chosen'));
  assert.ok(words(view({ mode: 'never' })).includes('one field — Always one is chosen'));
});

test('a side stamped by another window appears at the next render — the list is drawn from what the render is handed', () => {
  const before = rows(opened(view({ seen: [seen(HERE, 'windows', 'Windows', 0)] })).pane).map((row) => row[0]);
  const after = rows(opened(view()).pane).map((row) => row[0]);

  assert.deepEqual([before, after], [[HERE], [HERE, WSL]]);
});

test('a stored record with a time no Date can hold is ignored, and the page still opens', () => {
  // The code round's accepted finding on E2: the page wrote lastSeen 1e300 with toISOString and threw RangeError.
  const values = new Map<string, unknown>([
    [sideSeenKey(HERE), { family: 'windows', label: 'Windows', firstSeen: NOW - DAY, lastSeen: NOW }],
    [sideSeenKey(WSL), { family: 'posix', label: 'WSL: Ubuntu', firstSeen: NOW - DAY, lastSeen: 1e300 }],
  ]);
  const store = { keys: () => [...values.keys()], get: (key: string) => values.get(key), update: () => Promise.resolve() };

  const { pane } = opened(view({ seen: sidesIn(store, NOW) }));

  assert.deepEqual(rows(pane).map((row) => row[0]), [HERE]);
  assert.ok(pane.text().includes('only Windows has been seen'), 'a side seen "in the year 1e300" made two fields');
});
