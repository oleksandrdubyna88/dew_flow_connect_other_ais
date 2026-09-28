import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { PANEL_COMMANDS, PanelState, staticKey } from '../panelView';
import { everyPanelPage } from './panelPages';

/**
 * What has to repaint, and what must not.
 *
 * <p>The panel takes two update paths: a repaint (which reloads the webview and closes any open
 * dropdown) and a patch of the live regions. Which one runs is decided by a KEY over the state,
 * and anything missing from that key is a control that can never change — which is what happened
 * to the spending chart: clicking Today, Month or Year recorded the choice and repainted nothing,
 * so the section sat on Week for good.</p>
 */

const usage = [
  { utc: new Date().toISOString(), provider: 'codex', model: 'gpt-5.6', role: 'PlanCritique',
    stage: 'PlanReview', outcome: 'Ok', tokensIn: 40_500, tokensOut: 4_500, costUsd: null, seconds: 59 },
];

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'known', version: '0.6.0', remembered: false, updateOffered: true },
  side: '',
  perSide: false,
  latestServerVersion: '0.6.0',
  questions: [],
  openSections: ['usage'],
  sessions: [],
  usage,
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  ...over,
});

/**
 * The spending window is drawn on the rounds-log page now, not in the panel.
 *
 * <p>This test used to assert the opposite, and it was right while the spending chart was a section
 * of the panel: a click on Today, Month or Year that produced the same key repainted nothing. The chart
 * moved to the rounds log's second tab on 2026-09-05, and the key is now built from what the PANEL draws
 * (`todo/PLAN_settings_page.md`, F1) — so a window change must no longer reload the sidebar, whose
 * markup it does not touch. The chart's own repaint is the rounds log's.</p>
 */
test('choosing a spending window does not reload the panel, which no longer draws the chart', () => {
  assert.equal(
    staticKey(state({ usageWindow: 'month' })),
    staticKey(state()),
    'a key that moved here would reload the sidebar for a chart it does not show',
  );
});

test('a newly published server version repaints the Server section', () => {
  assert.notEqual(staticKey(state({ latestServerVersion: '0.7.0' })), staticKey(state()));
});

/**
 * The storage section, which now has a control in it (issue #115).
 *
 * <p>It was left out of the key while it was static text, and that was harmless for exactly as long
 * as nothing in it could change. The docstring above `staticKey` records four controls frozen for
 * the life of a panel by this same omission — the spending window, the local model list, the
 * consultant's prompt, the phrases list — and a *Change…* button whose directory never updates
 * would be the fifth.</p>
 */
test('choosing a different data directory repaints the section that says so', () => {
  const here = {
    directory: '/srv/coai/windows',
    side: 'windows',
    ignoredSide: '',
    refusal: '',
    notes: [],
    alsoWatched: [],
    env: { COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' },
    source: 'this side' as const,
  };
  const elsewhere = { ...here, directory: '/mnt/nas/coai', side: '', env: { COAI_DATA_DIR: '/mnt/nas/coai' } };

  assert.notEqual(
    staticKey(state({ storage: here })),
    staticKey(state({ storage: elsewhere })),
    'the directory changed and the panel would paint the same HTML — which a person reads as a '
    + 'button that did nothing',
  );
});

/**
 * The other half of the guard on {@link PANEL_COMMANDS}.
 *
 * <p>The provider switches over that list with an exhaustiveness check, so a declared command
 * without a case cannot compile. This covers the reverse: a BUTTON posting a name that was never
 * declared, which the provider would ignore in silence — exactly how the Update button did
 * nothing for a day.</p>
 */
test('every button in the panel posts a command the panel declares', () => {
  // Every page: a button that moves to another page must stay in this scan.
  const posted = everyPanelPage(state({ latestServerVersion: '9.9.9' }))
    .flatMap((page) => [...page.html.matchAll(/data-command="([a-zA-Z]+)"/g)].map((m) => m[1]!));

  assert.ok(posted.includes('installServer'), 'the Update button is the one this test exists for');
  for (const command of posted) {
    assert.ok(
      (PANEL_COMMANDS as readonly string[]).includes(command),
      `the markup posts "${command}", which nothing handles — a button wired to nothing looks exactly like one whose work failed silently`,
    );
  }
});
