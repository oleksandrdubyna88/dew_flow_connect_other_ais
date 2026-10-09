import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PANEL_SECTIONS, type PanelState } from '../panelView';
import { LIVE_REGION_IDS, SURFACE_IDS } from '../panelSurface';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { everyPanelPage, markOf, sectionHtml } from './panelPages';

/**
 * The section registry — the one declaration both the pages and their paint keys are built from.
 *
 * <p>Every list here is DERIVED from the registry or from `SURFACE_IDS`, never retyped: a test that
 * names the sections it checks is a second copy of the registry, and it stays green the day a section
 * is added that it does not know about.</p>
 */

const state = (over: Partial<PanelState> = {}): PanelState => ({
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
  usageWindow: 'day',
  latestServerVersion: '',
  cliStatus: {},
  cardPrices: {},
  snippetStatus: { kind: 'absent', current: 0 },
  ...over,
});

test('every section has an id of its own', () => {
  const ids = PANEL_SECTIONS.map((section) => section.id);
  assert.equal(new Set(ids).size, ids.length, `a repeated id: ${ids.join(', ')}`);
  for (const id of ids) {
    assert.match(id, /^[a-zA-Z]+$/, `"${id}" cannot be addressed by the page's data-section selectors`);
  }
});

test('the sidebar draws its sections, and the Settings surface is the Settings page, not sections', () => {
  // Since E5.1 step 5 of research/PLAN_one_model_catalog.md the Settings slot paints the catalog page (`catalogPage.ts`) and
  // no section is declared for it: a section given the settings surface would be drawn by nothing.
  assert.ok(PANEL_SECTIONS.some((section) => section.surface === 'sidebar'), 'the sidebar would be an empty page');
  assert.deepEqual(PANEL_SECTIONS.filter((section) => section.surface !== 'sidebar').map((section) => section.id), []);
  assert.ok(SURFACE_IDS.includes('settings'), 'the page census lost the Settings page');
});

test('every section is drawn on exactly one page', () => {
  for (const { id } of PANEL_SECTIONS) {
    // A settings section is found on the new page by the place that holds it (`OLD_TAB_PLACES`, E5.1 step 3).
    const pages = everyPanelPage(state()).filter((page) => page.html.includes(markOf(id)));
    assert.equal(pages.length, 1, `"${id}" is on ${pages.length} pages — two controls for one setting can disagree`);
  }
});

test('every live region is on exactly one page, exactly once', () => {
  for (const region of LIVE_REGION_IDS) {
    const count = everyPanelPage(state())
      .map((page) => page.html.split(`id="live-${region}"`).length - 1)
      .reduce((sum, n) => sum + n, 0);
    assert.equal(count, 1, `live-${region} is drawn ${count} times — a live patch would reach ${count === 0 ? 'nothing' : 'the wrong copy'}`);
  }
});

test('reading a section that no page draws is a failure, not an empty string', () => {
  assert.throws(() => sectionHtml(state(), 'noSuchSection'), /no page draws a section "noSuchSection"/);
  // And the positive half on both pages, so the helper is known to find what exists wherever it is.
  assert.match(sectionHtml(state(), 'bugz'), /^<details class="section sec-bugz" data-section="bugz"/);
  assert.match(sectionHtml(state(), 'bugz'), /<\/details>$/);
  // A settings section is its place's pane on the new page (E5.1 step 3): Limits is Reviews › Limits.
  assert.match(sectionHtml(state(), 'limits'), /^<div id="cpane-reviews-limits" class="subpane" role="tabpanel"/);
  assert.match(sectionHtml(state(), 'limits'), /<\/div>$/);
});
