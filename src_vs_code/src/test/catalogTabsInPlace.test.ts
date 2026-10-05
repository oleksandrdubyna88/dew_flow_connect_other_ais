import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { OLD_TAB_PLACES } from '../catalogPlaces';
import { BLANK_REGIONS } from '../panelSurface';
import { PANEL_SECTIONS, type PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * E4.1 of todo/PLAN_one_model_catalog.md: every old Settings section is drawn in the sub-tab that owns it on the new
 * page — by its own builder, never a copy — and each feature tab names the rows ticked for it, with the way to change
 * them on Models.
 */

/** The pane that holds a place: `cpane-<tab>` for a tab with no sub-tabs, `cpane-<tab>-<sub>` otherwise. */
function paneOf(html: string, place: string): string {
  const id = `cpane-${place.replace('/', '-')}`;
  const start = html.indexOf(`id="${id}"`);
  assert.ok(start >= 0, `the new page has no pane for ${place}`);
  // To the next tab panel: the sections nest their own divs, so a closing tag says nothing about where a pane ends.
  const next = html.indexOf('role="tabpanel"', html.indexOf('>', start));

  return html.slice(start, next < 0 ? html.length : next);
}

/** The old sections that move as they are: every Settings section but the reviewers (now Models) and the prompts (split). */
const MOVED_WHOLE = PANEL_SECTIONS.filter((spec) => spec.surface === 'settings' && !['reviewers', 'prompts', 'keys'].includes(spec.id));

test('every old section that moves whole is drawn in its place, by its own builder', () => {
  const state = panelState('reviewers');
  const body = catalogBody(state);

  for (const spec of MOVED_WHOLE) {
    const place = OLD_TAB_PLACES[spec.id];
    assert.ok(place !== undefined, `${spec.id} has no place on the new page`);
    assert.ok(paneOf(body, place).includes(spec.body(state, BLANK_REGIONS)), `${spec.title} is not drawn in ${place}`);
  }
});

test('the prompts section is split: the switches and budgets on Stages, the round pickers on Prompts per round', () => {
  const body = catalogBody(panelState('reviewers'));
  const stages = paneOf(body, 'reviews/stages');
  const prompts = paneOf(body, 'reviews/prompts');

  assert.match(stages, /data-setting="rounds"/);
  assert.match(stages, /data-setting="roleEnabled"/);
  assert.doesNotMatch(stages, /data-prompt=/, 'a round picker drawn twice on one page is two controls for one setting');
  assert.match(prompts, /data-prompt="[A-Za-z]+" data-round="1"/);
  assert.doesNotMatch(prompts, /data-setting="rounds"/);
});

test('no id is drawn twice on the new page — a label would name the wrong control', () => {
  const ids = [...catalogBody(panelState('reviewers')).matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]);
  const twice = ids.filter((id, at) => ids.indexOf(id) !== at);

  assert.deepEqual([...new Set(twice)], []);
});

test('only Roles & prompts and Commands still say where they are', () => {
  const body = catalogBody(panelState('reviewers'));
  const saying = [...body.matchAll(/data-pane="([^"]+)"[^>]*>\s*<p class="hint">[^<]* is still on the current Settings page/g)].map((match) => match[1]);

  assert.deepEqual(saying, ['reviews/roles', 'reviews/commands']);
});

test('the keys are counted across every row, not only the reviewers', () => {
  const endpoint: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-router', runtime: 'api', baseUrl: 'https://example.test/v1', plan: false, code: false, uses: ['consultant'] };
  const state: PanelState = { ...panelState('reviewers'), vendors: DEFAULT_VENDORS, catalogRows: [...DEFAULT_VENDORS, endpoint] };

  assert.match(paneOf(catalogBody(state), 'setup/keys'), /consult-router/);
});

test('a feature tab names the rows ticked for it, and a row ticked for something else is not named', () => {
  const asks: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-claude', plan: false, code: false, uses: ['consultant'] };
  const chats: Vendor = { ...DEFAULT_VENDORS[1]!, id: 'chat-fast', plan: false, code: false, uses: ['chat'] };
  const body = catalogBody({ ...panelState('reviewers'), catalogRows: [...DEFAULT_VENDORS, asks, chats] });

  assert.match(paneOf(body, 'consultants/consultant'), /data-used-by="consultant"[\s\S]*consult-claude/);
  assert.doesNotMatch(paneOf(body, 'consultants/consultant'), /chat-fast/);
  assert.match(paneOf(body, 'chat'), /data-used-by="chat"[\s\S]*chat-fast/);
});

test('a feature tab with nothing ticked says so, and the consultant says who answers until then (D2)', () => {
  const body = catalogBody({ ...panelState('reviewers'), catalogRows: DEFAULT_VENDORS });

  assert.match(paneOf(body, 'security'), /No model is ticked for the security lane yet/);
  assert.match(paneOf(body, 'consultants/consultant'), /each caller asks the pair it ships with/);
});

test('"Change on Models" opens Models narrowed to that use, in the page alone', () => {
  const change = new Node({ modelsUses: 'chat' }, 'BUTTON');
  const modelsTab = new Node({ tab: 'models' }, 'BUTTON');
  const chatTab = new Node({ tab: 'chat' }, 'BUTTON');
  const modelsPane = new Node({ pane: 'models' }, 'SECTION');
  const chatPane = new Node({ pane: 'chat' }, 'SECTION');
  const saved = { value: undefined as unknown };
  const page = runPageHtml(catalogHtml(panelState('reviewers'), 'test-nonce', 'chat'), {
    '[data-tab]': [modelsTab, chatTab],
    '[data-pane]': [modelsPane, chatPane],
    '[data-models-uses]': [change],
  }, undefined, saved);

  page.fire('click', change);

  assert.equal((saved.value as { models?: { uses?: string } }).models?.uses, 'chat');
  assert.equal(modelsPane.hidden, false, 'Models is not shown');
  assert.equal(chatPane.hidden, true);
  assert.deepEqual(page.posted.filter((one) => one['type'] === 'tab').map((one) => one['id']), ['models'], 'the host holds the place it opened');
});
