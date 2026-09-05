import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { liveRegions, PANEL_COMMANDS, panelHtml, PanelState, staticKey } from '../panelView';

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
  codexModels: [],
  localEngines: {},
  server: { kind: 'known', version: '0.6.0', remembered: false, updateOffered: true },
  side: '',
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

test('choosing a different window is a repaint, not a silent preference', () => {
  assert.notEqual(
    staticKey(state({ usageWindow: 'month' })),
    staticKey(state()),
    'the click changed the state and the panel would have painted the same HTML',
  );
});

test('a newly published server version repaints the Server section', () => {
  assert.notEqual(staticKey(state({ latestServerVersion: '0.7.0' })), staticKey(state()));
});

test('spending that advanced mid-round does NOT force a repaint', () => {
  // A repaint reloads the webview and closes an open dropdown. Usage moves every time a reviewer
  // finishes, so it must travel as a patch — the same treatment the round in flight gets.
  const busier = [...usage, { ...usage[0]!, provider: 'antigravity' }];
  assert.equal(staticKey(state({ usage: busier })), staticKey(state()));
  assert.ok(liveRegions(state({ usage: busier })).usage.includes('antigravity'));
});

test('the window tabs stay outside the patched region, so a click always lands', () => {
  const html = panelHtml(state(), 'n0nce');
  const tabs = html.indexOf('data-command="usageWindow"');
  const live = html.indexOf('id="live-usage"');
  assert.ok(tabs > 0 && live > 0);
  assert.ok(tabs < live, 'a button inside a patched region loses its listener on the next tick');
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
  const html = panelHtml(state({ latestServerVersion: '9.9.9' }), 'n0nce');
  const posted = [...html.matchAll(/data-command="([a-zA-Z]+)"/g)].map((m) => m[1]!);

  assert.ok(posted.includes('installServer'), 'the Update button is the one this test exists for');
  for (const command of posted) {
    assert.ok(
      (PANEL_COMMANDS as readonly string[]).includes(command),
      `the markup posts "${command}", which nothing handles — a button wired to nothing looks exactly like one whose work failed silently`,
    );
  }
});
